using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Accounts.Api.Services;

public class FilingWorkflowService(AccountsDbContext db, FinancialStatementsService statementsService, IxbrlService ixbrlService)
{
    public record FilingWorkflowStatus(
        CroFilingStatus Cro,
        RevenueFilingStatus Revenue,
        List<string> BlockingIssues,
        bool ReadyToFile
    );

    public record CroFilingStatus(
        FilingStatus Status,
        bool AccountsPdfReady,
        bool SignaturePageReady,
        bool PaymentCompleted,
        string? SubmissionReference,
        string? RejectionReason,
        DateTime? CorrectionDeadline
    );

    public record RevenueFilingStatus(
        FilingStatus Status,
        bool IxbrlReady,
        bool IxbrlValid,
        string? ValidationErrors,
        string? Ct1Reference
    );

    public async Task<FilingWorkflowStatus> GetStatusAsync(int companyId, int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.CroFilingPackage)
            .Include(p => p.RevenueFilingPackage)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new InvalidOperationException($"Period {periodId} not found");

        var cro = period.CroFilingPackage;
        var rev = period.RevenueFilingPackage;

        var croStatus = new CroFilingStatus(
            cro?.FilingStatus ?? FilingStatus.NotStarted,
            cro?.AccountsPdfGenerated ?? false,
            cro?.SignaturePageGenerated ?? false,
            cro?.PaymentCompleted ?? false,
            cro?.CroSubmissionReference,
            cro?.RejectionReason,
            cro?.CorrectionDeadline
        );

        var revStatus = new RevenueFilingStatus(
            rev?.FilingStatus ?? FilingStatus.NotStarted,
            rev?.IxbrlGenerated ?? false,
            rev?.IxbrlValidated ?? false,
            rev?.IxbrlValidationErrors,
            rev?.Ct1Reference
        );

        // Check blocking issues
        var issues = new List<string>();
        try
        {
            var readiness = await statementsService.GetReadinessScoreAsync(periodId);
            if (!readiness.BalanceSheetBalances) issues.Add("Balance sheet does not balance");
            if (readiness.CompletenessPercent < 100) issues.Add($"Filing completeness only {readiness.CompletenessPercent}%");
            issues.AddRange(readiness.MissingItems);
        }
        catch
        {
            issues.Add("Readiness data unavailable");
        }

        if (!croStatus.AccountsPdfReady) issues.Add("CRO accounts PDF not generated");
        if (!croStatus.SignaturePageReady) issues.Add("CRO signature page not generated");
        if (croStatus.Status == FilingStatus.Submitted && !croStatus.PaymentCompleted)
            issues.Add("CORE payment has not been confirmed. The B1 is not complete until payment is made.");
        if (croStatus.Status == FilingStatus.CorrectionRequired && croStatus.CorrectionDeadline.HasValue && croStatus.CorrectionDeadline.Value < DateTime.UtcNow)
            issues.Add("CRO correction deadline has passed. Treat the filing as not delivered and recalculate late filing exposure.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var overdueDeadlines = await db.FilingDeadlines
            .Where(d => d.CompanyId == companyId && d.PeriodId == periodId && d.FiledDate == null && d.DueDate < today)
            .OrderBy(d => d.DueDate)
            .ToListAsync();

        foreach (var deadline in overdueDeadlines)
            issues.Add($"{deadline.DeadlineType} deadline passed on {deadline.DueDate:yyyy-MM-dd} and has not been marked as filed.");

        return new FilingWorkflowStatus(croStatus, revStatus, issues, issues.Count == 0);
    }

    public async Task<CroFilingPackage> UpdateCroStatusAsync(int periodId, FilingStatus status, string? by = null)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.CroFilingPackage)
            .FirstAsync(p => p.Id == periodId);

        var pkg = period.CroFilingPackage;
        if (pkg == null)
        {
            pkg = new CroFilingPackage { PeriodId = periodId };
            db.CroFilingPackages.Add(pkg);
        }

        pkg.FilingStatus = status;
        if (status == FilingStatus.Approved) { pkg.ApprovedBy = by; pkg.ApprovedAt = DateTime.UtcNow; }
        if (status == FilingStatus.Submitted) { pkg.SubmittedBy = by; pkg.SubmittedAt = DateTime.UtcNow; }
        if (status == FilingStatus.CorrectionRequired) { pkg.CorrectionDeadline = DateTime.UtcNow.AddDays(14); }

        await db.SaveChangesAsync();
        return pkg;
    }

    public async Task<CroFilingPackage> MarkDocumentGeneratedAsync(int periodId, string documentType)
    {
        var period = await db.AccountingPeriods.Include(p => p.CroFilingPackage).FirstAsync(p => p.Id == periodId);
        var pkg = period.CroFilingPackage;
        if (pkg == null)
        {
            pkg = new CroFilingPackage { PeriodId = periodId };
            db.CroFilingPackages.Add(pkg);
        }

        if (documentType == "accounts") pkg.AccountsPdfGenerated = true;
        if (documentType == "signature") pkg.SignaturePageGenerated = true;
        if (pkg.AccountsPdfGenerated && pkg.SignaturePageGenerated && pkg.FilingStatus == FilingStatus.NotStarted)
            pkg.FilingStatus = FilingStatus.PackageGenerated;

        await db.SaveChangesAsync();
        return pkg;
    }

    public async Task<RevenueFilingPackage> ValidateIxbrlAsync(int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company)
            .Include(p => p.RevenueFilingPackage)
            .FirstAsync(p => p.Id == periodId);
        var pkg = period.RevenueFilingPackage;
        if (pkg == null)
        {
            pkg = new RevenueFilingPackage { PeriodId = periodId };
            db.RevenueFilingPackages.Add(pkg);
        }

        // Internal iXBRL validation checks. This is not a substitute for ROS validation.
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(period.Company.CroNumber))
            errors.Add("Company CRO number is required for iXBRL entity identification");

        try
        {
            var bs = await statementsService.GetBalanceSheetAsync(periodId);
            if (!bs.Balances)
                errors.Add($"Balance sheet does not balance; unexplained difference {bs.CapitalAndReserves.UnexplainedDifference:C}");
        }
        catch
        {
            errors.Add("Balance sheet could not be generated");
        }

        var hasTax = await db.TaxBalances.AnyAsync(t => t.PeriodId == periodId && t.TaxType == TaxType.CorporationTax);
        if (!hasTax) errors.Add("Corporation tax balance not entered");

        try
        {
            var xhtml = Encoding.UTF8.GetString(await ixbrlService.GenerateIxbrlAsync(periodId));
            if (!xhtml.Contains("<ix:header>") || !xhtml.Contains("xmlns:xbrli="))
                errors.Add("Generated XHTML is missing required inline XBRL header/resources");
            if (!xhtml.Contains("External ROS/iXBRL validation remains required"))
                errors.Add("Generated file must carry the external ROS validation warning");
        }
        catch (Exception ex)
        {
            errors.Add($"iXBRL generation failed: {ex.Message}");
        }

        pkg.IxbrlGenerated = true;
        pkg.IxbrlValidated = errors.Count == 0;
        pkg.IxbrlValidationErrors = errors.Count > 0
            ? string.Join("; ", errors)
            : "Internal checks passed. External ROS/iXBRL validation is still required before Revenue filing.";
        if (pkg.FilingStatus == FilingStatus.NotStarted) pkg.FilingStatus = FilingStatus.InProgress;

        await db.SaveChangesAsync();
        return pkg;
    }
}
