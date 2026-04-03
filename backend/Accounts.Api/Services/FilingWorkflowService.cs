using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public class FilingWorkflowService(AccountsDbContext db, FinancialStatementsService statementsService)
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
        var period = await db.AccountingPeriods.Include(p => p.RevenueFilingPackage).FirstAsync(p => p.Id == periodId);
        var pkg = period.RevenueFilingPackage;
        if (pkg == null)
        {
            pkg = new RevenueFilingPackage { PeriodId = periodId };
            db.RevenueFilingPackages.Add(pkg);
        }

        // Basic iXBRL validation checks
        var errors = new List<string>();

        // Check required tags are present by verifying data exists
        var hasBS = await db.Adjustments.AnyAsync(a => a.PeriodId == periodId);
        if (!hasBS) errors.Add("No adjustments generated — balance sheet data may be incomplete");

        var hasTax = await db.TaxBalances.AnyAsync(t => t.PeriodId == periodId && t.TaxType == TaxType.CorporationTax);
        if (!hasTax) errors.Add("Corporation tax balance not entered");

        pkg.IxbrlGenerated = true;
        pkg.IxbrlValidated = errors.Count == 0;
        pkg.IxbrlValidationErrors = errors.Count > 0 ? string.Join("; ", errors) : null;
        if (pkg.FilingStatus == FilingStatus.NotStarted) pkg.FilingStatus = FilingStatus.InProgress;

        await db.SaveChangesAsync();
        return pkg;
    }
}
