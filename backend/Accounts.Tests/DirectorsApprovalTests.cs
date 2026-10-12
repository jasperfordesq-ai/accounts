using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Accounts.Api.Endpoints;
using Xunit;

namespace Accounts.Tests;

public sealed partial class DirectorsApprovalTests
{
    [Fact]
    public void StatutorySources_CiteSection324AndTheCharitiesActCompanyExclusion()
    {
        Assert.Equal("companies-act-2014-s324", IrishStatutoryRuleSources.CompaniesActSection324.SourceId);
        Assert.StartsWith("https://www.irishstatutebook.ie/", IrishStatutoryRuleSources.CompaniesActSection324.Url);
        Assert.Equal("charities-act-2009-s50-company-exclusion", IrishStatutoryRuleSources.CharitiesActSection50CompanyExclusion.SourceId);
        Assert.Equal("DirectorsApprovalRecorded", AuditEventCodes.DirectorsApprovalRecorded);
    }

    internal static AccountsDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AccountsDbContext>().UseInMemoryDatabase($"s324-{Guid.NewGuid():N}").Options);

    internal static DirectorsApprovalPage SamplePage(int periodId, int firstId = 1, int? secondId = 2)
    {
        var page = new DirectorsApprovalPage
        {
            PeriodId = periodId,
            ApprovalDate = new DateOnly(2026, 11, 20),
            FirstDirectorOfficerId = firstId,
            SecondDirectorOfficerId = secondId,
            FirstDirectorName = "Director One",
            SecondDirectorName = secondId is null ? null : "Director Two",
            DirectorsInOfficeCount = secondId is null ? 1 : 3,
            ResolutionReference = "WR-2026-11-20-accounts",
            AccountsPdfSha256 = new string('a', 64),
            ArtifactSourceFingerprintSha256 = new string('b', 64),
            PageArtifact = [0x25, 0x50, 0x44, 0x46, 0x2d, 0x01],
            PageSha256 = new string('c', 64),
            GeneratedByUserId = "user:1",
            GeneratedByDisplayName = "Owner",
            GeneratedAtUtc = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc),
            RecordSha256 = ""
        };
        page.RecordSha256 = DirectorsApprovalIntegrity.ComputePageHash(page);
        return page;
    }

    internal sealed record SeededPeriod(int CompanyId, int Id, int FirstDirectorId, int SecondDirectorId, int ThirdDirectorId, int SecretaryId);

    internal static async Task<SeededPeriod> SeedPeriodAsync(AccountsDbContext db, bool auditExempt = true, ElectedRegime regime = ElectedRegime.Micro)
    {
        var company = new Company
        {
            TenantId = 1,
            LegalName = "Example Charity CLG",
            CroNumber = "600001",
            CompanyType = CompanyType.CompanyLimitedByGuarantee,
            IncorporationDate = new DateOnly(2017, 7, 20),
            AnnualReturnDate = new DateOnly(2026, 1, 20),
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Cork",
            RegisteredOfficeCounty = "Cork"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var d1 = new CompanyOfficer { CompanyId = company.Id, Name = "Director One", Role = OfficerRole.Director, AppointedDate = new DateOnly(2017, 7, 20) };
        var d2 = new CompanyOfficer { CompanyId = company.Id, Name = "Director Two", Role = OfficerRole.Director, AppointedDate = new DateOnly(2017, 7, 20) };
        var d3 = new CompanyOfficer { CompanyId = company.Id, Name = "Director Three", Role = OfficerRole.Director, AppointedDate = new DateOnly(2019, 1, 1) };
        var sec = new CompanyOfficer { CompanyId = company.Id, Name = "Director Two", Role = OfficerRole.Secretary, AppointedDate = new DateOnly(2017, 7, 20) };
        db.CompanyOfficers.AddRange(d1, d2, d3, sec);
        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2025, 8, 1),
            PeriodEnd = new DateOnly(2026, 7, 31),
            ApprovalDate = new DateOnly(2026, 11, 20),
            IsFirstYear = true
        };
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = regime,
            CanUseMicro = regime == ElectedRegime.Micro,
            CanFileAbridged = true,
            AuditExempt = auditExempt
        });
        db.CroFilingPackages.Add(new CroFilingPackage
        {
            PeriodId = period.Id,
            AccountsPdfGenerated = true,
            AccountsPdfArtifact = [0x25, 0x50, 0x44, 0x46, 0x2d, 0x31],
            AccountsPdfSha256 = new string('a', 64),
            // Realistic order: the board date (2026-11-20) is recorded at finalisation and the
            // accounts PDF is generated afterwards (the service clock is 2026-11-21 12:00 UTC).
            AccountsPdfGeneratedAtUtc = new DateTime(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc),
            ArtifactSourceFingerprintSha256 = new string('b', 64)
        });
        await db.SaveChangesAsync();
        return new SeededPeriod(company.Id, period.Id, d1.Id, d2.Id, d3.Id, sec.Id);
    }

    [Fact]
    public void PageHash_ChangesWhenAnyBoundFieldChanges()
    {
        var page = SamplePage(9);
        Assert.True(DirectorsApprovalIntegrity.IsValid(page));
        page.ResolutionReference = "WR-tampered";
        Assert.False(DirectorsApprovalIntegrity.IsValid(page));
    }

    [Fact]
    public async Task Pages_AreAppendOnly()
    {
        await using var db = CreateDb();
        var period = await SeedPeriodAsync(db);
        db.DirectorsApprovalPages.Add(SamplePage(period.Id, period.FirstDirectorId, period.SecondDirectorId));
        await db.SaveChangesAsync();

        var stored = await db.DirectorsApprovalPages.SingleAsync();
        stored.ResolutionReference = "changed";
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => db.SaveChangesAsync());
        Assert.Contains("append-only", error.Message);
    }

    [Fact]
    public async Task Pages_WithABadHash_AreRejectedOnInsert()
    {
        await using var db = CreateDb();
        var period = await SeedPeriodAsync(db);
        var page = SamplePage(period.Id, period.FirstDirectorId, period.SecondDirectorId);
        page.RecordSha256 = new string('0', 64);
        db.DirectorsApprovalPages.Add(page);
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.SaveChangesAsync());
    }

    private static readonly AuthenticatedUser Owner = new(
        UserId: 1, TenantId: 1, TenantName: "Firm", Email: "owner@example.ie",
        DisplayName: "Firm Owner", Role: "Owner");

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    internal sealed class FakePageRenderer : IDirectorsApprovalPageRenderer
    {
        public Task<byte[]> RenderAsync(int companyId, int periodId, DirectorsApprovalPageContent content) =>
            Task.FromResult<byte[]>([.. "%PDF-1.7\nunsigned "u8.ToArray(), .. System.Text.Encoding.UTF8.GetBytes(content.ResolutionReference)]);
    }

    internal static DirectorsApprovalService Service(AccountsDbContext db) =>
        new(db, new FakePageRenderer(), new AuditService(db), new FixedClock(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero)));

    private static byte[] Pdf(string marker) => [.. "%PDF-1.7\n"u8.ToArray(), .. System.Text.Encoding.UTF8.GetBytes(marker)];

    [Fact]
    public async Task ThreeDirectorsInOffice_RequireTwoDistinctDirectors()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db).GeneratePageAsync(
            p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, null, "WR-2026-11-20"), Owner));
        Assert.Contains("two directors must sign", error.Message);
    }

    [Fact]
    public async Task TheSamePersonThroughTwoOfficerRows_IsRejected()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        // A second Director row for the same person proves the name check.
        var duplicate = new CompanyOfficer { CompanyId = p.CompanyId, Name = " director two ", Role = OfficerRole.Director, AppointedDate = new DateOnly(2018, 1, 1) };
        db.CompanyOfficers.Add(duplicate);
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db).GeneratePageAsync(
            p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.SecondDirectorId, duplicate.Id, "WR-2026-11-20"), Owner));
        Assert.Contains("two different directors", error.Message);
    }

    [Fact]
    public async Task SecretaryOnlyOfficer_CannotSign()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db).GeneratePageAsync(
            p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecretaryId, "WR-2026-11-20"), Owner));
        Assert.Contains("was not a director in office on 2026-11-20", error.Message);
    }

    [Fact]
    public async Task SoleDirector_SignsAlone()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        foreach (var officer in db.CompanyOfficers.Where(o => o.Id != p.FirstDirectorId && o.Role == OfficerRole.Director))
            officer.ResignedDate = new DateOnly(2026, 1, 1);
        await db.SaveChangesAsync();

        var page = await Service(db).GeneratePageAsync(
            p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, null, "Board minute 2026-11-20"), Owner);

        Assert.Equal(1, page.DirectorsInOfficeCount);
        Assert.Null(page.SecondDirectorOfficerId);
        Assert.Equal("Director One", page.FirstDirectorName);
    }

    [Theory]
    [InlineData("2026-07-30", "on or after the period end")]
    [InlineData("2026-11-22", "cannot be in the future")]
    public async Task ApprovalDate_Bounds(string date, string expected)
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db).GeneratePageAsync(
            p.CompanyId, p.Id, new(DateOnly.Parse(date), p.FirstDirectorId, p.SecondDirectorId, "WR-2026"), Owner));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public async Task RecordApproval_HappyPath_BecomesCurrentAndLeavesApprovalDateUnchanged()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var page = await service.GeneratePageAsync(p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR-2026-11-20"), Owner);
        var signed = Pdf("signed");
        var minute = Pdf("minute");

        var approval = await service.RecordApprovalAsync(p.CompanyId, p.Id,
            new(page.Id, signed, DirectorsApprovalIntegrity.Sha256(signed), minute, DirectorsApprovalIntegrity.Sha256(minute)), Owner);

        Assert.Equal(DirectorsApprovalEventKind.Approved, approval.Kind);
        var current = await service.GetCurrentApprovalAsync(p.Id);
        Assert.NotNull(current);
        // Recording never changes the period's approval date (an input to the accounts).
        Assert.Equal(new DateOnly(2026, 11, 20), (await db.AccountingPeriods.FindAsync(p.Id))!.ApprovalDate);
        Assert.Single(db.AuditLogs.Where(a => a.Action == AuditEventCodes.DirectorsApprovalRecorded));
    }

    [Fact]
    public async Task RecordApproval_RejectsUnsignedPageDigestMismatchNonPdfAndIdenticalUploads()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var page = await service.GeneratePageAsync(p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR-2026-11-20"), Owner);
        var minute = Pdf("minute");

        var unsigned = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RecordApprovalAsync(p.CompanyId, p.Id,
            new(page.Id, page.PageArtifact, page.PageSha256, minute, DirectorsApprovalIntegrity.Sha256(minute)), Owner));
        Assert.Contains("identical to the unsigned page", unsigned.Message);

        var signed = Pdf("signed");
        var mismatch = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RecordApprovalAsync(p.CompanyId, p.Id,
            new(page.Id, signed, new string('0', 64), minute, DirectorsApprovalIntegrity.Sha256(minute)), Owner));
        Assert.Contains("SHA-256", mismatch.Message);

        var notPdf = "hello"u8.ToArray();
        var nonPdf = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RecordApprovalAsync(p.CompanyId, p.Id,
            new(page.Id, notPdf, DirectorsApprovalIntegrity.Sha256(notPdf), minute, DirectorsApprovalIntegrity.Sha256(minute)), Owner));
        Assert.Contains("PDF", nonPdf.Message);

        var same = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RecordApprovalAsync(p.CompanyId, p.Id,
            new(page.Id, signed, DirectorsApprovalIntegrity.Sha256(signed), signed, DirectorsApprovalIntegrity.Sha256(signed)), Owner));
        Assert.Contains("must be different documents", same.Message);
    }

    [Fact]
    public async Task RecordApproval_RejectsAPageForAccountsThatHaveSinceBeenRegenerated()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var page = await service.GeneratePageAsync(p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR-2026-11-20"), Owner);
        var cro = await db.CroFilingPackages.SingleAsync(c => c.PeriodId == p.Id);
        cro.AccountsPdfSha256 = new string('d', 64);
        await db.SaveChangesAsync();
        var signed = Pdf("signed");
        var minute = Pdf("minute");

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RecordApprovalAsync(p.CompanyId, p.Id,
            new(page.Id, signed, DirectorsApprovalIntegrity.Sha256(signed), minute, DirectorsApprovalIntegrity.Sha256(minute)), Owner));
        Assert.Contains("generate a new approval page", error.Message);
    }

    [Fact]
    public async Task RecordApproval_RechecksOfficersOnTheApprovalDate()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var page = await service.GeneratePageAsync(p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR-2026-11-20"), Owner);
        var second = await db.CompanyOfficers.FindAsync(p.SecondDirectorId);
        second!.ResignedDate = new DateOnly(2026, 10, 1);
        await db.SaveChangesAsync();
        var signed = Pdf("signed");
        var minute = Pdf("minute");

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RecordApprovalAsync(p.CompanyId, p.Id,
            new(page.Id, signed, DirectorsApprovalIntegrity.Sha256(signed), minute, DirectorsApprovalIntegrity.Sha256(minute)), Owner));
        Assert.Contains("was not a director in office", error.Message);
    }

    [Fact]
    public async Task Revocation_MakesTheApprovalNotCurrentAndChainsHashes()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var page = await service.GeneratePageAsync(p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR-2026-11-20"), Owner);
        var signed = Pdf("signed");
        var minute = Pdf("minute");
        var approved = await service.RecordApprovalAsync(p.CompanyId, p.Id,
            new(page.Id, signed, DirectorsApprovalIntegrity.Sha256(signed), minute, DirectorsApprovalIntegrity.Sha256(minute)), Owner);

        var period = await db.AccountingPeriods.Include(x => x.CroFilingPackage).SingleAsync(x => x.Id == p.Id);
        service.AppendRevocationIfCurrent(period, period.CroFilingPackage!, "accounts-regenerated");
        await db.SaveChangesAsync();

        Assert.Null(await service.GetCurrentApprovalAsync(p.Id));
        var revoked = await db.DirectorsApprovalEvents.SingleAsync(e => e.Kind == DirectorsApprovalEventKind.Revoked);
        Assert.Equal(approved.RecordSha256, revoked.PreviousRecordSha256);
    }

    [Fact]
    public async Task GeneratePage_RejectsADateThatDiffersFromThePeriodApprovalDate()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db).GeneratePageAsync(
            p.CompanyId, p.Id, new(new DateOnly(2026, 11, 19), p.FirstDirectorId, p.SecondDirectorId, "WR-2026-11-19"), Owner));
        Assert.Contains("must match the board approval date", error.Message);
        Assert.Contains("2026-11-20", error.Message);
    }

    [Fact]
    public async Task GenerateAndRecord_RejectAPeriodWithoutAnApprovalDate()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var page = await service.GeneratePageAsync(p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR-2026-11-20"), Owner);
        (await db.AccountingPeriods.FindAsync(p.Id))!.ApprovalDate = null;
        await db.SaveChangesAsync();

        var generate = await Assert.ThrowsAsync<BusinessRuleException>(() => service.GeneratePageAsync(
            p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR-2026-11-20"), Owner));
        Assert.Contains("Record the board approval date on the period", generate.Message);

        var signed = Pdf("signed");
        var minute = Pdf("minute");
        var record = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RecordApprovalAsync(p.CompanyId, p.Id,
            new(page.Id, signed, DirectorsApprovalIntegrity.Sha256(signed), minute, DirectorsApprovalIntegrity.Sha256(minute)), Owner));
        Assert.Contains("Record the board approval date on the period", record.Message);
        Assert.Null((await db.AccountingPeriods.FindAsync(p.Id))!.ApprovalDate);
    }

    private static FilingReleaseGate Gate(AccountsDbContext db) =>
        new(db, "candidate-a", new AuditService(db));

    private static async Task<DirectorsApprovalPage> ApproveAsync(AccountsDbContext db, SeededPeriod p)
    {
        var service = Service(db);
        var page = await service.GeneratePageAsync(p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR-2026-11-20"), Owner);
        var signed = Pdf("signed");
        var minute = Pdf("minute");
        await service.RecordApprovalAsync(p.CompanyId, p.Id, new(page.Id, signed, DirectorsApprovalIntegrity.Sha256(signed), minute, DirectorsApprovalIntegrity.Sha256(minute)), Owner);
        return page;
    }

    [Fact]
    public async Task RegeneratingTheAccountsPdf_RevokesTheDirectorsApproval()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        await ApproveAsync(db, p);
        var seededGeneratedAt = new DateTime(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc);

        await Gate(db).RecordCroArtifactAsync(p.CompanyId, p.Id, FilingReleaseArtifact.CroAccountsPdf, Pdf("accounts v2"));

        Assert.Null(await service.GetCurrentApprovalAsync(p.Id));
        var revoked = await db.DirectorsApprovalEvents.SingleAsync(e => e.Kind == DirectorsApprovalEventKind.Revoked);
        Assert.Equal("accounts-source-changed", revoked.RevocationReason); // seeded fingerprint is stale, so the source changed too
        var cro = await db.CroFilingPackages.SingleAsync(c => c.PeriodId == p.Id);
        Assert.NotEqual(seededGeneratedAt, cro.AccountsPdfGeneratedAtUtc);
        Assert.InRange(cro.AccountsPdfGeneratedAtUtc!.Value, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
        Assert.Single(db.AuditLogs.Where(a => a.Action == AuditEventCodes.DirectorsApprovalRevoked));
    }

    [Fact]
    public async Task RegeneratingIdenticalAccountsBytes_KeepsTheApprovalAndTheGenerationTime()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var gate = Gate(db);
        var accounts = Pdf("accounts v1");
        await gate.RecordCroArtifactAsync(p.CompanyId, p.Id, FilingReleaseArtifact.CroAccountsPdf, accounts);
        await ApproveAsync(db, p);
        var stamped = (await db.CroFilingPackages.SingleAsync(c => c.PeriodId == p.Id)).AccountsPdfGeneratedAtUtc;

        await gate.RecordCroArtifactAsync(p.CompanyId, p.Id, FilingReleaseArtifact.CroAccountsPdf, accounts);

        Assert.NotNull(await service.GetCurrentApprovalAsync(p.Id));
        Assert.Empty(db.DirectorsApprovalEvents.Where(e => e.Kind == DirectorsApprovalEventKind.Revoked));
        Assert.Equal(stamped, (await db.CroFilingPackages.SingleAsync(c => c.PeriodId == p.Id)).AccountsPdfGeneratedAtUtc);
    }

    [Fact]
    public async Task GeneratingTheSignaturePageAlone_NeitherRevokesNorStampsTheAccountsTime()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var gate = Gate(db);
        await gate.RecordCroArtifactAsync(p.CompanyId, p.Id, FilingReleaseArtifact.CroAccountsPdf, Pdf("accounts v1"));
        await ApproveAsync(db, p);
        var stamped = (await db.CroFilingPackages.SingleAsync(c => c.PeriodId == p.Id)).AccountsPdfGeneratedAtUtc;

        await gate.RecordCroArtifactAsync(p.CompanyId, p.Id, FilingReleaseArtifact.CroSignaturePage, Pdf("signature page"));

        Assert.NotNull(await service.GetCurrentApprovalAsync(p.Id));
        Assert.Empty(db.DirectorsApprovalEvents.Where(e => e.Kind == DirectorsApprovalEventKind.Revoked));
        Assert.Equal(stamped, (await db.CroFilingPackages.SingleAsync(c => c.PeriodId == p.Id)).AccountsPdfGeneratedAtUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReleaseCandidateChangeAlone_KeepsTheDirectorsApprovalButStillRevokesTheCroPackageApproval(bool signaturePageFirst)
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var accounts = Pdf("accounts v1");
        await Gate(db).RecordCroArtifactAsync(p.CompanyId, p.Id, FilingReleaseArtifact.CroAccountsPdf, accounts);
        await ApproveAsync(db, p);
        var cro = await db.CroFilingPackages.SingleAsync(c => c.PeriodId == p.Id);
        cro.ApprovedBy = "Qualified Reviewer";
        cro.ApprovedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var stamped = cro.AccountsPdfGeneratedAtUtc;

        // A new release candidate (a deploy) with identical accounts bytes and an unchanged source.
        var nextGate = new FilingReleaseGate(db, "candidate-b", new AuditService(db));
        if (signaturePageFirst)
            await nextGate.RecordCroArtifactAsync(p.CompanyId, p.Id, FilingReleaseArtifact.CroSignaturePage, Pdf("signature page"));
        await nextGate.RecordCroArtifactAsync(p.CompanyId, p.Id, FilingReleaseArtifact.CroAccountsPdf, accounts);

        Assert.NotNull(await service.GetCurrentApprovalAsync(p.Id));
        Assert.Empty(db.DirectorsApprovalEvents.Where(e => e.Kind == DirectorsApprovalEventKind.Revoked));
        Assert.Empty(db.AuditLogs.Where(a => a.Action == AuditEventCodes.DirectorsApprovalRevoked));
        cro = await db.CroFilingPackages.SingleAsync(c => c.PeriodId == p.Id);
        Assert.Equal(stamped, cro.AccountsPdfGeneratedAtUtc);
        Assert.Equal("candidate-b", cro.ArtifactReleaseCandidate);
        Assert.Null(cro.ApprovedBy); // the existing CRO package approval revocation on candidate change is unchanged
        Assert.Null(cro.ApprovedAt);
    }

    [Fact]
    public async Task ChangingTheAccountsPdfBytes_RevokesWithTheRegeneratedReason()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var gate = Gate(db);
        await gate.RecordCroArtifactAsync(p.CompanyId, p.Id, FilingReleaseArtifact.CroAccountsPdf, Pdf("accounts v1"));
        await ApproveAsync(db, p);

        await gate.RecordCroArtifactAsync(p.CompanyId, p.Id, FilingReleaseArtifact.CroAccountsPdf, Pdf("accounts v2"));

        Assert.Null(await service.GetCurrentApprovalAsync(p.Id));
        var revoked = await db.DirectorsApprovalEvents.SingleAsync(e => e.Kind == DirectorsApprovalEventKind.Revoked);
        Assert.Equal("accounts-regenerated", revoked.RevocationReason);
    }

    [Fact]
    public async Task ReApprovalAfterRevocation_BecomesCurrentAndChainsToTheRevocation()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var input = new DirectorsApprovalPageInput(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR-2026-11-20");
        var signed = Pdf("signed");
        var minute = Pdf("minute");
        var firstPage = await service.GeneratePageAsync(p.CompanyId, p.Id, input, Owner);
        await service.RecordApprovalAsync(p.CompanyId, p.Id,
            new(firstPage.Id, signed, DirectorsApprovalIntegrity.Sha256(signed), minute, DirectorsApprovalIntegrity.Sha256(minute)), Owner);

        var period = await db.AccountingPeriods.Include(x => x.CroFilingPackage).SingleAsync(x => x.Id == p.Id);
        service.AppendRevocationIfCurrent(period, period.CroFilingPackage!, "board-withdrew-approval");
        await db.SaveChangesAsync();
        var revoked = await db.DirectorsApprovalEvents.SingleAsync(e => e.Kind == DirectorsApprovalEventKind.Revoked);

        var secondPage = await service.GeneratePageAsync(p.CompanyId, p.Id, input, Owner);
        var signedAgain = Pdf("signed again");
        var reApproval = await service.RecordApprovalAsync(p.CompanyId, p.Id,
            new(secondPage.Id, signedAgain, DirectorsApprovalIntegrity.Sha256(signedAgain), minute, DirectorsApprovalIntegrity.Sha256(minute)), Owner);

        var current = await service.GetCurrentApprovalAsync(p.Id);
        Assert.NotNull(current);
        Assert.Equal(reApproval.Id, current!.Approval.Id);
        Assert.Equal(secondPage.Id, current.Page.Id);
        Assert.Equal(revoked.RecordSha256, reApproval.PreviousRecordSha256);
        Assert.Equal(new DateOnly(2026, 11, 20), (await db.AccountingPeriods.FindAsync(p.Id))!.ApprovalDate);
    }

    // ---- Endpoints -------------------------------------------------------------------------------

    private static Microsoft.AspNetCore.Http.DefaultHttpContext EndpointContext(
        string role, string method, string path, int companyId, int tenantId = 1)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 7, TenantId: tenantId, TenantName: "Firm", Email: "user@example.ie",
            DisplayName: "Example User", Role: role)
        { AllowedCompanyIds = new HashSet<int> { companyId } };
        return context;
    }

    private static ApiAccessService NoApiKey() =>
        new(Microsoft.Extensions.Options.Options.Create(new Accounts.Api.Rules.ApiAccessConfig { Enabled = false }),
            new EndpointTestEnvironment());

    private sealed class EndpointTestEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Accounts.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static int? Status(Microsoft.AspNetCore.Http.IResult result) =>
        Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(result).StatusCode;

    private static T Value<T>(Microsoft.AspNetCore.Http.IResult result) =>
        Assert.IsType<T>(Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IValueHttpResult>(result).Value);

    private static string FilingPath(int companyId, int periodId, string action) =>
        $"/api/companies/{companyId}/periods/{periodId}/filing/{action}";

    private static DirectorsApprovalRecordInput RecordInput(int pageId, byte[] signed, byte[] minute) =>
        new(pageId, signed, DirectorsApprovalIntegrity.Sha256(signed), minute, DirectorsApprovalIntegrity.Sha256(minute));

    [Fact]
    public async Task RecordApprovalEndpoint_Reviewer_RecordsAndReturnsTheCurrentApproval()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var page = await service.GeneratePageAsync(p.CompanyId, p.Id,
            new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR-2026-11-20"), Owner);
        var context = EndpointContext("Reviewer", "POST", FilingPath(p.CompanyId, p.Id, "directors-approval"), p.CompanyId);

        var result = await DirectorsApprovalEndpoints.RecordApprovalEndpointAsync(
            p.CompanyId, p.Id, RecordInput(page.Id, Pdf("signed"), Pdf("minute")),
            service, new FilingReadinessProfileService(db), db, context, NoApiKey());

        Assert.Equal(200, Status(result));
        var summary = Value<DirectorsApprovalSummary>(result);
        Assert.Equal(page.Id, summary.Page.Id);
        Assert.Single(await db.DirectorsApprovalEvents.ToListAsync());
    }

    [Theory]
    [InlineData("Client")]
    [InlineData("Accountant")]
    public async Task RecordApprovalEndpoint_IsDeniedBeforeAnyRowIsWritten(string role)
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var page = await service.GeneratePageAsync(p.CompanyId, p.Id,
            new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR-2026-11-20"), Owner);
        var context = EndpointContext(role, "POST", FilingPath(p.CompanyId, p.Id, "directors-approval"), p.CompanyId);

        var result = await DirectorsApprovalEndpoints.RecordApprovalEndpointAsync(
            p.CompanyId, p.Id, RecordInput(page.Id, Pdf("signed"), Pdf("minute")),
            service, new FilingReadinessProfileService(db), db, context, NoApiKey());

        Assert.Equal(403, Status(result));
        Assert.Empty(await db.DirectorsApprovalEvents.ToListAsync());
    }

    [Fact]
    public async Task GeneratePageEndpoint_AccountantMayGenerate_ClientMayNot()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var input = new DirectorsApprovalPageInput(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR-2026-11-20");
        var path = FilingPath(p.CompanyId, p.Id, "directors-approval-page");

        var denied = await DirectorsApprovalEndpoints.GeneratePageEndpointAsync(p.CompanyId, p.Id, input, service,
            new FilingReadinessProfileService(db), db, EndpointContext("Client", "POST", path, p.CompanyId), NoApiKey());
        Assert.Equal(403, Status(denied));
        Assert.Empty(await db.DirectorsApprovalPages.ToListAsync());

        var allowed = await DirectorsApprovalEndpoints.GeneratePageEndpointAsync(p.CompanyId, p.Id, input, service,
            new FilingReadinessProfileService(db), db, EndpointContext("Accountant", "POST", path, p.CompanyId), NoApiKey());
        Assert.Equal(200, Status(allowed));
        var summary = Value<DirectorsApprovalPageSummary>(allowed);
        Assert.Equal("Director One", summary.FirstDirectorName);
        Assert.Equal(3, summary.DirectorsInOfficeCount);
        Assert.Single(await db.DirectorsApprovalPages.ToListAsync());
    }

    [Fact]
    public async Task UnsupportedFilingPath_IsRejectedWithoutWritingAPage()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var company = await db.Companies.SingleAsync(c => c.Id == p.CompanyId);
        company.IsGroupMember = true;
        await db.SaveChangesAsync();
        var path = FilingPath(p.CompanyId, p.Id, "directors-approval-page");

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => DirectorsApprovalEndpoints.GeneratePageEndpointAsync(
            p.CompanyId, p.Id,
            new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR-2026-11-20"),
            Service(db), new FilingReadinessProfileService(db), db,
            EndpointContext("Owner", "POST", path, p.CompanyId), NoApiKey()));

        Assert.Equal("This company is outside the supported filing path.", error.Message);
        Assert.Empty(await db.DirectorsApprovalPages.ToListAsync());
    }

    [Fact]
    public async Task StateEndpoint_ListsDirectorsAtThePeriodApprovalDate_AndTheCurrentApproval()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        // Director Three leaves before the approval date; the period end (2026-07-31) would still include them.
        (await db.CompanyOfficers.SingleAsync(o => o.Id == p.ThirdDirectorId)).ResignedDate = new DateOnly(2026, 9, 1);
        await db.SaveChangesAsync();
        var service = Service(db);
        var context = EndpointContext("Client", "GET", FilingPath(p.CompanyId, p.Id, "directors-approval"), p.CompanyId);

        var before = Value<DirectorsApprovalStateResponse>(
            await DirectorsApprovalEndpoints.GetStateEndpointAsync(p.CompanyId, p.Id, null, service, db, context));
        Assert.Equal(["Director One", "Director Two"], before.EligibleDirectors.Select(d => d.Name).ToArray());
        Assert.Equal(new DateOnly(2026, 11, 20), before.PeriodApprovalDate);
        Assert.Null(before.LatestPage);
        Assert.Null(before.Current);

        var atPeriodEnd = Value<DirectorsApprovalStateResponse>(
            await DirectorsApprovalEndpoints.GetStateEndpointAsync(p.CompanyId, p.Id, new DateOnly(2026, 7, 31), service, db, context));
        Assert.Equal(3, atPeriodEnd.EligibleDirectors.Count);

        var page = await service.GeneratePageAsync(p.CompanyId, p.Id,
            new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR-2026-11-20"), Owner);
        var middle = Value<DirectorsApprovalStateResponse>(
            await DirectorsApprovalEndpoints.GetStateEndpointAsync(p.CompanyId, p.Id, null, service, db, context));
        Assert.Equal(page.Id, middle.LatestPage!.Id);
        Assert.Null(middle.Current);

        await service.RecordApprovalAsync(p.CompanyId, p.Id, RecordInput(page.Id, Pdf("signed"), Pdf("minute")), Owner);
        var after = Value<DirectorsApprovalStateResponse>(
            await DirectorsApprovalEndpoints.GetStateEndpointAsync(p.CompanyId, p.Id, null, service, db, context));
        Assert.Equal(page.Id, after.Current!.Page.Id);
    }

    [Fact]
    public async Task StateEndpoint_UnknownPeriod_IsNotFound()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var result = await DirectorsApprovalEndpoints.GetStateEndpointAsync(
            p.CompanyId, p.Id + 99, null, Service(db), db,
            EndpointContext("Owner", "GET", FilingPath(p.CompanyId, p.Id + 99, "directors-approval"), p.CompanyId));
        Assert.Equal(404, Status(result));
    }

    [Fact]
    public void StateResponse_SerialisesNullMembersExplicitly()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            new DirectorsApprovalStateResponse([], null, null, null, []),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
        Assert.Contains("\"latestPage\":null", json);
        Assert.Contains("\"current\":null", json);
        Assert.Contains("\"periodApprovalDate\":null", json);
    }

    [Fact]
    public async Task PagePdfEndpoint_ReturnsTheUnsignedPageBytes_AndRefusesUnknownPages()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var page = await Service(db).GeneratePageAsync(p.CompanyId, p.Id,
            new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR-2026-11-20"), Owner);
        var context = EndpointContext("Client", "GET", FilingPath(p.CompanyId, p.Id, $"directors-approval-page/{page.Id}"), p.CompanyId);

        var result = await DirectorsApprovalEndpoints.GetPagePdfEndpointAsync(p.CompanyId, p.Id, page.Id, db, context);
        var file = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.FileContentHttpResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal(page.PageArtifact, file.FileContents.ToArray());

        var missing = await DirectorsApprovalEndpoints.GetPagePdfEndpointAsync(p.CompanyId, p.Id, page.Id + 50, db, context);
        Assert.Equal(404, Status(missing));
    }

    private static CroSigningPackInput SigningPack(int directorId, int secretaryId, DateTime signedAtUtc)
    {
        var bytes = Pdf("executed signing pack");
        return new CroSigningPackInput(directorId, secretaryId, signedAtUtc, bytes, DirectorsApprovalIntegrity.Sha256(bytes));
    }

    private static async Task<(AccountingPeriod Period, FilingReleaseGate Gate, int DirectorId, int SecretaryId, int TenantId)> ReleaseReadyAsync(
        AccountsDbContext db, bool auditRequired = false)
    {
        var period = await FilingReleaseGateTests.SeedReleaseReadyPeriodAsync(db, auditRequired: auditRequired);
        var gate = new FilingReleaseGate(db, "candidate-a", new AuditService(db));
        if (!auditRequired) // an audited period cannot retain CRO artifacts until its signed report is recorded
        {
            await gate.RecordCroArtifactAsync(period.CompanyId, period.Id, FilingReleaseArtifact.CroAccountsPdf,
                System.Text.Encoding.UTF8.GetBytes("clean-cro-accounts-v1"), "reviewer@example.ie");
            await gate.RecordCroArtifactAsync(period.CompanyId, period.Id, FilingReleaseArtifact.CroSignaturePage,
                System.Text.Encoding.UTF8.GetBytes("clean-cro-signature-v1"), "reviewer@example.ie");
        }
        var director = await db.CompanyOfficers.SingleAsync(o => o.CompanyId == period.CompanyId && o.Role == OfficerRole.Director);
        var secretary = await db.CompanyOfficers.SingleAsync(o => o.CompanyId == period.CompanyId && o.Role == OfficerRole.Secretary);
        var tenantId = (await db.Companies.SingleAsync(c => c.Id == period.CompanyId)).TenantId!.Value;
        return (period, gate, director.Id, secretary.Id, tenantId);
    }

    [Fact]
    public async Task CroSigningPackEndpoint_CopiesNamesFromTheOfficerRowsAndAudits()
    {
        await using var db = CreateDb();
        var (period, gate, directorId, secretaryId, tenantId) = await ReleaseReadyAsync(db);
        var context = EndpointContext("Reviewer", "POST", FilingPath(period.CompanyId, period.Id, "cro-signing-pack"), period.CompanyId, tenantId);

        var result = await DirectorsApprovalEndpoints.RecordCroSigningPackEndpointAsync(
            period.CompanyId, period.Id, SigningPack(directorId, secretaryId, DateTime.UtcNow.AddMinutes(-2)),
            gate, new AuditService(db), db, context, NoApiKey());

        Assert.Equal(200, Status(result));
        var package = await db.CroFilingPackages.SingleAsync(c => c.PeriodId == period.Id);
        Assert.Equal("A Director", package.SignedByDirector);
        Assert.Equal("B Secretary", package.SignedBySecretary);
        Assert.NotNull(package.SignedPdfSha256);
        Assert.Single(await db.AuditLogs.Where(a => a.Action == AuditEventCodes.CroSigningPackRecorded).ToListAsync());
    }

    [Fact]
    public async Task CroSigningPackEndpoint_RejectsSignatoriesWhoAreNotInOfficeInTheRole()
    {
        await using var db = CreateDb();
        var (period, gate, directorId, secretaryId, tenantId) = await ReleaseReadyAsync(db);
        var context = EndpointContext("Owner", "POST", FilingPath(period.CompanyId, period.Id, "cro-signing-pack"), period.CompanyId, tenantId);

        // A director is not the secretary.
        var wrongRole = await Assert.ThrowsAsync<BusinessRuleException>(() => DirectorsApprovalEndpoints.RecordCroSigningPackEndpointAsync(
            period.CompanyId, period.Id, SigningPack(directorId, directorId, DateTime.UtcNow.AddMinutes(-2)),
            gate, new AuditService(db), db, context, NoApiKey()));
        Assert.Contains("not a secretary in office", wrongRole.Message);

        // A secretary who resigned before the signing date.
        (await db.CompanyOfficers.SingleAsync(o => o.Id == secretaryId)).ResignedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        await db.SaveChangesAsync();
        var resigned = await Assert.ThrowsAsync<BusinessRuleException>(() => DirectorsApprovalEndpoints.RecordCroSigningPackEndpointAsync(
            period.CompanyId, period.Id, SigningPack(directorId, secretaryId, DateTime.UtcNow.AddMinutes(-2)),
            gate, new AuditService(db), db, context, NoApiKey()));
        Assert.Contains("not a secretary in office", resigned.Message);

        // An officer id that is not an officer of this company.
        var foreign = await Assert.ThrowsAsync<ResourceNotFoundException>(() => DirectorsApprovalEndpoints.RecordCroSigningPackEndpointAsync(
            period.CompanyId, period.Id, SigningPack(directorId, 987654, DateTime.UtcNow.AddMinutes(-2)),
            gate, new AuditService(db), db, context, NoApiKey()));
        Assert.Contains("Officer not found", foreign.Message);

        var package = await db.CroFilingPackages.SingleAsync(c => c.PeriodId == period.Id);
        Assert.Null(package.SignedPdfSha256);
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == AuditEventCodes.CroSigningPackRecorded).ToListAsync());
    }

    [Fact]
    public async Task CroSigningPackEndpoint_DeniesAccountantAndClientBeforeAnyWrite()
    {
        await using var db = CreateDb();
        var (period, gate, directorId, secretaryId, tenantId) = await ReleaseReadyAsync(db);
        foreach (var role in new[] { "Accountant", "Client" })
        {
            var result = await DirectorsApprovalEndpoints.RecordCroSigningPackEndpointAsync(
                period.CompanyId, period.Id, SigningPack(directorId, secretaryId, DateTime.UtcNow.AddMinutes(-2)),
                gate, new AuditService(db), db,
                EndpointContext(role, "POST", FilingPath(period.CompanyId, period.Id, "cro-signing-pack"), period.CompanyId, tenantId),
                NoApiKey());
            Assert.Equal(403, Status(result));
        }
        Assert.Null((await db.CroFilingPackages.SingleAsync(c => c.PeriodId == period.Id)).SignedPdfSha256);
    }

    private static AuditorReportInput AuditorReport(byte[] report, bool accepted = true) =>
        new("Independent Audit Firm", "Named Statutory Auditor", "Chartered Accountants Ireland",
            "auditor-member-456", "AUDITOR-REPORT-2026-001", DateTime.UtcNow.AddMinutes(-10),
            accepted, report, DirectorsApprovalIntegrity.Sha256(report));

    [Fact]
    public async Task AuditorReportEndpoint_RecordsTheSignedReport_AttributingTheAcceptanceToTheAuthenticatedUser()
    {
        await using var db = CreateDb();
        var (period, gate, _, _, tenantId) = await ReleaseReadyAsync(db, auditRequired: true);
        var context = EndpointContext("Reviewer", "POST", FilingPath(period.CompanyId, period.Id, "auditor-report"), period.CompanyId, tenantId);
        var before = DateTime.UtcNow;

        var result = await DirectorsApprovalEndpoints.RecordAuditorReportEndpointAsync(
            period.CompanyId, period.Id, AuditorReport(Pdf("signed auditor report")),
            gate, new AuditService(db), db, context, NoApiKey(), TimeProvider.System);

        Assert.Equal(200, Status(result));
        var stored = await db.AccountingPeriods.SingleAsync(x => x.Id == period.Id);
        Assert.True(stored.AuditorsReportReceived);
        Assert.Equal("AUDITOR-REPORT-2026-001", stored.AuditorsReportReference);
        Assert.Equal(AuthenticatedIdentity.ReviewerDisplayName(AuthContext.RequireUser(context)), stored.AuditorsReportReviewedBy);
        Assert.Equal("accepted", stored.AuditorsReportReviewDecision);
        Assert.InRange(stored.AuditorsReportReviewedAt!.Value, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
        Assert.Single(await db.AuditLogs.Where(a => a.Action == AuditEventCodes.AuditorReportRecorded).ToListAsync());
    }

    [Fact]
    public async Task AuditorReportEndpoint_WithoutTheDirectorsAcceptance_IsRejectedAndStoresNothing()
    {
        await using var db = CreateDb();
        var (period, gate, _, _, tenantId) = await ReleaseReadyAsync(db, auditRequired: true);
        var context = EndpointContext("Owner", "POST", FilingPath(period.CompanyId, period.Id, "auditor-report"), period.CompanyId, tenantId);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => DirectorsApprovalEndpoints.RecordAuditorReportEndpointAsync(
            period.CompanyId, period.Id, AuditorReport(Pdf("signed auditor report"), accepted: false),
            gate, new AuditService(db), db, context, NoApiKey(), TimeProvider.System));

        Assert.Equal("Confirm that the directors have accepted the auditor's report.", error.Message);
        var stored = await db.AccountingPeriods.SingleAsync(x => x.Id == period.Id);
        Assert.False(stored.AuditorsReportReceived);
        Assert.Null(stored.AuditorsReportSha256);
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == AuditEventCodes.AuditorReportRecorded).ToListAsync());
    }

    [Fact]
    public void AuditorReportInput_CarriesNoCallerSuppliedReviewEvidence()
    {
        var names = typeof(AuditorReportInput).GetProperties().Select(p => p.Name).ToArray();
        Assert.DoesNotContain("ReviewedBy", names);
        Assert.DoesNotContain("ReviewedAtUtc", names);
        Assert.DoesNotContain("ReviewDecision", names);
        Assert.Contains("AcceptedByDirectors", names);
    }

    [Fact]
    public async Task AuditorReportEndpoint_ForAnAuditExemptPeriod_IsBlocked()
    {
        await using var db = CreateDb();
        var (period, gate, _, _, tenantId) = await ReleaseReadyAsync(db, auditRequired: false);

        await Assert.ThrowsAsync<FilingReleaseBlockedException>(() => DirectorsApprovalEndpoints.RecordAuditorReportEndpointAsync(
            period.CompanyId, period.Id, AuditorReport(Pdf("signed auditor report")),
            gate, new AuditService(db), db,
            EndpointContext("Owner", "POST", FilingPath(period.CompanyId, period.Id, "auditor-report"), period.CompanyId, tenantId),
            NoApiKey(), TimeProvider.System));
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == AuditEventCodes.AuditorReportRecorded).ToListAsync());
    }

    [Fact]
    public async Task CroApproval_BindsTheCurrentDirectorsApproval()
    {
        var harness = await FilingGateFixture.ReadyForApprovalAsync();
        await harness.RecordDirectorsApprovalAsync();

        await harness.Gate.BindApprovalAsync(harness.CompanyId, harness.PeriodId, FilingReleaseWorkflow.Cro, "user:1");

        var cro = await harness.Db.CroFilingPackages.SingleAsync(c => c.PeriodId == harness.PeriodId);
        Assert.Equal(FilingReleaseGate.BoardApprovalCapacity, cro.ApprovalCapacity);
        Assert.Equal("Director One and Director Two", cro.ApprovedBy);
        Assert.Equal(64, cro.ApprovedArtifactManifestSha256!.Length);
    }

    [Fact]
    public async Task CroApproval_WithoutADirectorsApproval_IsBlocked()
    {
        var harness = await FilingGateFixture.ReadyForApprovalAsync();
        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            harness.Gate.BindApprovalAsync(harness.CompanyId, harness.PeriodId, FilingReleaseWorkflow.Cro, "user:1"));
        Assert.Contains("directors' approval (s.324)", error.Message);
    }

    [Fact]
    public async Task AuditedCompany_StillRequiresTheAuditorsReport()
    {
        var harness = await FilingGateFixture.ReadyForApprovalAsync(auditExempt: false, withAuditorReport: false);
        await harness.RecordDirectorsApprovalAsync();
        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            harness.Gate.BindApprovalAsync(harness.CompanyId, harness.PeriodId, FilingReleaseWorkflow.Cro, "user:1"));
        Assert.Contains("auditor", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadinessProfile_RequiresDirectorsApprovalNotAnAccountant()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var profile = await new FilingReadinessProfileService(db).GetProfileAsync(p.CompanyId, p.Id);

        Assert.False(profile.AccountantReviewRequired);
        Assert.Equal("board-of-directors-s324", profile.ApprovalAuthority);
        Assert.Contains(profile.BlockingIssues, i => i.Code == "directors-approval-required");
        Assert.DoesNotContain(profile.BlockingIssues, i => i.Code == "accountant-review-required");
        Assert.Contains(profile.RequiredEvidence, e => e.Code == "directors-approval" && !e.Satisfied);
        Assert.Contains(profile.SourceReferences, s => s.SourceId == "companies-act-2014-s324");
        Assert.DoesNotContain("approve-cro-pack", profile.AllowedNextActions);
        Assert.False(profile.SignOffPacket.ReadyForDirectorsApproval);
        Assert.Equal("Blocked before directors' approval", profile.SignOffPacket.StateLabel);
        Assert.Contains(profile.SignOffPacket.Steps, s => s.Code == "directors-approval" && s.State == "pending");
        Assert.DoesNotContain(profile.SignOffPacket.Steps, s => s.Code == "accountant-approval");
    }

    [Fact]
    public async Task ReadinessProfile_DirectorsApprovalSatisfiesTheEvidence()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var page = await service.GeneratePageAsync(p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR-2026-11-20"), Owner);
        var signed = Pdf("signed"); var minute = Pdf("minute");
        await service.RecordApprovalAsync(p.CompanyId, p.Id, new(page.Id, signed, DirectorsApprovalIntegrity.Sha256(signed), minute, DirectorsApprovalIntegrity.Sha256(minute)), Owner);

        var profile = await new FilingReadinessProfileService(db).GetProfileAsync(p.CompanyId, p.Id);

        Assert.Contains(profile.RequiredEvidence, e => e.Code == "directors-approval" && e.Satisfied
            && e.Detail!.StartsWith("Approved by the board on 2026-11-20 (WR-2026-11-20); signed by Director One and Director Two;", StringComparison.Ordinal));
        Assert.DoesNotContain(profile.BlockingIssues, i => i.Code == "directors-approval-required");
        Assert.DoesNotContain("record-directors-approval", profile.AllowedNextActions);
        Assert.Contains(profile.SignOffPacket.Steps, s => s.Code == "directors-approval" && s.State == "complete");
    }

    [Fact]
    public async Task ReadinessProfile_OffersRecordingTheDirectorsApprovalThenApprovingTheCroPack()
    {
        var harness = await FilingGateFixture.ReadyForApprovalAsync();
        var readiness = new FilingReadinessProfileService(harness.Db);

        var before = await readiness.GetProfileAsync(harness.CompanyId, harness.PeriodId);
        Assert.Contains("record-directors-approval", before.AllowedNextActions);
        Assert.DoesNotContain("approve-cro-pack", before.AllowedNextActions);
        Assert.False(before.SignOffPacket.ReadyForDirectorsApproval);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => readiness.AssertCanApproveCroPackAsync(harness.CompanyId, harness.PeriodId));
        Assert.Contains("s.324", error.Message);

        await harness.RecordDirectorsApprovalAsync();
        var after = await readiness.GetProfileAsync(harness.CompanyId, harness.PeriodId);
        Assert.DoesNotContain("record-directors-approval", after.AllowedNextActions);
        Assert.Contains("approve-cro-pack", after.AllowedNextActions);
        Assert.True(after.SignOffPacket.ReadyForDirectorsApproval);
        Assert.Equal("ready-for-directors-approval", after.SignOffPacket.State);
        await readiness.AssertCanApproveCroPackAsync(harness.CompanyId, harness.PeriodId);
    }

    [Fact]
    public async Task MemberAuditNoticeAfterApproval_BringsBackTheAuditRequirement()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var period = await db.AccountingPeriods.FindAsync(p.Id);
        period!.MemberAuditNoticeReceived = true;
        await db.SaveChangesAsync();

        var profile = await new FilingReadinessProfileService(db).GetProfileAsync(p.CompanyId, p.Id);

        Assert.Contains(profile.BlockingIssues, i => i.Code == "auditor-handoff-required");
        Assert.True(profile.ManualProfessionalReviewRequired);
    }
}
