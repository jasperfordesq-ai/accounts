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

// Batch B2: an Owner can retire historic figures recorded from filed accounts. The evidence is never
// deleted: the row is kept as a RetiredHistoricAnchor with who/when/why. It no longer roots the
// s.280A chain (successors become stale), and the period is classifiable normally again, from its
// own freshly entered figures, never from the retired ones.
public partial class AccountsWorkflowTests
{
    private const string RetirementReason = "The filed accounts were for the wrong financial year";

    [Fact]
    public async Task HistoricAnchorRetired_KeepsTheEvidence_StopsRootingTheChain_AndMakesSuccessorsStale()
    {
        await using var db = CreateDbContext();
        var (first, _, third, current) = await SeedHistoricChainAsync(db);
        var anchor = await RecordAnchorAsync(db, third, SmallButNotMicro, CompanySizeClass.Small);
        var recorded = new
        {
            anchor.HistoricSourceReference,
            anchor.HistoricEvidenceSha256,
            Artifact = anchor.HistoricEvidenceArtifact!.ToArray(),
            anchor.HistoricAttestedBy,
            anchor.HistoricAttestedRole,
            anchor.HistoricAttestedAt,
            anchor.HistoricAttestedEffectiveClass,
            anchor.Turnover,
            anchor.BalanceSheetTotal,
            anchor.AvgEmployees
        };
        AddFiguresRow(db, current, SmallButNotMicro);
        await db.SaveChangesAsync();
        await ClassificationService(db).ClassifyAsync(current.CompanyId, current.Id);
        var currentPeriod = await db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == current.Id);
        await SizeClassificationService.EnsureDecisionChainCurrentAsync(db, currentPeriod, new DateOnly(2024, 1, 1), includeTarget: true);

        var retired = await RetireAnchorAsync(db, third);

