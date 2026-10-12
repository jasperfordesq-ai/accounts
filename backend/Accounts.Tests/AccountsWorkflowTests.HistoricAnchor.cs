using System.Security.Cryptography;
using System.Text.Json;
using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Middleware;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounts.Tests;

// Item 2(b): historic figures keyed in from accounts filed elsewhere, recorded on the predecessor's
// own size classification as a provenance-marked chain root. The s.280A test is never relaxed: an
// anchor without an attested class makes the next year need both years' raw figures to pass.
public partial class AccountsWorkflowTests
{
    // Captured from SizeClassificationService.ComputeInputFingerprint BEFORE the historic-anchor
    // change. Every existing Computed decision must keep hashing to exactly these bytes, otherwise
    // every retained decision chain would silently become stale (or, worse, a format drift could
    // hide a real input change).
    private const string PinnedFirstYearFingerprint = "2891882733018ff236f1f8f5baad02df8bf64fa748b73dda952cb12cc6c51a55";
    private const string PinnedSubsequentYearFingerprint = "4bc238db5848a2f21e6bb2c009ba4cc3360bff61b9f724cefcab222f28011e7e";

    [Fact]
    public void ComputedFingerprints_AreByteIdenticalToThePreChangeCapture()
    {
        var (first, second) = PinnedFingerprintFixture();

        Assert.Equal(
            PinnedFirstYearFingerprint,
            SizeClassificationService.ComputeInputFingerprint(first, first.SizeClassification!, new DateOnly(2024, 1, 1)));
        Assert.Equal(
            PinnedSubsequentYearFingerprint,
            SizeClassificationService.ComputeInputFingerprint(second, second.SizeClassification!, new DateOnly(2023, 1, 1), first));
    }

    // ---- Anchor as a chain root ------------------------------------------------------------------

