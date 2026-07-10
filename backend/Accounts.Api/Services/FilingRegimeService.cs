using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Accounts.Api.Services;

public class FilingRegimeService
{
    private readonly AccountsDbContext db;
    private readonly DeadlineService deadlineService;
    private readonly AuditService? audit;

    public FilingRegimeService(AccountsDbContext db)
        : this(db, new DeadlineService(db), null)
    {
    }

    public FilingRegimeService(AccountsDbContext db, DeadlineService deadlineService, AuditService? audit = null)
    {
        this.db = db;
        this.deadlineService = deadlineService;
        this.audit = audit;
    }

    public record FilingRequirements(
        ElectedRegime Regime,
        bool CanUseMicro,
        bool CanFileAbridged,
        bool AuditExempt,
        List<string> RequiredStatements,
        List<string> RequiredNotes,
        string Summary
    );

    public async Task<FilingRequirements> DetermineAsync(int companyId, int periodId, ElectedRegime? electedRegime = null, string? userId = null)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company)
            .Include(p => p.SizeClassification)
            .Include(p => p.FilingRegime)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        var sc = period.SizeClassification
            ?? throw new BusinessRuleException("Size classification must be completed first.");
        if (string.IsNullOrWhiteSpace(sc.DecisionInputFingerprintSha256)
            || sc.ThresholdElectionEffectiveFrom is null)
            throw new BusinessRuleException("Run the statutory size classification against current raw inputs before determining a filing regime.");
        if (period.Company.IsHolding)
            throw new BusinessRuleException("Holding-company filing regime requires a retained statutory group-size assessment.");
        await SizeClassificationService.EnsureDecisionChainCurrentAsync(
            db,
            period,
            sc.ThresholdElectionEffectiveFrom.Value,
            includeTarget: true);
        ValidateCurrentOverride(sc);

        var company = period.Company;
        var sizeClass = sc.OverrideClass ?? sc.CalculatedClass;
        var oldValue = period.FilingRegime is null ? null : FilingRegimeAuditSnapshot(period.FilingRegime);

        // Fifth Schedule ineligible entity check
        var isIneligible = company.CompanyType == CompanyType.PublicLimitedCompany
                        || company.IsListedSecurities || company.IsCreditInstitution
                        || company.IsInsuranceUndertaking || company.IsPensionFund
                        || company.IsFifthScheduleEntity || company.IsOtherIneligibleEntity;

        if (isIneligible) sizeClass = CompanySizeClass.Large;

        var microExcluded = isIneligible
                         || company.IsInvestment
                         || company.IsFinancialHoldingUndertaking
                         || company.PreparesGroupFinancialStatements
                         || company.IncludedInHigherConsolidatedFinancialStatements
                         || company.IsSubsidiary;
        var canUseMicro = sizeClass == CompanySizeClass.Micro && !microExcluded;
        var canFileAbridged = !isIneligible && sizeClass <= CompanySizeClass.Small;

        // Audit exemption: check s.334 member notice and CRO late-filing loss.
        var auditExemptionJeopardy = await deadlineService.CheckAuditExemptionJeopardyAsync(company.Id);
        var lateCroFilingCount = auditExemptionJeopardy.LateFilingCount;
        var hasLostAuditExemption = auditExemptionJeopardy.HasLostExemption;
        var auditExempt = !isIneligible && sizeClass <= CompanySizeClass.Small && !company.IsGroupMember;
        if (auditExempt && period.MemberAuditNoticeReceived)
        {
            auditExempt = false; // s.334: member(s) served notice requiring audit
        }
        if (auditExempt && hasLostAuditExemption)
        {
            auditExempt = false;
        }

        // Default regime based on classification
        var regime = electedRegime ?? DetermineDefaultRegime(sizeClass, canUseMicro);
        AssertElectionCompatible(sizeClass, regime, canUseMicro, canFileAbridged, isIneligible);

        var requiredStatements = GetRequiredStatements(regime, sizeClass);
        var requiredNotes = GetRequiredNotes(regime, sizeClass, company);
        var summary = GenerateSummary(regime, canUseMicro, canFileAbridged, auditExempt, hasLostAuditExemption, lateCroFilingCount);

        // Save or update filing regime
        var fr = period.FilingRegime;
        if (fr == null)
        {
            fr = new FilingRegime { PeriodId = periodId };
            db.FilingRegimes.Add(fr);
        }
        fr.CanUseMicro = canUseMicro;
        fr.CanFileAbridged = canFileAbridged;
        fr.AuditExempt = auditExempt;
        fr.ElectedRegime = regime;
        fr.RequiredNotesJson = JsonSerializer.Serialize(requiredNotes);
        fr.RequiredStatementsJson = JsonSerializer.Serialize(requiredStatements);
        fr.DeterminedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        var result = new FilingRequirements(regime, canUseMicro, canFileAbridged, auditExempt, requiredStatements, requiredNotes, summary);
        if (audit is not null)
        {
            await audit.LogAsync(
                company.Id,
                periodId,
                "FilingRegime",
                fr.Id,
                AuditEventCodes.FilingRegimeDetermined,
                oldValue,
                FilingRegimeAuditSnapshot(fr),
                userId);
        }

        return result;
    }

    private static ElectedRegime DetermineDefaultRegime(CompanySizeClass sizeClass, bool canUseMicro)
    {
        if (canUseMicro) return ElectedRegime.Micro;
        return sizeClass switch
        {
            CompanySizeClass.Micro => ElectedRegime.Small, // excluded from micro, falls to small
            CompanySizeClass.Small => ElectedRegime.SmallAbridged,
            CompanySizeClass.Medium => ElectedRegime.Medium,
            CompanySizeClass.Large => ElectedRegime.Full,
            _ => ElectedRegime.Full
        };
    }

    public static bool IsElectionCompatible(CompanySizeClass sizeClass, ElectedRegime regime) => regime switch
    {
        ElectedRegime.Micro => sizeClass == CompanySizeClass.Micro,
        ElectedRegime.Small or ElectedRegime.SmallAbridged => sizeClass <= CompanySizeClass.Small,
        ElectedRegime.Medium => sizeClass == CompanySizeClass.Medium,
        ElectedRegime.Full => true,
        _ => false
    };

    private static void AssertElectionCompatible(
        CompanySizeClass sizeClass,
        ElectedRegime regime,
        bool canUseMicro,
        bool canFileAbridged,
        bool isIneligible)
    {
        var valid = regime switch
        {
            ElectedRegime.Micro => canUseMicro,
            ElectedRegime.Small => !isIneligible && sizeClass <= CompanySizeClass.Small,
            ElectedRegime.SmallAbridged => canFileAbridged,
            ElectedRegime.Medium => !isIneligible && sizeClass == CompanySizeClass.Medium,
            ElectedRegime.Full => true,
            _ => false
        };
        if (!valid)
        {
            throw new BusinessRuleException(
                $"The {regime} election is incompatible with the current {sizeClass} statutory classification and entity exclusions.");
        }
    }

    private static void ValidateCurrentOverride(SizeClassification sc)
    {
        if (sc.OverrideClass is null)
            return;
        if (!SizeClassificationService.HasCurrentOverrideEvidence(sc))
            throw new BusinessRuleException("The statutory classification override lacks current authority or retained evidence.");
    }

    public static List<string> GetRequiredStatements(ElectedRegime regime, CompanySizeClass sizeClass)
    {
        return regime switch
        {
            ElectedRegime.Micro => [
                "Balance Sheet (Statement of Financial Position)",
                "Statement under s.280D Companies Act 2014",
                "Notes to the Financial Statements"
            ],
            ElectedRegime.SmallAbridged => [
                "Balance Sheet (Statement of Financial Position)",
                "Notes to the Financial Statements",
                "Directors' Report",
                "Statement under s.352 Companies Act 2014 (abridged)"
            ],
            ElectedRegime.Small => [
                "Profit and Loss Account (Income Statement)",
                "Balance Sheet (Statement of Financial Position)",
                "Notes to the Financial Statements",
                "Directors' Report",
                "Audit Exemption Statement (if applicable)"
            ],
            ElectedRegime.Medium => [
                "Profit and Loss Account (Income Statement)",
                "Balance Sheet (Statement of Financial Position)",
                "Cash Flow Statement",
                "Statement of Changes in Equity",
                "Notes to the Financial Statements",
                "Directors' Report",
                "Auditor's Report"
            ],
            ElectedRegime.Full => [
                "Profit and Loss Account (Income Statement)",
                "Balance Sheet (Statement of Financial Position)",
                "Cash Flow Statement",
                "Statement of Changes in Equity",
                "Notes to the Financial Statements",
                "Directors' Report",
                "Auditor's Report"
            ],
            _ => ["Full Statutory Financial Statements"]
        };
    }

    private static List<string> GetRequiredNotes(ElectedRegime regime, CompanySizeClass sizeClass, Company company)
        => StatutoryNoteCodes.RegimeRequiredCodes(regime, sizeClass, company).ToList();

    private static string GenerateSummary(ElectedRegime regime, bool canUseMicro, bool canFileAbridged, bool auditExempt, bool hasLostAuditExemption, int lateCroFilingCount)
    {
        var parts = new List<string>();

        parts.Add($"Filing regime: {regime}");

        if (regime == ElectedRegime.Micro)
            parts.Add("Under the micro companies regime (FRS 105), only a balance sheet and limited notes are required for CRO filing.");
        else if (regime == ElectedRegime.SmallAbridged)
            parts.Add("Abridged accounts for CRO: balance sheet + notes (no P&L). Full accounts required for members.");
        else if (regime == ElectedRegime.Small)
            parts.Add("Full small company accounts: P&L + balance sheet + notes + directors' report.");
        else
            parts.Add("Full statutory accounts required including all primary statements.");

        if (auditExempt)
            parts.Add("Audit exemption is available under s.360 Companies Act 2014.");
        else if (hasLostAuditExemption)
            parts.Add($"Statutory audit is required because the company has {lateCroFilingCount} late CRO filings in the five-year audit-exemption lookback.");
        else
            parts.Add("Statutory audit is required.");

        if (canUseMicro && regime != ElectedRegime.Micro)
            parts.Add("Note: this company qualifies for the micro regime if elected.");

        if (canFileAbridged && regime != ElectedRegime.SmallAbridged)
            parts.Add("Note: abridged filing is available for CRO if elected.");

        return string.Join(" ", parts);
    }

    private static object FilingRegimeAuditSnapshot(FilingRegime fr) => new
    {
        fr.ElectedRegime,
        fr.CanUseMicro,
        fr.CanFileAbridged,
        fr.AuditExempt,
        fr.DeterminedAt,
        RequiredStatements = DeserializeStringList(fr.RequiredStatementsJson),
        RequiredNotes = DeserializeStringList(fr.RequiredNotesJson)
    };

    private static List<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