        // The evidence is retained on the row, marked retired with who, when and why.
        var row = await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == third.Id);
        Assert.Equal(row.Id, retired.Id);
        Assert.Equal(SizeInputBasis.RetiredHistoricAnchor, row.InputBasis);
        Assert.Equal(recorded.HistoricSourceReference, row.HistoricSourceReference);
        Assert.Equal(recorded.HistoricEvidenceSha256, row.HistoricEvidenceSha256);
        Assert.Equal(recorded.Artifact, row.HistoricEvidenceArtifact);
        Assert.Equal(recorded.HistoricAttestedBy, row.HistoricAttestedBy);
        Assert.Equal(recorded.HistoricAttestedRole, row.HistoricAttestedRole);
        Assert.Equal(recorded.HistoricAttestedAt, row.HistoricAttestedAt);
        Assert.Equal(recorded.HistoricAttestedEffectiveClass, row.HistoricAttestedEffectiveClass);
        Assert.Equal((recorded.Turnover, recorded.BalanceSheetTotal, recorded.AvgEmployees), (row.Turnover, row.BalanceSheetTotal, row.AvgEmployees));
        Assert.Equal("Example Owner", row.HistoricRetiredBy);
        Assert.Equal(RetirementReason, row.HistoricRetiredReason);
        Assert.NotNull(row.HistoricRetiredAt);
        // Never a decision: no fingerprint, no class reported.
        Assert.Null(row.DecisionInputFingerprintSha256);
        Assert.False(row.HasCurrentDecision);
        Assert.Null(row.CalculatedClassDecision);

        // The chain is no longer rooted at the retired figures: it walks back to the first year again.
        var evaluation = await SizeClassificationService.EvaluateDecisionChainAsync(db, currentPeriod, new DateOnly(2024, 1, 1), includeTarget: true);
        Assert.Equal(first.Id, evaluation.Periods[0].Id);
        Assert.NotNull(evaluation.FirstFailure);
        Assert.Equal(first.Id, evaluation.FirstFailure!.Period.Id);
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => SizeClassificationService.EnsureDecisionChainCurrentAsync(db, currentPeriod, new DateOnly(2024, 1, 1), includeTarget: true));
        await Assert.ThrowsAsync<BusinessRuleException>(() => ClassificationService(db).ClassifyAsync(current.CompanyId, current.Id));
        var successor = await ClassificationService(db).GetPrerequisitesAsync(current.CompanyId, current.Id);
        Assert.False(successor.Ready);

        // Audited with the retired anchor's snapshot and the principal.
        var audit = Assert.Single(await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == AuditEventCodes.SizeClassificationHistoricAnchorRetired)
            .ToListAsync());
        Assert.Equal("user:1", audit.UserId);
        Assert.Contains(recorded.HistoricEvidenceSha256!, audit.OldValueJson!, StringComparison.Ordinal);
        Assert.Contains("HistoricAnchor", audit.OldValueJson!, StringComparison.Ordinal);
        Assert.Contains(RetirementReason, audit.NewValueJson!, StringComparison.Ordinal);
        Assert.Contains("RetiredHistoricAnchor", audit.NewValueJson!, StringComparison.Ordinal);
        Assert.DoesNotContain("HistoricEvidenceArtifact", audit.NewValueJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HistoricAnchorRetired_PeriodIsClassifiableNormally_FromFreshFiguresOnly()
    {
        await using var db = CreateDbContext();
        var (first, second, third, current) = await SeedHistoricChainAsync(db);
        await RecordAnchorAsync(db, third, SmallButNotMicro);
        await RetireAnchorAsync(db, third);

        // No longer refused as an anchor: the retired period gets the normal s.280A preflight.
        var prerequisites = await ClassificationService(db).GetPrerequisitesAsync(third.CompanyId, third.Id);
        Assert.NotEqual(SizeClassificationService.HistoricAnchorNotATargetMessage, prerequisites.Message);
        Assert.Equal(first.Id, prerequisites.FirstBlockingPeriod?.PeriodId);
        // It is undecided, not a classification, for readiness and the filing regime.
        var readiness = await new FinancialStatementsService(db).GetReadinessScoreAsync(third.CompanyId, third.Id);
        Assert.Contains("Size classification not completed", readiness.MissingItems);
        var regime = await Assert.ThrowsAsync<BusinessRuleException>(
            () => new FilingRegimeService(db).DetermineAsync(third.CompanyId, third.Id));
        Assert.Equal("Run the statutory size classification against current raw inputs before determining a filing regime.", regime.Message);

        await ClassifyFiguresAsync(db, first, SmallButNotMicro);
        await ClassifyFiguresAsync(db, second, SmallButNotMicro);

        // The retired figures are never classified as they stand.
        var stillRetired = await Assert.ThrowsAsync<BusinessRuleException>(() => ClassificationService(db).ClassifyAsync(third.CompanyId, third.Id));
        Assert.Equal(SizeClassificationService.RetiredHistoricAnchorMessage, stillRetired.Message);
        Assert.Equal(SizeInputBasis.RetiredHistoricAnchor, (await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == third.Id)).InputBasis);

        // Classifying with this period's own figures makes it an ordinary computed decision; the
        // retired evidence stays on the row.
        var result = await ClassificationEndpoints.ClassifySizeEndpointAsync(
            third.CompanyId,
            third.Id,
            new SizeClassificationInput(MediumOnly.Turnover, MediumOnly.BalanceSheetTotal, MediumOnly.AvgEmployees, null),
            ClassificationService(db),
            db,
            new AuditService(db),
            AuthenticatedRequest("Accountant", HttpMethods.Post, $"/api/companies/{third.CompanyId}/periods/{third.Id}/classify"),
            DisabledApiAccess());
        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(result));
        var decided = await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == third.Id);
        Assert.Equal(SizeInputBasis.Computed, decided.InputBasis);
        Assert.True(decided.HasCurrentDecision);
        Assert.Equal(MediumOnly.Turnover, decided.Turnover);
        Assert.NotNull(decided.HistoricEvidenceArtifact);
        Assert.NotNull(decided.HistoricEvidenceSha256);
        Assert.Equal(RetirementReason, decided.HistoricRetiredReason);
        Assert.NotNull(decided.HistoricRetiredAt);

        AddFiguresRow(db, current, SmallButNotMicro);
        await db.SaveChangesAsync();
        await ClassificationService(db).ClassifyAsync(current.CompanyId, current.Id);
        var currentPeriod = await db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == current.Id);
        var chain = await SizeClassificationService.EvaluateDecisionChainAsync(db, currentPeriod, new DateOnly(2024, 1, 1), includeTarget: true);
        Assert.Null(chain.FirstFailure);
        Assert.Equal(first.Id, chain.Periods[0].Id);
    }

    [Fact]
    public async Task HistoricAnchorRetired_SavingFreshInputs_MakesTheRowComputedAndUndecided_KeepingTheEvidence()
    {
        await using var db = CreateDbContext();
        var (_, _, third, _) = await SeedHistoricChainAsync(db);
        var anchor = await RecordAnchorAsync(db, third, SmallButNotMicro);
        var sha = anchor.HistoricEvidenceSha256;
        await RetireAnchorAsync(db, third);

        var result = await ClassificationEndpoints.SaveSizeClassificationEndpointAsync(
            third.CompanyId,
            third.Id,
            new SizeClassificationInput(SmallButNotMicro.Turnover, SmallButNotMicro.BalanceSheetTotal, SmallButNotMicro.AvgEmployees, null),
            db,
            new AuditService(db),
            AuthenticatedRequest("Accountant", HttpMethods.Put, $"/api/companies/{third.CompanyId}/periods/{third.Id}/size-classification"),
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(result));
        var row = await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == third.Id);
        Assert.Equal(SizeInputBasis.Computed, row.InputBasis);
        Assert.False(row.HasCurrentDecision);
        Assert.Equal(sha, row.HistoricEvidenceSha256);
        Assert.Equal(RetirementReason, row.HistoricRetiredReason);
    }

    [Theory]
    [InlineData("Reviewer")]
    [InlineData("Accountant")]
    [InlineData("Client")]
    public async Task HistoricAnchorRetirement_IsOwnerOnly(string role)
    {
        await using var db = CreateDbContext();
        var (_, _, third, _) = await SeedHistoricChainAsync(db);
        await RecordAnchorAsync(db, third, SmallButNotMicro);
        var before = await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == third.Id);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => RetireAnchorAsync(db, third, role: role));
        Assert.Equal("Only an Owner may retire historic size figures.", error.Message);
        var endpoint = await ClassificationEndpoints.RetireHistoricAnchorEndpointAsync(
            third.CompanyId,
            third.Id,
            new HistoricAnchorRetirementInput(RetirementReason),
            ClassificationService(db),
            db,
            AuthenticatedRequest(role, HttpMethods.Post, RetirePath(third)),
            DisabledApiAccess());
        // A Client without explicit company scope cannot even see the period (404); others are forbidden.
        Assert.Equal(role == "Client" ? StatusCodes.Status404NotFound : StatusCodes.Status403Forbidden, ResultStatusCode(endpoint));
        Assert.False(RoleAuthorizationService.Authorize(AuthenticatedRole(role), new PathString(RetirePath(third)), HttpMethods.Post).IsAllowed);

        var after = await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == third.Id);
        Assert.Equal(SizeInputBasis.HistoricAnchor, after.InputBasis);
        Assert.Equal(before.DecisionInputFingerprintSha256, after.DecisionInputFingerprintSha256);
        Assert.Null(after.HistoricRetiredAt);
        Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == AuditEventCodes.SizeClassificationHistoricAnchorRetired));
    }

    [Fact]
    public async Task HistoricAnchorRetirementRoute_AllowsAnOwner_WithARecentAuthenticatorVerificationOnly()
    {
        const string path = "/api/companies/1/periods/8/classification/historic-anchor/retire";
        Assert.True(RoleAuthorizationService.Authorize(AuthenticatedRole("Owner"), new PathString(path), HttpMethods.Post).IsAllowed);
        Assert.True(RecentAuthenticationMiddleware.RequiresRecentMfa(new PathString(path), HttpMethods.Post));
        Assert.True(RecentAuthenticationMiddleware.RequiresRecentMfa(new PathString(path + "/"), HttpMethods.Post));

        var reachedEndpoint = false;
        var middleware = new RecentAuthenticationMiddleware(_ =>
        {
            reachedEndpoint = true;
            return Task.CompletedTask;
        });
        var context = AuthenticatedRequest("Owner", HttpMethods.Post, path);
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
    public async Task HistoricAnchorRetirementEndpoint_RecordsThePrincipal_AndReturnsTheRetiredRowWithoutTheArtifact()
    {
        await using var db = CreateDbContext();
        var (_, _, third, _) = await SeedHistoricChainAsync(db);
        await RecordAnchorAsync(db, third, SmallButNotMicro);

        var result = await ClassificationEndpoints.RetireHistoricAnchorEndpointAsync(
            third.CompanyId,
            third.Id,
            new HistoricAnchorRetirementInput($"  {RetirementReason}  "),
            ClassificationService(db),
            db,
            AuthenticatedRequest("Owner", HttpMethods.Post, RetirePath(third)),
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(result));
        var row = await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == third.Id);
        Assert.Equal("Example User", row.HistoricRetiredBy);
        Assert.Equal(RetirementReason, row.HistoricRetiredReason);
        Assert.Equal("user:1", (await db.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == AuditEventCodes.SizeClassificationHistoricAnchorRetired)).UserId);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(result).Value, ApiJson));
        Assert.Equal("RetiredHistoricAnchor", json.RootElement.GetProperty("inputBasis").GetString());
        Assert.Equal(RetirementReason, json.RootElement.GetProperty("historicRetiredReason").GetString());
        Assert.False(json.RootElement.TryGetProperty("historicEvidenceArtifact", out _));
    }

    [Theory]
    [InlineData("reason")]
    [InlineData("no-anchor")]
    [InlineData("already-retired")]
    [InlineData("locked")]
    public async Task HistoricAnchorRetirement_IsRefused_WithoutAReasonACurrentAnchorOrAnOpenPeriod(string refusal)
    {
        await using var db = CreateDbContext();
        var (_, second, third, _) = await SeedHistoricChainAsync(db);
        await RecordAnchorAsync(db, third, SmallButNotMicro);
        var target = refusal == "no-anchor" ? second : third;
        if (refusal == "already-retired")
            await RetireAnchorAsync(db, third);
        if (refusal == "locked")
        {
            var tracked = await db.AccountingPeriods.SingleAsync(p => p.Id == third.Id);
            tracked.Status = PeriodStatus.Finalised;
            tracked.LockedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        var auditsBefore = await db.AuditLogs.CountAsync(a => a.Action == AuditEventCodes.SizeClassificationHistoricAnchorRetired);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => RetireAnchorAsync(db, target, reason: refusal == "reason" ? " \t " : RetirementReason));

        Assert.Equal(refusal switch
        {
            "reason" => "Give the reason the historic figures are being retired (up to 1000 characters).",
            "locked" => "This period is finalised or locked; reopen it before retiring historic figures.",
            _ => "This period holds no current historic figures to retire."
        }, error.Message);
        Assert.Equal(auditsBefore, await db.AuditLogs.CountAsync(a => a.Action == AuditEventCodes.SizeClassificationHistoricAnchorRetired));
        if (refusal is "reason" or "locked")
            Assert.Equal(SizeInputBasis.HistoricAnchor, (await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == third.Id)).InputBasis);
    }

    [Fact]
    public async Task HistoricAnchorRetired_CanBeRecordedAgain_WhileTheHistoryIsGenuinelyMissing()
    {
        await using var db = CreateDbContext();
        var (_, _, third, current) = await SeedHistoricChainAsync(db);
        await RecordAnchorAsync(db, third, SmallButNotMicro);
        await RetireAnchorAsync(db, third);
        Assert.True((await ClassificationService(db).GetPrerequisitesAsync(third.CompanyId, third.Id)).CanRecordHistoricFigures);

        var again = await RecordAnchorAsync(db, third, SmallButNotMicro, CompanySizeClass.Small, evidence: Pdf("correct filed accounts"));

        Assert.Equal(SizeInputBasis.HistoricAnchor, again.InputBasis);
        Assert.Null(again.HistoricRetiredAt);
        Assert.Null(again.HistoricRetiredBy);
        Assert.Null(again.HistoricRetiredReason);
        // The replaced retirement survives in the audit trail.
        var recordings = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == AuditEventCodes.SizeClassificationHistoricAnchorRecorded)
            .OrderBy(a => a.Id)
            .ToListAsync();
        Assert.Contains(RetirementReason, recordings[^1].OldValueJson!, StringComparison.Ordinal);
        Assert.True((await ClassificationService(db).GetPrerequisitesAsync(current.CompanyId, current.Id)).Ready);
    }

    [Fact]
    public async Task HistoricAnchorRetired_CannotBeRecordedAgain_OnceEarlierPeriodsAreClassified()
    {
        await using var db = CreateDbContext();
        var (first, second, third, _) = await SeedHistoricChainAsync(db);
        await RecordAnchorAsync(db, third, SmallButNotMicro);
        await RetireAnchorAsync(db, third);
        await ClassifyFiguresAsync(db, first, SmallButNotMicro);
        await ClassifyFiguresAsync(db, second, SmallButNotMicro);
        var firstFingerprint = (await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == first.Id)).DecisionInputFingerprintSha256;

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => RecordAnchorAsync(db, third, SmallButNotMicro));

        Assert.Equal(SizeClassificationService.ClassifiableNormallyMessage, error.Message);
        Assert.False((await ClassificationService(db).GetPrerequisitesAsync(third.CompanyId, third.Id)).CanRecordHistoricFigures);
        Assert.Equal(SizeInputBasis.RetiredHistoricAnchor, (await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == third.Id)).InputBasis);
        Assert.Equal(firstFingerprint, (await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == first.Id)).DecisionInputFingerprintSha256);
    }

    private static string RetirePath(AccountingPeriod period) =>
        $"/api/companies/{period.CompanyId}/periods/{period.Id}/classification/historic-anchor/retire";

    private static Task<SizeClassification> RetireAnchorAsync(
        AccountsDbContext db,
        AccountingPeriod period,
        string role = "Owner",
        string reason = RetirementReason) =>
        ClassificationService(db).RetireHistoricAnchorAsync(
            period.CompanyId,
            period.Id,
            reason,
            role,
            "Example Owner",
            "user:1");
}

