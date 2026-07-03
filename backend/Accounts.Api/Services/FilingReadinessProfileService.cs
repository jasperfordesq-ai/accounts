using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public sealed record FilingReadinessEvidenceItem(
    string Code,
    string Label,
    bool Required,
    bool Satisfied,
    string? Detail,
    IReadOnlyList<LegalSourceReference> Sources);

public sealed record FilingReadinessIssue(
    string Code,
    string Severity,
    string Message,
    IReadOnlyList<LegalSourceReference> Sources);

public sealed record FilingReadinessProfile(
    int CompanyId,
    int PeriodId,
    CompanyType CompanyType,
    CompanySizeClass? SizeClass,
    ElectedRegime? ElectedRegime,
    bool? AuditExempt,
    bool SupportedPath,
    bool ManualProfessionalReviewRequired,
    bool AccountantReviewRequired,
    string AccountantReviewState,
    bool DirectCroSubmissionSupported,
    bool DirectRosSubmissionSupported,
    RevenueIxbrlTaxonomySelection RevenueTaxonomy,
    IReadOnlyList<FilingReadinessEvidenceItem> RequiredEvidence,
    IReadOnlyList<FilingReadinessIssue> BlockingIssues,
    IReadOnlyList<FilingReadinessIssue> WarningIssues,
    IReadOnlyList<LegalSourceReference> SourceReferences,
    IReadOnlyList<string> AllowedNextActions);

public class FilingReadinessProfileService(AccountsDbContext db)
{
    private static readonly IReadOnlyList<CompanyType> CoreSupportedCompanyTypes =
    [
        CompanyType.Private,
        CompanyType.DesignatedActivityCompany,
        CompanyType.CompanyLimitedByGuarantee
    ];

    public async Task<FilingReadinessProfile> GetProfileAsync(int companyId, int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company).ThenInclude(c => c.CharityInfo)
            .Include(p => p.SizeClassification)
            .Include(p => p.FilingRegime)
            .Include(p => p.CroFilingPackage)
            .Include(p => p.RevenueFilingPackage)
            .Include(p => p.CharityFilingPackage)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        var company = period.Company;
        var sizeClass = period.SizeClassification?.OverrideClass ?? period.SizeClassification?.CalculatedClass;
        var regime = period.FilingRegime?.ElectedRegime;
        var taxonomy = RevenueIxbrlTaxonomySelector.Select(period.PeriodStart, regime);

        var sourceReferences = new List<LegalSourceReference>
        {
            IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
            IrishStatutoryRuleSources.RevenueIxbrlOverview,
            IrishStatutoryRuleSources.RevenueIxbrlContents,
            IrishStatutoryRuleSources.RevenueAcceptedTaxonomies,
            IrishStatutoryRuleSources.FrcFrs102
        };
        if (regime == ElectedRegime.Micro)
            sourceReferences.Add(IrishStatutoryRuleSources.FrcFrs105);
        if (company.CompanyType == CompanyType.CompanyLimitedByGuarantee)
            sourceReferences.Add(IrishStatutoryRuleSources.CroGuaranteeCompany);
        if (company.CompanyType == CompanyType.PrivateUnlimited)
            sourceReferences.Add(IrishStatutoryRuleSources.CroUnlimitedCompany);
        if (company.IsCharitableOrganisation)
            sourceReferences.Add(IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport);
        if (company.IsGroupMember || company.IsHolding || company.IsSubsidiary)
            sourceReferences.Add(IrishStatutoryRuleSources.CroGroupCompany);

        var evidence = new List<FilingReadinessEvidenceItem>();
        var blockers = new List<FilingReadinessIssue>();
        var warnings = new List<FilingReadinessIssue>();

        var supportedPath = true;
        var manualHandoff = false;

        void Block(string code, string message, params LegalSourceReference[] sources)
        {
            blockers.Add(new FilingReadinessIssue(code, "blocking", message, sources));
        }

        void Warn(string code, string message, params LegalSourceReference[] sources)
        {
            warnings.Add(new FilingReadinessIssue(code, "warning", message, sources));
        }

        void RequireEvidence(string code, string label, bool satisfied, string? detail, params LegalSourceReference[] sources)
        {
            evidence.Add(new FilingReadinessEvidenceItem(code, label, true, satisfied, detail, sources));
        }

