using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Accounts.Api.Services;

public class FilingWorkflowService(
    AccountsDbContext db,
    FinancialStatementsService statementsService,
    IxbrlService ixbrlService,
    AuditService? audit = null,
    ILogger<FilingWorkflowService>? logger = null)
{
    public record FilingWorkflowStatus(
        CroFilingStatus Cro,
        RevenueFilingStatus Revenue,
        CharityFilingStatus Charity,
        List<string> BlockingIssues,
        List<string> WarningIssues,
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
        bool IxbrlInternalChecksPassed,
        bool IxbrlValid,
        string? ValidationErrors,
        string? Ct1Reference
    );

    public record CharityFilingStatus(
        FilingStatus Status,
        bool SofaGenerated,
        bool TrusteesReportGenerated,
        string? AnnualReturnReference,
        string? RejectionReason,
        DateTime? CorrectionDeadline,
        string? SubmittedBy,
        DateTime? SubmittedAt,
        string? AcceptedBy,
        DateTime? AcceptedAt
    );

    public async Task<FilingWorkflowStatus> GetStatusAsync(int companyId, int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.CroFilingPackage)
            .Include(p => p.RevenueFilingPackage)
            .Include(p => p.CharityFilingPackage)
            .Include(p => p.Company).ThenInclude(c => c.CharityInfo)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        var cro = period.CroFilingPackage;
        var rev = period.RevenueFilingPackage;
        var charity = period.CharityFilingPackage;

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
            InternalIxbrlChecksPassed(rev),
            rev?.IxbrlValidated ?? false,
            rev?.IxbrlValidationErrors,
            rev?.Ct1Reference
        );

        var charityStatus = new CharityFilingStatus(
            charity?.FilingStatus ?? FilingStatus.NotStarted,
            charity?.SofaGenerated ?? false,
            charity?.TrusteesReportGenerated ?? false,
            charity?.AnnualReturnReference,
            charity?.RejectionReason,
            charity?.CorrectionDeadline,
            charity?.SubmittedBy,
            charity?.SubmittedAt,
            charity?.AcceptedBy,
            charity?.AcceptedAt
        );

        // Check blocking issues
        var issues = new List<string>();
        var warnings = new List<string>();
        try
        {
            var readiness = await statementsService.GetReadinessScoreAsync(companyId, periodId);
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
        var hasActiveDirector = await db.CompanyOfficers.AnyAsync(o =>
            o.CompanyId == companyId && o.Role == OfficerRole.Director && o.ResignedDate == null);
        var hasActiveSecretary = await db.CompanyOfficers.AnyAsync(o =>
            o.CompanyId == companyId && (o.Role == OfficerRole.Secretary || o.Role == OfficerRole.CompanySecretary) && o.ResignedDate == null);
        if (!hasActiveDirector) issues.Add("No active director recorded for CRO accounts certification.");
        if (!hasActiveSecretary) issues.Add("No active company secretary recorded for CRO accounts certification.");
        if (croStatus.Status == FilingStatus.Submitted && !croStatus.PaymentCompleted)
            issues.Add("CORE payment has not been confirmed. The B1 is not complete until payment is made.");
        if (croStatus.Status == FilingStatus.CorrectionRequired && croStatus.CorrectionDeadline.HasValue && croStatus.CorrectionDeadline.Value < DateTime.UtcNow)
            issues.Add("CRO correction deadline has passed. Treat the filing as not delivered and recalculate late filing exposure.");

        if (period.Company.IsCharitableOrganisation)
        {
            if (string.IsNullOrWhiteSpace(period.Company.CharityInfo?.CharityNumber))
                issues.Add("Charity number is required for the Charities Regulator annual return.");
            var hasFundBalances = await db.FundBalances.AnyAsync(f => f.PeriodId == periodId);
            if (!hasFundBalances)
                issues.Add("Charity fund balances must be recorded before the annual return can be approved.");
            if (!charityStatus.SofaGenerated)
                issues.Add("Charity SoFA report not generated");
            if (!charityStatus.TrusteesReportGenerated)
                issues.Add("Trustees' Annual Report not generated");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var overdueDeadlines = await db.FilingDeadlines
            .Where(d => d.CompanyId == companyId && d.PeriodId == periodId && d.FiledDate == null && d.DueDate < today)
            .OrderBy(d => d.DueDate)
            .ToListAsync();

        foreach (var deadline in overdueDeadlines)
            warnings.Add($"{deadline.DeadlineType} deadline passed on {deadline.DueDate:yyyy-MM-dd}. Filing can still proceed, but late filing exposure and audit exemption impact must be reviewed.");

        return new FilingWorkflowStatus(croStatus, revStatus, charityStatus, issues, warnings, issues.Count == 0);
    }

    public async Task<CroFilingPackage> UpdateCroStatusAsync(int companyId, int periodId, FilingStatus status, string? by = null, string? reason = null, string? submissionReference = null, string? auditUserId = null)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.CroFilingPackage)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        var pkg = period.CroFilingPackage;
        if (pkg == null)
        {
            pkg = new CroFilingPackage { PeriodId = periodId };
            db.CroFilingPackages.Add(pkg);
        }
        var oldValue = CroFilingAuditSnapshot(pkg);
        string? coreSubmissionReference = null;

        if (status == FilingStatus.Approved)
        {
            var statusSnapshot = await GetStatusAsync(companyId, periodId);
            if (statusSnapshot.BlockingIssues.Count > 0)
                throw new BusinessRuleException($"Cannot approve CRO filing while blockers remain: {string.Join("; ", statusSnapshot.BlockingIssues.Distinct())}");
        }

        if (status == FilingStatus.Submitted)
        {
            if (pkg.FilingStatus != FilingStatus.Approved)
                throw new BusinessRuleException("Approve the CRO filing pack before marking it as submitted to CORE.");
            if (!pkg.AccountsPdfGenerated || !pkg.SignaturePageGenerated)
                throw new BusinessRuleException("Generate the CRO filing pack and signature page before submission.");

            coreSubmissionReference = NormalizeFilingReference(submissionReference ?? pkg.CroSubmissionReference, "CORE submission reference");

            await statementsService.AssertFinalOutputReadinessAsync(companyId, periodId, "CRO submission");

            var statusSnapshot = await GetStatusAsync(companyId, periodId);
            if (statusSnapshot.BlockingIssues.Count > 0)
                throw new BusinessRuleException($"Cannot submit CRO filing while blockers remain: {string.Join("; ", statusSnapshot.BlockingIssues.Distinct())}");
        }

        if (status == FilingStatus.Accepted)
        {
            if (pkg.FilingStatus != FilingStatus.Submitted)
                throw new BusinessRuleException("Only a submitted CRO filing can be marked as accepted.");
            if (!pkg.PaymentCompleted)
                throw new BusinessRuleException("Confirm CORE payment before marking the CRO filing as accepted.");
            pkg.CroSubmissionReference = NormalizeFilingReference(pkg.CroSubmissionReference, "CORE submission reference");
        }

        pkg.FilingStatus = status;
        if (status == FilingStatus.Approved) { pkg.ApprovedBy = by; pkg.ApprovedAt = DateTime.UtcNow; }
        if (status == FilingStatus.Submitted)
        {
            pkg.SubmittedBy = by;
            pkg.SubmittedAt = DateTime.UtcNow;
            pkg.CroSubmissionReference = coreSubmissionReference;
        }
        if (status == FilingStatus.CorrectionRequired)
        {
            pkg.RejectionReason = string.IsNullOrWhiteSpace(reason) ? "CRO send-back/correction required. Correct and redeliver within 14 days." : reason.Trim();
            pkg.CorrectionDeadline = DateTime.UtcNow.AddDays(14);
        }

        await db.SaveChangesAsync();
        if (audit is not null)
        {
            await audit.LogAsync(
                companyId,
                periodId,
                "CroFilingPackage",
                pkg.Id,
                AuditEventCodes.CroFilingStatusChanged,
                oldValue,
                CroFilingAuditSnapshot(pkg),
                auditUserId ?? by);
        }
        return pkg;
    }

    public async Task<CroFilingPackage> ConfirmCroPaymentAsync(int companyId, int periodId, string? by = null, string? auditUserId = null)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.CroFilingPackage)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        var pkg = period.CroFilingPackage
            ?? throw new BusinessRuleException("Generate and submit the CRO filing pack before confirming payment.");
        var oldValue = CroFilingAuditSnapshot(pkg);

        if (pkg.FilingStatus != FilingStatus.Submitted && pkg.FilingStatus != FilingStatus.Accepted)
            throw new BusinessRuleException("CORE payment can only be confirmed after the filing is marked as submitted.");

        pkg.PaymentCompleted = true;
        if (!string.IsNullOrWhiteSpace(by) && string.IsNullOrWhiteSpace(pkg.SubmittedBy))
            pkg.SubmittedBy = by.Trim();

        await db.SaveChangesAsync();
        if (audit is not null)
        {
            await audit.LogAsync(
                companyId,
                periodId,
                "CroFilingPackage",
                pkg.Id,
                AuditEventCodes.CroPaymentConfirmed,
                oldValue,
                CroFilingAuditSnapshot(pkg),
                auditUserId ?? by);
        }
        return pkg;
    }

    public Task<CroFilingPackage> MarkDocumentGeneratedAsync(int companyId, int periodId, string documentType, string? auditUserId = null) =>
        throw new BusinessRuleException("CRO document readiness is recorded only after the server generates the document. Download the CRO filing pack or signature page again.");

    public async Task<CroFilingPackage> RecordCroDocumentGeneratedAsync(int companyId, int periodId, string documentType, string? auditUserId = null)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.CroFilingPackage)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");
        var pkg = period.CroFilingPackage;
        if (pkg == null)
        {
            pkg = new CroFilingPackage { PeriodId = periodId };
            db.CroFilingPackages.Add(pkg);
        }
        var oldValue = CroFilingAuditSnapshot(pkg);

        if (documentType == "accounts") pkg.AccountsPdfGenerated = true;
        else if (documentType == "signature") pkg.SignaturePageGenerated = true;
        else throw new BusinessRuleException("Unknown CRO document type.");
        if (pkg.AccountsPdfGenerated && pkg.SignaturePageGenerated && pkg.FilingStatus == FilingStatus.NotStarted)
            pkg.FilingStatus = FilingStatus.PackageGenerated;

        await db.SaveChangesAsync();
        if (audit is not null)
        {
            await audit.LogAsync(
                companyId,
                periodId,
                "CroFilingPackage",
                pkg.Id,
                AuditEventCodes.CroDocumentGenerated,
                oldValue,
                new
                {
                    DocumentType = documentType,
                    Package = CroFilingAuditSnapshot(pkg)
                },
                auditUserId);
        }
        return pkg;
    }

    public async Task<RevenueFilingPackage> ValidateIxbrlAsync(int companyId, int periodId, string? auditUserId = null)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company)
            .Include(p => p.RevenueFilingPackage)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");
        var pkg = period.RevenueFilingPackage;
        if (pkg == null)
        {
            pkg = new RevenueFilingPackage { PeriodId = periodId };
            db.RevenueFilingPackages.Add(pkg);
        }
        var oldValue = RevenueFilingAuditSnapshot(pkg);

        // Internal iXBRL validation checks. This is not a substitute for ROS validation.
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(period.Company.CroNumber))
            errors.Add("Company CRO number is required for iXBRL entity identification");

        try
        {
            var bs = await statementsService.GetBalanceSheetAsync(companyId, periodId);
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
            var xhtml = Encoding.UTF8.GetString(await ixbrlService.GenerateIxbrlAsync(companyId, periodId));
            if (!xhtml.Contains("<ix:header>") || !xhtml.Contains("xmlns:xbrli="))
                errors.Add("Generated XHTML is missing required inline XBRL header/resources");
            if (!xhtml.Contains("External ROS/iXBRL validation remains required"))
                errors.Add("Generated file must carry the external ROS validation warning");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "iXBRL generation failed during internal validation for period {PeriodId}", periodId);
            errors.Add("iXBRL generation failed. Check server logs and retry.");
        }

        pkg.IxbrlGenerated = !errors.Any(e => e.StartsWith("iXBRL generation failed.", StringComparison.Ordinal));
        pkg.IxbrlValidated = false;
        pkg.IxbrlValidationErrors = errors.Count > 0
            ? string.Join("; ", errors)
            : "Internal checks passed. External ROS/iXBRL validation is still required before Revenue filing.";
        if (pkg.FilingStatus == FilingStatus.NotStarted) pkg.FilingStatus = FilingStatus.InProgress;

        await db.SaveChangesAsync();
        if (audit is not null)
        {
            await audit.LogAsync(
                period.CompanyId,
                periodId,
                "RevenueFilingPackage",
                pkg.Id,
                AuditEventCodes.IxbrlInternalCheckCompleted,
                oldValue,
                RevenueFilingAuditSnapshot(pkg),
                auditUserId);
        }
        return pkg;
    }

    public async Task<CharityFilingPackage> RecordCharityReportGeneratedAsync(int companyId, int periodId, string reportType, string? auditUserId = null)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company)
            .Include(p => p.CharityFilingPackage)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        if (!period.Company.IsCharitableOrganisation)
            throw new BusinessRuleException("Charity annual return reports can only be recorded for charitable organisations.");

        var pkg = period.CharityFilingPackage;
        if (pkg == null)
        {
            pkg = new CharityFilingPackage { PeriodId = periodId };
            db.CharityFilingPackages.Add(pkg);
        }
        var oldValue = CharityFilingAuditSnapshot(pkg);

        if (reportType == "sofa") pkg.SofaGenerated = true;
        else if (reportType is "trustees-report" or "tar") pkg.TrusteesReportGenerated = true;
        else throw new BusinessRuleException("Unknown charity report type.");

        if (pkg.SofaGenerated && pkg.TrusteesReportGenerated && pkg.FilingStatus == FilingStatus.NotStarted)
        {
            pkg.FilingStatus = FilingStatus.PackageGenerated;
            pkg.Status = FilingPackageStatus.Generated;
        }

        await db.SaveChangesAsync();
        if (audit is not null)
        {
            await audit.LogAsync(
                companyId,
                periodId,
                "CharityFilingPackage",
                pkg.Id,
                AuditEventCodes.CharityReportGenerated,
                oldValue,
                new
                {
                    ReportType = reportType,
                    Package = CharityFilingAuditSnapshot(pkg)
                },
                auditUserId);
        }
        return pkg;
    }

    public async Task<CharityFilingPackage> UpdateCharityStatusAsync(
        int companyId,
        int periodId,
        FilingStatus status,
        string? by = null,
        string? reason = null,
        string? annualReturnReference = null,
        string? auditUserId = null)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.CharityFilingPackage)
            .Include(p => p.Company).ThenInclude(c => c.CharityInfo)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        if (!period.Company.IsCharitableOrganisation)
            throw new BusinessRuleException("Charity annual return workflow can only be used for charitable organisations.");

        var pkg = period.CharityFilingPackage;
        if (pkg == null)
        {
            pkg = new CharityFilingPackage { PeriodId = periodId };
            db.CharityFilingPackages.Add(pkg);
        }
        var oldValue = CharityFilingAuditSnapshot(pkg);

        if (status == FilingStatus.Approved)
        {
            await AssertCharityAnnualReturnEvidenceAsync(period, pkg);
            pkg.ApprovedBy = by;
            pkg.ApprovedAt = DateTime.UtcNow;
        }

        if (status == FilingStatus.Submitted)
        {
            if (pkg.FilingStatus != FilingStatus.Approved)
                throw new BusinessRuleException("Approve the Charity annual return pack before marking it as submitted.");
            await AssertCharityAnnualReturnEvidenceAsync(period, pkg);
            await statementsService.AssertFinalOutputReadinessAsync(companyId, periodId, "Charity annual return");

            var reference = NormalizeFilingReference(annualReturnReference ?? pkg.AnnualReturnReference, "Charity annual return reference");
            pkg.AnnualReturnReference = reference;
            pkg.SubmittedBy = by;
            pkg.SubmittedAt = DateTime.UtcNow;
            pkg.Status = FilingPackageStatus.Submitted;
        }

        if (status == FilingStatus.Accepted)
        {
            if (pkg.FilingStatus != FilingStatus.Submitted)
                throw new BusinessRuleException("Only a submitted Charity annual return can be marked as accepted.");
            _ = NormalizeFilingReference(pkg.AnnualReturnReference, "Charity annual return reference");
            pkg.AcceptedBy = by;
            pkg.AcceptedAt = DateTime.UtcNow;
            pkg.Status = FilingPackageStatus.Accepted;
        }

        if (status == FilingStatus.CorrectionRequired)
        {
            pkg.RejectionReason = string.IsNullOrWhiteSpace(reason) ? "Charity annual return correction required." : reason.Trim();
            pkg.CorrectionDeadline = DateTime.UtcNow.AddDays(14);
        }

        pkg.FilingStatus = status;

        await db.SaveChangesAsync();
        if (audit is not null)
        {
            await audit.LogAsync(
                companyId,
                periodId,
                "CharityFilingPackage",
                pkg.Id,
                AuditEventCodes.CharityFilingStatusChanged,
                oldValue,
                CharityFilingAuditSnapshot(pkg),
                auditUserId ?? by);
        }
        return pkg;
    }

    private static object CroFilingAuditSnapshot(CroFilingPackage pkg) => new
    {
        pkg.FilingStatus,
        pkg.AccountsPdfGenerated,
        pkg.SignaturePageGenerated,
        pkg.PaymentCompleted,
        pkg.CroSubmissionReference,
        pkg.RejectionReason,
        pkg.CorrectionDeadline,
        pkg.ApprovedBy,
        pkg.ApprovedAt,
        pkg.SubmittedBy,
        pkg.SubmittedAt
    };

    private static object RevenueFilingAuditSnapshot(RevenueFilingPackage pkg) => new
    {
        pkg.FilingStatus,
        pkg.IxbrlGenerated,
        pkg.IxbrlValidated,
        pkg.IxbrlValidationErrors,
        pkg.Ct1Reference
    };

    private async Task AssertCharityAnnualReturnEvidenceAsync(AccountingPeriod period, CharityFilingPackage pkg)
    {
        if (string.IsNullOrWhiteSpace(period.Company.CharityInfo?.CharityNumber))
            throw new BusinessRuleException("Charity number is required before approving the Charity annual return pack.");
        if (!pkg.SofaGenerated || !pkg.TrusteesReportGenerated)
            throw new BusinessRuleException("Generate the Charity SoFA and Trustees' Annual Report before approving the annual return pack.");

        var hasFundBalances = await db.FundBalances.AnyAsync(f => f.PeriodId == period.Id);
        if (!hasFundBalances)
            throw new BusinessRuleException("Record charity fund balances before approving the annual return pack.");
        var hasActiveTrustee = await db.CompanyOfficers.AnyAsync(o =>
            o.CompanyId == period.CompanyId && o.Role == OfficerRole.Director && o.ResignedDate == null);
        if (!hasActiveTrustee)
            throw new BusinessRuleException("Record at least one active trustee before approving the annual return pack.");
    }

    private static object CharityFilingAuditSnapshot(CharityFilingPackage pkg) => new
    {
        pkg.FilingStatus,
        pkg.Status,
        pkg.SofaGenerated,
        pkg.TrusteesReportGenerated,
        pkg.AnnualReturnReference,
        pkg.RejectionReason,
        pkg.CorrectionDeadline,
        pkg.ApprovedBy,
        pkg.ApprovedAt,
        pkg.SubmittedBy,
        pkg.SubmittedAt,
        pkg.AcceptedBy,
        pkg.AcceptedAt
    };

    private static string NormalizeFilingReference(string? reference, string label)
    {
        var trimmed = reference?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new BusinessRuleException($"{label} is required.");
        if (trimmed.Length > 200)
            throw new BusinessRuleException($"{label} must be 200 characters or fewer.");
        return trimmed;
    }

    private static bool InternalIxbrlChecksPassed(RevenueFilingPackage? pkg) =>
        pkg?.IxbrlGenerated == true
        && pkg.IxbrlValidationErrors?.StartsWith("Internal checks passed.", StringComparison.Ordinal) == true;
}
