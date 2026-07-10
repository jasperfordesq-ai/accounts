using System.Text;
using System.Text.Json;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public sealed class CharityReportingService(AccountsDbContext db)
{
    // Kept for compatibility with existing callers. Artifact eligibility is decided by
    // CharitySorpDecisionService using the reporting-period start date and charity form.
    public static int DetermineSorpTier(decimal grossIncome) =>
        CharitySorpDecisionService.Determine2026Tier(grossIncome);

    public async Task<SofaData> GenerateSofaAsync(int companyId, int periodId)
    {
        var periodExists = await db.AccountingPeriods
            .AsNoTracking()
            .AnyAsync(p => p.Id == periodId && p.CompanyId == companyId);
        if (!periodExists)
            throw new ResourceNotFoundException($"Period {periodId} not found");

        var funds = await db.FundBalances
            .AsNoTracking()
            .Where(f => f.PeriodId == periodId)
            .OrderBy(f => f.FundType).ThenBy(f => f.FundName)
            .ToListAsync();

        var unrestricted = funds.Where(f => f.FundType is "Unrestricted" or "Designated").ToList();
        var restricted = funds.Where(f => f.FundType == "Restricted").ToList();
        var endowment = funds.Where(f => f.FundType == "Endowment").ToList();

        return new SofaData(
            unrestricted.Select(ToLine).ToList(),
            restricted.Select(ToLine).ToList(),
            endowment.Select(ToLine).ToList(),
            funds.Sum(f => f.IncomingResources),
            funds.Sum(f => f.ResourcesExpended),
            funds.Sum(f => f.Transfers),
            funds.Sum(f => f.GainsLosses),
            funds.Sum(f => f.IncomingResources - f.ResourcesExpended + f.Transfers + f.GainsLosses),
            funds.Sum(f => f.OpeningBalance),
            funds.Sum(f => f.ClosingBalance));
    }

    public async Task<SofaReconciliation> ReconcileSofaToNetAssetsAsync(
        int companyId,
        int periodId,
        decimal balanceSheetNetAssets)
    {
        var sofa = await GenerateSofaAsync(companyId, periodId);
        var difference = decimal.Round(sofa.TotalClosingFunds - balanceSheetNetAssets, 2);
        return new SofaReconciliation(
            sofa.TotalClosingFunds,
            balanceSheetNetAssets,
            difference,
            Math.Abs(difference) <= 0.01m);
    }

    public async Task<TrusteesReportData> GenerateTarAsync(int companyId, int periodId)
    {
        var period = await LoadCharityPeriodAsync(companyId, periodId);
        var charityInfo = period.Company.CharityInfo;
        var trustees = GetTrusteesServingPeriod(period.Company.Officers, period.PeriodStart, period.PeriodEnd);
        var sofa = await GenerateSofaAsync(companyId, periodId);

        return new TrusteesReportData(
            period.Company.LegalName,
            charityInfo?.CharityNumber ?? "",
            period.Company.CroNumber ?? "",
            period.PeriodStart.ToString("dd MMMM yyyy"),
            period.PeriodEnd.ToString("dd MMMM yyyy"),
            trustees,
            charityInfo?.CharitableObjectives ?? "",
            charityInfo?.PrincipalActivities ?? "",
            sofa.TotalIncoming,
            sofa.TotalExpended,
            sofa.NetMovement,
            sofa.TotalClosingFunds,
            charityInfo?.GovernanceCodeCompliant,
            charityInfo?.GovernanceCodeNote,
            charityInfo?.GovernanceEvidenceReference,
            charityInfo?.GovernanceReviewedBy,
            charityInfo?.GovernanceReviewedAtUtc,
            charityInfo?.TrusteeRemunerationPaid ?? false,
            charityInfo?.TrusteeRemunerationAmount ?? 0,
            charityInfo?.TrusteeExpensesDetails,
            charityInfo?.HasInternationalTransfers ?? false,
            charityInfo?.InternationalTransferDetails,
            period.PeriodEnd.AddMonths(10).ToString("dd MMMM yyyy"));
    }

    public async Task<CharitySorpDecision> GetSorpDecisionAsync(int companyId, int periodId)
    {
        var period = await LoadCharityPeriodAsync(companyId, periodId);
        var info = period.Company.CharityInfo;
        return CharitySorpDecisionService.Decide(
            period.PeriodStart,
            period.Company.CompanyType,
            info?.CharityType,
            info?.GrossIncome ?? -1m);
    }

    public async Task<CharityFilingPackage> RecordTrusteeReviewAsync(
        int companyId,
        int periodId,
        bool accepted,
        string? evidenceReference,
        byte[]? evidenceArtifact,
        string reviewer)
    {
        if (!accepted)
            throw new BusinessRuleException("Trustee population must be explicitly accepted before charity artifacts can be generated.");
        if (string.IsNullOrWhiteSpace(evidenceReference))
            throw new BusinessRuleException("A retained trustee-review evidence reference is required.");
        if (evidenceArtifact is null || evidenceArtifact.Length == 0)
            throw new BusinessRuleException("A retained trustee-review evidence artifact is required.");
        if (string.IsNullOrWhiteSpace(reviewer))
            throw new BusinessRuleException("A named trustee-population reviewer is required.");

        var period = await LoadCharityPeriodAsync(companyId, periodId);
        var trustees = GetTrusteesServingPeriod(period.Company.Officers, period.PeriodStart, period.PeriodEnd);
        EnsureTrusteeDatesAreDeterminate(period.Company.Officers, period.PeriodStart, period.PeriodEnd);
        if (trustees.Count == 0)
            throw new BusinessRuleException("At least one director who served during the reporting period is required for the trustee population.");

        var populationJson = CanonicalTrusteePopulation(trustees);
        var package = period.CharityFilingPackage ?? new CharityFilingPackage { PeriodId = periodId };
        if (period.CharityFilingPackage is null)
            db.CharityFilingPackages.Add(package);

        InvalidateArtifacts(package);
        var decision = CharitySorpDecisionService.Decide(
            period.PeriodStart,
            period.Company.CompanyType,
            period.Company.CharityInfo?.CharityType,
            period.Company.CharityInfo?.GrossIncome ?? -1m);
        package.ManualProfessionalHandoffReason = decision.ManualProfessionalHandoffRequired
            ? decision.DecisionReason
            : null;
        package.TrusteeReviewAccepted = true;
        package.TrusteeReviewReference = evidenceReference.Trim();
        package.TrusteeReviewedBy = reviewer.Trim();
        package.TrusteeReviewedAtUtc = DateTime.UtcNow;
        package.TrusteeReviewArtifact = evidenceArtifact.ToArray();
        package.TrusteeReviewArtifactSha256 = FilingReleaseGate.ComputeSha256(evidenceArtifact);
        package.TrusteePopulationJson = populationJson;
        package.TrusteePopulationSha256 = FilingReleaseGate.ComputeSha256(Encoding.UTF8.GetBytes(populationJson));
        package.FilingStatus = FilingStatus.InProgress;
        await db.SaveChangesAsync();
        return package;
    }

    public async Task<CharityArtifactEvidence> BuildArtifactEvidenceAsync(
        int companyId,
        int periodId,
        decimal balanceSheetNetAssets,
        CharityFilingPackage package)
    {
        var period = await LoadCharityPeriodAsync(companyId, periodId);
        if (!period.Company.IsCharitableOrganisation)
            throw new BusinessRuleException("Charity artifacts can only be generated for a charitable organisation.");

        var info = period.Company.CharityInfo
            ?? throw new BusinessRuleException("Complete the charity profile before generating charity artifacts.");
        var decision = CharitySorpDecisionService.Decide(
            period.PeriodStart,
            period.Company.CompanyType,
            info.CharityType,
            info.GrossIncome);
        if (decision.ManualProfessionalHandoffRequired)
            throw new BusinessRuleException(decision.DecisionReason);

        EnsureRequiredProfileEvidence(info);
        EnsureTrusteeReviewEvidence(package);
        EnsureTrusteeDatesAreDeterminate(period.Company.Officers, period.PeriodStart, period.PeriodEnd);

        var trustees = GetTrusteesServingPeriod(period.Company.Officers, period.PeriodStart, period.PeriodEnd);
        if (trustees.Count == 0)
            throw new BusinessRuleException("At least one director who served during the reporting period is required.");
        var currentTrusteeJson = CanonicalTrusteePopulation(trustees);
        var currentTrusteeHash = FilingReleaseGate.ComputeSha256(Encoding.UTF8.GetBytes(currentTrusteeJson));
        if (!string.Equals(currentTrusteeHash, package.TrusteePopulationSha256, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("The directors serving during the period changed after trustee review. Repeat the trustee-population review.");

        var sofa = await GenerateSofaAsync(companyId, periodId);
        if (sofa.UnrestrictedFunds.Count + sofa.RestrictedFunds.Count + sofa.EndowmentFunds.Count == 0)
            throw new BusinessRuleException("At least one reconciled charity fund is required before generating the SoFA.");
        var reconciliation = await ReconcileSofaToNetAssetsAsync(companyId, periodId, balanceSheetNetAssets);
        if (!reconciliation.Reconciles)
            throw new BusinessRuleException($"SoFA closing funds ({reconciliation.TotalClosingFunds:N2}) must equal balance-sheet net assets ({reconciliation.BalanceSheetNetAssets:N2}) before charity artifacts can be generated.");

        var tar = await GenerateTarAsync(companyId, periodId);
        var sourcePayload = new
        {
            CandidateIndependentVersion = 1,
            Company = new
            {
                period.Company.Id,
                period.Company.LegalName,
                period.Company.CroNumber,
                period.Company.CompanyType,
                period.Company.IsCharitableOrganisation
            },
            Period = new { period.Id, period.PeriodStart, period.PeriodEnd },
            Charity = new
            {
                info.CharityNumber,
                info.CharityType,
                info.GrossIncome,
                info.CharitableObjectives,
                info.PrincipalActivities,
                info.GovernanceCodeCompliant,
                info.GovernanceCodeNote,
                info.GovernanceEvidenceReference,
                info.GovernanceReviewedBy,
                info.GovernanceReviewedAtUtc,
                info.GovernanceEvidenceArtifactSha256,
                info.HasInternationalTransfers,
                info.InternationalTransferDetails,
                info.TrusteeRemunerationPaid,
                info.TrusteeRemunerationAmount,
                info.TrusteeExpensesDetails
            },
            TrusteeReview = new
            {
                package.TrusteeReviewAccepted,
                package.TrusteeReviewReference,
                package.TrusteeReviewedBy,
                package.TrusteeReviewedAtUtc,
                package.TrusteeReviewArtifactSha256,
                CurrentTrusteePopulationSha256 = currentTrusteeHash,
                CurrentTrusteePopulation = trustees
            },
            Sorp = new
            {
                decision.FrameworkCode,
                decision.Tier,
                decision.SofaBasis,
                decision.DecisionSha256,
                decision.Sources
            },
            Sofa = sofa,
            BalanceSheetNetAssets = decimal.Round(balanceSheetNetAssets, 2),
            ReconciliationDifference = reconciliation.Difference
        };
        var sourceBytes = JsonSerializer.SerializeToUtf8Bytes(sourcePayload);
        var sourceFingerprint = FilingReleaseGate.ComputeSha256(sourceBytes);

        return new CharityArtifactEvidence(
            period,
            info,
            decision,
            sofa,
            tar,
            reconciliation,
            trustees,
            sourceFingerprint);
    }

    public async Task<CharityInfo> SaveCharityInfoAsync(
        int companyId,
        CharityInfo input,
        string? governanceReviewer = null)
    {
        var existing = await db.CharityInfos.FirstOrDefaultAsync(c => c.CompanyId == companyId);
        var retainExistingGovernanceEvidence = existing is not null
            && input.GovernanceEvidenceArtifact is not { Length: > 0 }
            && existing.GovernanceCodeCompliant == input.GovernanceCodeCompliant
            && string.Equals(existing.GovernanceCodeNote, input.GovernanceCodeNote, StringComparison.Ordinal)
            && string.Equals(existing.GovernanceEvidenceReference, input.GovernanceEvidenceReference, StringComparison.Ordinal);
        if (existing is null)
        {
            input.CompanyId = companyId;
            input.SorpTier = input.GrossIncome < 0m ? 0 : DetermineSorpTier(input.GrossIncome);
            existing = input;
            db.CharityInfos.Add(existing);
        }
        else if (!retainExistingGovernanceEvidence)
        {
            existing.CharityNumber = input.CharityNumber;
            existing.CharityType = input.CharityType;
            existing.GrossIncome = input.GrossIncome;
            existing.SorpTier = input.GrossIncome < 0m ? 0 : DetermineSorpTier(input.GrossIncome);
            existing.CharitableObjectives = input.CharitableObjectives;
            existing.PrincipalActivities = input.PrincipalActivities;
            existing.GovernanceCodeCompliant = input.GovernanceCodeCompliant;
            existing.GovernanceCodeNote = input.GovernanceCodeNote;
            existing.GovernanceEvidenceReference = input.GovernanceEvidenceReference;
            existing.HasInternationalTransfers = input.HasInternationalTransfers;
            existing.InternationalTransferDetails = input.InternationalTransferDetails;
            existing.TrusteeRemunerationPaid = input.TrusteeRemunerationPaid;
            existing.TrusteeRemunerationAmount = input.TrusteeRemunerationAmount;
            existing.TrusteeExpensesDetails = input.TrusteeExpensesDetails;
        }

        if (input.GovernanceEvidenceArtifact is { Length: > 0 })
        {
            existing.GovernanceEvidenceArtifact = input.GovernanceEvidenceArtifact.ToArray();
            existing.GovernanceEvidenceArtifactSha256 = FilingReleaseGate.ComputeSha256(input.GovernanceEvidenceArtifact);
            existing.GovernanceReviewedBy = governanceReviewer?.Trim();
            existing.GovernanceReviewedAtUtc = DateTime.UtcNow;
        }
        else
        {
            existing.GovernanceEvidenceArtifact = null;
            existing.GovernanceEvidenceArtifactSha256 = null;
            existing.GovernanceReviewedBy = null;
            existing.GovernanceReviewedAtUtc = null;
        }

        var packages = await db.CharityFilingPackages
            .Where(p => p.Period.CompanyId == companyId)
            .ToListAsync();
        foreach (var package in packages)
            InvalidateArtifacts(package);

        await db.SaveChangesAsync();
        return existing;
    }

    public static void InvalidateArtifacts(CharityFilingPackage package)
    {
        package.SofaArtifact = null;
        package.SofaSha256 = null;
        package.TrusteesReportArtifact = null;
        package.TrusteesReportSha256 = null;
        package.SofaGenerated = false;
        package.TrusteesReportGenerated = false;
        package.ArtifactReleaseCandidate = null;
        package.ArtifactSourceFingerprintSha256 = null;
        package.SorpFrameworkCode = null;
        package.SorpTier = null;
        package.SofaBasis = null;
        package.SorpDecisionSha256 = null;
        package.CharityNumberSnapshot = null;
        package.SofaClosingFunds = null;
        package.BalanceSheetNetAssets = null;
        package.ReconciliationDifference = null;
        package.ReconciledAtUtc = null;
        package.ManualProfessionalHandoffReason = null;
        package.ApprovedBy = null;
        package.ApprovedAt = null;
        package.ApprovedArtifactManifestSha256 = null;
        package.ApprovedReleaseCandidate = null;
        package.ApproverProfessionalBody = null;
        package.ApproverMembershipNumber = null;
        package.ApproverTenantId = null;
        package.ApprovalScope = null;
        package.ApprovalCapacity = null;
        package.ApprovalDecision = null;
        package.ApproverVerificationReference = null;
        package.ApproverVerificationArtifact = null;
        package.ApproverVerificationArtifactSha256 = null;
        package.ApproverVerifiedAt = null;
        package.ApproverCredentialValidUntil = null;
        package.SubmittedBy = null;
        package.SubmittedAt = null;
        package.AcceptedBy = null;
        package.AcceptedAt = null;
        package.AnnualReturnReference = null;
        package.Status = FilingPackageStatus.Draft;
        package.FilingStatus = FilingStatus.InProgress;
    }

    public static void InvalidateTrusteeReview(CharityFilingPackage package)
    {
        InvalidateArtifacts(package);
        package.TrusteeReviewAccepted = false;
        package.TrusteeReviewReference = null;
        package.TrusteeReviewedBy = null;
        package.TrusteeReviewedAtUtc = null;
        package.TrusteeReviewArtifact = null;
        package.TrusteeReviewArtifactSha256 = null;
        package.TrusteePopulationJson = null;
        package.TrusteePopulationSha256 = null;
    }

    private async Task<AccountingPeriod> LoadCharityPeriodAsync(int companyId, int periodId) =>
        await db.AccountingPeriods
            .Include(p => p.Company).ThenInclude(c => c.Officers)
            .Include(p => p.Company).ThenInclude(c => c.CharityInfo)
            .Include(p => p.CharityFilingPackage)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
        ?? throw new ResourceNotFoundException($"Period {periodId} not found");

    private static List<CharityTrusteeLine> GetTrusteesServingPeriod(
        IEnumerable<CompanyOfficer> officers,
        DateOnly periodStart,
        DateOnly periodEnd) =>
        officers
            .Where(o => o.Role == OfficerRole.Director)
            .Where(o => o.AppointedDate is not null && o.AppointedDate <= periodEnd)
            .Where(o => o.ResignedDate is null || o.ResignedDate >= periodStart)
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.Id)
            .Select(o => new CharityTrusteeLine(o.Id, o.Name, o.AppointedDate!.Value, o.ResignedDate))
            .ToList();

    private static void EnsureTrusteeDatesAreDeterminate(
        IEnumerable<CompanyOfficer> officers,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        var missing = officers
            .Where(o => o.Role == OfficerRole.Director && o.AppointedDate is null)
            .Where(o => o.ResignedDate is null || o.ResignedDate >= periodStart)
            .Select(o => o.Name)
            .OrderBy(x => x)
            .ToList();
        if (missing.Count > 0)
            throw new BusinessRuleException($"Appointment dates are required to determine whether these directors served during the period: {string.Join(", ", missing)}.");

        var invalid = officers
            .Where(o => o.Role == OfficerRole.Director && o.AppointedDate > periodEnd && o.ResignedDate is not null && o.ResignedDate < o.AppointedDate)
            .Select(o => o.Name)
            .ToList();
        if (invalid.Count > 0)
            throw new BusinessRuleException($"Director service dates are invalid for: {string.Join(", ", invalid)}.");
    }

    private static string CanonicalTrusteePopulation(IReadOnlyList<CharityTrusteeLine> trustees) =>
        JsonSerializer.Serialize(trustees);

    private static void EnsureRequiredProfileEvidence(CharityInfo info)
    {
        if (string.IsNullOrWhiteSpace(info.CharityNumber))
            throw new BusinessRuleException("A Charities Regulator number is required.");
        if (string.IsNullOrWhiteSpace(info.CharitableObjectives) || string.IsNullOrWhiteSpace(info.PrincipalActivities))
            throw new BusinessRuleException("Charitable objectives and principal activities are required for the Trustees' Annual Report.");
        if (info.GovernanceCodeCompliant is null)
            throw new BusinessRuleException("Answer the Charities Governance Code question explicitly before generation.");
        if (string.IsNullOrWhiteSpace(info.GovernanceEvidenceReference)
            || string.IsNullOrWhiteSpace(info.GovernanceReviewedBy)
            || info.GovernanceReviewedAtUtc is null)
            throw new BusinessRuleException("Governance status requires a retained evidence reference, named reviewer and review time.");
        if (info.GovernanceEvidenceArtifact is null || info.GovernanceEvidenceArtifact.Length == 0
            || string.IsNullOrWhiteSpace(info.GovernanceEvidenceArtifactSha256)
            || !string.Equals(
                FilingReleaseGate.ComputeSha256(info.GovernanceEvidenceArtifact),
                info.GovernanceEvidenceArtifactSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("Governance status requires a retained evidence artifact with a matching SHA-256 hash.");
    }

    private static void EnsureTrusteeReviewEvidence(CharityFilingPackage package)
    {
        if (!package.TrusteeReviewAccepted
            || string.IsNullOrWhiteSpace(package.TrusteeReviewReference)
            || string.IsNullOrWhiteSpace(package.TrusteeReviewedBy)
            || package.TrusteeReviewedAtUtc is null
            || string.IsNullOrWhiteSpace(package.TrusteePopulationSha256))
            throw new BusinessRuleException("A named, accepted trustee-population review is required before generation.");
        if (package.TrusteeReviewArtifact is null || package.TrusteeReviewArtifact.Length == 0
            || string.IsNullOrWhiteSpace(package.TrusteeReviewArtifactSha256)
            || !string.Equals(
                FilingReleaseGate.ComputeSha256(package.TrusteeReviewArtifact),
                package.TrusteeReviewArtifactSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("Trustee review requires a retained evidence artifact with a matching SHA-256 hash.");
    }

    private static FundLine ToLine(FundBalance fund) => new(
        fund.FundName,
        fund.FundType,
        fund.OpeningBalance,
        fund.IncomingResources,
        fund.ResourcesExpended,
        fund.Transfers,
        fund.GainsLosses,
        fund.ClosingBalance);
}

public sealed record CharityTrusteeLine(int OfficerId, string Name, DateOnly AppointedDate, DateOnly? ResignedDate);

public sealed record CharityArtifactEvidence(
    AccountingPeriod Period,
    CharityInfo CharityInfo,
    CharitySorpDecision SorpDecision,
    SofaData Sofa,
    TrusteesReportData TrusteesReport,
    SofaReconciliation Reconciliation,
    IReadOnlyList<CharityTrusteeLine> Trustees,
    string SourceFingerprintSha256);

public sealed record FundLine(
    string FundName,
    string FundType,
    decimal OpeningBalance,
    decimal IncomingResources,
    decimal ResourcesExpended,
    decimal Transfers,
    decimal GainsLosses,
    decimal ClosingBalance);

public sealed record SofaReconciliation(
    decimal TotalClosingFunds,
    decimal BalanceSheetNetAssets,
    decimal Difference,
    bool Reconciles);

public sealed record SofaData(
    List<FundLine> UnrestrictedFunds,
    List<FundLine> RestrictedFunds,
    List<FundLine> EndowmentFunds,
    decimal TotalIncoming,
    decimal TotalExpended,
    decimal TotalTransfers,
    decimal TotalGainsLosses,
    decimal NetMovement,
    decimal TotalOpeningFunds,
    decimal TotalClosingFunds);

public sealed record TrusteesReportData(
    string CharityName,
    string CharityNumber,
    string CroNumber,
    string PeriodStart,
    string PeriodEnd,
    List<CharityTrusteeLine> Trustees,
    string CharitableObjectives,
    string PrincipalActivities,
    decimal TotalIncome,
    decimal TotalExpenditure,
    decimal NetMovement,
    decimal ClosingFunds,
    bool? GovernanceCodeCompliant,
    string? GovernanceCodeNote,
    string? GovernanceEvidenceReference,
    string? GovernanceReviewedBy,
    DateTime? GovernanceReviewedAtUtc,
    bool TrusteeRemunerationPaid,
    decimal TrusteeRemunerationAmount,
    string? TrusteeExpensesDetails,
    bool HasInternationalTransfers,
    string? InternationalTransferDetails,
    string FilingDeadline);
