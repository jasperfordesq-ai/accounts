using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Endpoints;

public static class CharityEndpoints
{
    public static void MapCharityEndpoints(this WebApplication app)
    {
        var companyGroup = app.MapGroup("/api/companies/{companyId:int}/charity").WithTags("Charity");
        var periodGroup = app.MapGroup("/api/companies/{companyId:int}/periods/{periodId:int}/charity").WithTags("Charity");

        companyGroup.MapGet("/info", async (int companyId, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            var info = await db.CharityInfos.FirstOrDefaultAsync(c => c.CompanyId == companyId);
            return info != null ? Results.Ok(info) : Results.Ok(new { message = "No charity info configured" });
        });

        companyGroup.MapPut("/info", SaveCharityInfoEndpointAsync);

        periodGroup.MapGet("/sorp-decision", async (int companyId, int periodId, CharityReportingService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();
            return Results.Ok(await service.GetSorpDecisionAsync(companyId, periodId));
        });

        periodGroup.MapPut("/trustee-review", RecordTrusteeReviewEndpointAsync);

        periodGroup.MapGet("/sofa", async (int companyId, int periodId, CharityReportingService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var sofa = await service.GenerateSofaAsync(companyId, periodId);
            return Results.Ok(sofa);
        });

        // filing-charity-pdf-and-reconciliation: the SoFA total funds must reconcile to the balance-sheet
        // net assets; this surfaces the difference so the UI can block/warn on a mismatch.
        periodGroup.MapGet("/sofa/reconciliation", async (int companyId, int periodId, CharityReportingService service, FinancialStatementsService statements, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var balanceSheet = await statements.GetBalanceSheetAsync(companyId, periodId);
            var reconciliation = await service.ReconcileSofaToNetAssetsAsync(companyId, periodId, balanceSheet.NetAssets);
            return Results.Ok(reconciliation);
        });

        periodGroup.MapGet("/trustees-report", async (int companyId, int periodId, CharityReportingService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var tar = await service.GenerateTarAsync(companyId, periodId);
            return Results.Ok(tar);
        });

        periodGroup.MapGet("/artifacts/status", GetArtifactStatusEndpointAsync);
        periodGroup.MapGet("/sofa/review-pdf", DownloadSofaReviewEndpointAsync);
        periodGroup.MapGet("/trustees-report/review-pdf", DownloadTrusteesReportReviewEndpointAsync);
        periodGroup.MapGet("/sofa/final", DownloadSofaFinalEndpointAsync);
        periodGroup.MapGet("/trustees-report/final", DownloadTrusteesReportFinalEndpointAsync);

        periodGroup.MapGet("/funds", ListFundBalancesEndpointAsync);

        periodGroup.MapPost("/funds", CreateFundBalanceEndpointAsync);

        periodGroup.MapPut("/funds/{id:int}", UpdateFundBalanceEndpointAsync);

        periodGroup.MapDelete("/funds/{id:int}", DeleteFundBalanceEndpointAsync);
    }

    public static async Task<IResult> SaveCharityInfoEndpointAsync(
        int companyId,
        CharityInfoInput input,
        CharityReportingService service,
        AccountingWriteGuard writeGuard,
        AuditService audit,
        AccountsDbContext db,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        if (await writeGuard.BlockIfCompanyMasterDataLockedAsync(companyId) is { } blocked)
            return blocked;

        if (input.GovernanceEvidenceArtifact is { Length: > 5 * 1024 * 1024 })
            return Results.BadRequest(new { error = "Governance evidence artifacts must not exceed 5 MB." });

        if (input.GovernanceCodeCompliant is null)
            return Results.BadRequest(new { error = "Answer the Charities Governance Code question explicitly." });
        if (string.IsNullOrWhiteSpace(input.GovernanceEvidenceReference))
            return Results.BadRequest(new { error = "A governance evidence reference is required." });
        if (input.GovernanceCodeCompliant == false && string.IsNullOrWhiteSpace(input.GovernanceCodeNote))
            return Results.BadRequest(new { error = "Explain the governance position when compliance is answered No." });

        var existing = await db.CharityInfos
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CompanyId == companyId);
        var retainedEvidenceStillApplies = existing is not null
            && !string.IsNullOrWhiteSpace(existing.GovernanceEvidenceArtifactSha256)
            && existing.GovernanceCodeCompliant == input.GovernanceCodeCompliant
            && string.Equals(existing.GovernanceCodeNote, input.GovernanceCodeNote, StringComparison.Ordinal)
            && string.Equals(existing.GovernanceEvidenceReference, input.GovernanceEvidenceReference, StringComparison.Ordinal);
        if (input.GovernanceEvidenceArtifact is not { Length: > 0 } && !retainedEvidenceStillApplies)
            return Results.BadRequest(new { error = "A retained governance evidence artifact is required for this answer." });
        var oldValue = existing is null ? null : CharityInfoSnapshot(existing);
        var charityInfo = input.ToEntity(companyId);
        var user = AuthContext.RequireUser(context);
        var result = await service.SaveCharityInfoAsync(
            companyId,
            charityInfo,
            AuthenticatedIdentity.ReviewerDisplayName(user));
        await audit.LogAsync(
            companyId,
            null,
            "CharityInfo",
            result.Id,
            existing is null ? AuditEventCodes.CharityInfoCreated : AuditEventCodes.CharityInfoUpdated,
            oldValue,
            CharityInfoSnapshot(result),
            AuthenticatedIdentity.AuditUserId(user));

