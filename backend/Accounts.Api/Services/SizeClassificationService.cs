using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Services;

public class SizeClassificationService(AccountsDbContext db, IOptions<SizeThresholdConfig> thresholds)
{
    private readonly SizeThresholdConfig _config = thresholds.Value;

    public record ClassificationResult(
        CompanySizeClass CalculatedClass,
        string QualificationNotes,
        bool CanUseMicro,
        bool CanFileAbridged,
        bool AuditExempt,
        List<string> AvailableRegimes
    );

    public async Task<ClassificationResult> ClassifyAsync(int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company)
            .Include(p => p.SizeClassification)
            .FirstOrDefaultAsync(p => p.Id == periodId)
            ?? throw new InvalidOperationException($"Period {periodId} not found");

        var company = period.Company;

        // Get or create size classification
        var sc = period.SizeClassification;
        if (sc == null)
            throw new InvalidOperationException("Size classification data not yet entered for this period.");

        var turnover = sc.Turnover;
        var balanceSheet = sc.BalanceSheetTotal;
        var employees = sc.AvgEmployees;
        var isFirstYear = period.IsFirstYear;

        // Apply "2 out of 3" threshold test
        var currentClass = DetermineClass(turnover, balanceSheet, employees);

        // For subsequent years, check prior year too
        CompanySizeClass? priorClass = sc.PriorYearClass;
        CompanySizeClass effectiveClass;
        var notes = new List<string>();

        if (isFirstYear)
        {
            effectiveClass = currentClass;
            notes.Add($"First financial year — classified based on current year figures only.");
        }
        else if (priorClass.HasValue)
        {
            // Company must qualify for two consecutive years to change size class
            // The stricter (larger) classification applies until both years qualify
            effectiveClass = (CompanySizeClass)Math.Max((int)currentClass, (int)priorClass.Value);
            if (effectiveClass != currentClass)
            {
                notes.Add($"Current year qualifies as {currentClass}, but prior year was {priorClass.Value}.");
                notes.Add("Must meet thresholds for two consecutive years to change classification.");
            }
            else
            {
                notes.Add($"Qualifies as {currentClass} for both current and prior year.");
            }
        }
        else
        {
            effectiveClass = currentClass;
            notes.Add("No prior year classification available — using current year only.");
        }

        // Check micro exclusions
        bool microExcluded = company.IsHolding || company.IsInvestment || company.IsSubsidiary;
        bool canUseMicro = effectiveClass == CompanySizeClass.Micro && !microExcluded;

        if (effectiveClass == CompanySizeClass.Micro && microExcluded)
        {
            notes.Add("Micro regime excluded: company is a holding, investment, or subsidiary company.");
            notes.Add("Falls back to small company regime.");
        }

        // Determine available regimes
        var regimes = new List<string>();
        if (canUseMicro) regimes.Add("Micro (FRS 105)");
        if (effectiveClass <= CompanySizeClass.Small)
        {
            regimes.Add("Small — Full");
            regimes.Add("Small — Abridged");
        }
        if (effectiveClass == CompanySizeClass.Medium) regimes.Add("Medium");
        if (effectiveClass == CompanySizeClass.Large) regimes.Add("Full");

        // Audit exemption: s.360 Companies Act 2014
        // Available to small companies (not groups, not public interest entities)
        bool auditExempt = effectiveClass <= CompanySizeClass.Small && !company.IsGroupMember;
        if (auditExempt)
            notes.Add("Audit exemption available under s.360 Companies Act 2014.");
        else if (effectiveClass <= CompanySizeClass.Small && company.IsGroupMember)
            notes.Add("Audit exemption not available — company is part of a group.");
        else
            notes.Add("Audit required — company exceeds small company thresholds.");

        notes.Add($"Thresholds applied: Turnover €{turnover:N0}, BS €{balanceSheet:N0}, Employees {employees}");
        notes.Add($"Effective classification: {effectiveClass}");

        // Update the entity
        sc.CalculatedClass = effectiveClass;
        sc.QualificationNotes = string.Join("\n", notes);
        sc.CalculatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return new ClassificationResult(
            effectiveClass,
            string.Join("\n", notes),
            canUseMicro,
            effectiveClass <= CompanySizeClass.Small,
            auditExempt,
            regimes
        );
    }

    private CompanySizeClass DetermineClass(decimal turnover, decimal balanceSheet, int employees)
    {
        // Check micro (2 out of 3)
        if (MeetsTwoOfThree(turnover, balanceSheet, employees, _config.Micro))
            return CompanySizeClass.Micro;

        // Check small (2 out of 3)
        if (MeetsTwoOfThree(turnover, balanceSheet, employees, _config.Small))
            return CompanySizeClass.Small;

        // Check medium (2 out of 3)
        if (MeetsTwoOfThree(turnover, balanceSheet, employees, _config.Medium))
            return CompanySizeClass.Medium;

        return CompanySizeClass.Large;
    }

    private static bool MeetsTwoOfThree(decimal turnover, decimal balanceSheet, int employees, ThresholdSet thresholds)
    {
        int met = 0;
        if (turnover <= thresholds.Turnover) met++;
        if (balanceSheet <= thresholds.BalanceSheet) met++;
        if (employees <= thresholds.Employees) met++;
        return met >= 2;
    }
}
