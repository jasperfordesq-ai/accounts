using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

// Why a generic account with entries keeps its id under a legacy code instead of its own code.
public enum ChartAdoptionLegacyReason
{
    // The code is a charity chart code.
    CharityCode,
    // The code lies in a range the charity chart gives a meaning to (spec §4 / CharityCustomLine).
    CharityCodeRange,
    // The account holds a system role that a charity chart account takes over.
    CharityRole
}

public enum ChartAdoptionRefusal
{
    PlanChanged,
    Blocked,
    StaleFilingArtifactsNotAcknowledged
}

public sealed record ChartAdoptionRename(
    int CategoryId,
    AccountSystemRole Role,
    string FromCode,
    string FromName,
    string ToCode,
    string ToName);

public sealed record ChartAdoptionLegacyRecode(
    int CategoryId,
    string FromCode,
    string FromName,
    string ToCode,
    string ToName,
    int TransactionCount,
    int JournalCount,
    int OpeningBalanceCount,
    int RuleCount,
    ChartAdoptionLegacyReason Reason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] AccountSystemRole? ReleasedRole);

public sealed record ChartAdoptionArchive(
    int CategoryId,
    string FromCode,
    string Name,
    string ToCode,
    int RuleCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] AccountSystemRole? ReleasedRole);

public sealed record ChartAdoptionRetained(
    int CategoryId,
    string Code,
    string Name,
    int TransactionCount,
    int JournalCount,
    int OpeningBalanceCount);

public sealed record ChartAdoptionInsert(
    string Code,
    string Name,
    AccountCategoryType Type,
    AccountStatementLine StatementLine,
    AccountCashFlowClass CashFlowClass,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] AccountSystemRole? SystemRole);

public sealed record ChartAdoptionStaleArtifact(
    int PeriodId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    PeriodStatus Status,
    FilingPackageStatus CroPackageStatus,
    FilingStatus CroFilingStatus);

// The dry-run plan of adopting the charity chart. PlanSha256 is the SHA-256 of the canonical
// serialisation of every other field; execution recomputes the plan and refuses unless it matches.
public sealed record ChartAdoptionPlan(
    int CompanyId,
    ChartOfAccountsVariant CurrentVariant,
    ChartOfAccountsVariant TargetVariant,
    IReadOnlyList<ChartAdoptionRename> Renames,
    IReadOnlyList<ChartAdoptionLegacyRecode> LegacyRecodes,
    IReadOnlyList<ChartAdoptionArchive> Archives,
    IReadOnlyList<ChartAdoptionRetained> Retained,
    IReadOnlyList<ChartAdoptionInsert> Inserts,
    IReadOnlyList<ChartAdoptionStaleArtifact> StaleFilingArtifacts,
    IReadOnlyList<string> BlockedBy,
    string PlanSha256);

public sealed record ChartAdoptionInput(
    ChartOfAccountsVariant Variant,
    string PlanSha256,
    bool AcknowledgeStaleFilingArtifacts);

public sealed record ChartAdoptionOutcome(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ChartAdoptionRefusal? Refusal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Message,
    ChartAdoptionPlan Plan)
{
    public bool Succeeded => Refusal is null;
}

/// <summary>
/// Adopts the charity / CLG chart on an existing company (spec §2.4). Only the bank control and the
/// retained earnings accounts are renamed in place; every other account with entries keeps its id, type,
/// statement line and entries under a legacy code and stays selectable; accounts without entries are
/// archived; the charity accounts whose codes are then free are inserted. It never moves a transaction,
/// never deletes an account and never creates an opening balance: re-categorising is the owner's job.
/// </summary>
public sealed class ChartAdoptionService(AccountsDbContext db, AuditService audit)
{
    public const string LegacyCodePrefix = "G-";
    public const string LegacyNameSuffix = " (legacy)";
    private const string TemporaryCodePrefix = "~c6-";