        if (!CoreSupportedCompanyTypes.Contains(company.CompanyType))
        {
            supportedPath = false;
            manualHandoff = true;
            var source = company.CompanyType == CompanyType.PrivateUnlimited
                ? IrishStatutoryRuleSources.CroUnlimitedCompany
                : IrishStatutoryRuleSources.CroFinancialStatementsRequirements;
            Block(
                "unsupported-company-type",
                $"{company.CompanyType} workflows require manual professional handoff before statutory filing.",
                source);
        }

        if (company.IsListedSecurities || company.IsCreditInstitution || company.IsInsuranceUndertaking || company.IsPensionFund)
        {
            supportedPath = false;
            manualHandoff = true;
            Block(
                "regulated-entity-manual-handoff",
                "Listed, credit institution, insurance undertaking, pension fund and other Fifth Schedule/excluded entities are not safe for automated filing.",
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs102);
        }

        if (company.IsGroupMember || company.IsHolding || company.IsSubsidiary)
        {
            supportedPath = false;
            manualHandoff = true;
            Block(
                "group-context-manual-handoff",
                "Group, holding, subsidiary and consolidation contexts require manual professional review and are outside the supported filing path.",
                IrishStatutoryRuleSources.CroGroupCompany,
                IrishStatutoryRuleSources.FrcFrs102);
        }

        RequireEvidence(
            "size-classification",
            "Company size classification completed",
            sizeClass.HasValue,
            sizeClass.HasValue ? sizeClass.Value.ToString() : "Run size classification before determining filing requirements.",
            IrishStatutoryRuleSources.CroFinancialStatementsRequirements);
        if (!sizeClass.HasValue)
            Block("size-classification-required", "Size classification must be completed before filing readiness can be assessed.", IrishStatutoryRuleSources.CroFinancialStatementsRequirements);

        RequireEvidence(
            "filing-regime",
            "Filing regime and required statement set determined",
            period.FilingRegime is not null,
            regime?.ToString() ?? "Run filing regime determination after classification.",
            IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
            IrishStatutoryRuleSources.FrcFrs102);
        if (period.FilingRegime is null)
            Block("filing-regime-required", "Filing regime must be determined before statutory outputs are approved.", IrishStatutoryRuleSources.CroFinancialStatementsRequirements);

        var hasDirector = await db.CompanyOfficers.AnyAsync(o =>
            o.CompanyId == companyId && o.Role == OfficerRole.Director && o.ResignedDate == null);
        var hasSecretary = await db.CompanyOfficers.AnyAsync(o =>
            o.CompanyId == companyId && (o.Role == OfficerRole.Secretary || o.Role == OfficerRole.CompanySecretary) && o.ResignedDate == null);
        RequireEvidence(
            "cro-signatories",
            "Active director and company secretary recorded",
            hasDirector && hasSecretary,
            hasDirector && hasSecretary ? "Director and secretary present." : "CRO certification requires director and secretary evidence.",
            IrishStatutoryRuleSources.CroFinancialStatementsRequirements);
        if (!hasDirector)
            Block("director-required", "Record an active director before approving or submitting CRO accounts.", IrishStatutoryRuleSources.CroFinancialStatementsRequirements);
        if (!hasSecretary)
            Block("secretary-required", "Record an active company secretary before approving or submitting CRO accounts.", IrishStatutoryRuleSources.CroFinancialStatementsRequirements);

        var cro = period.CroFilingPackage;
        RequireEvidence(
            "cro-accounts-pdf",
            "CRO accounts PDF generated by the platform",
            cro?.AccountsPdfGenerated == true,
            cro?.AccountsPdfGenerated == true ? "Generated." : "Generate the CRO accounts PDF from the server workflow.",
            IrishStatutoryRuleSources.CroFinancialStatementsRequirements);
        RequireEvidence(
            "cro-signature-page",
            "CRO certification/signature page generated",
            cro?.SignaturePageGenerated == true,
            cro?.SignaturePageGenerated == true ? "Generated." : "Generate the CRO signature page from the server workflow.",
            IrishStatutoryRuleSources.CroFinancialStatementsRequirements);
        if (cro?.AccountsPdfGenerated != true)
            Block("cro-accounts-pdf-required", "Generate the CRO accounts PDF before approval or submission.", IrishStatutoryRuleSources.CroFinancialStatementsRequirements);
        if (cro?.SignaturePageGenerated != true)
            Block("cro-signature-page-required", "Generate the CRO signature page before approval or submission.", IrishStatutoryRuleSources.CroFinancialStatementsRequirements);

