using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Accounts.Api.Services;

/// <summary>
/// Builds and retains internal accountant working papers. These artifacts are evidence and review
/// aids only: they are never filing returns and expose no CRO/ROS submission operation.
/// </summary>
public class AccountantWorkingPaperService(
    AccountsDbContext db,
    FinancialStatementsService statements,
    TaxComputationService tax,
    FilingReleaseIdentityProvider releaseIdentity,
    AuditService audit,
    TimeProvider timeProvider)
{
    public const string OutputKind = "internal-accountant-working-paper-pack";
    public const string SchemaVersion = "accounts-working-papers-v1";
    public const string Warning = "INTERNAL ACCOUNTANT WORKING PAPERS — NOT A CRO OR CT1 RETURN — NOTHING IS SUBMITTED";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public sealed record ArtifactIdentity(
        string SchemaVersion,
        int ArtifactVersion,
        int TenantId,
        int CompanyId,
        string CompanyName,
        int PeriodId,
        string PeriodStart,
        string PeriodEnd,
        string GeneratedByUserId,
        string GeneratedByDisplayName,
        string GeneratedByRole,
        DateTime GeneratedAtUtc,
        string ReleaseCandidate,
        string SourceDataSha256,
        string ArtifactSha256);

    public sealed record SourceReference(
        string SourceType,
        int EntityId,
        int PeriodId,
        string Label,
        decimal? Amount,
        string? EvidenceReference,
        string? ReviewedBy,
        DateTime? ReviewedAtUtc,
        string DrillDownRoute);

    public sealed record Reconciliation(
        string Code,
        string Description,
        decimal Left,
        decimal Right,
        decimal Difference,
        bool Reconciles);

    public sealed record LeadScheduleRow(
        string Code,
        string Name,
        string AccountType,
        decimal OpeningDebit,
        decimal OpeningCredit,
        decimal TransactionDebit,
        decimal TransactionCredit,
        decimal JournalDebit,
        decimal JournalCredit,
        decimal ClosingDebit,
        decimal ClosingCredit,
        IReadOnlyList<SourceReference> Sources);

    public sealed record LeadSchedulesArtifact(
        string OutputKind,
        IReadOnlyList<LeadScheduleRow> Rows,
        IReadOnlyList<Reconciliation> Reconciliations,
        string ArtifactSha256);

    public sealed record CategorizedTransactionRow(
        int TransactionId,
        string Date,
        string Description,
        decimal Amount,
        int BankAccountId,
        string BankAccountName,
        int? ImportBatchId,
        string? ImportFilename,
        string? SourceFileSha256,
        int? CategoryId,
        string? CategoryCode,
        string? CategoryName,
        string CategorizationStatus,
        decimal? ConfidenceScore,
        bool ManualOverride,
        bool IncludedInLedger,
        string DuplicateReviewStatus,
        string? DuplicateDecisionBy,
        DateTime? DuplicateDecisionAtUtc,
        IReadOnlyList<SourceReference> Sources);

    public sealed record CategorizedTransactionsArtifact(
        string OutputKind,
        int TotalCount,
        int CategorizedCount,
        int UncategorisedCount,
        decimal IncludedNetMovement,
        IReadOnlyList<CategorizedTransactionRow> Rows,
        string ArtifactSha256);

    public sealed record ReviewException(
        string Code,
        string Severity,
        string Message,
        string ResolutionRoute,
        IReadOnlyList<SourceReference> Sources);

    public sealed record ReviewExceptionsArtifact(
        string OutputKind,
        int BlockingCount,
        int WarningCount,
        IReadOnlyList<ReviewException> Items,
        string ArtifactSha256);

    public sealed record AdjustedTrialBalanceRow(
        string Code,
        string Name,
        string AccountType,
        decimal UnadjustedDebit,
        decimal UnadjustedCredit,
        decimal JournalDebit,
        decimal JournalCredit,
        decimal AdjustedDebit,
        decimal AdjustedCredit,
        IReadOnlyList<SourceReference> Sources);

    public sealed record AdjustedTrialBalanceArtifact(
        string OutputKind,
        decimal TotalUnadjustedDebits,
        decimal TotalUnadjustedCredits,
        decimal TotalJournalDebits,
        decimal TotalJournalCredits,
        decimal TotalAdjustedDebits,
        decimal TotalAdjustedCredits,
        IReadOnlyList<AdjustedTrialBalanceRow> Rows,
        IReadOnlyList<Reconciliation> Reconciliations,
        string ArtifactSha256);

    public sealed record CorporationTaxBridgeRow(
        string Code,
        string Description,
        decimal Amount,
        string Basis,
        IReadOnlyList<SourceReference> Sources);

    public sealed record CorporationTaxBridgeArtifact(
        string OutputKind,
        bool IsCompleteCt1Return,
        bool DirectRosSubmissionSupported,
        bool QualifiedAccountantReviewRequired,
        string TaxCalculationSha256,
        decimal AccountingProfitBeforeTax,
        decimal TradingProfitBeforeLossRelief,
        decimal CapitalAllowances,
        decimal TradingLossUsed,
        decimal TradingProfitAfterLossRelief,
        decimal PassiveNonTradingIncome,
        decimal TaxableProfit,
        decimal CorporationTaxDue,
        decimal PreliminaryTaxPaid,
        decimal BalanceDue,
        IReadOnlyList<CorporationTaxBridgeRow> Rows,
        IReadOnlyList<Reconciliation> Reconciliations,
        IReadOnlyList<string> BlockingReasons,
        string ArtifactSha256);

    public sealed record WorkingPaperIndexEntry(
        string Code,
        string Title,
        string Endpoint,
        int ItemCount,
        string Status,
        string ArtifactSha256);

    public sealed record WorkingPaperIndexArtifact(
        string OutputKind,
        IReadOnlyList<WorkingPaperIndexEntry> Entries,
        string ArtifactSha256);

    public sealed record Pack(
        string OutputKind,
        bool IsFilingArtifact,
        bool DirectSubmissionSupported,
        bool QualifiedAccountantReviewRequired,
        string Warning,
        ArtifactIdentity Identity,
        LeadSchedulesArtifact LeadSchedules,
        CategorizedTransactionsArtifact CategorizedTransactions,
        ReviewExceptionsArtifact ReviewExceptions,
        AdjustedTrialBalanceArtifact AdjustedTrialBalance,
        WorkingPaperIndexArtifact WorkingPaperIndex,
        CorporationTaxBridgeArtifact CorporationTaxBridge);

    private sealed record BuildData(
        AccountingPeriod Period,
        Company Company,
        IReadOnlyList<FinancialStatementsService.StatementSourceSummary> StatementSources,
        FinancialStatementsService.ProfitAndLoss ProfitAndLoss,
        FinancialStatementsService.BalanceSheet BalanceSheet,
        FinancialStatementsService.ReadinessScore Readiness,
        TaxComputationService.Ct1SupportData TaxSupport,
        IReadOnlyList<ImportedTransaction> Transactions,
        IReadOnlyList<Adjustment> Adjustments,
        IReadOnlyList<OpeningBalance> OpeningBalances,
        IReadOnlyList<BankAccount> BankAccounts,
        IReadOnlyList<YearEndReviewConfirmation> ReviewConfirmations,
        IReadOnlyList<SourceReference> YearEndFacts,
        IReadOnlyDictionary<string, IReadOnlyList<SourceReference>> AccountSources,
        string SourceDataSha256);

    public async Task<Pack> GenerateAndRetainAsync(
        int companyId,
        int periodId,
        AuthenticatedUser actor,
        string? requestId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureCanGenerate(actor);
        var data = await LoadBuildDataAsync(companyId, periodId, cancellationToken);
        if (data.Company.TenantId != actor.TenantId)
            throw new ResourceNotFoundException($"Period {periodId} not found");

        var generatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        IDbContextTransaction? transaction = null;
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var report = new Report
        {
            PeriodId = periodId,
            Type = ReportType.LeadSchedule,
            DataJson = $"{{\"outputKind\":\"{OutputKind}\",\"generationState\":\"pending\"}}",
            GeneratedAt = generatedAtUtc
        };
        try
        {
            db.Reports.Add(report);
            await db.SaveChangesAsync(cancellationToken);
            var pack = BuildPack(
                data,
                actor,
                releaseIdentity.GetRequiredCandidate(),
                report.Id,
                generatedAtUtc);
            report.DataJson = JsonSerializer.Serialize(pack, JsonOptions);
            await db.SaveChangesAsync(cancellationToken);

            await WriteAuditAsync(companyId, periodId, report, pack, actor, requestId, cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return pack;
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            else if (report.Id != 0)
            {
                // EF's in-memory provider has no transactions. Compensate so behavioral tests and
                // non-relational development use retain the same all-or-nothing contract.
                db.ChangeTracker.Clear();
                var retained = await db.Reports.FindAsync([report.Id], CancellationToken.None);
                if (retained is not null)
                    db.Reports.Remove(retained);
                var partialAudit = await db.AuditLogs
                    .Where(item => item.EntityType == nameof(Report)
                        && item.EntityId == report.Id
                        && item.Action == AuditEventCodes.AccountantWorkingPaperPackGenerated)
                    .ToListAsync(CancellationToken.None);
                db.AuditLogs.RemoveRange(partialAudit);
                await db.SaveChangesAsync(CancellationToken.None);
            }
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    public async Task<Pack?> GetLatestRetainedAsync(
        int companyId,
        int periodId,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        EnsureInternalRole(actor);
        var period = await db.AccountingPeriods
            .AsNoTracking()
            .Include(item => item.Company)
            .FirstOrDefaultAsync(item => item.Id == periodId && item.CompanyId == companyId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");
        if (period.Company.TenantId != actor.TenantId)
            throw new ResourceNotFoundException($"Period {periodId} not found");

        var json = await db.Reports
            .AsNoTracking()
            .Where(report => report.PeriodId == periodId
                && report.Type == ReportType.LeadSchedule
                && report.DataJson != null
                && report.DataJson.Contains($"\"outputKind\":\"{OutputKind}\""))
            .OrderByDescending(report => report.GeneratedAt)
            .ThenByDescending(report => report.Id)
            .Select(report => report.DataJson!)
            .FirstOrDefaultAsync(cancellationToken);
        if (json is null)
            return null;

        Pack pack;
        try
        {
            pack = JsonSerializer.Deserialize<Pack>(json, JsonOptions)
                ?? throw new JsonException("The retained artifact was empty.");
        }
        catch (JsonException)
        {
            throw new BusinessRuleException("The retained working-paper artifact is malformed and must be regenerated.");
        }

        ValidatePackHash(pack);
        if (pack.Identity.TenantId != actor.TenantId
            || pack.Identity.CompanyId != companyId
            || pack.Identity.PeriodId != periodId)
        {
            throw new BusinessRuleException("The retained working-paper artifact identity does not match this company and period.");
        }
        if (!string.Equals(pack.Identity.ReleaseCandidate, releaseIdentity.GetRequiredCandidate(), StringComparison.Ordinal))
            throw new BusinessRuleException("The retained working-paper artifact belongs to a different release candidate and must be regenerated.");

        var currentData = await LoadBuildDataAsync(companyId, periodId, cancellationToken);
        if (!string.Equals(pack.Identity.SourceDataSha256, currentData.SourceDataSha256, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("Accounting source data changed after this working-paper artifact was retained; regenerate it before review.");
        return pack;
    }

    public static void EnsureInternalRole(AuthenticatedUser actor)
    {
        var role = actor.Role.Trim();
        if (!role.Equals("Owner", StringComparison.OrdinalIgnoreCase)
            && !role.Equals("Accountant", StringComparison.OrdinalIgnoreCase)
            && !role.Equals("Reviewer", StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkingPaperAccessDeniedException();
        }
    }

    public static void EnsureCanGenerate(AuthenticatedUser actor)
    {
        EnsureInternalRole(actor);
        var role = actor.Role.Trim();
        if (!role.Equals("Owner", StringComparison.OrdinalIgnoreCase)
            && !role.Equals("Accountant", StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkingPaperAccessDeniedException("Only Owner or Accountant users can generate retained working papers.");
        }
    }

    public static void ValidatePackHash(Pack pack)
    {
        if (!string.Equals(pack.OutputKind, OutputKind, StringComparison.Ordinal)
            || pack.IsFilingArtifact
            || pack.DirectSubmissionSupported
            || !pack.QualifiedAccountantReviewRequired
            || !string.Equals(pack.Identity.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
        {
            throw new BusinessRuleException("The retained working-paper artifact violates the support-only contract.");
        }

        var expected = Hash(pack with
        {
            Identity = pack.Identity with { ArtifactSha256 = string.Empty }
        });
        if (!string.Equals(pack.Identity.ArtifactSha256, expected, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("The retained working-paper artifact hash does not match its contents.");
    }

    private async Task<BuildData> LoadBuildDataAsync(
        int companyId,
        int periodId,
        CancellationToken cancellationToken)
    {
        var period = await db.AccountingPeriods
            .AsNoTracking()
            .Include(item => item.Company)
            .FirstOrDefaultAsync(item => item.Id == periodId && item.CompanyId == companyId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");
        var statementSources = await statements.GetStatementSourcesAsync(companyId, periodId);
        var profitAndLoss = await statements.GetProfitAndLossAsync(companyId, periodId);
        var balanceSheet = await statements.GetBalanceSheetAsync(companyId, periodId);
        var readiness = await statements.GetReadinessScoreAsync(companyId, periodId);
        var taxSupport = await tax.GetCt1SupportDataAsync(companyId, periodId);
        var transactions = await db.ImportedTransactions
            .AsNoTracking()
            .Include(item => item.BankAccount)
            .Include(item => item.ImportBatch)
            .Include(item => item.Category)
            .Where(item => item.PeriodId == periodId)
            .OrderBy(item => item.Date)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
        var adjustments = await db.Adjustments
            .AsNoTracking()
            .Include(item => item.DebitCategory)
            .Include(item => item.CreditCategory)
            .Where(item => item.PeriodId == periodId)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
        var openings = await db.OpeningBalances
            .AsNoTracking()
            .Include(item => item.AccountCategory)
            .Where(item => item.PeriodId == periodId)
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken);
        var banks = await db.BankAccounts
            .AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken);
        var confirmations = await db.YearEndReviewConfirmations
            .AsNoTracking()
            .Where(item => item.PeriodId == periodId)
            .OrderBy(item => item.SectionKey)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
        var yearEndFacts = await LoadYearEndFactSourcesAsync(companyId, period, cancellationToken);
        var accountSources = BuildAccountSources(period, transactions, adjustments, openings, banks, yearEndFacts, statementSources);
        var sourceHash = Hash(new
        {
            period.Id,
            period.CompanyId,
            Company = new
            {
                period.Company.TenantId,
                period.Company.LegalName,
                period.Company.CroNumber,
                period.Company.TaxReference,
                period.Company.CompanyType,
                period.Company.IncorporationDate
            },
            period.PeriodStart,
            period.PeriodEnd,
            StatementSources = statementSources,
            ProfitAndLoss = profitAndLoss,
            BalanceSheet = balanceSheet,
            Readiness = readiness,
            TaxCalculationSha256 = taxSupport.CalculationSha256,
            BankAccounts = banks.Select(item => new
            {
                item.Id,
                item.Name,
                item.Currency,
                item.OpeningBalance,
                item.OpeningBalanceDate
            }),
            Transactions = transactions.Select(item => new
            {
                item.Id, item.Date, item.Amount, item.BankAccountId, item.ImportBatchId, item.CategoryId,
                item.ConfidenceScore, item.ManualOverride, item.IsDuplicate, item.DuplicateReviewStatus,
                item.DuplicateDecisionVersion, item.DuplicateDecisionAtUtc
            }),
            Adjustments = adjustments.Select(item => new
            {
                item.Id, item.DebitCategoryId, item.CreditCategoryId, item.Amount, item.ApprovedBy,
                item.ApprovedAt, item.Reason, item.LegalBasis
            }),
            Openings = openings.Select(item => new
            {
                item.Id, item.AccountCategoryId, item.Debit, item.Credit, item.SourceNote,
                item.Reviewed, item.ReviewedBy, item.ReviewedAt
            }),
            YearEndFacts = yearEndFacts,
            Confirmations = confirmations.Select(item => new
            {
                item.Id, item.SectionKey, item.Confirmed, item.ConfirmedBy, item.ConfirmedAt, item.Note
            })
        });
        return new BuildData(
            period,
            period.Company,
            statementSources,
            profitAndLoss,
            balanceSheet,
            readiness,
            taxSupport,
            transactions,
            adjustments,
            openings,
            banks,
            confirmations,
            yearEndFacts,
            accountSources,
            sourceHash);
    }

    protected virtual Task WriteAuditAsync(
        int companyId,
        int periodId,
        Report report,
        Pack pack,
        AuthenticatedUser actor,
        string? requestId,
        CancellationToken cancellationToken) =>
        audit.LogAsync(
            companyId,
            periodId,
            nameof(Report),
            report.Id,
            AuditEventCodes.AccountantWorkingPaperPackGenerated,
            newValue: new
            {
                pack.Identity.ArtifactVersion,
                pack.Identity.ArtifactSha256,
                pack.Identity.SourceDataSha256,
                pack.Identity.ReleaseCandidate,
                pack.Identity.GeneratedByDisplayName,
                pack.Identity.GeneratedByRole,
                OutputKind
            },
            userId: AuthenticatedIdentity.AuditUserId(actor),
            requestId: requestId,
            actorDisplayName: AuthenticatedIdentity.ReviewerDisplayName(actor),
            cancellationToken: cancellationToken);

    private Pack BuildPack(
        BuildData data,
        AuthenticatedUser actor,
        string candidate,
        int version,
        DateTime generatedAtUtc)
    {
        var lead = BuildLeadSchedules(data);
        var categorized = BuildCategorizedTransactions(data);
        var adjusted = BuildAdjustedTrialBalance(data);
        var taxBridge = BuildTaxBridge(data);
        var exceptions = BuildReviewExceptions(data, lead, adjusted, taxBridge);
        var prefix = $"/api/companies/{data.Company.Id}/periods/{data.Period.Id}/working-papers";
        var index = WithHash(new WorkingPaperIndexArtifact(
            "working-paper-index",
            [
                new("lead-schedules", "Lead schedules", $"{prefix}/lead-schedules", lead.Rows.Count, SectionStatus(lead.Reconciliations), lead.ArtifactSha256),
                new("categorized-transactions", "Categorized transactions", $"{prefix}/categorized-transactions", categorized.Rows.Count, categorized.UncategorisedCount == 0 ? "ready" : "review-required", categorized.ArtifactSha256),
                new("review-exceptions", "Review exceptions", $"{prefix}/review-exceptions", exceptions.Items.Count, exceptions.BlockingCount == 0 ? "ready" : "blocked", exceptions.ArtifactSha256),
                new("adjusted-trial-balance", "Adjusted trial balance", $"{prefix}/adjusted-trial-balance", adjusted.Rows.Count, SectionStatus(adjusted.Reconciliations), adjusted.ArtifactSha256),
                new("corporation-tax-bridge", "Corporation-tax bridge", $"{prefix}/corporation-tax-bridge", taxBridge.Rows.Count, taxBridge.BlockingReasons.Count == 0 ? "review-required" : "blocked", taxBridge.ArtifactSha256)
            ],
            string.Empty));
        var identity = new ArtifactIdentity(
            SchemaVersion,
            version,
            actor.TenantId,
            data.Company.Id,
            data.Company.LegalName,
            data.Period.Id,
            data.Period.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            data.Period.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            AuthenticatedIdentity.AuditUserId(actor),
            AuthenticatedIdentity.ReviewerDisplayName(actor),
            actor.Role.Trim(),
            generatedAtUtc,
            candidate,
            data.SourceDataSha256,
            string.Empty);
        var pack = new Pack(
            OutputKind,
            IsFilingArtifact: false,
            DirectSubmissionSupported: false,
            QualifiedAccountantReviewRequired: true,
            Warning,
            identity,
            lead,
            categorized,
            exceptions,
            adjusted,
            index,
            taxBridge);
        return pack with
        {
            Identity = identity with { ArtifactSha256 = Hash(pack) }
        };
    }

    private static LeadSchedulesArtifact BuildLeadSchedules(BuildData data)
    {
        var rows = data.StatementSources.Select(source => new LeadScheduleRow(
            source.Code,
            source.Name,
            source.Type,
            source.OpeningDebit,
            source.OpeningCredit,
            source.TransactionDebit,
            source.TransactionCredit,
            source.AdjustmentDebit,
            source.AdjustmentCredit,
            source.ClosingDebit,
            source.ClosingCredit,
            data.AccountSources.TryGetValue(source.Code, out var references) ? references : [])).ToList();
        var reconciliations = new List<Reconciliation>
        {
            Reconcile("lead-schedule-debits-credits", "Lead-schedule closing debits equal closing credits.", rows.Sum(item => item.ClosingDebit), rows.Sum(item => item.ClosingCredit)),
            Reconcile("lead-schedule-trial-balance", "Lead schedules reproduce the adjusted trial-balance totals.", rows.Sum(item => item.ClosingDebit), data.StatementSources.Sum(item => item.ClosingDebit))
        };
        return WithHash(new LeadSchedulesArtifact("lead-schedules", rows, reconciliations, string.Empty));
    }

    private static CategorizedTransactionsArtifact BuildCategorizedTransactions(BuildData data)
    {
        var rows = data.Transactions.Select(item =>
        {
            var source = Source(
                "imported-transaction",
                item.Id,
                data.Period.Id,
                item.Description,
                item.Amount,
                item.ImportBatch?.SourceFileSha256,
                item.DuplicateDecisionByDisplayName,
                item.DuplicateDecisionAtUtc,
                $"/companies/{data.Company.Id}/periods/{data.Period.Id}/categorise?transactionId={item.Id}");
            return new CategorizedTransactionRow(
                item.Id,
                item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                item.Description,
                item.Amount,
                item.BankAccountId,
                item.BankAccount.Name,
                item.ImportBatchId,
                item.ImportBatch?.Filename,
                item.ImportBatch?.SourceFileSha256,
                item.CategoryId,
                item.Category?.Code,
                item.Category?.Name,
                item.CategoryId is null ? "uncategorised" : "categorised",
                item.ConfidenceScore,
                item.ManualOverride,
                !item.IsDuplicate,
                item.DuplicateReviewStatus.ToString(),
                item.DuplicateDecisionByDisplayName,
                item.DuplicateDecisionAtUtc,
                [source]);
        }).ToList();
        return WithHash(new CategorizedTransactionsArtifact(
            "categorized-transactions",
            rows.Count,
            rows.Count(item => item.CategoryId is not null),
            rows.Count(item => item.CategoryId is null),
            data.Transactions.Where(item => !item.IsDuplicate).Sum(item => item.Amount),
            rows,
            string.Empty));
    }

    private static AdjustedTrialBalanceArtifact BuildAdjustedTrialBalance(BuildData data)
    {
        var rows = data.StatementSources.Select(source => new AdjustedTrialBalanceRow(
            source.Code,
            source.Name,
            source.Type,
            source.OpeningDebit + source.TransactionDebit,
            source.OpeningCredit + source.TransactionCredit,
            source.AdjustmentDebit,
            source.AdjustmentCredit,
            source.ClosingDebit,
            source.ClosingCredit,
            data.AccountSources.TryGetValue(source.Code, out var references) ? references : [])).ToList();
        var reconciliations = new List<Reconciliation>
        {
            Reconcile("unadjusted-trial-balance", "Unadjusted debits equal unadjusted credits.", rows.Sum(item => item.UnadjustedDebit), rows.Sum(item => item.UnadjustedCredit)),
            Reconcile("journal-balance", "Journal debits equal journal credits.", rows.Sum(item => item.JournalDebit), rows.Sum(item => item.JournalCredit)),
            Reconcile("adjusted-trial-balance", "Adjusted debits equal adjusted credits.", rows.Sum(item => item.AdjustedDebit), rows.Sum(item => item.AdjustedCredit)),
            Reconcile("statement-trial-balance", "Adjusted trial balance exactly matches the statement ledger.", rows.Sum(item => item.AdjustedDebit), data.StatementSources.Sum(item => item.ClosingDebit))
        };
        return WithHash(new AdjustedTrialBalanceArtifact(
            "adjusted-trial-balance",
            rows.Sum(item => item.UnadjustedDebit),
            rows.Sum(item => item.UnadjustedCredit),
            rows.Sum(item => item.JournalDebit),
            rows.Sum(item => item.JournalCredit),
            rows.Sum(item => item.AdjustedDebit),
            rows.Sum(item => item.AdjustedCredit),
            rows,
            reconciliations,
            string.Empty));
    }

    private static CorporationTaxBridgeArtifact BuildTaxBridge(BuildData data)
    {
        var support = data.TaxSupport;
        var taxSources = data.YearEndFacts
            .Where(item => item.SourceType is "corporation-tax-scope-review" or "corporation-tax-loss-record"
                or "capital-allowance-claim" or "fixed-asset" or "payroll-summary" or "tax-balance")
            .ToList();
        var rows = new List<CorporationTaxBridgeRow>
        {
            new("accounting-profit", "Accounting profit before tax", support.NetProfit, "Financial statements", data.AccountSources.Values.SelectMany(item => item).DistinctBy(item => (item.SourceType, item.EntityId)).ToList()),
            new("tax-adjustments", "Net tax adjustments", support.Adjustments.Sum(item => item.Amount), "Corporation-tax adjustment bridge", taxSources),
            new("capital-allowances", "Capital allowances", -support.CapitalAllowances, "Reviewed fixed-asset and capital-allowance schedules", taxSources),
            new("trading-loss-used", "Same-trade brought-forward loss used", -support.TradingLossUsed, "Retained corporation-tax loss ledger", taxSources),
            new("passive-income", "Passive/non-trading income", support.PassiveNonTradingIncome, "Reviewed income classification", taxSources),
            new("taxable-profit", "Supported taxable profit", support.TaxableProfit, "Bounded corporation-tax calculation", taxSources),
            new("corporation-tax", "Supported corporation tax due", support.TaxDue, "12.5%/25% supported streams", taxSources),
            new("preliminary-tax", "Preliminary tax recorded", -support.PreliminaryTaxPaid, "Retained tax balance/payment evidence", taxSources),
            new("balance-due", "Supported balance due", support.BalanceDue, "Corporation tax less preliminary tax", taxSources)
        };
        var reconciliations = new List<Reconciliation>
        {
            Reconcile("accounting-profit", "Tax bridge starts from the final statement profit before tax.", support.NetProfit, data.ProfitAndLoss.ProfitBeforeTax),
            Reconcile("taxable-streams", "Taxable profit equals supported trading and passive streams.", support.TaxableProfit, Math.Max(0m, support.TradingProfitAfterLossRelief) + Math.Max(0m, support.PassiveNonTradingIncome)),
            Reconcile("tax-balance", "Tax due less preliminary tax equals the supported balance.", support.BalanceDue, support.TaxDue - support.PreliminaryTaxPaid)
        };
        var blockers = support.BlockingReasons.ToList();
        if (support.IsCompleteCt1Return)
            blockers.Add("The underlying tax support unexpectedly claimed to be a complete CT1 return.");
        return WithHash(new CorporationTaxBridgeArtifact(
            "corporation-tax-bridge-not-ct1-return",
            IsCompleteCt1Return: false,
            DirectRosSubmissionSupported: false,
            QualifiedAccountantReviewRequired: true,
            support.CalculationSha256,
            support.NetProfit,
            support.TradingProfitBeforeLossRelief,
            support.CapitalAllowances,
            support.TradingLossUsed,
            support.TradingProfitAfterLossRelief,
            support.PassiveNonTradingIncome,
            support.TaxableProfit,
            support.TaxDue,
            support.PreliminaryTaxPaid,
            support.BalanceDue,
            rows,
            reconciliations,
            blockers.Distinct(StringComparer.Ordinal).ToList(),
            string.Empty));
    }

    private static ReviewExceptionsArtifact BuildReviewExceptions(
        BuildData data,
        LeadSchedulesArtifact lead,
        AdjustedTrialBalanceArtifact adjusted,
        CorporationTaxBridgeArtifact taxBridge)
    {
        var items = new List<ReviewException>();
        foreach (var transaction in data.Transactions.Where(item => item.CategoryId is null))
        {
            items.Add(Exception(
                "uncategorised-transaction",
                "blocking",
                $"Transaction #{transaction.Id} is not categorised.",
                $"/companies/{data.Company.Id}/periods/{data.Period.Id}/categorise?transactionId={transaction.Id}",
                Source("imported-transaction", transaction.Id, data.Period.Id, transaction.Description, transaction.Amount, transaction.ImportBatch?.SourceFileSha256, null, null, $"/companies/{data.Company.Id}/periods/{data.Period.Id}/categorise?transactionId={transaction.Id}")));
        }
        foreach (var transaction in data.Transactions.Where(item => item.DuplicateReviewStatus == DuplicateReviewStatus.Pending))
        {
            items.Add(Exception(
                "pending-duplicate-review",
                "blocking",
                $"Duplicate candidate transaction #{transaction.Id} requires an explicit retain/discard decision.",
                $"/companies/{data.Company.Id}/periods/{data.Period.Id}/import",
                Source("imported-transaction", transaction.Id, data.Period.Id, transaction.Description, transaction.Amount, transaction.ImportBatch?.SourceFileSha256, null, null, $"/companies/{data.Company.Id}/periods/{data.Period.Id}/import")));
        }
        foreach (var adjustment in data.Adjustments.Where(item => item.ApprovedAt is null))
        {
            items.Add(Exception(
                "unapproved-journal",
                "blocking",
                $"Journal #{adjustment.Id} has not been approved.",
                $"/companies/{data.Company.Id}/periods/{data.Period.Id}/adjustments",
                AdjustmentSource(data, adjustment)));
        }
        foreach (var opening in data.OpeningBalances.Where(item => !item.Reviewed))
        {
            items.Add(Exception(
                "unreviewed-opening-balance",
                "blocking",
                $"Opening balance #{opening.Id} has not been reviewed.",
                $"/companies/{data.Company.Id}/periods/{data.Period.Id}?tab=opening-balances",
                OpeningSource(data, opening)));
        }
        items.AddRange(data.Readiness.MissingItems.Select(message => Exception(
            "statement-readiness",
            "blocking",
            message,
            $"/companies/{data.Company.Id}/periods/{data.Period.Id}/year-end")));
        items.AddRange(data.Readiness.Warnings.Select(message => Exception(
            "statement-warning",
            "warning",
            message,
            $"/companies/{data.Company.Id}/periods/{data.Period.Id}/statements")));
        items.AddRange(lead.Rows
            .Where(item => (item.ClosingDebit != 0m || item.ClosingCredit != 0m) && item.Sources.Count == 0)
            .Select(item => Exception(
                "missing-lead-schedule-source",
                "blocking",
                $"Account {item.Code} has a closing figure without a structured drill-down source.",
                $"/companies/{data.Company.Id}/periods/{data.Period.Id}/statements")));
        items.AddRange(taxBridge.Rows
            .Where(item => item.Amount != 0m && item.Sources.Count == 0)
            .Select(item => Exception(
                "missing-tax-bridge-source",
                "blocking",
                $"Corporation-tax bridge item {item.Code} has a figure without retained source evidence.",
                $"/companies/{data.Company.Id}/periods/{data.Period.Id}/year-end")));
        items.AddRange(lead.Reconciliations.Where(item => !item.Reconciles).Select(item => Exception(
            item.Code,
            "blocking",
            item.Description,
            $"/companies/{data.Company.Id}/periods/{data.Period.Id}/working-papers")));
        items.AddRange(adjusted.Reconciliations.Where(item => !item.Reconciles).Select(item => Exception(
            item.Code,
            "blocking",
            item.Description,
            $"/companies/{data.Company.Id}/periods/{data.Period.Id}/working-papers")));
        items.AddRange(taxBridge.BlockingReasons.Select(message => Exception(
            "corporation-tax-review",
            "blocking",
            message,
            $"/companies/{data.Company.Id}/periods/{data.Period.Id}/year-end")));
        items = items
            .DistinctBy(item => (item.Code, item.Message))
            .OrderBy(item => item.Severity == "blocking" ? 0 : 1)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .ThenBy(item => item.Message, StringComparer.Ordinal)
            .ToList();
        return WithHash(new ReviewExceptionsArtifact(
            "review-exceptions",
            items.Count(item => item.Severity == "blocking"),
            items.Count(item => item.Severity == "warning"),
            items,
            string.Empty));
    }

    private async Task<IReadOnlyList<SourceReference>> LoadYearEndFactSourcesAsync(
        int companyId,
        AccountingPeriod period,
        CancellationToken cancellationToken)
    {
        var route = $"/companies/{companyId}/periods/{period.Id}/year-end";
        var facts = new List<SourceReference>();
        facts.AddRange((await db.Debtors.AsNoTracking().Where(item => item.PeriodId == period.Id).ToListAsync(cancellationToken))
            .Select(item => Source("debtor", item.Id, period.Id, $"{item.Name} [{item.Type}]", item.Amount, item.Notes, null, null, route)));
        facts.AddRange((await db.Creditors.AsNoTracking().Where(item => item.PeriodId == period.Id).ToListAsync(cancellationToken))
            .Select(item => Source("creditor", item.Id, period.Id, $"{item.Name} [{item.Type}]", item.Amount, item.Notes, null, null, route)));
        facts.AddRange((await db.Inventories.AsNoTracking().Where(item => item.PeriodId == period.Id).ToListAsync(cancellationToken))
            .Select(item => Source("inventory", item.Id, period.Id, item.Description, item.Value, item.ValuationMethod.ToString(), null, null, route)));
        facts.AddRange((await db.FixedAssets.AsNoTracking()
                .Where(item => item.CompanyId == companyId && item.AcquisitionDate <= period.PeriodEnd)
                .ToListAsync(cancellationToken))
            .Select(item => Source("fixed-asset", item.Id, period.Id, $"{item.Name} [{item.Category}]", item.Cost, item.CapitalAllowanceEvidence, item.CapitalAllowanceReviewedBy, item.CapitalAllowanceReviewedAtUtc, route)));
        facts.AddRange((await db.CapitalAllowanceClaims.AsNoTracking().Where(item => item.PeriodId == period.Id).ToListAsync(cancellationToken))
            .Select(item => Source("capital-allowance-claim", item.Id, period.Id, $"Asset #{item.AssetId} capital allowance", item.Claim, $"Cost basis {item.Cost:0.00}", null, null, route)));
        facts.AddRange((await db.LoanBalanceSnapshots.AsNoTracking().Include(item => item.Loan).Where(item => item.PeriodId == period.Id).ToListAsync(cancellationToken))
            .Select(item => Source("loan-balance-snapshot", item.Id, period.Id, item.Loan.Lender, item.ClosingBalance, item.Notes, item.EnteredBy, item.EnteredAt, route)));
        facts.AddRange((await db.PayrollSummaries.AsNoTracking().Where(item => item.PeriodId == period.Id).ToListAsync(cancellationToken))
            .Select(item => Source("payroll-summary", item.Id, period.Id, "Payroll and directors' remuneration", item.GrossWages + item.DirectorsFees + item.EmployerPrsi + item.PensionContributions, $"Staff count {item.StaffCount}", null, null, route)));
        facts.AddRange((await db.TaxBalances.AsNoTracking().Where(item => item.PeriodId == period.Id).ToListAsync(cancellationToken))
            .Select(item => Source("tax-balance", item.Id, period.Id, item.TaxType.ToString(), item.Balance, $"Liability {item.Liability:0.00}; paid {item.Paid:0.00}", null, null, route)));
        facts.AddRange((await db.Dividends.AsNoTracking().Where(item => item.PeriodId == period.Id).ToListAsync(cancellationToken))
            .Select(item => Source("dividend", item.Id, period.Id, "Dividend", item.Amount, $"Declared {item.DateDeclared:yyyy-MM-dd}; paid {item.DatePaid:yyyy-MM-dd}", null, null, route)));
        facts.AddRange((await db.CorporationTaxScopeReviews.AsNoTracking().Where(item => item.PeriodId == period.Id).ToListAsync(cancellationToken))
            .Select(item => Source("corporation-tax-scope-review", item.Id, period.Id, "Corporation-tax scope review", null, item.EvidenceNote, item.PreparedBy, item.PreparedAtUtc, route)));
        facts.AddRange((await db.CorporationTaxLossRecords.AsNoTracking().Where(item => item.PeriodId == period.Id).ToListAsync(cancellationToken))
            .Select(item => Source("corporation-tax-loss-record", item.Id, period.Id, "Corporation-tax loss ledger", item.ClosingTradingLoss, $"Calculation SHA-256 {item.CalculationSha256}", item.RecordedBy, item.RecordedAtUtc, route)));
        return facts.OrderBy(item => item.SourceType).ThenBy(item => item.EntityId).ToList();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<SourceReference>> BuildAccountSources(
        AccountingPeriod period,
        IReadOnlyList<ImportedTransaction> transactions,
        IReadOnlyList<Adjustment> adjustments,
        IReadOnlyList<OpeningBalance> openings,
        IReadOnlyList<BankAccount> banks,
        IReadOnlyList<SourceReference> yearEndFacts,
        IReadOnlyList<FinancialStatementsService.StatementSourceSummary> statementSources)
    {
        var result = statementSources.ToDictionary(
            item => item.Code,
            _ => new List<SourceReference>(),
            StringComparer.Ordinal);
        void Add(string? code, SourceReference source)
        {
            if (code is not null && result.TryGetValue(code, out var sources))
                sources.Add(source);
        }
        foreach (var opening in openings)
            Add(opening.AccountCategory.Code, OpeningSource(period.Company, period, opening));
        foreach (var bank in banks.Where(item => item.OpeningBalance != 0 && item.OpeningBalanceDate <= period.PeriodStart))
            Add("1400", Source("bank-opening", bank.Id, period.Id, bank.Name, bank.OpeningBalance, $"Opening date {bank.OpeningBalanceDate:yyyy-MM-dd}", null, null, $"/companies/{period.CompanyId}/periods/{period.Id}?tab=opening-balances"));
        foreach (var transaction in transactions.Where(item => item.CategoryId is not null && !item.IsDuplicate))
        {
            var reference = Source("imported-transaction", transaction.Id, period.Id, transaction.Description, transaction.Amount, transaction.ImportBatch?.SourceFileSha256, transaction.DuplicateDecisionByDisplayName, transaction.DuplicateDecisionAtUtc, $"/companies/{period.CompanyId}/periods/{period.Id}/categorise?transactionId={transaction.Id}");
            Add(transaction.Category?.Code, reference);
            Add("1400", reference);
        }
        foreach (var adjustment in adjustments)
        {
            var reference = AdjustmentSource(period.Company, period, adjustment);
            Add(adjustment.DebitCategory?.Code, reference);
            Add(adjustment.CreditCategory?.Code, reference);
        }
        foreach (var fact in yearEndFacts)
        {
            Add(FactAccountCode(fact.SourceType, fact.Label), fact);
        }
        foreach (var source in statementSources)
        {
            const string priorPrefix = "Carried from exact prior period #";
            var priorNote = source.SourceNotes.FirstOrDefault(note => note.StartsWith(priorPrefix, StringComparison.Ordinal));
            if (priorNote is not null)
            {
                var idText = new string(priorNote[priorPrefix.Length..].TakeWhile(char.IsDigit).ToArray());
                if (!int.TryParse(idText, CultureInfo.InvariantCulture, out var priorPeriodId))
                    throw new BusinessRuleException($"Prior-period source identity for account {source.Code} is malformed.");
                Add(source.Code, Source(
                    "prior-period-ledger",
                    priorPeriodId,
                    priorPeriodId,
                    $"Prior-period roll-forward for {source.Code}",
                    source.ClosingDebit - source.ClosingCredit,
                    priorNote,
                    null,
                    null,
                    $"/companies/{period.CompanyId}/periods/{priorPeriodId}/working-papers"));
            }
        }
        return result.ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<SourceReference>)item.Value
                .DistinctBy(source => (source.SourceType, source.EntityId))
                .OrderBy(source => source.SourceType)
                .ThenBy(source => source.EntityId)
                .ToList(),
            StringComparer.Ordinal);
    }

    private static string? FactAccountCode(string sourceType, string label) => sourceType switch
    {
        "inventory" => "1000",
        "debtor" when label.Contains("prepayment", StringComparison.OrdinalIgnoreCase) => "1200",
        "debtor" => "1100",
        "creditor" when label.Contains("accrual", StringComparison.OrdinalIgnoreCase) => "2100",
        "creditor" => "2000",
        "loan-balance-snapshot" => "2700",
        "tax-balance" when label.Contains("VAT", StringComparison.OrdinalIgnoreCase) => "2200",
        "tax-balance" => "2400",
        "dividend" => "3200",
        "fixed-asset" when label.Contains("Land & Buildings", StringComparison.OrdinalIgnoreCase)
            || label.Contains("Property", StringComparison.OrdinalIgnoreCase) => "0010",
        "fixed-asset" when label.Contains("Plant & Machinery", StringComparison.OrdinalIgnoreCase) => "0020",
        "fixed-asset" when label.Contains("Motor Vehicles", StringComparison.OrdinalIgnoreCase)
            || label.Contains("Vehicles", StringComparison.OrdinalIgnoreCase) => "0030",
        "fixed-asset" when label.Contains("[Computer Equipment]", StringComparison.OrdinalIgnoreCase)
            || label.Contains("[IT]", StringComparison.OrdinalIgnoreCase) => "0050",
        "fixed-asset" => "0040",
        _ => null
    };

    private static SourceReference OpeningSource(BuildData data, OpeningBalance opening) =>
        OpeningSource(data.Company, data.Period, opening);

    private static SourceReference OpeningSource(Company company, AccountingPeriod period, OpeningBalance opening) =>
        Source(
            "opening-balance",
            opening.Id,
            period.Id,
            opening.AccountCategory.Name,
            opening.Debit - opening.Credit,
            opening.SourceNote,
            opening.ReviewedBy,
            opening.ReviewedAt,
            $"/companies/{company.Id}/periods/{period.Id}?tab=opening-balances");

    private static SourceReference AdjustmentSource(BuildData data, Adjustment adjustment) =>
        AdjustmentSource(data.Company, data.Period, adjustment);

    private static SourceReference AdjustmentSource(Company company, AccountingPeriod period, Adjustment adjustment) =>
        Source(
            "journal",
            adjustment.Id,
            period.Id,
            adjustment.Description,
            adjustment.Amount,
            string.Join(" | ", new[] { adjustment.Reason, adjustment.LegalBasis }.Where(value => !string.IsNullOrWhiteSpace(value))),
            adjustment.ApprovedBy,
            adjustment.ApprovedAt,
            $"/companies/{company.Id}/periods/{period.Id}/adjustments");

    private static SourceReference Source(
        string type,
        int entityId,
        int periodId,
        string label,
        decimal? amount,
        string? evidence,
        string? reviewer,
        DateTime? reviewedAt,
        string route) =>
        new(type, entityId, periodId, label, amount, evidence, reviewer, reviewedAt, route);

    private static ReviewException Exception(
        string code,
        string severity,
        string message,
        string route,
        params SourceReference[] sources) =>
        new(code, severity, message, route, sources);

    private static Reconciliation Reconcile(string code, string description, decimal left, decimal right)
    {
        var difference = decimal.Round(left - right, 2, MidpointRounding.AwayFromZero);
        return new Reconciliation(code, description, left, right, difference, Math.Abs(difference) < 0.01m);
    }

    private static string SectionStatus(IEnumerable<Reconciliation> reconciliations) =>
        reconciliations.All(item => item.Reconciles) ? "ready" : "blocked";

    private static T WithHash<T>(T artifact) where T : notnull
    {
        var property = typeof(T).GetProperty("ArtifactSha256")
            ?? throw new InvalidOperationException($"{typeof(T).Name} lacks ArtifactSha256.");
        var clone = JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(artifact, JsonOptions), JsonOptions)
            ?? throw new InvalidOperationException($"Unable to clone {typeof(T).Name}.");
        property.SetValue(clone, Hash(artifact));
        return clone;
    }

    private static string Hash<T>(T value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions))));
}

public sealed class WorkingPaperAccessDeniedException : Exception
{
    public WorkingPaperAccessDeniedException()
        : base("Internal accountant working papers require Owner, Accountant, or Reviewer access.")
    {
    }

    public WorkingPaperAccessDeniedException(string message)
        : base(message)
    {
    }
}
