using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounts.Tests;

/// <summary>
/// The optional "reviewed by an accountant" attestation: recorded and shown in readiness, never a gate.
/// </summary>
public sealed partial class DirectorsApprovalTests
{
    private static AccountantReviewAttestationInput Review(
        string name = "A. Accountant",
        string body = "Chartered Accountants Ireland",
        string membership = "12345",
        DateOnly? date = null,
        byte[]? letter = null,
        string? letterSha = null) =>
        new(name, body, membership, date ?? new DateOnly(2026, 11, 18), letter, letterSha);

    [Fact]
    public async Task OptionalAccountantReview_IsRecordedButNeverBlocks()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        await Service(db).RecordAccountantReviewAsync(p.CompanyId, p.Id, Review(), Owner);

        var profile = await new FilingReadinessProfileService(db).GetProfileAsync(p.CompanyId, p.Id);

        Assert.Equal("Optional review recorded: A. Accountant (Chartered Accountants Ireland, 12345)", profile.AccountantReviewState);
        Assert.Contains(profile.RequiredEvidence, e => e.Code == "accountant-review-optional" && !e.Required && e.Satisfied);
        Assert.DoesNotContain(profile.BlockingIssues, i => i.Code.Contains("accountant"));
        Assert.False(profile.AccountantReviewRequired);
        Assert.Single(db.AuditLogs.Where(a => a.Action == AuditEventCodes.AccountantReviewAttestationRecorded));
    }

    [Fact]
    public async Task OptionalAccountantReview_NotRecorded_IsReportedAsNotRequiredAndNeverBlocks()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);

        var profile = await new FilingReadinessProfileService(db).GetProfileAsync(p.CompanyId, p.Id);

        Assert.Equal("Not required by law — not recorded", profile.AccountantReviewState);
        Assert.Contains(profile.RequiredEvidence, e => e.Code == "accountant-review-optional" && !e.Required && !e.Satisfied);
        Assert.DoesNotContain(profile.BlockingIssues, i => i.Code.Contains("accountant"));
    }

    [Fact]
    public async Task OptionalAccountantReview_RetainsAValidatedReviewLetter()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var letter = Pdf("review letter");

        await Service(db).RecordAccountantReviewAsync(p.CompanyId, p.Id,
            Review(name: "  A. Accountant  ", letter: letter, letterSha: DirectorsApprovalIntegrity.Sha256(letter)), Owner);

        var cro = await db.CroFilingPackages.SingleAsync(c => c.PeriodId == p.Id);
        Assert.Equal("A. Accountant", cro.OptionalReviewerName);
        Assert.Equal("Chartered Accountants Ireland", cro.ApproverProfessionalBody);
        Assert.Equal("12345", cro.ApproverMembershipNumber);
        Assert.Equal(new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc), cro.ApproverVerifiedAt);
        Assert.Equal(letter, cro.ApproverVerificationArtifact);
        Assert.Equal(DirectorsApprovalIntegrity.Sha256(letter), cro.ApproverVerificationArtifactSha256);
        Assert.Null(cro.ApprovalCapacity);
        Assert.Null(cro.ApprovedBy);
    }

    [Fact]
    public async Task OptionalAccountantReview_RejectsInvalidInput()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var letter = Pdf("review letter");
        var notPdf = new byte[] { 1, 2, 3, 4, 5, 6, 7 };

        async Task Rejects(AccountantReviewAttestationInput input, string expected)
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RecordAccountantReviewAsync(p.CompanyId, p.Id, input, Owner));
            Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
        }

        await Rejects(Review(name: "A"), "reviewer name");
        await Rejects(Review(name: new string('x', 201)), "reviewer name");
        await Rejects(Review(body: "Self-Declared Experts Ltd"), "professional body");
        await Rejects(Review(membership: "  "), "membership number");
        await Rejects(Review(date: new DateOnly(2026, 11, 22)), "future");
        await Rejects(Review(date: new DateOnly(2026, 7, 30)), "period end");
        await Rejects(Review(letter: notPdf, letterSha: DirectorsApprovalIntegrity.Sha256(notPdf)), "pdf");
        await Rejects(Review(letter: letter, letterSha: new string('0', 64)), "digest");
        await Rejects(Review(letter: letter, letterSha: null), "digest");

        var cro = await db.CroFilingPackages.SingleAsync(c => c.PeriodId == p.Id);
        Assert.Null(cro.OptionalReviewerName);
        Assert.Empty(db.AuditLogs.Where(a => a.Action == AuditEventCodes.AccountantReviewAttestationRecorded));
    }

    [Fact]
    public async Task OptionalAccountantReview_RequiresGeneratedAccounts()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var cro = await db.CroFilingPackages.SingleAsync(c => c.PeriodId == p.Id);
        cro.AccountsPdfGenerated = false;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db).RecordAccountantReviewAsync(p.CompanyId, p.Id, Review(), Owner));
    }

    [Fact]
    public async Task OptionalAccountantReview_SurvivesTheBoardApprovalBinding()
    {
        var harness = await FilingGateFixture.ReadyForApprovalAsync();
        await Service(harness.Db).RecordAccountantReviewAsync(
            harness.CompanyId, harness.PeriodId, Review(date: new DateOnly(2026, 3, 2)), Owner);
        await harness.RecordDirectorsApprovalAsync();

        await harness.Gate.BindApprovalAsync(harness.CompanyId, harness.PeriodId, FilingReleaseWorkflow.Cro, "reviewer@example.ie");

        var cro = await harness.Db.CroFilingPackages.SingleAsync(c => c.PeriodId == harness.PeriodId);
        Assert.Equal("board-of-directors-s324", cro.ApprovalCapacity);
        Assert.Equal("A. Accountant", cro.OptionalReviewerName);
        Assert.Equal("Chartered Accountants Ireland", cro.ApproverProfessionalBody);
        var profile = await new FilingReadinessProfileService(harness.Db).GetProfileAsync(harness.CompanyId, harness.PeriodId);
        Assert.Equal("Optional review recorded: A. Accountant (Chartered Accountants Ireland, 12345)", profile.AccountantReviewState);
    }

    [Fact]
    public async Task RegeneratingTheAccountsPdf_ClearsTheOptionalAccountantReview()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var letter = Pdf("review letter");
        await Service(db).RecordAccountantReviewAsync(p.CompanyId, p.Id,
            Review(letter: letter, letterSha: DirectorsApprovalIntegrity.Sha256(letter)), Owner);

        await Gate(db).RecordCroArtifactAsync(p.CompanyId, p.Id, FilingReleaseArtifact.CroAccountsPdf, Pdf("accounts v2"));

        var cro = await db.CroFilingPackages.SingleAsync(c => c.PeriodId == p.Id);
        Assert.Null(cro.OptionalReviewerName);
        Assert.Null(cro.ApproverProfessionalBody);
        Assert.Null(cro.ApproverMembershipNumber);
        Assert.Null(cro.ApproverVerifiedAt);
        Assert.Null(cro.ApproverVerificationArtifact);
        Assert.Null(cro.ApproverVerificationArtifactSha256);
        var profile = await new FilingReadinessProfileService(db).GetProfileAsync(p.CompanyId, p.Id);
        Assert.Equal("Not required by law — not recorded", profile.AccountantReviewState);
    }

    [Fact]
    public async Task RegeneratingIdenticalAccountsBytes_KeepsTheOptionalAccountantReview()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var gate = Gate(db);
        var accounts = Pdf("accounts v1");
        await gate.RecordCroArtifactAsync(p.CompanyId, p.Id, FilingReleaseArtifact.CroAccountsPdf, accounts);
        await Service(db).RecordAccountantReviewAsync(p.CompanyId, p.Id, Review(), Owner);

        await gate.RecordCroArtifactAsync(p.CompanyId, p.Id, FilingReleaseArtifact.CroAccountsPdf, accounts);

        var cro = await db.CroFilingPackages.SingleAsync(c => c.PeriodId == p.Id);
        Assert.Equal("A. Accountant", cro.OptionalReviewerName);
    }
}