// Batch B fix round: retired or replaced historic evidence is archived append-only (PDF bytes, hash,
// figures, source, attestation and retirement), readable by Owner, Reviewer and Accountant; and
// figures that finalised, filed or locked work relies on cannot be retired.
public partial class AccountsWorkflowTests
{
    [Fact]
    public async Task HistoricAnchorRetired_IsArchivedWithItsPdfFiguresAttestationAndRetirement()
    {
        await using var db = CreateDbContext();
        var (_, _, third, _) = await SeedHistoricChainAsync(db);
        var evidence = Pdf("wrong-year filed accounts");
        await RecordAnchorAsync(db, third, SmallButNotMicro, CompanySizeClass.Small, role: "Reviewer", evidence: evidence);

        await RetireAnchorAsync(db, third);

        var archived = Assert.Single(await db.HistoricAnchorEvidenceRecords.AsNoTracking().Where(r => r.PeriodId == third.Id).ToListAsync());
        Assert.Equal(HistoricAnchorEvidenceEvent.Retired, archived.Event);
        Assert.Equal(evidence, archived.EvidenceArtifact);
        Assert.Equal(Sha256Hex(evidence), archived.EvidenceSha256);
        Assert.Equal((SmallButNotMicro.Turnover, SmallButNotMicro.BalanceSheetTotal, SmallButNotMicro.AvgEmployees),
            (archived.Turnover, archived.BalanceSheetTotal, archived.AvgEmployees));
        Assert.Equal("Members' accounts for the year ended 31 December 2024, filed with the CRO", archived.SourceReference);
        Assert.Equal("Example Attester", archived.AttestedBy);
        Assert.Equal("Reviewer", archived.AttestedRole);
        Assert.Equal(CompanySizeClass.Small, archived.AttestedEffectiveClass);
        Assert.Equal("Example Owner", archived.RetiredBy);
        Assert.Equal(RetirementReason, archived.RetiredReason);
        Assert.NotNull(archived.RetiredAt);
        Assert.Equal("Example Owner", archived.ArchivedBy);
    }