    [Fact]
    public async Task HistoricAnchor_OnTheImmediatePredecessor_LetsTheNextPeriodClassify()
    {
        await using var db = CreateDbContext();
        var (_, _, third, current) = await SeedHistoricChainAsync(db);

        var anchor = await RecordAnchorAsync(db, third, SmallButNotMicro);

        Assert.Equal(SizeInputBasis.HistoricAnchor, anchor.InputBasis);
        Assert.Equal(CompanySizeClass.Small, anchor.RawCurrentClass);
        Assert.Null(anchor.HistoricAttestedEffectiveClass);
        // Without an attested class the anchor is not treated as qualified for carry-forward.
        Assert.Equal(CompanySizeClass.Large, anchor.CalculatedClass);
        Assert.False(string.IsNullOrWhiteSpace(anchor.DecisionInputFingerprintSha256));

        var prerequisites = await ClassificationService(db).GetPrerequisitesAsync(current.CompanyId, current.Id);
        Assert.True(prerequisites.Ready);

        AddFiguresRow(db, current, SmallButNotMicro);
        await db.SaveChangesAsync();
        var result = await ClassificationService(db).ClassifyAsync(current.CompanyId, current.Id);

        // Both years' raw figures meet the small conditions, so the two-year test itself passes.
        Assert.Equal(CompanySizeClass.Small, result.CalculatedClass);
        Assert.Contains("keyed in from accounts filed elsewhere", result.QualificationNotes, StringComparison.Ordinal);
        var decided = await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == current.Id);
        Assert.Equal(SizeInputBasis.Computed, decided.InputBasis);
        Assert.Equal(CompanySizeClass.Large, decided.PriorYearClass);
    }

    [Fact]
    public async Task HistoricAnchor_WithoutAttestedClass_NeedsBothYearsRawFiguresToPass()
    {
        // Current year exceeds the small limits; the anchor year met them. Carry-forward from a small
        // prior year would keep the company small, but an anchor without an attested class must not:
        // the current AND prior raw tests must both pass (s.280A), so the result is medium.
        await using var db = CreateDbContext();
        var (_, _, third, current) = await SeedHistoricChainAsync(db);
        await RecordAnchorAsync(db, third, SmallButNotMicro);
        AddFiguresRow(db, current, MediumOnly);
        await db.SaveChangesAsync();

        var result = await ClassificationService(db).ClassifyAsync(current.CompanyId, current.Id);

        Assert.Equal(CompanySizeClass.Medium, result.CalculatedClass);
        Assert.False(result.AuditExempt);
        Assert.Equal(CompanySizeClass.Medium, result.RawCurrentClass);
        Assert.Equal(CompanySizeClass.Small, result.RawPriorClass);
    }

    [Fact]
    public async Task HistoricAnchor_WithAttestedClassAndEvidence_UnlocksCarryForward()
    {
        await using var db = CreateDbContext();
        var (_, _, third, current) = await SeedHistoricChainAsync(db);
        var anchor = await RecordAnchorAsync(db, third, SmallButNotMicro, CompanySizeClass.Small, role: "Reviewer");
        Assert.Equal(CompanySizeClass.Small, anchor.HistoricAttestedEffectiveClass);
        Assert.Equal("Reviewer", anchor.HistoricAttestedRole);
        AddFiguresRow(db, current, MediumOnly);
        await db.SaveChangesAsync();

        var result = await ClassificationService(db).ClassifyAsync(current.CompanyId, current.Id);

        // Same figures as the test above: the evidenced small class carries forward one year.
        Assert.Equal(CompanySizeClass.Small, result.CalculatedClass);
        Assert.True(result.AuditExempt);
    }

    [Fact]
    public async Task HistoricAnchor_ChainWalkStartsAtTheAnchor_AndStillChecksLaterPeriods()
    {
        await using var db = CreateDbContext();
        var (_, second, third, current) = await SeedHistoricChainAsync(db);
        await RecordAnchorAsync(db, second, SmallButNotMicro);
        AddFiguresRow(db, current, SmallButNotMicro);
        await db.SaveChangesAsync();

        // Third (between the anchor and the target) is still unclassified, so the target is refused.
        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => ClassificationService(db).ClassifyAsync(current.CompanyId, current.Id));
        Assert.Equal(
            "A subsequent-year classification requires the immediately preceding period's raw turnover, balance-sheet and employee figures.",
            error.Message);

        AddFiguresRow(db, third, SmallButNotMicro);
        await db.SaveChangesAsync();
        await ClassificationService(db).ClassifyAsync(third.CompanyId, third.Id);
        var result = await ClassificationService(db).ClassifyAsync(current.CompanyId, current.Id);
        Assert.Equal(CompanySizeClass.Small, result.CalculatedClass);
    }

    [Fact]
    public async Task HistoricAnchor_Replaced_MakesEverySuccessorStale()
    {
        await using var db = CreateDbContext();
        var (_, _, third, current) = await SeedHistoricChainAsync(db);
        await RecordAnchorAsync(db, third, SmallButNotMicro);
        AddFiguresRow(db, current, SmallButNotMicro);
        await db.SaveChangesAsync();
        await ClassificationService(db).ClassifyAsync(current.CompanyId, current.Id);
        var currentPeriod = await db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == current.Id);
        await SizeClassificationService.EnsureDecisionChainCurrentAsync(db, currentPeriod, new DateOnly(2024, 1, 1), includeTarget: true);

        await RecordAnchorAsync(db, third, SmallButNotMicro, evidence: Pdf("replacement filed accounts"));

        var stale = await Assert.ThrowsAsync<BusinessRuleException>(
            () => SizeClassificationService.EnsureDecisionChainCurrentAsync(db, currentPeriod, new DateOnly(2024, 1, 1), includeTarget: true));
        Assert.Equal(
            $"The statutory size decision for preceding period {current.Id} is stale; re-run it before relying on consecutive-year qualification.",
            stale.Message);
        var recordings = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == AuditEventCodes.SizeClassificationHistoricAnchorRecorded)
            .ToListAsync();
        Assert.Equal(2, recordings.Count);
        Assert.Contains(recordings, entry => entry.OldValueJson != null && entry.OldValueJson.Contains("HistoricEvidenceSha256", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HistoricAnchor_TamperedEvidence_BreaksTheChain()
    {
        await using var db = CreateDbContext();
        var (_, _, third, current) = await SeedHistoricChainAsync(db);
        await RecordAnchorAsync(db, third, SmallButNotMicro);
        var row = await db.SizeClassifications.SingleAsync(s => s.PeriodId == third.Id);
        row.HistoricEvidenceArtifact = Pdf("altered after recording");
        await db.SaveChangesAsync();
        AddFiguresRow(db, current, SmallButNotMicro);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => ClassificationService(db).ClassifyAsync(current.CompanyId, current.Id));
        Assert.Equal(
            $"The historic figures recorded for preceding period {third.Id} lack current retained evidence or authority; record them again before relying on them.",
            error.Message);
    }

    // ---- Recording refusals ---------------------------------------------------------------------

    [Fact]
    public async Task HistoricAnchor_EvidenceMissing_IsRefused()
    {
        await using var db = CreateDbContext();
        var (_, _, third, _) = await SeedHistoricChainAsync(db);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => RecordAnchorAsync(db, third, SmallButNotMicro, evidence: [], sha: Sha256Hex([])));
        Assert.Equal("A PDF copy of the filed accounts is required as evidence.", error.Message);
        Assert.Empty(await db.SizeClassifications.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task HistoricAnchor_EvidenceHashMismatch_IsRefused()
    {
        await using var db = CreateDbContext();
        var (_, _, third, _) = await SeedHistoricChainAsync(db);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => RecordAnchorAsync(db, third, SmallButNotMicro, sha: new string('0', 64)));
        Assert.Equal("The evidence bytes do not match the supplied SHA-256 digest.", error.Message);
        Assert.Empty(await db.SizeClassifications.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task HistoricAnchor_NonPdfEvidence_IsRefused()
    {
        await using var db = CreateDbContext();
        var (_, _, third, _) = await SeedHistoricChainAsync(db);
        var notPdf = "<html>not a pdf</html>"u8.ToArray();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => RecordAnchorAsync(db, third, SmallButNotMicro, evidence: notPdf, sha: Sha256Hex(notPdf)));
        Assert.Equal("The evidence must be a PDF copy of the filed accounts.", error.Message);
        Assert.Empty(await db.SizeClassifications.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task HistoricAnchor_MissingSourceReference_IsRefused()
    {
        await using var db = CreateDbContext();
        var (_, _, third, _) = await SeedHistoricChainAsync(db);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => RecordAnchorAsync(db, third, SmallButNotMicro, sourceReference: "  "));
        Assert.Equal(
            "Identify the filed accounts the figures come from (a source reference of up to 300 characters).",
            error.Message);
    }

    [Theory]
    [InlineData("Accountant")]
    [InlineData("Client")]
    public async Task HistoricAnchor_WrongRole_IsRefused(string role)
    {
        await using var db = CreateDbContext();
        var (_, _, third, _) = await SeedHistoricChainAsync(db);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => RecordAnchorAsync(db, third, SmallButNotMicro, role: role));
        Assert.Equal("Only an Owner or Reviewer may record historic size figures from filed accounts.", error.Message);

        var endpoint = await ClassificationEndpoints.RecordHistoricAnchorEndpointAsync(
            third.CompanyId,
            third.Id,
            AnchorInput(SmallButNotMicro),
            ClassificationService(db),
            db,
            AuthenticatedRequest(role, HttpMethods.Put, AnchorPath(third)),
            DisabledApiAccess());
        // A Client without explicit company scope cannot even see the period (404); an Accountant is forbidden.
        Assert.Equal(role == "Client" ? StatusCodes.Status404NotFound : StatusCodes.Status403Forbidden, ResultStatusCode(endpoint));

        var decision = RoleAuthorizationService.Authorize(AuthenticatedRole(role), new PathString(AnchorPath(third)), HttpMethods.Put);
        Assert.False(decision.IsAllowed);
        Assert.Empty(await db.SizeClassifications.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Reviewer")]
    public void HistoricAnchorRoute_AllowsOwnerAndReviewer(string role) =>
        Assert.True(RoleAuthorizationService.Authorize(AuthenticatedRole(role), new PathString("/api/companies/1/periods/8/classification/historic-anchor"), HttpMethods.Put).IsAllowed);

    [Fact]
    public async Task HistoricAnchorRoute_WithoutRecentAuthenticatorVerification_IsRefused()
    {
        Assert.True(RecentAuthenticationMiddleware.RequiresRecentMfa(new PathString("/api/companies/1/periods/8/classification/historic-anchor"), HttpMethods.Put));
        Assert.True(RecentAuthenticationMiddleware.RequiresRecentMfa(new PathString("/api/companies/1/periods/8/classification/historic-anchor/"), HttpMethods.Put));

        var reachedEndpoint = false;
        var middleware = new RecentAuthenticationMiddleware(_ =>
        {
            reachedEndpoint = true;
            return Task.CompletedTask;
        });
        var context = AuthenticatedRequest("Owner", HttpMethods.Put, "/api/companies/1/periods/8/classification/historic-anchor");
        context.Items[AuthContext.ItemKey] = AuthenticatedRole("Owner") with
        {
            MfaMethod = "totp",
            MfaVerifiedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-45)
        };
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, Options.Create(new AuthSessionConfig()), TimeProvider.System);

        Assert.False(reachedEndpoint);
        Assert.Equal(StatusCodes.Status428PreconditionRequired, context.Response.StatusCode);
    }

    [Fact]
    public async Task HistoricAnchor_PeriodWithAComputedDecision_IsRefused()
    {
        await using var db = CreateDbContext();
        var (first, _, _, _) = await SeedHistoricChainAsync(db);
        await ClassifyFiguresAsync(db, first, SmallButNotMicro);
        var before = await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == first.Id);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => RecordAnchorAsync(db, first, SmallButNotMicro));

        Assert.Equal(
            "This period already has a size classification computed by the system; historic figures cannot replace it.",
            error.Message);
        var after = await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == first.Id);
        Assert.Equal(SizeInputBasis.Computed, after.InputBasis);
        Assert.Equal(before.DecisionInputFingerprintSha256, after.DecisionInputFingerprintSha256);
    }

    [Fact]
    public async Task HistoricAnchor_InputsOnlyRow_IsReplacedByTheAnchor()
    {
        await using var db = CreateDbContext();
        var (_, _, third, _) = await SeedHistoricChainAsync(db);
        AddInputsOnlyRow(db, third);
        await db.SaveChangesAsync();

        var anchor = await RecordAnchorAsync(db, third, SmallButNotMicro);

        Assert.Equal(SizeInputBasis.HistoricAnchor, anchor.InputBasis);
        Assert.Equal(SmallButNotMicro.Turnover, anchor.Turnover);
        Assert.Single(await db.SizeClassifications.AsNoTracking().Where(s => s.PeriodId == third.Id).ToListAsync());
    }

    [Fact]
    public async Task HistoricAnchor_LockedPeriod_IsRefused()
    {
        await using var db = CreateDbContext();
        var (_, _, third, _) = await SeedHistoricChainAsync(db);
        third.Status = PeriodStatus.Finalised;
        third.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => RecordAnchorAsync(db, third, SmallButNotMicro));
        Assert.Equal("This period is finalised or locked; reopen it before recording historic figures.", error.Message);
    }

    [Fact]
    public async Task HistoricAnchor_LatestPeriod_IsRefused()
    {
        await using var db = CreateDbContext();
        var (_, _, _, current) = await SeedHistoricChainAsync(db);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => RecordAnchorAsync(db, current, SmallButNotMicro));
        Assert.Equal(
            "Historic figures can be recorded only for a period that precedes another accounting period of the company.",
            error.Message);
    }

    [Fact]
    public async Task HistoricAnchor_Pre2017Period_KeepsTheScheduleFloor()
    {
        await using var db = CreateDbContext();
        var (first, _, _, _) = await SeedHistoricChainAsync(db);
        first.PeriodStart = new DateOnly(2016, 1, 1);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => RecordAnchorAsync(db, first, SmallButNotMicro));
        Assert.StartsWith(
            "Automated statutory size classification is supported only for financial years beginning on or after 2017-01-01",
            error.Message,
            StringComparison.Ordinal);
    }

    // ---- An anchor is never a filing basis --------------------------------------------------------

    [Fact]
    public async Task HistoricAnchorPeriod_IsRefusedAsAClassificationOverrideOrFilingTarget()
    {
        await using var db = CreateDbContext();
        var (_, _, third, _) = await SeedHistoricChainAsync(db);
        await RecordAnchorAsync(db, third, SmallButNotMicro, CompanySizeClass.Small);

        var classify = await Assert.ThrowsAsync<BusinessRuleException>(
            () => ClassificationService(db).ClassifyAsync(third.CompanyId, third.Id));
        Assert.Equal(SizeClassificationService.HistoricAnchorNotATargetMessage, classify.Message);

        var regime = await Assert.ThrowsAsync<BusinessRuleException>(
            () => new FilingRegimeService(db).DetermineAsync(third.CompanyId, third.Id));
        Assert.Equal(SizeClassificationService.HistoricAnchorNotATargetMessage, regime.Message);

        var evidence = Pdf("override evidence");
        var overrideError = await Assert.ThrowsAsync<BusinessRuleException>(
            () => ClassificationService(db).ApplyOverrideAsync(
                third.CompanyId,
                third.Id,
                new SizeClassificationOverrideRequest(CompanySizeClass.Large, "Conservative override for test purposes only.", evidence, Sha256Hex(evidence)),
                "Owner",
                "Example Owner"));
        Assert.Equal(SizeClassificationService.HistoricAnchorNotATargetMessage, overrideError.Message);

        var save = await Assert.ThrowsAsync<BusinessRuleException>(
            () => ClassificationEndpoints.SaveSizeClassificationEndpointAsync(
                third.CompanyId,
                third.Id,
                new SizeClassificationInput(1m, 1m, 0, null),
                db,
                new AuditService(db),
                ClassificationRequest(third, "PUT", "size-classification"),
                DisabledApiAccess()));
        Assert.Equal(SizeClassificationService.HistoricAnchorNotATargetMessage, save.Message);

        var row = await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == third.Id);
        Assert.Equal(SmallButNotMicro.Turnover, row.Turnover);
        Assert.Null(row.OverrideClass);
        Assert.Null(await db.FilingRegimes.AsNoTracking().FirstOrDefaultAsync(f => f.PeriodId == third.Id));

        var prerequisites = await ClassificationService(db).GetPrerequisitesAsync(third.CompanyId, third.Id);
        Assert.False(prerequisites.Ready);
        Assert.Null(prerequisites.FirstBlockingPeriod);
        Assert.Equal(SizeClassificationService.HistoricAnchorNotATargetMessage, prerequisites.Message);
    }

    [Fact]
    public async Task HistoricAnchorPeriod_IsRefusedByTheFilingReleaseGate()
    {
        await using var db = CreateDbContext();
        var (_, _, third, _) = await SeedHistoricChainAsync(db);
        await RecordAnchorAsync(db, third, SmallButNotMicro, CompanySizeClass.Small);
        third.Status = PeriodStatus.Finalised;
        third.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(
            () => new FilingReleaseGate(db).AssertCanRecordFiledAsync(third.CompanyId, third.Id, FilingReleaseWorkflow.Cro, "CORE-1"));
        Assert.Equal(
            "Final filing release cannot rest on historic figures keyed in from accounts filed elsewhere; this period is evidence for the following year only.",
            error.Message);
    }

    // ---- Prerequisites offer both routes ----------------------------------------------------------

    [Fact]
    public async Task ClassificationPrerequisites_BrokenChain_OffersBothRoutes()
    {
        await using var db = CreateDbContext();
        var (first, _, third, current) = await SeedHistoricChainAsync(db);

        var prerequisites = await ClassificationService(db).GetPrerequisitesAsync(current.CompanyId, current.Id);

        Assert.False(prerequisites.Ready);
        Assert.Equal(first.Id, prerequisites.FirstBlockingPeriod?.PeriodId);
        var historic = Assert.IsType<ClassificationBlockingPeriod>(prerequisites.HistoricFiguresPeriod);
        Assert.Equal(third.Id, historic.PeriodId);
        Assert.Equal(new DateOnly(2024, 1, 1), historic.PeriodStart);
        Assert.Equal("record historic figures from the filed accounts", historic.Reason);
        Assert.Equal(
            "Classify 1 Jan 2018 – 31 Dec 2018 first, or record historic figures from the filed accounts for 1 Jan 2024 – 31 Dec 2024. Each year's size test uses the previous year's figures (Companies Act 2014, s.280A), so without historic figures every period from 1 Jan 2018 – 31 Dec 2018 must be classified in order.",
            prerequisites.Message);
        Assert.False(prerequisites.CanRecordHistoricFigures);

        // The predecessor's own Classify page offers the keyed-in route.
        var predecessor = await ClassificationService(db).GetPrerequisitesAsync(third.CompanyId, third.Id);
        Assert.True(predecessor.CanRecordHistoricFigures);
    }

    [Fact]
    public async Task ClassificationPrerequisites_HistoricFiguresRoute_IsOfferedOnlyWhereNothingIsReplaced()
    {
        await using var db = CreateDbContext();
        var (first, second, third, _) = await SeedHistoricChainAsync(db);
        await ClassifyFiguresAsync(db, first, SmallButNotMicro);
        await RecordAnchorAsync(db, third, SmallButNotMicro);
        second.Status = PeriodStatus.Finalised;
        second.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        Assert.False((await ClassificationService(db).GetPrerequisitesAsync(first.CompanyId, first.Id)).CanRecordHistoricFigures);
        Assert.False((await ClassificationService(db).GetPrerequisitesAsync(second.CompanyId, second.Id)).CanRecordHistoricFigures);
        // An existing anchor may be replaced with new evidence.
        Assert.True((await ClassificationService(db).GetPrerequisitesAsync(third.CompanyId, third.Id)).CanRecordHistoricFigures);
    }

    [Fact]
    public async Task ClassificationPrerequisites_DecidedButStalePredecessor_DoesNotOfferHistoricFigures()
    {
        await using var db = CreateDbContext();
        // The third period has no figures at all, so only the stale (not missing) first period stands
        // between it and a classifiable chain: finish that classification, do not key history in.
        var (first, second, third, current) = await SeedHistoricChainAsync(db);
        await ClassifyFiguresAsync(db, first, SmallButNotMicro);
        await ClassifyFiguresAsync(db, second, SmallButNotMicro);
        await ChangeRetainedTurnoverAsync(db, first);

        var prerequisites = await ClassificationService(db).GetPrerequisitesAsync(current.CompanyId, current.Id);

        Assert.False(prerequisites.Ready);
        Assert.Equal(first.Id, prerequisites.FirstBlockingPeriod?.PeriodId);
        Assert.Null(prerequisites.HistoricFiguresPeriod);
        Assert.False((await ClassificationService(db).GetPrerequisitesAsync(third.CompanyId, third.Id)).CanRecordHistoricFigures);
    }

    [Fact]
    public async Task HistoricAnchorEndpoint_RecordsTheAnchorWithThePrincipalAsAttester()
    {
        await using var db = CreateDbContext();
        var (_, _, third, _) = await SeedHistoricChainAsync(db);

        var result = await ClassificationEndpoints.RecordHistoricAnchorEndpointAsync(
            third.CompanyId,
            third.Id,
            AnchorInput(SmallButNotMicro) with { AttestedEffectiveClass = CompanySizeClass.Small },
            ClassificationService(db),
            db,
            AuthenticatedRequest("Owner", HttpMethods.Put, AnchorPath(third)),
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(result));
        var row = await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == third.Id);
        Assert.Equal(SizeInputBasis.HistoricAnchor, row.InputBasis);
        Assert.Equal("Example User", row.HistoricAttestedBy);
        Assert.Equal("Owner", row.HistoricAttestedRole);
        Assert.Equal(CompanySizeClass.Small, row.HistoricAttestedEffectiveClass);
        Assert.Equal("Members' accounts for the year ended 31 December 2024, filed with the CRO", row.HistoricSourceReference);
        var audit = Assert.Single(await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == AuditEventCodes.SizeClassificationHistoricAnchorRecorded)
            .ToListAsync());
        Assert.Equal("user:1", audit.UserId);
        Assert.DoesNotContain("HistoricEvidenceArtifact", audit.NewValueJson!, StringComparison.Ordinal);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(result).Value, ApiJson));
        Assert.Equal("HistoricAnchor", json.RootElement.GetProperty("inputBasis").GetString());
        Assert.False(json.RootElement.TryGetProperty("historicEvidenceArtifact", out _));
    }

    [Fact]
    public async Task HistoricAnchorEndpoint_PeriodOfAnotherCompany_IsNotFound()
    {
        await using var db = CreateDbContext();
        var (_, _, third, _) = await SeedHistoricChainAsync(db);

        var result = await ClassificationEndpoints.RecordHistoricAnchorEndpointAsync(
            third.CompanyId + 1000,
            third.Id,
            AnchorInput(SmallButNotMicro),
            ClassificationService(db),
            db,
            AuthenticatedRequest("Owner", HttpMethods.Put, $"/api/companies/{third.CompanyId + 1000}/periods/{third.Id}/classification/historic-anchor"),
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(result));
        Assert.Empty(await db.SizeClassifications.AsNoTracking().ToListAsync());
    }

    // ---- Fix round 1: the anchor is a fallback for genuinely missing history only ------------------

    [Fact]
    public async Task HistoricAnchor_PeriodClassifiableFromComputedPredecessors_IsRefused()
    {
        // Reviewer's example: the preceding periods hold current computed decisions (the last one
        // Medium) and this period is merely undecided, so it must be classified normally.
        await using var db = CreateDbContext();
        var (first, second, third, current) = await SeedHistoricChainAsync(db);
        AddFiguresRow(db, first, MediumOnly);
        await db.SaveChangesAsync();
        await ClassificationService(db).ClassifyAsync(first.CompanyId, first.Id);
        AddFiguresRow(db, second, MediumOnly);
        await db.SaveChangesAsync();
        var secondResult = await ClassificationService(db).ClassifyAsync(second.CompanyId, second.Id);
        Assert.Equal(CompanySizeClass.Medium, secondResult.CalculatedClass);
        AddInputsOnlyRow(db, third);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => RecordAnchorAsync(db, third, SmallButNotMicro));

        Assert.Equal(SizeClassificationService.ClassifiableNormallyMessage, error.Message);
        Assert.Equal(
            "This period can be classified from the earlier periods already in FilingBridge; classify it normally instead.",
            error.Message);
        var row = await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == third.Id);
        Assert.Equal(SizeInputBasis.Computed, row.InputBasis);
        Assert.False((await ClassificationService(db).GetPrerequisitesAsync(third.CompanyId, third.Id)).CanRecordHistoricFigures);
        var dependent = await ClassificationService(db).GetPrerequisitesAsync(current.CompanyId, current.Id);
        Assert.Null(dependent.HistoricFiguresPeriod);
    }

    [Fact]
    public async Task HistoricAnchor_FirstYear_IsRefused()
    {
        await using var db = CreateDbContext();
        var (first, _, _, _) = await SeedHistoricChainAsync(db);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => RecordAnchorAsync(db, first, SmallButNotMicro));

        Assert.Equal(SizeClassificationService.ClassifiableNormallyMessage, error.Message);
        Assert.False((await ClassificationService(db).GetPrerequisitesAsync(first.CompanyId, first.Id)).CanRecordHistoricFigures);
        Assert.Empty(await db.SizeClassifications.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task HistoricAnchor_GenuinelyMissingHistory_IsAllowed()
    {
        await using var db = CreateDbContext();
        var (first, _, third, _) = await SeedHistoricChainAsync(db);
        await ClassifyFiguresAsync(db, first, SmallButNotMicro);

        // The second period has no figures at all, so the third cannot be classified from the chain.
        Assert.True((await ClassificationService(db).GetPrerequisitesAsync(third.CompanyId, third.Id)).CanRecordHistoricFigures);
        var anchor = await RecordAnchorAsync(db, third, SmallButNotMicro);
        Assert.Equal(SizeInputBasis.HistoricAnchor, anchor.InputBasis);
    }

    [Fact]
    public async Task HistoricAnchor_HoldingCompany_IsRefused()
    {
        await using var db = CreateDbContext();
        var (_, _, third, _) = await SeedHistoricChainAsync(db);
        var company = await db.Companies.SingleAsync(c => c.Id == third.CompanyId);
        company.IsHolding = true;
        await db.SaveChangesAsync();

        Assert.False((await ClassificationService(db).GetPrerequisitesAsync(third.CompanyId, third.Id)).CanRecordHistoricFigures);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => RecordAnchorAsync(db, third, SmallButNotMicro));
        Assert.StartsWith("Holding-company size must be determined", error.Message, StringComparison.Ordinal);
        Assert.Empty(await db.SizeClassifications.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task HistoricAnchor_StaleComputedPeriodAfterTheAnchor_BlocksTheTarget()
    {
        await using var db = CreateDbContext();
        var (_, second, third, current) = await SeedHistoricChainAsync(db);
        await RecordAnchorAsync(db, second, SmallButNotMicro);
        await ClassifyFiguresAsync(db, third, SmallButNotMicro);
        await ChangeRetainedTurnoverAsync(db, third);
        AddFiguresRow(db, current, SmallButNotMicro);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => ClassificationService(db).ClassifyAsync(current.CompanyId, current.Id));

        Assert.Equal(
            $"The statutory size decision for preceding period {third.Id} is stale; re-run it before relying on consecutive-year qualification.",
            error.Message);
    }

    [Fact]
    public async Task HistoricAnchor_ElectionRuleStillBindsDecidedYearsBeforeTheRoot()
    {
        // 2023 (first year, decided with the 2023 election), 2024 (no figures), 2025 (anchor) and
        // 2026: the 2026 test uses the 2024 election, which conflicts with the decided 2023 year
        // even though that year is before the anchor root.
        await using var db = CreateDbContext();
        var first = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        first.PeriodStart = new DateOnly(2023, 1, 1);
        first.PeriodEnd = new DateOnly(2023, 12, 31);
        var gap = new AccountingPeriod { CompanyId = first.CompanyId, PeriodStart = new DateOnly(2024, 1, 1), PeriodEnd = new DateOnly(2024, 12, 31) };
        var anchored = new AccountingPeriod { CompanyId = first.CompanyId, PeriodStart = new DateOnly(2025, 1, 1), PeriodEnd = new DateOnly(2025, 12, 31) };
        var target = new AccountingPeriod { CompanyId = first.CompanyId, PeriodStart = new DateOnly(2026, 1, 1), PeriodEnd = new DateOnly(2026, 12, 31) };
        db.AccountingPeriods.AddRange(gap, anchored, target);
        await db.SaveChangesAsync();
        await ClassifyFiguresAsync(db, first, SmallButNotMicro, new DateOnly(2023, 1, 1));
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = target.Id,
            Turnover = SmallButNotMicro.Turnover,
            BalanceSheetTotal = SmallButNotMicro.BalanceSheetTotal,
            AvgEmployees = SmallButNotMicro.AvgEmployees,
            ThresholdElectionEffectiveFrom = new DateOnly(2024, 1, 1)
        });
        await db.SaveChangesAsync();

        // The anchor form defaults to the dependent period's election.
        var anchorPage = await ClassificationService(db).GetPrerequisitesAsync(anchored.CompanyId, anchored.Id);
        Assert.True(anchorPage.CanRecordHistoricFigures);
        Assert.Equal(new DateOnly(2024, 1, 1), anchorPage.SuggestedThresholdElection);

        await RecordAnchorAsync(db, anchored, SmallButNotMicro);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => ClassificationService(db).ClassifyAsync(target.CompanyId, target.Id));
        Assert.Equal(
            "The 2024 threshold adjustment election must be applied consistently to every financial year of the company beginning on or after 2023-01-01.",
            error.Message);
    }

    // ---- Fix round 2: "classifiable normally" is decided structurally --------------------------------

    [Fact]
    public async Task HistoricAnchor_PredecessorMadeUndecidedByAnInputEdit_IsRefusedAndNotOffered()
    {
        // The second period was decided Medium; one PUT of changed inputs leaves it undecided. That
        // must not open the historic-figures route on the third period.
        await using var db = CreateDbContext();
        var (first, second, third, current) = await SeedHistoricChainAsync(db);
        await ClassifyFiguresAsync(db, first, MediumOnly);
        await ClassifyFiguresAsync(db, second, MediumOnly);
        Assert.Equal(CompanySizeClass.Medium, (await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == second.Id)).CalculatedClass);
        await ClassificationEndpoints.SaveSizeClassificationEndpointAsync(
            second.CompanyId,
            second.Id,
            new SizeClassificationInput(MediumOnly.Turnover + 1m, MediumOnly.BalanceSheetTotal, MediumOnly.AvgEmployees, null),
            db,
            new AuditService(db),
            ClassificationRequest(second, "PUT", "size-classification"),
            DisabledApiAccess());
        Assert.Null((await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == second.Id)).DecisionInputFingerprintSha256);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => RecordAnchorAsync(db, third, SmallButNotMicro, CompanySizeClass.Small));

        Assert.Equal(
            "An earlier period (1 Jan 2019 – 31 Dec 2019) already has figures in FilingBridge; finish classifying it instead of recording historic figures.",
            error.Message);
        Assert.Null(await db.SizeClassifications.AsNoTracking().FirstOrDefaultAsync(s => s.PeriodId == third.Id));
        Assert.False((await ClassificationService(db).GetPrerequisitesAsync(third.CompanyId, third.Id)).CanRecordHistoricFigures);
        Assert.Null((await ClassificationService(db).GetPrerequisitesAsync(current.CompanyId, current.Id)).HistoricFiguresPeriod);
    }

    [Fact]
    public async Task HistoricAnchor_PredecessorStaledByACompanyFlagChange_IsRefusedAndNotOffered()
    {
        await using var db = CreateDbContext();
        var (first, second, third, current) = await SeedHistoricChainAsync(db);
        await ClassifyFiguresAsync(db, first, SmallButNotMicro);
        await ClassifyFiguresAsync(db, second, SmallButNotMicro);
        var company = await db.Companies.SingleAsync(c => c.Id == first.CompanyId);
        company.IsInvestment = true; // part of every decision fingerprint, so the whole chain is stale
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => RecordAnchorAsync(db, third, SmallButNotMicro));

        Assert.Equal(
            "An earlier period (1 Jan 2018 – 31 Dec 2018) already has figures in FilingBridge; finish classifying it instead of recording historic figures.",
            error.Message);
        Assert.False((await ClassificationService(db).GetPrerequisitesAsync(third.CompanyId, third.Id)).CanRecordHistoricFigures);
        Assert.Null((await ClassificationService(db).GetPrerequisitesAsync(current.CompanyId, current.Id)).HistoricFiguresPeriod);
    }

    [Fact]
    public async Task HistoricAnchor_SuggestedElection_ComesFromTheImmediateDependentPeriod()
    {
        await using var db = CreateDbContext();
        var (_, second, third, current) = await SeedHistoricChainAsync(db);
        // A later period has a 2023 election; the immediately following one has no figures yet.
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = current.Id,
            Turnover = 1m,
            BalanceSheetTotal = 1m,
            AvgEmployees = 0,
            ThresholdElectionEffectiveFrom = new DateOnly(2023, 1, 1)
        });
        await db.SaveChangesAsync();

        var secondPage = await ClassificationService(db).GetPrerequisitesAsync(second.CompanyId, second.Id);
        Assert.True(secondPage.CanRecordHistoricFigures);
        Assert.Null(secondPage.SuggestedThresholdElection);

        var thirdPage = await ClassificationService(db).GetPrerequisitesAsync(third.CompanyId, third.Id);
        Assert.Equal(new DateOnly(2023, 1, 1), thirdPage.SuggestedThresholdElection);
    }

    // ---- Fix round 1: an anchor is not this period's size classification ------------------------

    [Fact]
    public async Task HistoricAnchorPeriod_IsNotAClassificationForReadinessNotesOrTheFilingProfile()
    {
        await using var db = CreateDbContext();
        var (_, _, third, _) = await SeedHistoricChainAsync(db);
        // An attested Medium class would drive medium-only notes and sources if read as this period's class.
        await RecordAnchorAsync(db, third, SmallButNotMicro, CompanySizeClass.Medium);

        var readiness = await new FinancialStatementsService(db).GetReadinessScoreAsync(third.CompanyId, third.Id);
        Assert.Contains("Historic figures only — not classified here", readiness.MissingItems);

        var notes = await new NotesDisclosureService(db).GenerateNotesAsync(third.CompanyId, third.Id);
        Assert.DoesNotContain(notes, note => note.Code == StatutoryNoteCodes.FinancialInstruments);

        var profile = await new FilingReadinessProfileService(db).GetProfileAsync(third.CompanyId, third.Id);
        var sizeEvidence = Assert.Single(profile.RequiredEvidence, item => item.Code == "size-classification");
        Assert.False(sizeEvidence.Satisfied);
    }

    // ---- Fixture helpers ----------------------------------------------------------------------

    private sealed record SizeFigures(decimal Turnover, decimal BalanceSheetTotal, int AvgEmployees);

    // Fictitious figures: within the small limits but above two of the micro limits.
    private static readonly SizeFigures SmallButNotMicro = new(1_000_000m, 500_000m, 5);
    // Above every small limit, within the medium limits.
    private static readonly SizeFigures MediumOnly = new(20_000_000m, 10_000_000m, 60);

    private static byte[] Pdf(string marker) => System.Text.Encoding.ASCII.GetBytes($"%PDF-1.4\n% {marker}\n%%EOF\n");

    private static string Sha256Hex(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    private static string AnchorPath(AccountingPeriod period) =>
        $"/api/companies/{period.CompanyId}/periods/{period.Id}/classification/historic-anchor";

    private static HistoricAnchorInput AnchorInput(SizeFigures figures)
    {
        var evidence = Pdf("filed accounts");
        return new HistoricAnchorInput(
            figures.Turnover,
            figures.BalanceSheetTotal,
            figures.AvgEmployees,
            "Members' accounts for the year ended 31 December 2024, filed with the CRO",
            evidence,
            Sha256Hex(evidence));
    }

    private static async Task<SizeClassification> RecordAnchorAsync(
        AccountsDbContext db,
        AccountingPeriod period,
        SizeFigures figures,
        CompanySizeClass? attestedClass = null,
        string role = "Owner",
        byte[]? evidence = null,
        string? sha = null,
        string sourceReference = "Members' accounts for the year ended 31 December 2024, filed with the CRO")
    {
        evidence ??= Pdf("filed accounts");
        return await ClassificationService(db).RecordHistoricAnchorAsync(
            period.CompanyId,
            period.Id,
            new HistoricSizeFiguresRequest(
                figures.Turnover,
                figures.BalanceSheetTotal,
                figures.AvgEmployees,
                null,
                sourceReference,
                evidence,
                sha ?? Sha256Hex(evidence),
                attestedClass),
            role,
            "Example Attester",
            "user:7");
    }

    /// <summary>
    /// Fictitious calendar-year chain: first year 2018, then 2019, 2024 and the current 2025 period,
    /// all unclassified.
    /// </summary>
    private static async Task<(AccountingPeriod First, AccountingPeriod Second, AccountingPeriod Third, AccountingPeriod Current)>
        SeedHistoricChainAsync(AccountsDbContext db)
    {
        var first = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        first.PeriodStart = new DateOnly(2018, 1, 1);
        first.PeriodEnd = new DateOnly(2018, 12, 31);
        AccountingPeriod Year(int year) => new()
        {
            CompanyId = first.CompanyId,
            PeriodStart = new DateOnly(year, 1, 1),
            PeriodEnd = new DateOnly(year, 12, 31),
            IsFirstYear = false
        };
        var second = Year(2019);
        var third = Year(2024);
        var current = Year(2025);
        db.AccountingPeriods.AddRange(second, third, current);
        await db.SaveChangesAsync();
        return (first, second, third, current);
    }

    private static async Task ClassifyFiguresAsync(
        AccountsDbContext db,
        AccountingPeriod period,
        SizeFigures figures,
        DateOnly? election = null)
    {
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            Turnover = figures.Turnover,
            BalanceSheetTotal = figures.BalanceSheetTotal,
            AvgEmployees = figures.AvgEmployees,
            ThresholdElectionEffectiveFrom = election
        });
        await db.SaveChangesAsync();
        await ClassificationService(db).ClassifyAsync(period.CompanyId, period.Id);
    }

    private static void AddFiguresRow(AccountsDbContext db, AccountingPeriod period, SizeFigures figures) =>
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            Turnover = figures.Turnover,
            BalanceSheetTotal = figures.BalanceSheetTotal,
            AvgEmployees = figures.AvgEmployees
        });

    private static (AccountingPeriod First, AccountingPeriod Second) PinnedFingerprintFixture()
    {
        var company = new Company
        {
            Id = 41,
            TenantId = 1,
            LegalName = "Pinned Fingerprint CLG",
            CompanyType = CompanyType.CompanyLimitedByGuarantee,
            IsSubsidiary = true,
            IncludedInHigherConsolidatedFinancialStatements = false
        };
        var first = new AccountingPeriod
        {
            Id = 501,
            CompanyId = company.Id,
            Company = company,
            PeriodStart = new DateOnly(2021, 3, 1),
            PeriodEnd = new DateOnly(2022, 2, 28),
            IsFirstYear = true,
            SizeClassification = new SizeClassification
            {
                Turnover = 1_234_567.891m,
                BalanceSheetTotal = 98_765.4m,
                AvgEmployees = 7,
                CalculatedClass = CompanySizeClass.Small,
                OverrideClass = CompanySizeClass.Medium,
                DecisionInputFingerprintSha256 = new string('c', 64)
            }
        };
        var second = new AccountingPeriod
        {
            Id = 502,
            CompanyId = company.Id,
            Company = company,
            PeriodStart = new DateOnly(2022, 3, 1),
            PeriodEnd = new DateOnly(2023, 2, 28),
            IsFirstYear = false,
            SizeClassification = new SizeClassification
            {
                Turnover = 0m,
                BalanceSheetTotal = 12.5m,
                AvgEmployees = 0
            }
        };
        return (first, second);
    }
}