        var auditRequired = period.FilingRegime?.AuditExempt == false
            || sizeClass is CompanySizeClass.Medium or CompanySizeClass.Large;
        RequireEvidence(
            "audit-report",
            "Signed auditor report evidence where audit is required",
            !auditRequired || period.AuditorsReportReceived,
            auditRequired
                ? period.AuditorsReportReceived ? period.AuditorsReportReference ?? "Auditor report received." : "Auditor handoff and signed auditor report are required."
                : "Audit exemption currently indicated by filing regime.",
            IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
            IrishStatutoryRuleSources.FrcFrs102);
        if (auditRequired && !period.AuditorsReportReceived)
        {
            manualHandoff = true;
            Block("auditor-handoff-required", "Audit is required, so final filing is blocked until a signed auditor report is recorded.", IrishStatutoryRuleSources.CroFinancialStatementsRequirements);
        }

        var reviewState = cro?.ApprovedAt is not null && !string.IsNullOrWhiteSpace(cro.ApprovedBy)
            ? $"Approved by {cro.ApprovedBy}"
            : "Required";
        RequireEvidence(
            "accountant-review",
            "Named qualified-accountant review and approval recorded",
            reviewState != "Required",
            reviewState,
            IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
            IrishStatutoryRuleSources.RevenueIxbrlOverview);
        if (reviewState == "Required")
            Block("accountant-review-required", "A named qualified accountant must approve the filing pack before any real CRO/Revenue use.", IrishStatutoryRuleSources.CroFinancialStatementsRequirements);

        var revenue = period.RevenueFilingPackage;
        var internalIxbrlChecksPassed = revenue?.IxbrlGenerated == true
            && revenue.IxbrlValidationErrors?.StartsWith("Internal checks passed.", StringComparison.Ordinal) == true;
        RequireEvidence(
            "ixbrl-internal-checks",
            "Internal iXBRL generation checks completed",
            internalIxbrlChecksPassed,
            internalIxbrlChecksPassed ? "Internal checks passed." : "Run internal iXBRL checks before Revenue workflow approval.",
            IrishStatutoryRuleSources.RevenueIxbrlOverview,
            IrishStatutoryRuleSources.RevenueIxbrlContents,
            IrishStatutoryRuleSources.RevenueAcceptedTaxonomies);
        if (!internalIxbrlChecksPassed)
            Block("ixbrl-internal-checks-required", "Internal iXBRL checks must pass before Revenue filing workflow approval.", IrishStatutoryRuleSources.RevenueIxbrlOverview);

        RequireEvidence(
            "external-ros-validation",
            "External ROS/iXBRL validation completed outside the platform",
            revenue?.IxbrlValidated == true,
            revenue?.IxbrlValidated == true
                ? "External validation recorded."
                : "The platform only records internal checks; ROS validation remains a manual external gate.",
            IrishStatutoryRuleSources.RevenueIxbrlOverview,
            IrishStatutoryRuleSources.RevenueAcceptedTaxonomies);
        if (revenue?.IxbrlValidated != true)
            Warn("external-ros-validation-required", "External ROS/iXBRL validation must be completed and evidenced before real Revenue filing.", IrishStatutoryRuleSources.RevenueIxbrlOverview);

        if (company.IsCharitableOrganisation)
        {
            var charity = period.CharityFilingPackage;
            var hasCharityNumber = !string.IsNullOrWhiteSpace(company.CharityInfo?.CharityNumber);
            RequireEvidence(
                "charity-number",
                "Charity number recorded",
                hasCharityNumber,
                hasCharityNumber ? company.CharityInfo!.CharityNumber : "Charity number is required for the Charities Regulator annual return.",
                IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport);
            if (!hasCharityNumber)
                Block("charity-number-required", "Charity number is required for the Charities Regulator annual return.", IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport);

            RequireEvidence(
                "charity-reports",
                "SoFA and Trustees' Annual Report generated",
                charity?.SofaGenerated == true && charity.TrusteesReportGenerated,
                charity?.SofaGenerated == true && charity.TrusteesReportGenerated ? "Generated." : "Generate charity annual report pack before approval.",
                IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport);
        }

