using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

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
    bool ReadyForDirectorsApproval,
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
    string ApprovalAuthority,
    bool DirectCroSubmissionSupported,
    bool DirectRosSubmissionSupported,
    bool RevenueIxbrlGenerationSupported,
    bool RevenueManualHandoffRequired,
    string RevenueGenerationSupportReason,
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
        // Historic figures keyed in from filed accounts are never this period's size classification.
        var classification = period.SizeClassification is { InputBasis: SizeInputBasis.Computed } computed ? computed : null;
        var currentOverride = classification?.OverrideClass is not null
            && classification.OverrideRequiresRereview == false
            && classification.OverrideEvidenceArtifact is { Length: > 0 }
            && classification.OverrideEvidenceSha256 is { Length: 64 }
            && string.Equals(
                FilingReleaseGate.ComputeSha256(classification.OverrideEvidenceArtifact),
                classification.OverrideEvidenceSha256,
                StringComparison.OrdinalIgnoreCase);
        var sizeClass = currentOverride ? classification!.OverrideClass : classification?.CalculatedClass;
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

        if (company.IsListedSecurities || company.IsCreditInstitution || company.IsInsuranceUndertaking || company.IsPensionFund
            || company.IsFifthScheduleEntity || company.IsOtherIneligibleEntity)
        {
            supportedPath = false;
            manualHandoff = true;
            Block(
                "regulated-entity-manual-handoff",
                "Listed, credit institution, insurance undertaking, pension fund and other Fifth Schedule/excluded entities are not safe for automated filing.",
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.FrcFrs102);
        }

        if (classification?.OverrideClass is not null && !currentOverride)
        {
            Block(
                "classification-override-rereview-required",
                "The classification override is stale or its retained evidence no longer matches; re-review it before any filing use.",
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements);
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
            sizeClass.HasValue && !string.IsNullOrWhiteSpace(classification?.DecisionInputFingerprintSha256),
            sizeClass.HasValue && !string.IsNullOrWhiteSpace(classification?.DecisionInputFingerprintSha256)
                ? sizeClass.Value.ToString()
                : "Run size classification against the current raw figures and threshold election before determining filing requirements.",
            IrishStatutoryRuleSources.CroFinancialStatementsRequirements);
        if (!sizeClass.HasValue || string.IsNullOrWhiteSpace(classification?.DecisionInputFingerprintSha256))
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
            || period.MemberAuditNoticeReceived
            || sizeClass is CompanySizeClass.Medium or CompanySizeClass.Large;
        var completeAuditorReport = !auditRequired || FilingReleaseGate.HasCompleteAuditorReportEvidence(period);
        if (auditRequired)
            sourceReferences.Add(IrishStatutoryRuleSources.CroAuditorsReport);
        RequireEvidence(
            "audit-report",
            "Signed auditor report evidence where audit is required",
            completeAuditorReport,
            auditRequired
                ? completeAuditorReport
                    ? $"{period.AuditorsReportReference}; retained PDF SHA-256 {period.AuditorsReportSha256}."
                    : "Retained signed auditor-report PDF, firm/signer identity and the directors' acceptance of the report are required."
                : "Audit exemption currently indicated by filing regime.",
            IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
            IrishStatutoryRuleSources.CroMediumCompany,
            IrishStatutoryRuleSources.CroAuditorsReport,
            IrishStatutoryRuleSources.FrcFrs102);
        if (auditRequired && !completeAuditorReport)
        {
            manualHandoff = true;
            Block(
                "auditor-handoff-required",
                "Audit is required, so final filing is blocked until a signed auditor report is recorded.",
                IrishStatutoryRuleSources.CroFinancialStatementsRequirements,
                IrishStatutoryRuleSources.CroMediumCompany,
                IrishStatutoryRuleSources.CroAuditorsReport);
        }

        var directorsEvents = await db.DirectorsApprovalEvents.AsNoTracking().Where(e => e.PeriodId == periodId).OrderBy(e => e.Id).ToListAsync();
        var directorsPages = await db.DirectorsApprovalPages.AsNoTracking().Where(p => p.PeriodId == periodId).ToListAsync();
        var currentApproval = DirectorsApprovalService.ResolveCurrent(directorsEvents, directorsPages, cro);
        var integrityFailure = directorsEvents.Count > 0 && currentApproval is null
            && directorsEvents.Any(e => !DirectorsApprovalIntegrity.IsValid(e));
        RequireEvidence(
            "directors-approval",
            "Board approval of the financial statements, s.324",
            currentApproval is not null,
            currentApproval is not null
                ? $"Approved by the board on {currentApproval.Page.ApprovalDate:yyyy-MM-dd} ({currentApproval.Page.ResolutionReference}); signed by {currentApproval.Page.FirstDirectorName}{(currentApproval.Page.SecondDirectorName is null ? " (sole director)" : $" and {currentApproval.Page.SecondDirectorName}")}; record {currentApproval.Approval.RecordSha256[..12]}."
                : integrityFailure
                    ? "Directors' approval record failed integrity verification."
                    : "Generate the s.324 approval page, have the directors sign it, then upload it with the minute or written resolution.",
            IrishStatutoryRuleSources.CompaniesActSection324);
        if (currentApproval is null)
            Block("directors-approval-required", "The board must approve the financial statements and two directors (or the sole director) must sign on its behalf (Companies Act 2014, s.324).", IrishStatutoryRuleSources.CompaniesActSection324);
        sourceReferences.Add(IrishStatutoryRuleSources.CompaniesActSection324);
        var reviewState = OptionalAccountantReviewState(cro);
        // Irish law does not require an accountant: recorded when supplied, never a blocker or gate.
        evidence.Add(new FilingReadinessEvidenceItem(
            "accountant-review-optional",
            "Reviewed by an accountant (optional)",
            false,
            reviewState.StartsWith("Optional review recorded", StringComparison.Ordinal),
            reviewState,
            []));

        TaxComputationService.TaxComputation? taxSupport = null;
        string? taxSupportFailure = null;
        // A company holding a charitable tax exemption with no corporation tax confirmed as due has no
        // CT1 to support, so the scope questionnaire is not required of it; the exemption evidence is
        // the record instead. Once a liability is confirmed as due the full scope review applies.
        var exemptWithoutConfirmedDue = !await CorporationTaxObligation.RevenueAppliesAsync(db, periodId);
        if (!exemptWithoutConfirmedDue)
        {
            try
            {
                taxSupport = await new TaxComputationService(db, new FinancialStatementsService(db))
                    .ComputeAsync(companyId, periodId);
            }
            catch (BusinessRuleException exception)
            {
                taxSupportFailure = exception.Message;
            }
        }
        var taxSupportSatisfied = exemptWithoutConfirmedDue || taxSupport?.FinalTaxChargeSupported == true;
        var taxSupportDetail = exemptWithoutConfirmedDue
            ? $"Charitable tax exemption held (Revenue reference {period.Company.CharitableTaxExemptionReference}{(period.Company.CharitableTaxExemptionConfirmedDate is { } confirmedOn ? $", confirmed {confirmedOn:yyyy-MM-dd}" : string.Empty)}); no corporation-tax return is due unless a liability is confirmed as due, so the scope questionnaire is not required."
            : taxSupport is not null
                ? taxSupportSatisfied
                    ? $"Simple-scope support calculation {taxSupport.CalculationSha256}; this is still not a complete CT1 return."
                    : string.Join("; ", taxSupport.BlockingReasons)
                : $"Corporation-tax support calculation could not be produced: {taxSupportFailure}";
        RequireEvidence(
            "corporation-tax-scope",
            "Corporation-tax support scope and retained loss movement are current",
            taxSupportSatisfied,
            taxSupportDetail,
            IrishStatutoryRuleSources.RevenueIxbrlOverview);
        if (!taxSupportSatisfied)
        {
            Block(
                "corporation-tax-scope-required",
                "Revenue-ready handoff is blocked until corporation-tax scope and loss evidence pass: "
                + taxSupportDetail,
                IrishStatutoryRuleSources.RevenueIxbrlOverview);
        }

        var revenue = period.RevenueFilingPackage;
        var internalIxbrlChecksPassed = RevenueIxbrlGenerationPolicy.FilingReadyGenerationEnabled
            && revenue?.IxbrlGenerated == true
            && revenue.IxbrlValidationErrors?.StartsWith("Internal checks passed.", StringComparison.Ordinal) == true;
        RequireEvidence(
            "ixbrl-internal-checks",
            "Internal iXBRL generation checks completed",
            internalIxbrlChecksPassed,
            internalIxbrlChecksPassed
                ? "Internal checks passed."
                : RevenueIxbrlGenerationPolicy.FilingReadyGenerationEnabled
                    ? "Run internal iXBRL checks before Revenue workflow approval."
                    : RevenueIxbrlGenerationPolicy.ManualHandoffReason,
            IrishStatutoryRuleSources.RevenueIxbrlOverview,
            IrishStatutoryRuleSources.RevenueIxbrlContents,
            IrishStatutoryRuleSources.RevenueAcceptedTaxonomies);
        if (RevenueIxbrlGenerationPolicy.FilingReadyGenerationEnabled && !internalIxbrlChecksPassed)
            Block("ixbrl-internal-checks-required", "Internal iXBRL checks must pass before Revenue filing workflow approval.", IrishStatutoryRuleSources.RevenueIxbrlOverview);

        var externalRevenueValidationComplete = HasCompleteExternalRevenueValidationEvidence(revenue);
        RequireEvidence(
            "external-ros-validation",
            "External ROS/iXBRL validation completed outside the platform",
            externalRevenueValidationComplete,
            externalRevenueValidationComplete
                ? "Retained validator response, validator/taxonomy identity and exact artifact hashes are present."
                : "A boolean is insufficient: retain the exact artifact plus complete ROS validator response and identity evidence.",
            IrishStatutoryRuleSources.RevenueIxbrlOverview,
            IrishStatutoryRuleSources.RevenueAcceptedTaxonomies);
        if (!externalRevenueValidationComplete)
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

        var revenueIxbrlGenerationSupported = RevenueIxbrlGenerationPolicy.FilingReadyGenerationEnabled;
        var revenueManualHandoffRequired = !revenueIxbrlGenerationSupported;
        if (revenueManualHandoffRequired)
        {
            Block(
                "ixbrl-generation-manual-handoff",
                RevenueIxbrlGenerationPolicy.ManualHandoffReason,
                IrishStatutoryRuleSources.RevenueIxbrlContents);
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

        var allowedNextActions = DetermineAllowedNextActions(supportedPath, manualHandoff, evidence, blockers, cro, revenue, currentApproval);
        sourceReferences.AddRange(taxonomy.Sources);
        var distinctSourceReferences = sourceReferences.DistinctBy(s => s.SourceId).ToList();
        var signOffPacket = BuildSignOffPacket(
            supportedPath,
            manualHandoff,
            company.IsCharitableOrganisation,
            auditRequired,
            regime,
            currentApproval,
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
            AccountantReviewRequired: false,
            reviewState,
            ApprovalAuthority: FilingReleaseGate.BoardApprovalCapacity,
            DirectCroSubmissionSupported: false,
            DirectRosSubmissionSupported: false,
            revenueIxbrlGenerationSupported,
            revenueManualHandoffRequired,
            RevenueIxbrlGenerationPolicy.ManualHandoffReason,
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
            .Where(i => !IsRevenueOnlyBlocker(i.Code))
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
            .Where(i => !IsRevenueOnlyBlocker(i.Code))
            .Select(i => i.Message)
            .Distinct()
            .ToList();

        if (profile.ManualProfessionalReviewRequired)
        {
            // On a supported path the only manual handoff is an audit-required company without its
            // signed auditor's report: say so rather than calling the company unsupported.
            var onlyAuditorReportMissing = profile.SupportedPath
                && profile.BlockingIssues.Any(i => i.Code == "auditor-handoff-required");
            blockers.Insert(0, onlyAuditorReportMissing
                ? "Record the signed auditor's report before approving the CRO pack."
                : "This company is outside the supported filing path.");
        }

        if (blockers.Count > 0)
            throw new BusinessRuleException($"Cannot approve CRO filing while readiness blockers remain: {string.Join("; ", blockers)}");
    }

    private static IReadOnlyList<string> DetermineAllowedNextActions(
        bool supportedPath,
        bool manualHandoff,
        IReadOnlyList<FilingReadinessEvidenceItem> evidence,
        IReadOnlyList<FilingReadinessIssue> blockers,
        CroFilingPackage? cro,
        RevenueFilingPackage? revenue,
        CurrentDirectorsApproval? currentApproval)
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

        var docsReady = evidence.All(e =>
            e.Code is not ("cro-accounts-pdf" or "cro-signature-page" or "cro-signatories")
            || e.Satisfied);
        if (docsReady && currentApproval is null)
            actions.Add("record-directors-approval");
        if (docsReady
            && currentApproval is not null
            && blockers.All(i => IsRevenueOnlyBlocker(i.Code))
            && cro?.FilingStatus != FilingStatus.Approved)
            actions.Add("approve-cro-pack");
        if (cro is { AccountsPdfGenerated: true, SignaturePageGenerated: true } && cro.SignedPdfSha256 is null)
            actions.Add("record-cro-signing-pack");

        if (cro?.FilingStatus == FilingStatus.Approved && blockers.All(i => IsRevenueOnlyBlocker(i.Code)))
            actions.Add("mark-cro-submitted");
        if (cro?.FilingStatus == FilingStatus.Submitted && cro.PaymentCompleted == false)
            actions.Add("confirm-core-payment");
        if (cro?.FilingStatus == FilingStatus.Submitted && cro.PaymentCompleted)
            actions.Add("mark-cro-accepted");

        return actions.Distinct().ToList();
    }

    private static bool IsRevenueOnlyBlocker(string code) =>
        code is "ixbrl-internal-checks-required"
            or "ixbrl-generation-manual-handoff"
            or "taxonomy-not-revenue-accepted"
            or "corporation-tax-scope-required";

    private static bool HasCompleteExternalRevenueValidationEvidence(RevenueFilingPackage? package)
    {
        if (package is null
            || !package.IxbrlValidated
            || package.ExternalValidatedAt is null
            || package.ExternalValidatedAt.Value.Kind != DateTimeKind.Utc
            || package.ExternalValidatedAt > DateTime.UtcNow.AddMinutes(5)
            || string.IsNullOrWhiteSpace(package.ExternalValidationReference)
            || string.IsNullOrWhiteSpace(package.ExternalValidatorProvider)
            || string.IsNullOrWhiteSpace(package.ExternalValidatorVersion)
            || package.ExternalValidationWarningDisposition is not ("accepted" or "remediated")
            || !RetainedShaMatches(package.IxbrlArtifact, package.IxbrlSha256)
            || !string.Equals(package.ExternalValidationArtifactSha256, package.IxbrlSha256, StringComparison.OrdinalIgnoreCase)
            || package.ExternalTaxonomyPackageSha256?.Length != 64
            || !RetainedShaMatches(package.ExternalValidationResponseArtifact, package.ExternalValidationResponseSha256))
        {
            return false;
        }

        return true;
    }

    private static bool RetainedShaMatches(byte[]? content, string? expectedSha256)
    {
        if (content is not { Length: > 0 } || expectedSha256?.Length != 64)
            return false;

        var actual = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static FilingReadinessSignOffPacket BuildSignOffPacket(
        bool supportedPath,
        bool manualHandoff,
        bool charityReportingRequired,
        bool auditReportRequired,
        ElectedRegime? regime,
        CurrentDirectorsApproval? currentApproval,
        CroFilingPackage? cro,
        RevenueFilingPackage? revenue,
        IReadOnlyList<FilingReadinessEvidenceItem> evidence,
        IReadOnlyList<FilingReadinessIssue> blockers,
        IReadOnlyList<FilingReadinessIssue> warnings,
        IReadOnlyList<string> allowedNextActions)
    {
        var approvedBy = string.IsNullOrWhiteSpace(cro?.ApprovedBy) ? null : cro.ApprovedBy;
        var approvedAt = cro?.ApprovedAt;
        var directorsApproved = currentApproval is not null
            && cro?.ApprovalCapacity == FilingReleaseGate.BoardApprovalCapacity
            && cro.ApprovedAt is not null;
        var readyForDirectorsApproval = currentApproval is not null
            && cro?.FilingStatus != FilingStatus.Approved
            && supportedPath
            && !manualHandoff
            && blockers.All(issue => IsRevenueOnlyBlocker(issue.Code));
        var readyForExternalFiling = supportedPath
            && !manualHandoff
            && directorsApproved
            && blockers.Count == 0
            && warnings.Count == 0;
        var (state, stateLabel) = DetermineSignOffState(
            supportedPath,
            manualHandoff,
            directorsApproved,
            readyForDirectorsApproval,
            readyForExternalFiling,
            blockers);

        return new FilingReadinessSignOffPacket(
            state,
            stateLabel,
            readyForDirectorsApproval,
            readyForExternalFiling,
            approvedBy,
            approvedAt,
            BuildSignOffSteps(
                supportedPath,
                manualHandoff,
                charityReportingRequired,
                auditReportRequired,
                regime,
                currentApproval,
                revenue,
                evidence),
            blockers.Select(issue => issue.Message).Distinct().ToList(),
            warnings.Select(issue => issue.Message).Distinct().ToList(),
            allowedNextActions);
    }

    private static (string State, string Label) DetermineSignOffState(
        bool supportedPath,
        bool manualHandoff,
        bool directorsApproved,
        bool readyForDirectorsApproval,
        bool readyForExternalFiling,
        IReadOnlyList<FilingReadinessIssue> blockers)
    {
        if (!supportedPath || manualHandoff)
            return ("manual-handoff", "Manual professional handoff");
        if (readyForExternalFiling)
            return ("ready-for-external-filing", "Ready for external filing");
        if (directorsApproved && blockers.Count == 0)
            return ("approved-external-evidence-open", "Directors approved, external evidence open");
        if (readyForDirectorsApproval)
            return ("ready-for-directors-approval", "Directors' approval recorded, ready to approve the CRO pack");

        return ("blocked", "Blocked before directors' approval");
    }

    private static IReadOnlyList<FilingReadinessSignOffStep> BuildSignOffSteps(
        bool supportedPath,
        bool manualHandoff,
        bool charityReportingRequired,
        bool auditReportRequired,
        ElectedRegime? regime,
        CurrentDirectorsApproval? currentApproval,
        RevenueFilingPackage? revenue,
        IReadOnlyList<FilingReadinessEvidenceItem> evidence)
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
                "directors-approval",
                "Directors' approval (s.324)",
                currentApproval is not null ? "complete" : "pending",
                currentApproval is not null
                    ? $"Approved by the board on {currentApproval.Page.ApprovalDate:yyyy-MM-dd}; signed by {currentApproval.Page.FirstDirectorName}{(currentApproval.Page.SecondDirectorName is null ? "" : $" and {currentApproval.Page.SecondDirectorName}")}."
                    : "Record the board's approval: generate the s.324 page, sign it, upload it with the minute or resolution.",
                [IrishStatutoryRuleSources.CompaniesActSection324])
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

    internal static string OptionalAccountantReviewState(CroFilingPackage? cro) =>
        cro is { OptionalReviewerName: { Length: > 0 } name, ApproverProfessionalBody: { Length: > 0 } body, ApproverMembershipNumber: { Length: > 0 } membership, ApproverVerifiedAt: not null }
            ? $"Optional review recorded: {name} ({body}, {membership})"
            : "Not required by law — not recorded";

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
