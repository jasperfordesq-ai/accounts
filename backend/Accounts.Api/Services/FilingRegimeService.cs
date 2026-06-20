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

        var company = period.Company;
        var sizeClass = sc.OverrideClass ?? sc.CalculatedClass;
        var oldValue = period.FilingRegime is null ? null : FilingRegimeAuditSnapshot(period.FilingRegime);

        // Fifth Schedule ineligible entity check
        var isIneligible = company.IsListedSecurities || company.IsCreditInstitution
                        || company.IsInsuranceUndertaking || company.IsPensionFund;

        if (isIneligible) sizeClass = CompanySizeClass.Large;

        var microExcluded = isIneligible || company.IsHolding || company.IsInvestment || company.IsSubsidiary;
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
    {
        var notes = new List<string>();

        // All regimes
        notes.Add("Accounting policies");
        notes.Add("Basis of preparation");

        if (regime == ElectedRegime.Micro)
        {
            notes.Add("Statement of compliance with FRS 105");
            notes.Add("Advances, credits and guarantees to directors (if any)");
            // Micro entities have very limited note requirements
            return notes;
        }

        // Small and above
        notes.Add("Tangible fixed assets");
        notes.Add("Debtors");
        notes.Add("Creditors: amounts falling due within one year");
        notes.Add("Creditors: amounts falling due after more than one year");
        notes.Add("Share capital");
        notes.Add("Reserves");
        notes.Add("Approval of financial statements");

        if (company.IsEmployer)
        {
            notes.Add("Staff numbers and costs");
        }

        notes.Add("Directors' remuneration");

        if (regime == ElectedRegime.SmallAbridged)
        {
            // Abridged does not require P&L-related notes for CRO filing
            return notes;
        }

        // Full and medium add more notes
        notes.Add("Turnover analysis");
        notes.Add("Tax on profit");
        notes.Add("Dividends");

        if (company.IsGroupMember)
        {
            notes.Add("Related party transactions");
            notes.Add("Ultimate controlling party");
        }

        if (sizeClass >= CompanySizeClass.Medium)
        {
            notes.Add("Financial instruments");
            notes.Add("Contingent liabilities");
            notes.Add("Capital commitments");
            notes.Add("Post balance sheet events");
        }

        return notes;
    }

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