    [Fact]
    public async Task HistoricAnchorRetired_ThenRecordedAgain_KeepsTheRetiredPdfInTheArchive()
    {
        await using var db = CreateDbContext();
        var (_, _, third, _) = await SeedHistoricChainAsync(db);
        var wrong = Pdf("wrong-year filed accounts");
        await RecordAnchorAsync(db, third, SmallButNotMicro, evidence: wrong);
        await RetireAnchorAsync(db, third);

        var correct = Pdf("correct filed accounts");
        await RecordAnchorAsync(db, third, MediumOnly, role: "Reviewer", evidence: correct);

        var archive = await db.HistoricAnchorEvidenceRecords.AsNoTracking().Where(r => r.PeriodId == third.Id).OrderBy(r => r.Id).ToListAsync();
        Assert.Equal([HistoricAnchorEvidenceEvent.Retired, HistoricAnchorEvidenceEvent.Replaced], archive.Select(r => r.Event).ToArray());
        Assert.All(archive, record => Assert.Equal(wrong, record.EvidenceArtifact));
        Assert.All(archive, record => Assert.Equal(SmallButNotMicro.Turnover, record.Turnover));
        Assert.Equal(RetirementReason, archive[1].RetiredReason);
        Assert.Equal("Example Attester", archive[1].ArchivedBy);
        Assert.Equal(correct, (await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == third.Id)).HistoricEvidenceArtifact);
    }

    [Fact]
    public async Task HistoricAnchorRetired_FreshInputsByAnAccountant_ThenRecordedByAReviewer_KeepsTheRetiredPdf()
    {
        await using var db = CreateDbContext();
        var (_, _, third, _) = await SeedHistoricChainAsync(db);
        var wrong = Pdf("wrong-year filed accounts");
        await RecordAnchorAsync(db, third, SmallButNotMicro, evidence: wrong);
        await RetireAnchorAsync(db, third);
        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(await ClassificationEndpoints.SaveSizeClassificationEndpointAsync(
            third.CompanyId,
            third.Id,
            new SizeClassificationInput(MediumOnly.Turnover, MediumOnly.BalanceSheetTotal, MediumOnly.AvgEmployees, null),
            db,
            new AuditService(db),
            AuthenticatedRequest("Accountant", HttpMethods.Put, $"/api/companies/{third.CompanyId}/periods/{third.Id}/size-classification"),
            DisabledApiAccess())));

        var correct = Pdf("correct filed accounts");
        await RecordAnchorAsync(db, third, SmallButNotMicro, role: "Reviewer", evidence: correct);

        var retired = Assert.Single(await db.HistoricAnchorEvidenceRecords.AsNoTracking().Where(r => r.PeriodId == third.Id).ToListAsync());
        Assert.Equal(HistoricAnchorEvidenceEvent.Retired, retired.Event);
        Assert.Equal(wrong, retired.EvidenceArtifact);
        Assert.Equal(SmallButNotMicro.Turnover, retired.Turnover);
        Assert.Equal(RetirementReason, retired.RetiredReason);
        Assert.Equal(correct, (await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == third.Id)).HistoricEvidenceArtifact);
    }

    [Fact]
    public async Task HistoricAnchorReplaced_WhileActive_ArchivesThePreviousPdf()
    {
        await using var db = CreateDbContext();
        var (_, _, third, _) = await SeedHistoricChainAsync(db);
        var first = Pdf("first filed accounts");
        await RecordAnchorAsync(db, third, SmallButNotMicro, evidence: first);

        await RecordAnchorAsync(db, third, SmallButNotMicro, evidence: Pdf("replacement filed accounts"));

        var replaced = Assert.Single(await db.HistoricAnchorEvidenceRecords.AsNoTracking().Where(r => r.PeriodId == third.Id).ToListAsync());
        Assert.Equal(HistoricAnchorEvidenceEvent.Replaced, replaced.Event);
        Assert.Equal(first, replaced.EvidenceArtifact);
        Assert.Null(replaced.RetiredAt);
    }

    [Fact]
    public async Task HistoricAnchorEvidenceArchive_IsAppendOnly()
    {
        await using var db = CreateDbContext();
        var (_, _, third, _) = await SeedHistoricChainAsync(db);
        await RecordAnchorAsync(db, third, SmallButNotMicro);
        await RetireAnchorAsync(db, third);
        var record = await db.HistoricAnchorEvidenceRecords.SingleAsync(r => r.PeriodId == third.Id);

        record.RetiredReason = "Rewritten";
        var update = await Assert.ThrowsAsync<BusinessRuleException>(() => db.SaveChangesAsync());
        Assert.Equal("Archived historic size evidence is append-only and cannot be changed or deleted.", update.Message);
        db.Entry(record).State = EntityState.Unchanged;
        db.HistoricAnchorEvidenceRecords.Remove(record);
        var delete = await Assert.ThrowsAsync<BusinessRuleException>(() => db.SaveChangesAsync());
        Assert.Equal("Archived historic size evidence is append-only and cannot be changed or deleted.", delete.Message);
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Reviewer")]
    [InlineData("Accountant")]
    public async Task HistoricAnchorEvidenceArchive_IsReadableAndDownloadable_ByOwnerReviewerAndAccountant(string role)
    {
        await using var db = CreateDbContext();
        var (_, _, third, _) = await SeedHistoricChainAsync(db);
        var evidence = Pdf("wrong-year filed accounts");
        await RecordAnchorAsync(db, third, SmallButNotMicro, evidence: evidence);
        await RetireAnchorAsync(db, third);
        var archivePath = ArchivePath(third);
        Assert.True(RoleAuthorizationService.Authorize(AuthenticatedRole(role), new PathString(archivePath), HttpMethods.Get).IsAllowed);

        var list = await ClassificationEndpoints.ListHistoricAnchorEvidenceEndpointAsync(
            third.CompanyId, third.Id, db, AuthenticatedRequest(role, HttpMethods.Get, archivePath));

        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(list));
        var items = Assert.IsAssignableFrom<IReadOnlyList<HistoricAnchorEvidenceSummary>>(Assert.IsAssignableFrom<IValueHttpResult>(list).Value);
        var item = Assert.Single(items);
        Assert.Equal(HistoricAnchorEvidenceEvent.Retired, item.Event);
        Assert.Equal(RetirementReason, item.RetiredReason);
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(items, ApiJson)))
            Assert.False(json.RootElement[0].TryGetProperty("evidenceArtifact", out _));

        var download = await ClassificationEndpoints.DownloadHistoricAnchorEvidenceEndpointAsync(
            third.CompanyId, third.Id, item.Id, db, AuthenticatedRequest(role, HttpMethods.Get, $"{archivePath}/{item.Id}/evidence"));
        var file = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.FileContentHttpResult>(download);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal(evidence, file.FileContents.ToArray());
    }

    [Fact]
    public async Task HistoricAnchorEvidenceArchive_IsNotAvailableToClients_OrAcrossPeriods()
    {
        await using var db = CreateDbContext();
        var (_, second, third, _) = await SeedHistoricChainAsync(db);
        await RecordAnchorAsync(db, third, SmallButNotMicro);
        await RetireAnchorAsync(db, third);
        var record = await db.HistoricAnchorEvidenceRecords.AsNoTracking().SingleAsync(r => r.PeriodId == third.Id);

        Assert.False(RoleAuthorizationService.Authorize(AuthenticatedRole("Client"), new PathString(ArchivePath(third)), HttpMethods.Get).IsAllowed);
        Assert.False(RoleAuthorizationService.Authorize(AuthenticatedRole("Client"), new PathString($"{ArchivePath(third)}/{record.Id}/evidence"), HttpMethods.Get).IsAllowed);
        var clientContext = AuthenticatedRequest("Client", HttpMethods.Get, ArchivePath(third));
        clientContext.Items[AuthContext.ItemKey] = AuthenticatedRole("Client") with { AllowedCompanyIds = new HashSet<int> { third.CompanyId } };
        Assert.Equal(StatusCodes.Status403Forbidden, ResultStatusCode(
            await ClassificationEndpoints.ListHistoricAnchorEvidenceEndpointAsync(third.CompanyId, third.Id, db, clientContext)));

        // A record of another period is not served through this period's route.
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(await ClassificationEndpoints.DownloadHistoricAnchorEvidenceEndpointAsync(
            second.CompanyId, second.Id, record.Id, db, AuthenticatedRequest("Owner", HttpMethods.Get, $"{ArchivePath(second)}/{record.Id}/evidence"))));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(await ClassificationEndpoints.ListHistoricAnchorEvidenceEndpointAsync(
            third.CompanyId + 1000, third.Id, db, AuthenticatedRequest("Owner", HttpMethods.Get, ArchivePath(third)))));
    }

    [Theory]
    [InlineData(PeriodStatus.Finalised, false)]
    [InlineData(PeriodStatus.Filed, false)]
    [InlineData(PeriodStatus.Draft, true)]
    public async Task HistoricAnchorRetirement_IsRefused_WhenALaterPeriodThatReliesOnItIsFinalisedFiledOrLocked(PeriodStatus status, bool lockedAt)
    {
        await using var db = CreateDbContext();
        var (_, _, third, current) = await SeedHistoricChainAsync(db);
        await RecordAnchorAsync(db, third, SmallButNotMicro);
        var relying = await db.AccountingPeriods.SingleAsync(p => p.Id == current.Id);
        relying.Status = status;
        relying.LockedAt = lockedAt ? DateTime.UtcNow : null;
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => RetireAnchorAsync(db, third));

        Assert.Equal(
            "These historic figures cannot be retired: 1 Jan 2025 – 31 Dec 2025 relies on them for its size test and is finalised, filed or locked. Filed or locked work is never changed silently; reopen that period first if it can be reopened.",
            error.Message);
        Assert.Equal(SizeInputBasis.HistoricAnchor, (await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == third.Id)).InputBasis);
        Assert.Empty(await db.HistoricAnchorEvidenceRecords.AsNoTracking().ToListAsync());
        Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == AuditEventCodes.SizeClassificationHistoricAnchorRetired));
    }

    [Fact]
    public async Task HistoricAnchorRetirement_IgnoresLockedPeriodsRootedAtALaterAnchor()
    {
        await using var db = CreateDbContext();
        var (_, second, third, current) = await SeedHistoricChainAsync(db);
        // The 2024 anchor first: once 2019 is anchored, 2024 would be classifiable from it instead.
        await RecordAnchorAsync(db, third, SmallButNotMicro);
        await RecordAnchorAsync(db, second, SmallButNotMicro);
        var later = await db.AccountingPeriods.SingleAsync(p => p.Id == current.Id);
        later.Status = PeriodStatus.Filed;
        later.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // The filed 2025 period is rooted at the 2024 anchor, not at the 2019 one.
        var retired = await RetireAnchorAsync(db, second);

        Assert.Equal(SizeInputBasis.RetiredHistoricAnchor, retired.InputBasis);
    }

    private static string ArchivePath(AccountingPeriod period) =>
        $"/api/companies/{period.CompanyId}/periods/{period.Id}/classification/historic-anchor/archive";
}

