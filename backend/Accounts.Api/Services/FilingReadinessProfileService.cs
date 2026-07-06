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

public sealed record FilingReadinessSignOffStep(
    string Code,
    string Label,
    string State,
    string Detail,
    IReadOnlyList<LegalSourceReference> Sources);

public sealed record FilingReadinessSignOffPacket(
    string State,
    string StateLabel,
    bool ReadyForAccountantApproval,
    bool ReadyForExternalFiling,
    string? ApprovedBy,
    DateTime? ApprovedAt,
    IReadOnlyList<FilingReadinessSignOffStep> Steps,
    IReadOnlyList<string> OpenBlockers,
    IReadOnlyList<string> OpenWarnings,
    IReadOnlyList<string> AllowedNextActions);

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
    FilingReadinessSignOffPacket SignOffPacket,
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
        if (sizeClass is CompanySizeClass.Medium or CompanySizeClass.Large || regime is ElectedRegime.Medium or ElectedRegime.Full)
            sourceReferences.Add(IrishStatutoryRuleSources.CroMediumCompany);

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

        LegalSourceReference[] filingRegimeSources = regime == ElectedRegime.Micro
            ? [IrishStatutoryRuleSources.CroFinancialStatementsRequirements, IrishStatutoryRuleSources.FrcFrs105]
            : [IrishStatutoryRuleSources.CroFinancialStatementsRequirements, IrishStatutoryRuleSources.FrcFrs102];

        RequireEvidence(
            "filing-regime",
            "Filing regime and required statement set determined",
            period.FilingRegime is not null,
            regime?.ToString() ?? "Run filing regime determination after classification.",
            filingRegimeSources);
        if (period.FilingRegime is null)
            Block("filing-regime-required", "Filing regime must be determined before statutory outputs are approved.", IrishStatutoryRuleSources.CroFinancialStatementsRequirements);

        if (regime == ElectedRegime.SmallAbridged)
        {
            var abridgementEligible = period.FilingRegime?.CanFileAbridged == true
                && sizeClass is CompanySizeClass.Small or CompanySizeClass.Micro;
            RequireEvidence(
                "cro-abridgement-election",
                "Small-company Section 352 abridgement eligibility confirmed",
                abridgementEligible,
                abridgementEligible
                    ? "Section 352 abridgement election recorded for an eligible small company; full accounts remain required for members."
                    : "Small abridged CRO filing was elected, but abridgement eligibility has not been proven.",
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs102);
            if (!abridgementEligible)
                Block(
                    "abridgement-election-required",
                    "Section 352 abridgement eligibility must be confirmed before a small abridged CRO filing pack is approved.",
                    IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                    IrishStatutoryRuleSources.FrcFrs102);
        }

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
        if (auditRequired)
            sourceReferences.Add(IrishStatutoryRuleSources.CroAuditorsReport);
        RequireEvidence(
            "audit-report",
            "Signed auditor report evidence where audit is required",
            !auditRequired || period.AuditorsReportReceived,
            auditRequired
                ? period.AuditorsReportReceived ? period.AuditorsReportReference ?? "Auditor report received." : "Auditor handoff and signed auditor report are required."
                : "Audit exemption currently indicated by filing regime.",
            IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
            IrishStatutoryRuleSources.CroMediumCompany,
            IrishStatutoryRuleSources.CroAuditorsReport,
            IrishStatutoryRuleSources.FrcFrs102);
        if (auditRequired && !period.AuditorsReportReceived)
        {
            manualHandoff = true;
            Block(
                "auditor-handoff-required",
                "Audit is required, so final filing is blocked until a signed auditor report is recorded.",
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.CroMediumCompany,
                IrishStatutoryRuleSources.CroAuditorsReport);
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
        var distinctSourceReferences = sourceReferences.DistinctBy(s => s.SourceId).ToList();
        var signOffPacket = BuildSignOffPacket(
            supportedPath,
            manualHandoff,
            company.IsCharitableOrganisation,
            auditRequired,
            regime,
            reviewState,
            cro,
            revenue,
            evidence,
            blockers,
            warnings,
            allowedNextActions);

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
            signOffPacket,
            evidence,
            blockers,
            warnings,
            distinctSourceReferences,
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

    private static FilingReadinessSignOffPacket BuildSignOffPacket(
        bool supportedPath,
        bool manualHandoff,
        bool charityReportingRequired,
        bool auditReportRequired,
        ElectedRegime? regime,
        string reviewState,
        CroFilingPackage? cro,
        RevenueFilingPackage? revenue,
        IReadOnlyList<FilingReadinessEvidenceItem> evidence,
        IReadOnlyList<FilingReadinessIssue> blockers,
        IReadOnlyList<FilingReadinessIssue> warnings,
        IReadOnlyList<string> allowedNextActions)
    {
        var approvedBy = string.IsNullOrWhiteSpace(cro?.ApprovedBy) ? null : cro.ApprovedBy;
        var approvedAt = cro?.ApprovedAt;
        var accountantApproved = approvedBy is not null && approvedAt is not null;
        var readyForAccountantApproval = !accountantApproved
            && supportedPath
            && !manualHandoff
            && blockers.All(issue => issue.Code == "accountant-review-required");
        var readyForExternalFiling = supportedPath
            && !manualHandoff
            && accountantApproved
            && blockers.Count == 0
            && warnings.Count == 0;
        var (state, stateLabel) = DetermineSignOffState(
            supportedPath,
            manualHandoff,
            accountantApproved,
            readyForAccountantApproval,
            readyForExternalFiling,
            blockers);

        return new FilingReadinessSignOffPacket(
            state,
            stateLabel,
            readyForAccountantApproval,
            readyForExternalFiling,
            approvedBy,
            approvedAt,
            BuildSignOffSteps(
                supportedPath,
                manualHandoff,
                charityReportingRequired,
                auditReportRequired,
                regime,
                accountantApproved,
                reviewState,
                cro,
                revenue,
                evidence,
                readyForAccountantApproval),
            blockers.Select(issue => issue.Message).Distinct().ToList(),
            warnings.Select(issue => issue.Message).Distinct().ToList(),
            allowedNextActions);
    }

    private static (string State, string Label) DetermineSignOffState(
        bool supportedPath,
        bool manualHandoff,
        bool accountantApproved,
        bool readyForAccountantApproval,
        bool readyForExternalFiling,
        IReadOnlyList<FilingReadinessIssue> blockers)
    {
        if (!supportedPath || manualHandoff)
            return ("manual-handoff", "Manual professional handoff");
        if (readyForExternalFiling)
            return ("ready-for-external-filing", "Ready for external filing");
        if (accountantApproved && blockers.Count == 0)
            return ("approved-external-evidence-open", "Accountant approved, external evidence open");
        if (readyForAccountantApproval)
            return ("ready-for-accountant-review", "Ready for accountant review");

        return ("blocked", "Blocked before accountant review");
    }

    private static IReadOnlyList<FilingReadinessSignOffStep> BuildSignOffSteps(
        bool supportedPath,
        bool manualHandoff,
        bool charityReportingRequired,
        bool auditReportRequired,
        ElectedRegime? regime,
        bool accountantApproved,
        string reviewState,
        CroFilingPackage? cro,
        RevenueFilingPackage? revenue,
        IReadOnlyList<FilingReadinessEvidenceItem> evidence,
        bool readyForAccountantApproval)
    {
        var abridgementEvidenceComplete = EvidenceSatisfiedOrNotRequired(evidence, "cro-abridgement-election");
        var statutoryEvidenceComplete = EvidenceSatisfied(evidence, "size-classification")
            && EvidenceSatisfied(evidence, "filing-regime")
            && abridgementEvidenceComplete;
        var directorCertificationComplete = EvidenceSatisfied(evidence, "cro-signatories")
            && EvidenceSatisfied(evidence, "cro-signature-page");
        var generatedOutputsComplete = EvidenceSatisfied(evidence, "cro-accounts-pdf")
            && EvidenceSatisfied(evidence, "ixbrl-internal-checks");
        var externalValidationComplete = revenue?.IxbrlValidated == true;
        var accountingStandardSource = regime == ElectedRegime.Micro
            ? IrishStatutoryRuleSources.FrcFrs105
            : IrishStatutoryRuleSources.FrcFrs102;

        var steps = new List<FilingReadinessSignOffStep>
        {
            new FilingReadinessSignOffStep(
                "supported-path",
                "Company path support",
                supportedPath && !manualHandoff ? "complete" : "blocked",
                supportedPath && !manualHandoff
                    ? "Core LTD, DAC or CLG filing path is supported by the platform."
                    : "Manual professional handoff is required for this filing path.",
                [IrishStatutoryRuleSources.CroFinancialStatementsRequirements]),
            new FilingReadinessSignOffStep(
                "statutory-basis",
                "Statutory basis",
                statutoryEvidenceComplete ? "complete" : "blocked",
                statutoryEvidenceComplete
                    ? regime == ElectedRegime.SmallAbridged
                        ? "Size classification, filing regime and Section 352 abridgement evidence are recorded."
                        : "Size classification and filing regime evidence are recorded."
                    : "Complete size classification and filing regime evidence before review.",
                [IrishStatutoryRuleSources.CroFinancialStatementsRequirements, accountingStandardSource]),
            new FilingReadinessSignOffStep(
                "directors-certification",
                "Director and secretary certification",
                directorCertificationComplete ? "complete" : "blocked",
                directorCertificationComplete
                    ? "Director, secretary and CRO signature-page evidence are present."
                    : "Director/secretary certification evidence is incomplete.",
                [IrishStatutoryRuleSources.CroFinancialStatementsRequirements]),
            new FilingReadinessSignOffStep(
                "generated-outputs",
                "Generated statutory outputs",
                generatedOutputsComplete ? "complete" : "blocked",
                generatedOutputsComplete
                    ? "CRO accounts PDF and internal iXBRL checks are complete."
                    : "Generate the CRO accounts PDF and complete internal iXBRL checks.",
                [
                    IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                    IrishStatutoryRuleSources.RevenueIxbrlOverview,
                    IrishStatutoryRuleSources.RevenueAcceptedTaxonomies
                ]),
            new FilingReadinessSignOffStep(
                "external-validation",
                "External ROS validation",
                externalValidationComplete ? "complete" : "warning",
                externalValidationComplete
                    ? "External ROS/iXBRL validation evidence is recorded."
                    : "External ROS validation evidence pending",
                [IrishStatutoryRuleSources.RevenueIxbrlOverview, IrishStatutoryRuleSources.RevenueAcceptedTaxonomies]),
            new FilingReadinessSignOffStep(
                "accountant-approval",
                "Qualified accountant approval",
                accountantApproved ? "complete" : readyForAccountantApproval ? "blocked" : "pending",
                accountantApproved
                    ? $"Approved by {cro!.ApprovedBy} on {cro.ApprovedAt!.Value:yyyy-MM-dd HH:mm} UTC."
                    : readyForAccountantApproval
                        ? "Ready for named qualified-accountant approval."
                        : $"Resolve blockers before accountant approval. Current state: {reviewState}.",
                [IrishStatutoryRuleSources.CroFinancialStatementsRequirements, IrishStatutoryRuleSources.RevenueIxbrlOverview])
        };

        if (charityReportingRequired)
        {
            var charityEvidenceComplete = EvidenceSatisfied(evidence, "charity-number")
                && EvidenceSatisfied(evidence, "charity-reports");
            steps.Add(new FilingReadinessSignOffStep(
                "charity-reporting",
                "Charity reporting evidence",
                charityEvidenceComplete ? "complete" : "blocked",
                charityEvidenceComplete
                    ? "Charity number, SoFA and Trustees' Annual Report evidence are present."
                    : "Record the charity number and generate SoFA and Trustees' Annual Report evidence before charity annual-return review.",
                [IrishStatutoryRuleSources.CharitiesRegulatorAnnualReport]));
        }

        if (auditReportRequired)
        {
            var auditEvidence = evidence.FirstOrDefault(item => item.Code == "audit-report");
            var auditorEvidenceComplete = auditEvidence?.Satisfied == true;
            steps.Add(new FilingReadinessSignOffStep(
                "auditor-handoff",
                "Auditor handoff",
                auditorEvidenceComplete ? "complete" : "blocked",
                auditorEvidenceComplete
                    ? $"Signed auditor report evidence is recorded: {auditEvidence!.Detail}"
                    : "Signed auditor report evidence is required before final output generation.",
                [
                    IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                    IrishStatutoryRuleSources.CroMediumCompany,
                    IrishStatutoryRuleSources.CroAuditorsReport
                ]));
        }

        return steps;
    }

    private static bool EvidenceSatisfied(IReadOnlyList<FilingReadinessEvidenceItem> evidence, string code)
    {
        return evidence.FirstOrDefault(item => item.Code == code)?.Satisfied == true;
    }

    private static bool EvidenceSatisfiedOrNotRequired(IReadOnlyList<FilingReadinessEvidenceItem> evidence, string code)
    {
        var item = evidence.FirstOrDefault(evidenceItem => evidenceItem.Code == code);
        return item is null || item.Satisfied;
    }
}