        if (!taxonomy.AcceptedByRevenue)
        {
            supportedPath = false;
            manualHandoff = true;
            Block(
                "taxonomy-not-revenue-accepted",
                $"No Revenue-accepted iXBRL taxonomy is pinned for period start {period.PeriodStart:yyyy-MM-dd}.",
                IrishStatutoryRuleSources.RevenueAcceptedTaxonomies);
        }

        var allowedNextActions = DetermineAllowedNextActions(supportedPath, manualHandoff, evidence, blockers, cro, revenue);
        sourceReferences.AddRange(taxonomy.Sources);

        return new FilingReadinessProfile(
            companyId,
            periodId,
            company.CompanyType,
            sizeClass,
            regime,
            period.FilingRegime?.AuditExempt,
            supportedPath,
            manualHandoff,
            AccountantReviewRequired: true,
            reviewState,
            DirectCroSubmissionSupported: false,
            DirectRosSubmissionSupported: false,
            taxonomy,
            evidence,
            blockers,
            warnings,
            sourceReferences.DistinctBy(s => s.SourceId).ToList(),
            allowedNextActions);
    }

    public async Task AssertCanRecordCroSubmissionAsync(int companyId, int periodId)
    {
        var profile = await GetProfileAsync(companyId, periodId);
        var blockers = profile.BlockingIssues
            .Where(i => i.Code is not "ixbrl-internal-checks-required")
            .Select(i => i.Message)
            .Distinct()
            .ToList();

        if (profile.ManualProfessionalReviewRequired)
            blockers.Insert(0, "Manual professional handoff is required for this filing path.");

        if (blockers.Count > 0)
            throw new BusinessRuleException($"Cannot mark CRO filing as submitted while readiness blockers remain: {string.Join("; ", blockers)}");
    }

    public async Task AssertCanApproveCroPackAsync(int companyId, int periodId)
    {
        var profile = await GetProfileAsync(companyId, periodId);
        var blockers = profile.BlockingIssues
            .Where(i => i.Code is not "accountant-review-required" and not "ixbrl-internal-checks-required")
            .Select(i => i.Message)
            .Distinct()
            .ToList();

        if (profile.ManualProfessionalReviewRequired)
            blockers.Insert(0, "Manual professional handoff is required for this filing path.");

        if (blockers.Count > 0)
            throw new BusinessRuleException($"Cannot approve CRO filing while readiness blockers remain: {string.Join("; ", blockers)}");
    }

    private static IReadOnlyList<string> DetermineAllowedNextActions(
        bool supportedPath,
        bool manualHandoff,
        IReadOnlyList<FilingReadinessEvidenceItem> evidence,
        IReadOnlyList<FilingReadinessIssue> blockers,
        CroFilingPackage? cro,
        RevenueFilingPackage? revenue)
    {
        if (!supportedPath || manualHandoff)
            return [];

        var actions = new List<string>();
        if (cro?.AccountsPdfGenerated != true)
            actions.Add("generate-cro-accounts-pdf");
        if (cro?.SignaturePageGenerated != true)
            actions.Add("generate-cro-signature-page");
        if (revenue?.IxbrlGenerated != true)
            actions.Add("run-internal-ixbrl-checks");

        var onlyReviewAndExternalRosRemain = blockers.All(i =>
            i.Code is "accountant-review-required" or "ixbrl-internal-checks-required")
            || blockers.Count == 0;
        var docsReady = evidence.All(e =>
            e.Code is not ("cro-accounts-pdf" or "cro-signature-page" or "cro-signatories")
            || e.Satisfied);
        if (docsReady && onlyReviewAndExternalRosRemain)
            actions.Add("approve-cro-pack");

        if (cro?.FilingStatus == FilingStatus.Approved && blockers.Count == 0)
            actions.Add("mark-cro-submitted");
        if (cro?.FilingStatus == FilingStatus.Submitted && cro.PaymentCompleted == false)
            actions.Add("confirm-core-payment");
        if (cro?.FilingStatus == FilingStatus.Submitted && cro.PaymentCompleted)
            actions.Add("mark-cro-accepted");

        return actions.Distinct().ToList();
    }
}