// Batch B fix round 2: replacing an active anchor that filed work relies on is refused like
// retirement, and archive records carry the principals' user ids for subject access.
public partial class AccountsWorkflowTests
{
    [Theory]
    [InlineData(PeriodStatus.Finalised, false)]
    [InlineData(PeriodStatus.Filed, false)]
    [InlineData(PeriodStatus.Draft, true)]
    public async Task HistoricAnchorReplacement_IsRefused_WhenALaterPeriodThatReliesOnItIsFinalisedFiledOrLocked(PeriodStatus status, bool lockedAt)
    {
        await using var db = CreateDbContext();
        var (_, _, third, current) = await SeedHistoricChainAsync(db);
        var original = Pdf("filed accounts relied on");
        await RecordAnchorAsync(db, third, SmallButNotMicro, evidence: original);
        var relying = await db.AccountingPeriods.SingleAsync(p => p.Id == current.Id);
        relying.Status = status;
        relying.LockedAt = lockedAt ? DateTime.UtcNow : null;
        await db.SaveChangesAsync();
        var before = await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == third.Id);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => RecordAnchorAsync(db, third, SmallButNotMicro, CompanySizeClass.Small, evidence: Pdf("replacement")));

        Assert.Equal(
            "These historic figures cannot be replaced: 1 Jan 2025 – 31 Dec 2025 relies on them for its size test and is finalised, filed or locked. Filed or locked work is never changed silently; reopen that period first if it can be reopened.",
            error.Message);
        var after = await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == third.Id);
        Assert.Equal(original, after.HistoricEvidenceArtifact);
        Assert.Equal(before.DecisionInputFingerprintSha256, after.DecisionInputFingerprintSha256);
        Assert.Null(after.HistoricAttestedEffectiveClass);
        Assert.Empty(await db.HistoricAnchorEvidenceRecords.AsNoTracking().ToListAsync());
        Assert.Single(await db.AuditLogs.AsNoTracking().Where(a => a.Action == AuditEventCodes.SizeClassificationHistoricAnchorRecorded).ToListAsync());
    }

    [Fact]
    public async Task HistoricAnchorEvidenceArchive_RecordsTheUserIdsOfWhoArchivedAndWhoRetired()
    {
        await using var db = CreateDbContext();
        var (_, _, third, _) = await SeedHistoricChainAsync(db);
        await RecordAnchorAsync(db, third, SmallButNotMicro);
        await RetireAnchorAsync(db, third);
        await RecordAnchorAsync(db, third, SmallButNotMicro, evidence: Pdf("correct filed accounts"));

        var archive = await db.HistoricAnchorEvidenceRecords.AsNoTracking().Where(r => r.PeriodId == third.Id).OrderBy(r => r.Id).ToListAsync();

        // Retirement by "user:1"; the replacement recorded by "user:7" (see RecordAnchorAsync).
        Assert.Equal(("user:1", "user:1"), (archive[0].ArchivedByUserId, archive[0].RetiredByUserId));
        Assert.Equal(("user:7", "user:1"), (archive[1].ArchivedByUserId, archive[1].RetiredByUserId));
    }
}