        return Results.Ok(result);
    }

    public static async Task<IResult> RecordTrusteeReviewEndpointAsync(
        int companyId,
        int periodId,
        CharityTrusteeReviewInput input,
        CharityReportingService service,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();
        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;
        if (input.EvidenceArtifact is { Length: > 5 * 1024 * 1024 })
            return Results.BadRequest(new { error = "Trustee-review evidence artifacts must not exceed 5 MB." });

        var user = AuthContext.RequireUser(context);
        var package = await service.RecordTrusteeReviewAsync(
            companyId,
            periodId,
            input.Accepted,
            input.EvidenceReference,
            input.EvidenceArtifact,
            AuthenticatedIdentity.ReviewerDisplayName(user));
        await audit.LogAsync(
            companyId,
            periodId,
            "CharityFilingPackage",
            package.Id,
            "CharityTrusteePopulationReviewed",
            null,
            new
            {
                package.TrusteeReviewAccepted,
                package.TrusteeReviewReference,
                package.TrusteeReviewedBy,
                package.TrusteeReviewedAtUtc,
                package.TrusteeReviewArtifactSha256,
                package.TrusteePopulationSha256
            },
            AuthenticatedIdentity.AuditUserId(user));
        return Results.Ok(package);
    }

    public static async Task<IResult> GetArtifactStatusEndpointAsync(
        int companyId,
        int periodId,
        CharityReportingService service,
        AccountsDbContext db,
        HttpContext context)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        var package = await db.CharityFilingPackages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PeriodId == periodId);
        var decision = await service.GetSorpDecisionAsync(companyId, periodId);
        return Results.Ok(new
        {
            decision,
            package = package is null ? null : new
            {
                package.FilingStatus,
                package.SofaGenerated,
                package.TrusteesReportGenerated,
                package.SofaSha256,
                package.TrusteesReportSha256,
                package.ArtifactReleaseCandidate,
                package.ArtifactSourceFingerprintSha256,
                package.SorpFrameworkCode,
                package.SorpTier,
                package.SofaBasis,
                package.CharityNumberSnapshot,
                package.SofaClosingFunds,
                package.BalanceSheetNetAssets,
                package.ReconciliationDifference,
                package.ReconciledAtUtc,
                package.TrusteeReviewAccepted,
                package.TrusteeReviewReference,
                package.TrusteeReviewedBy,
                package.TrusteeReviewedAtUtc,
                package.TrusteeReviewArtifactSha256,
                package.TrusteePopulationSha256,
                package.ManualProfessionalHandoffReason,
                package.ApprovedBy,
                package.ApprovedAt,
                package.ApprovedArtifactManifestSha256,
                package.ApprovedReleaseCandidate
            }
        });
    }

    public static Task<IResult> DownloadSofaReviewEndpointAsync(
        int companyId,
        int periodId,
        CharityReportingService reporting,
        CharityPdfService pdf,
        FinancialStatementsService statements,
        AccountsDbContext db,
        HttpContext context) =>
        DownloadReviewEndpointAsync(companyId, periodId, true, reporting, pdf, statements, db, context);

    public static Task<IResult> DownloadTrusteesReportReviewEndpointAsync(
        int companyId,
        int periodId,
        CharityReportingService reporting,
        CharityPdfService pdf,
        FinancialStatementsService statements,
        AccountsDbContext db,
        HttpContext context) =>
        DownloadReviewEndpointAsync(companyId, periodId, false, reporting, pdf, statements, db, context);

    public static async Task<IResult> DownloadSofaFinalEndpointAsync(
        int companyId,
        int periodId,
        FilingReleaseGate gate,
        AccountsDbContext db,
        HttpContext context) =>
        await DownloadFinalEndpointAsync(companyId, periodId, FilingReleaseArtifact.CharitySofa, gate, db, context);

    public static async Task<IResult> DownloadTrusteesReportFinalEndpointAsync(
        int companyId,
        int periodId,
        FilingReleaseGate gate,
        AccountsDbContext db,
        HttpContext context) =>
        await DownloadFinalEndpointAsync(companyId, periodId, FilingReleaseArtifact.CharityTrusteesReport, gate, db, context);

    private static async Task<IResult> DownloadReviewEndpointAsync(
        int companyId,
        int periodId,
        bool sofa,
        CharityReportingService reporting,
        CharityPdfService pdf,
        FinancialStatementsService statements,
        AccountsDbContext db,
        HttpContext context)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        var package = await db.CharityFilingPackages.FirstOrDefaultAsync(p => p.PeriodId == periodId);
        if (package is null)
            throw new BusinessRuleException("Record and accept the trustee-population review before downloading review PDFs.");
        var balanceSheet = await statements.GetBalanceSheetAsync(companyId, periodId);
        var evidence = await reporting.BuildArtifactEvidenceAsync(companyId, periodId, balanceSheet.NetAssets, package);
        var bytes = sofa
            ? pdf.GenerateSofa(evidence, reviewCopy: true)
            : pdf.GenerateTrusteesAnnualReport(evidence, reviewCopy: true);
        return Results.File(
            bytes,
            "application/pdf",
            sofa
                ? $"REVIEW_NOT_FOR_FILING_charity_sofa_{periodId}.pdf"
                : $"REVIEW_NOT_FOR_FILING_trustees_annual_report_{periodId}.pdf");
    }

    private static async Task<IResult> DownloadFinalEndpointAsync(
        int companyId,
        int periodId,
        FilingReleaseArtifact artifactType,
        FilingReleaseGate gate,
        AccountsDbContext db,
        HttpContext context)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        var artifact = await gate.GetFinalArtifactAsync(
            companyId,
            periodId,
            artifactType,
            AuthenticatedIdentity.AuditUserId(AuthContext.RequireUser(context)));
        context.Response.Headers["X-Artifact-Sha256"] = artifact.Sha256;
        context.Response.Headers["X-Release-Candidate"] = artifact.ReleaseCandidate;
        return Results.File(artifact.Content, artifact.MediaType, artifact.FileName);
    }

    public static async Task<IResult> ListFundBalancesEndpointAsync(
        int companyId,
        int periodId,
        AccountsDbContext db,
        HttpContext context)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        var funds = await db.FundBalances
            .Where(f => f.PeriodId == periodId)
            .OrderBy(f => f.FundType)
            .ThenBy(f => f.FundName)
            .ToListAsync();
        return Results.Ok(funds);
    }

    public static async Task<IResult> CreateFundBalanceEndpointAsync(
        int companyId,
        int periodId,
        FundBalanceInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        if (await ValidateFundWritePeriodAsync(db, companyId, periodId) is { } blocked)
            return blocked;

        if (ValidateFundBalanceInput(input) is { } invalid)
            return invalid;

        var user = AuthContext.RequireUser(context);
        var fund = input.ToEntity(periodId);
        PrepareFundBalance(fund, input);
        db.FundBalances.Add(fund);
        await InvalidateCharityPackageAsync(db, periodId);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "FundBalance",
            fund.Id,
            AuditEventCodes.FundBalanceCreated,
            null,
            FundBalanceSnapshot(fund),
            AuthenticatedIdentity.AuditUserId(user));

        return Results.Created($"/api/companies/{companyId}/periods/{periodId}/charity/funds/{fund.Id}", fund);
    }

    public static async Task<IResult> UpdateFundBalanceEndpointAsync(
        int companyId,
        int periodId,
        int id,
        FundBalanceInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        if (await ValidateFundWritePeriodAsync(db, companyId, periodId) is { } blocked)
            return blocked;

        if (ValidateFundBalanceInput(input) is { } invalid)
            return invalid;

        var item = await db.FundBalances.FirstOrDefaultAsync(f => f.Id == id && f.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        var user = AuthContext.RequireUser(context);
        var oldValue = FundBalanceSnapshot(item);
        ApplyFundBalance(item, input);
        await InvalidateCharityPackageAsync(db, periodId);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "FundBalance",
            item.Id,
            AuditEventCodes.FundBalanceUpdated,
            oldValue,
            FundBalanceSnapshot(item),
            AuthenticatedIdentity.AuditUserId(user));

        return Results.Ok(item);
    }

    public static async Task<IResult> DeleteFundBalanceEndpointAsync(
        int companyId,
        int periodId,
        int id,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        if (await ValidateFundWritePeriodAsync(db, companyId, periodId) is { } blocked)
            return blocked;

        var item = await db.FundBalances.FirstOrDefaultAsync(f => f.Id == id && f.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        var user = AuthContext.RequireUser(context);
        var oldValue = FundBalanceSnapshot(item);
        db.FundBalances.Remove(item);
        await InvalidateCharityPackageAsync(db, periodId);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "FundBalance",
            id,
            AuditEventCodes.FundBalanceDeleted,
            oldValue,
            new { Deleted = true },
            AuthenticatedIdentity.AuditUserId(user));

        return Results.NoContent();
    }

    private static async Task<IResult?> ValidateFundWritePeriodAsync(
        AccountsDbContext db,
        int companyId,
        int periodId)
    {
        var period = await db.AccountingPeriods
            .AsNoTracking()
            .Where(p => p.Id == periodId && p.CompanyId == companyId)
            .Select(p => new { p.Status, p.LockedAt })
            .FirstOrDefaultAsync();
        if (period is null)
            return Results.NotFound();

        return period.Status is PeriodStatus.Finalised or PeriodStatus.Filed || period.LockedAt is not null
            ? Results.Conflict(new { error = "Accounting period is locked. Reopen the period before changing charity fund balances." })
            : null;
    }

    private static void PrepareFundBalance(FundBalance fund, FundBalanceInput input)
    {
        fund.ClosingBalance = CalculateClosingBalance(input);
    }

    private static IResult? ValidateFundBalanceInput(FundBalanceInput fund)
    {
        if (string.IsNullOrWhiteSpace(fund.FundName))
            return Results.BadRequest(new { error = "Fund name is required." });

        if (string.IsNullOrWhiteSpace(fund.FundType))
            return Results.BadRequest(new { error = "Fund type is required." });

        return null;
    }

    private static void ApplyFundBalance(FundBalance target, FundBalanceInput input)
    {
        target.FundName = input.FundName;
        target.FundType = input.FundType;
        target.OpeningBalance = input.OpeningBalance;
        target.IncomingResources = input.IncomingResources;
        target.ResourcesExpended = input.ResourcesExpended;
        target.Transfers = input.Transfers;
        target.GainsLosses = input.GainsLosses;
        target.ClosingBalance = CalculateClosingBalance(input);
        target.Notes = input.Notes;
    }

    private static decimal CalculateClosingBalance(FundBalanceInput fund) =>
        fund.OpeningBalance + fund.IncomingResources - fund.ResourcesExpended + fund.Transfers + fund.GainsLosses;

    private static async Task InvalidateCharityPackageAsync(AccountsDbContext db, int periodId)
    {
        var package = await db.CharityFilingPackages.FirstOrDefaultAsync(p => p.PeriodId == periodId);
        if (package is not null)
            CharityReportingService.InvalidateArtifacts(package);
    }

    private static object CharityInfoSnapshot(CharityInfo info) => new
    {
        info.Id,
        info.CompanyId,
        info.CharityNumber,
        info.CharityType,
        info.GrossIncome,
        info.SorpTier,
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
    };

    private static object FundBalanceSnapshot(FundBalance fund) => new
    {
        fund.Id,
        fund.PeriodId,
        fund.FundName,
        fund.FundType,
        fund.OpeningBalance,
        fund.IncomingResources,
        fund.ResourcesExpended,
        fund.Transfers,
        fund.GainsLosses,
        fund.ClosingBalance,
        fund.Notes
    };
}