    private static readonly JsonSerializerOptions CanonicalJson = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false
    };

    public async Task<ChartAdoptionPlan> PlanAsync(int companyId, CancellationToken cancellationToken = default) =>
        (await BuildAsync(companyId, tracked: false, cancellationToken)).Plan;

    public async Task<ChartAdoptionOutcome> AdoptAsync(
        int companyId,
        ChartAdoptionInput input,
        AuthenticatedUser actor,
        string? requestId = null,
        CancellationToken cancellationToken = default)
    {
        if (input.Variant != ChartOfAccountsVariant.CharityClg)
            throw new BusinessRuleException("Only the charity / CLG chart of accounts can be adopted on an existing company.");

        var relational = db.Database.IsRelational();
        await using var transaction = relational && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        if (relational)
            await AccountingConcurrencyCoordinator.AcquireCompanyAdvisoryLockAsync(db, companyId, cancellationToken);

        // Recomputed under the company lock: what executes is exactly what the owner reviewed.
        var state = await BuildAsync(companyId, tracked: true, cancellationToken);
        var plan = state.Plan;
        if (!string.Equals(plan.PlanSha256, input.PlanSha256?.Trim(), StringComparison.OrdinalIgnoreCase))
            return new(ChartAdoptionRefusal.PlanChanged, "The accounts changed since the plan was reviewed. Review the updated plan before adopting the chart.", plan);
        if (plan.BlockedBy.Count > 0)
            return new(ChartAdoptionRefusal.Blocked, string.Join(" ", plan.BlockedBy), plan);
        if (plan.StaleFilingArtifacts.Count > 0 && !input.AcknowledgeStaleFilingArtifacts)
            return new(
                ChartAdoptionRefusal.StaleFilingArtifactsNotAcknowledged,
                "Adopting the chart makes the generated CRO filing pack and directors' approval (s.324) of the listed periods stale. Acknowledge this to continue; they must then be regenerated and approved again.",
                plan);

        var untouchable = await PostingSnapshotAsync(companyId, cancellationToken);
        var touched = state.Decisions.Where(decision => decision.Action != RowAction.Retain).ToList();
        var before = touched.Select(decision => DomainAuditCoverage.CategorySnapshot(decision.Row)).ToList();
        var previousVariant = state.Company.ChartOfAccountsVariant;

        // The company's locked periods were verified by the plan (no affected account has entries in them),
        // so the concurrency interceptor may let these account and variant changes through for them only.
        db.ChartAdoptionAllowance = new ChartAdoptionWriteAllowance(companyId, state.VerifiedLockedPeriodIds);
        try
        {
            // Phase 1: temporary codes (the final codes permute, e.g. 1400 -> 1200 while 1200 -> G-1200),
            // and release the roles the charity accounts take, so no unique index is ever violated.
            foreach (var decision in touched)
            {
                MaterialiseClassification(decision.Row);
                decision.Row.Code = TemporaryCodePrefix + decision.Row.Id.ToString(CultureInfo.InvariantCulture);
                if (decision.Action != RowAction.Rename)
                    decision.Row.SystemRole = decision.FinalRole;
            }
            await db.SaveChangesAsync(cancellationToken);

            // Phase 2: final codes, names and archive flags; renamed rows take the charity attributes.
            foreach (var decision in touched)
            {
                decision.Row.Code = decision.FinalCode;
                decision.Row.Name = decision.FinalName;
                if (decision.Action == RowAction.Archive)
                    decision.Row.IsArchived = true;
                if (decision.RenameTarget is { } target)
                {
                    decision.Row.Type = target.Type;
                    decision.Row.TaxTreatment = target.TaxTreatment;
                    decision.Row.StatementLine = target.StatementLine;
                    decision.Row.CashFlowClass = target.CashFlowClass;
                    decision.Row.SystemRole = target.SystemRole;
                    decision.Row.IsArchived = false;
                }
            }
            await db.SaveChangesAsync(cancellationToken);

            // Phase 3: the charity accounts whose codes are now free, and the company's variant.
            var inserted = state.Inserts.Select(account => new AccountCategory
            {
                CompanyId = companyId,
                Code = account.Code,
                Name = account.Name,
                Type = account.Type,
                TaxTreatment = account.TaxTreatment,
                IsSystem = true,
                StatementLine = account.StatementLine,
                CashFlowClass = account.CashFlowClass,
                SystemRole = account.SystemRole
            }).ToList();
            db.AccountCategories.AddRange(inserted);
            state.Company.ChartOfAccountsVariant = ChartOfAccountsVariant.CharityClg;
            await db.SaveChangesAsync(cancellationToken);

            // Prove, inside the transaction, that no transaction, journal or opening balance moved.
            if (await PostingSnapshotAsync(companyId, cancellationToken) != untouchable)
                throw new InvalidOperationException("Chart adoption would have changed a posting; nothing was changed.");

            await audit.LogAsync(
                companyId,
                null,
                nameof(Company),
                companyId,
                AuditEventCodes.AccountCategoriesChartAdopted,
                new { ChartOfAccountsVariant = previousVariant, Categories = before },
                new
                {
                    ChartOfAccountsVariant = ChartOfAccountsVariant.CharityClg,
                    plan.PlanSha256,
                    AcknowledgedStaleFilingArtifacts = plan.StaleFilingArtifacts.Select(item => item.PeriodId).ToList(),
                    Categories = touched.Select(decision => decision.Row).Concat(inserted)
                        .Select(DomainAuditCoverage.CategorySnapshot)
                        .ToList()
                },
                AuthenticatedIdentity.AuditUserId(actor),
                actor.TenantId,
                requestId,
                AuthenticatedIdentity.ReviewerDisplayName(actor),
                cancellationToken: cancellationToken);

            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            db.ChartAdoptionAllowance = null;
        }

        return new(null, null, plan);
    }

    // ---- Plan -----------------------------------------------------------------------------------

    private enum RowAction { Rename, Legacy, Archive, Retain }

    private sealed record RowDecision(
        AccountCategory Row,
        RowAction Action,
        string FinalCode,
        string FinalName,
        AccountSystemRole? FinalRole,
        ChartAccountDefinition? RenameTarget);

    private sealed record PlanState(
        Company Company,
        ChartAdoptionPlan Plan,
        IReadOnlyList<RowDecision> Decisions,
        IReadOnlyList<ChartAccountDefinition> Inserts,
        IReadOnlySet<int> VerifiedLockedPeriodIds);

    private sealed record Usage(int Transactions, int AnyTransactions, int Journals, int Openings, int Rules)
    {
        public bool HasPostings => AnyTransactions + Journals + Openings > 0;
    }

    private async Task<PlanState> BuildAsync(int companyId, bool tracked, CancellationToken cancellationToken)
    {
        var companies = tracked ? db.Companies : db.Companies.AsNoTracking();
        var company = await companies.SingleOrDefaultAsync(c => c.Id == companyId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Company {companyId} not found");
        var target = ChartDefinitions.CharityClg;
        var blockedBy = new List<string>();

        if (company.ChartOfAccountsVariant == ChartOfAccountsVariant.CharityClg)
        {
            blockedBy.Add("The company already uses the charity / CLG chart of accounts.");
            return new PlanState(
                company,
                Seal(new ChartAdoptionPlan(companyId, company.ChartOfAccountsVariant, ChartOfAccountsVariant.CharityClg,
                    [], [], [], [], [], [], blockedBy, "")),
                [], [], new HashSet<int>());
        }

        if (company.CompanyType != CompanyType.CompanyLimitedByGuarantee)
            blockedBy.Add("Only a company limited by guarantee can adopt the charity / CLG chart of accounts.");

        // The charity chart has no land and buildings account: after adoption a land-class asset would
        // make journal generation and the balance sheet fail, so adoption waits until it is re-classed.
        var landAssets = (await db.FixedAssets.AsNoTracking()
                .Where(asset => asset.CompanyId == companyId)
                .OrderBy(asset => asset.Id)
                .Select(asset => new { asset.Name, asset.Category })
                .ToListAsync(cancellationToken))
            .Where(asset => ChartDefinitions.IsLandAssetClass(asset.Category))
            .ToList();
        if (landAssets.Count > 0)
            blockedBy.Add("The charity chart has no land and buildings account, so these assets would have nowhere to post: "
                + string.Join("; ", landAssets.Select(asset => $"{asset.Name} ({asset.Category})"))
                + ". Keep the current chart, or change an asset's class if it is not land or buildings.");

        var categories = tracked ? db.AccountCategories : db.AccountCategories.AsNoTracking();
        var rows = await categories
            .Where(c => c.CompanyId == companyId)
            .OrderBy(c => c.Id)
            .ToListAsync(cancellationToken);
        var usage = await UsageAsync(companyId, cancellationToken);
        Usage UsageOf(AccountCategory row) => usage.GetValueOrDefault(row.Id, new Usage(0, 0, 0, 0, 0));

        var charityCodes = target.Accounts.Concat(target.LazyRoleAccounts)
            .Select(account => account.Code)
            .ToHashSet(StringComparer.Ordinal);
        var charityRoles = target.Accounts
            .Where(account => account.SystemRole is not null)
            .ToDictionary(account => account.SystemRole!.Value, account => account);

        // Spec §2.4 (binding, narrower than the research map): only these two are renamed in place.
        var renames = new Dictionary<int, ChartAccountDefinition>();
        foreach (var role in new[] { AccountSystemRole.BankControl, AccountSystemRole.RetainedEarnings })
        {
            var holder = rows.Where(row => CategoryClassification.Role(row) == role).OrderBy(row => row.Id).FirstOrDefault();
            if (holder is null) continue;
            var charity = charityRoles[role];
            if (holder.Type != charity.Type
                || CategoryClassification.Line(holder) != charity.StatementLine
                || CategoryClassification.CashFlow(holder) != charity.CashFlowClass)
            {
                blockedBy.Add($"Account {holder.Code} {holder.Name} holds the {role} role but is not presented like the charity chart's {charity.Code} {charity.Name}; renaming it would restate the accounts. Correct it first.");
            }
            renames[holder.Id] = charity;
        }

        var decisions = new List<RowDecision>();
        var reserved = new HashSet<string>(charityCodes, StringComparer.Ordinal);
        var legacyReasons = new Dictionary<int, ChartAdoptionLegacyReason>();
        foreach (var row in rows)
        {
            if (renames.TryGetValue(row.Id, out var charity))
            {
                decisions.Add(new RowDecision(row, RowAction.Rename, charity.Code, charity.Name, charity.SystemRole, charity));
                continue;
            }

            var role = CategoryClassification.Role(row);
            var roleTaken = role is { } held && charityRoles.ContainsKey(held);
            var codeClash = charityCodes.Contains(row.Code);
            if (UsageOf(row).HasPostings)
            {
                ChartAdoptionLegacyReason? reason = codeClash ? ChartAdoptionLegacyReason.CharityCode
                    : ChartDefinitions.CharityCustomLine(row.Code) is not null ? ChartAdoptionLegacyReason.CharityCodeRange
                    : roleTaken ? ChartAdoptionLegacyReason.CharityRole
                    : null;
                if (reason is null)
                {
                    reserved.Add(row.Code);
                    decisions.Add(new RowDecision(row, RowAction.Retain, row.Code, row.Name, role, null));
                    continue;
                }
                legacyReasons[row.Id] = reason.Value;
                decisions.Add(new RowDecision(row, RowAction.Legacy, "", LegacyName(row.Name), roleTaken ? null : role, null));
                continue;
            }

            if (row.IsArchived && !codeClash && role is null)
            {
                reserved.Add(row.Code);
                decisions.Add(new RowDecision(row, RowAction.Retain, row.Code, row.Name, null, null));
                continue;
            }
            // No entries: archived; given a legacy code only where a charity code must be freed.
            if (!codeClash) reserved.Add(row.Code);
            decisions.Add(new RowDecision(row, RowAction.Archive, codeClash ? "" : row.Code, row.Name, null, null));
        }

        // Legacy codes, in id order: G-<code>, kept unique and within the 20-character code limit.
        for (var index = 0; index < decisions.Count; index++)
        {
            var decision = decisions[index];
            if (decision.FinalCode.Length > 0) continue;
            var code = LegacyCodePrefix + decision.Row.Code;
            if (code.Length > 20 || reserved.Contains(code))
                code = LegacyCodePrefix + decision.Row.Id.ToString(CultureInfo.InvariantCulture);
            for (var suffix = 2; reserved.Contains(code); suffix++)
                code = $"{LegacyCodePrefix}{decision.Row.Id.ToString(CultureInfo.InvariantCulture)}-{suffix.ToString(CultureInfo.InvariantCulture)}";
            reserved.Add(code);
            decisions[index] = decision with { FinalCode = code };
        }

        var renamedRoles = renames.Values.Select(definition => definition.SystemRole).ToHashSet();
        var inserts = target.Accounts
            .Where(account => account.SystemRole is null || !renamedRoles.Contains(account.SystemRole))
            .ToList();

        var (verifiedLockedPeriods, lockedBlockers) = await LockedPeriodBlockersAsync(companyId, rows, cancellationToken);
        blockedBy.AddRange(lockedBlockers);

        var stale = await StaleArtifactsAsync(companyId, cancellationToken);
        foreach (var item in stale.Where(item => item.Status == PeriodStatus.Filed
                     || item.CroPackageStatus is FilingPackageStatus.Submitted or FilingPackageStatus.Accepted
                     || item.CroFilingStatus is FilingStatus.Submitted or FilingStatus.Accepted))
        {
            blockedBy.Add($"The CRO filing pack for the period {Dates(item.PeriodStart, item.PeriodEnd)} has been filed or submitted; adopting the chart would make its records differ from what was filed.");
        }

        var plan = new ChartAdoptionPlan(
            companyId,
            company.ChartOfAccountsVariant,
            ChartOfAccountsVariant.CharityClg,
            decisions.Where(d => d.Action == RowAction.Rename)
                .OrderBy(d => d.RenameTarget!.SystemRole == AccountSystemRole.BankControl ? 0 : 1)
                .Select(d => new ChartAdoptionRename(d.Row.Id, d.RenameTarget!.SystemRole!.Value, d.Row.Code, d.Row.Name, d.FinalCode, d.FinalName))
                .ToList(),
            decisions.Where(d => d.Action == RowAction.Legacy)
                .Select(d => new ChartAdoptionLegacyRecode(
                    d.Row.Id, d.Row.Code, d.Row.Name, d.FinalCode, d.FinalName,
                    UsageOf(d.Row).Transactions, UsageOf(d.Row).Journals, UsageOf(d.Row).Openings, UsageOf(d.Row).Rules,
                    legacyReasons[d.Row.Id],
                    CategoryClassification.Role(d.Row) is { } held && held != d.FinalRole ? held : null))
                .ToList(),
            decisions.Where(d => d.Action == RowAction.Archive)
                .Select(d => new ChartAdoptionArchive(d.Row.Id, d.Row.Code, d.Row.Name, d.FinalCode, UsageOf(d.Row).Rules, CategoryClassification.Role(d.Row)))
                .ToList(),
            decisions.Where(d => d.Action == RowAction.Retain && UsageOf(d.Row).HasPostings)
                .Select(d => new ChartAdoptionRetained(d.Row.Id, d.Row.Code, d.Row.Name, UsageOf(d.Row).Transactions, UsageOf(d.Row).Journals, UsageOf(d.Row).Openings))
                .ToList(),
            inserts.Select(account => new ChartAdoptionInsert(account.Code, account.Name, account.Type, account.StatementLine!.Value, account.CashFlowClass!.Value, account.SystemRole)).ToList(),
            stale,
            blockedBy,
            "");
        return new PlanState(company, Seal(plan), decisions, inserts, verifiedLockedPeriods);
    }

    private static ChartAdoptionPlan Seal(ChartAdoptionPlan plan)
    {
        var canonical = JsonSerializer.Serialize(plan with { PlanSha256 = "" }, CanonicalJson);
        return plan with { PlanSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))) };
    }

    private static string LegacyName(string name)
    {
        if (name.EndsWith(LegacyNameSuffix, StringComparison.Ordinal)) return name;
        return (name.Length + LegacyNameSuffix.Length > 200 ? name[..(200 - LegacyNameSuffix.Length)] : name) + LegacyNameSuffix;
    }

    // A re-coded row keeps exactly its presentation: an unclassified row's code-derived attributes are made
    // explicit before its code changes. (A null role on a classified row already means "no role".)
    private static void MaterialiseClassification(AccountCategory row)
    {
        if (row.StatementLine is not null) return;
        var role = CategoryClassification.Role(row);
        row.CashFlowClass ??= CategoryClassification.CashFlow(row);
        row.StatementLine = CategoryClassification.Line(row);
        row.SystemRole = role;
    }

    private async Task<Dictionary<int, Usage>> UsageAsync(int companyId, CancellationToken cancellationToken)
    {
        var transactions = await db.ImportedTransactions.AsNoTracking()
            .Where(t => t.BankAccount.CompanyId == companyId && t.CategoryId != null)
            .GroupBy(t => t.CategoryId!.Value)
            .Select(group => new { Id = group.Key, All = group.Count(), Posted = group.Count(t => !t.IsDuplicate) })
            .ToListAsync(cancellationToken);
        var adjustments = await db.Adjustments.AsNoTracking()
            .Where(a => a.Period.CompanyId == companyId)
            .Select(a => new { a.DebitCategoryId, a.CreditCategoryId })
            .ToListAsync(cancellationToken);
        var openings = await db.OpeningBalances.AsNoTracking()
            .Where(o => o.Period.CompanyId == companyId)
            .GroupBy(o => o.AccountCategoryId)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var rules = await db.TransactionRules.AsNoTracking()
            .Where(r => r.CompanyId == companyId)
            .GroupBy(r => r.CategoryId)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var ids = transactions.Select(t => t.Id)
            .Concat(adjustments.SelectMany(a => new[] { a.DebitCategoryId, a.CreditCategoryId }).OfType<int>())
            .Concat(openings.Select(o => o.Id))
            .Concat(rules.Select(r => r.Id))
            .Distinct();
        return ids.ToDictionary(id => id, id => new Usage(
            transactions.FirstOrDefault(t => t.Id == id)?.Posted ?? 0,
            transactions.FirstOrDefault(t => t.Id == id)?.All ?? 0,
            adjustments.Count(a => a.DebitCategoryId == id || a.CreditCategoryId == id),
            openings.FirstOrDefault(o => o.Id == id)?.Count ?? 0,
            rules.FirstOrDefault(r => r.Id == id)?.Count ?? 0));
    }

    // A Finalised, Filed or locked period may not change, and adopting the chart re-presents every period's
    // statements (account codes and names, members' funds). The plan therefore refuses when any locked period
    // has any entry at all: an opening (including a carried-forward or bank opening balance), a transaction
    // (categorised or not) or a journal, read from that period's own ledger and its rows. Locked periods with
    // no entries are verified and returned, so the write path may change the chart without restating them.
    private async Task<(IReadOnlySet<int> Verified, List<string> Blockers)> LockedPeriodBlockersAsync(
        int companyId,
        IReadOnlyList<AccountCategory> rows,
        CancellationToken cancellationToken)
    {
        var locked = await db.AccountingPeriods.AsNoTracking()
            .Where(p => p.CompanyId == companyId
                && (p.Status == PeriodStatus.Finalised || p.Status == PeriodStatus.Filed || p.LockedAt != null))
            .OrderBy(p => p.PeriodStart)
            .ToListAsync(cancellationToken);
        var verified = new HashSet<int>();
        var blockers = new List<string>();
        var ledger = new AccountingLedgerService(db);
        foreach (var period in locked)
        {
            var state = period.Status is PeriodStatus.Finalised or PeriodStatus.Filed
                ? period.Status.ToString().ToLowerInvariant()
                : "locked";
            HashSet<int> active;
            try
            {
                var snapshot = await ledger.BuildAsync(companyId, period.Id);
                active = snapshot.Lines.Values
                    .Where(line => line.Debit != 0 || line.Credit != 0 || line.TransactionCount > 0 || line.AdjustmentCount > 0)
                    .Select(line => line.Category.Id)
                    .ToHashSet();
            }
            catch (BusinessRuleException ex)
            {
                blockers.Add($"The ledger of the {state} period {Dates(period.PeriodStart, period.PeriodEnd)} cannot be built ({ex.Message}), so it cannot be shown that the chart change leaves it unchanged.");
                continue;
            }
            var transactions = await db.ImportedTransactions.AsNoTracking()
                .Where(t => t.PeriodId == period.Id && !t.IsDuplicate)
                .Select(t => t.CategoryId)
                .ToListAsync(cancellationToken);
            active.UnionWith(transactions.OfType<int>());
            var uncategorised = transactions.Count(id => id is null);
            var journals = await db.Adjustments.AsNoTracking().CountAsync(a => a.PeriodId == period.Id, cancellationToken);
            var openings = await db.OpeningBalances.AsNoTracking().CountAsync(o => o.PeriodId == period.Id, cancellationToken);

            if (active.Count == 0 && uncategorised == 0 && journals == 0 && openings == 0)
            {
                verified.Add(period.Id);
                continue;
            }
            var entries = rows.Where(row => active.Contains(row.Id)).Select(row => $"{row.Code} {row.Name}").ToList();
            if (uncategorised > 0)
                entries.Add($"{uncategorised} uncategorised transaction{(uncategorised == 1 ? "" : "s")}");
            blockers.Add($"The period {Dates(period.PeriodStart, period.PeriodEnd)} is {state} and has entries ({string.Join(", ", entries)}). Adopting the chart would re-present that period's statements; reopen it before adopting the chart.");
        }
        return (verified, blockers);
    }

    // The CRO filing pack and directors' approval (s.324) of every period whose pack carries a source
    // fingerprint go stale: the fingerprint covers the trial balance (codes and names) and the balance
    // sheet, which the charity chart presents as members' funds.
    private async Task<List<ChartAdoptionStaleArtifact>> StaleArtifactsAsync(int companyId, CancellationToken cancellationToken) =>
        (await db.AccountingPeriods.AsNoTracking()
            .Where(p => p.CompanyId == companyId
                && p.CroFilingPackage != null
                && p.CroFilingPackage.ArtifactSourceFingerprintSha256 != null)
            .OrderBy(p => p.PeriodStart)
            .ThenBy(p => p.Id)
            .Select(p => new ChartAdoptionStaleArtifact(
                p.Id, p.PeriodStart, p.PeriodEnd, p.Status, p.CroFilingPackage!.Status, p.CroFilingPackage.FilingStatus))
            .ToListAsync(cancellationToken));

    // Every transaction's category, every journal's accounts, every opening balance's account and every
    // transaction rule's target account.
    private async Task<string> PostingSnapshotAsync(int companyId, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        foreach (var t in await db.ImportedTransactions.AsNoTracking()
                     .Where(t => t.BankAccount.CompanyId == companyId)
                     .OrderBy(t => t.Id)
                     .Select(t => new { t.Id, t.CategoryId, t.Amount, t.IsDuplicate })
                     .ToListAsync(cancellationToken))
            builder.Append(CultureInfo.InvariantCulture, $"t{t.Id}:{t.CategoryId}:{t.Amount}:{t.IsDuplicate};");
        foreach (var a in await db.Adjustments.AsNoTracking()
                     .Where(a => a.Period.CompanyId == companyId)
                     .OrderBy(a => a.Id)
                     .Select(a => new { a.Id, a.DebitCategoryId, a.CreditCategoryId, a.Amount })
                     .ToListAsync(cancellationToken))
            builder.Append(CultureInfo.InvariantCulture, $"a{a.Id}:{a.DebitCategoryId}:{a.CreditCategoryId}:{a.Amount};");
        foreach (var o in await db.OpeningBalances.AsNoTracking()
                     .Where(o => o.Period.CompanyId == companyId)
                     .OrderBy(o => o.Id)
                     .Select(o => new { o.Id, o.AccountCategoryId, o.Debit, o.Credit })
                     .ToListAsync(cancellationToken))
            builder.Append(CultureInfo.InvariantCulture, $"o{o.Id}:{o.AccountCategoryId}:{o.Debit}:{o.Credit};");
        foreach (var rule in await db.TransactionRules.AsNoTracking()
                     .Where(rule => rule.CompanyId == companyId)
                     .OrderBy(rule => rule.Id)
                     .Select(rule => new { rule.Id, rule.CategoryId })
                     .ToListAsync(cancellationToken))
            builder.Append(CultureInfo.InvariantCulture, $"r{rule.Id}:{rule.CategoryId};");
        return builder.ToString();
    }

    private static string Dates(DateOnly start, DateOnly end) =>
        $"{start.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}–{end.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";
}

// Set on the DbContext only while ChartAdoptionService writes: the company's locked periods that the plan
// verified carry no entry on any account the adoption changes.
public sealed record ChartAdoptionWriteAllowance(int CompanyId, IReadOnlySet<int> VerifiedLockedPeriodIds);
