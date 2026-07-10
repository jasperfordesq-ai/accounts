using System.Security.Cryptography;
using System.Text;
using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Accounts.Tests;

public sealed class ExternalFilingHandoffEndpointTests
{
    private const string Candidate = "file001-test-candidate";

    [Fact]
    public async Task Endpoints_EnforceInternalRoleTenantAndPeriodScope()
    {
        var owner = Actor(1, 1, "Owner");
        var accessor = new HttpContextAccessor { HttpContext = Context(owner, HttpMethods.Get, "/api/companies/1/periods/1/external-filing-handoff/workspace") };
        await using var db = CreateDb(accessor);
        var fixture = await SeedAsync(db);
        var service = Service(db);

        var allowed = await ExternalFilingHandoffEndpoints.GetWorkspaceEndpointAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            service,
            db,
            accessor.HttpContext!);
        var workspace = Assert.IsType<ExternalFilingHandoffService.Workspace>(Assert.IsAssignableFrom<IValueHttpResult>(allowed).Value);
        Assert.False(workspace.DirectCroSubmissionSupported);
        Assert.False(workspace.DirectRosSubmissionSupported);
        Assert.Equal(fixture.Company.LegalName, workspace.Preparation.LegalName);
        Assert.Equal(2, workspace.Preparation.Officers.Count);
        Assert.Contains(workspace.Preparation.Officers, item => item.OfficerId == fixture.Director.Id && item.Role == nameof(OfficerRole.Director));
        Assert.Contains(workspace.Preparation.Officers, item => item.OfficerId == fixture.Secretary.Id && item.Role == nameof(OfficerRole.Secretary));

        accessor.HttpContext = Context(Actor(fixture.Tenant.Id, fixture.Company.Id, "Client"), HttpMethods.Get, accessor.HttpContext.Request.Path);
        var clientDenied = await ExternalFilingHandoffEndpoints.GetWorkspaceEndpointAsync(
            fixture.Company.Id, fixture.Period.Id, service, db, accessor.HttpContext);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(clientDenied).StatusCode);

        accessor.HttpContext = Context(Actor(999, fixture.Company.Id, "Owner"), HttpMethods.Get, accessor.HttpContext.Request.Path);
        var crossTenant = await ExternalFilingHandoffEndpoints.GetWorkspaceEndpointAsync(
            fixture.Company.Id, fixture.Period.Id, service, db, accessor.HttpContext);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(crossTenant).StatusCode);

        var reviewerSnapshotPath = $"/api/companies/{fixture.Company.Id}/periods/{fixture.Period.Id}/external-filing-handoff/cro/snapshots";
        accessor.HttpContext = Context(Actor(fixture.Tenant.Id, fixture.Company.Id, "Reviewer"), HttpMethods.Post, reviewerSnapshotPath, "reviewer-cannot-prepare");
        var reviewerDenied = await ExternalFilingHandoffEndpoints.GenerateCroSnapshotEndpointAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            CroInput(fixture.Director.Id, fixture.Secretary.Id),
            service,
            db,
            accessor.HttpContext,
            new IdempotencyService(db));
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(reviewerDenied).StatusCode);
    }

    [Fact]
    public async Task Endpoints_RetainAuthorityReplaySnapshotAmendmentExactArtifactAndChronology()
    {
        var reviewer = Actor(1, 1, "Reviewer");
        var accessor = new HttpContextAccessor();
        await using var db = CreateDb(accessor);
        var fixture = await SeedAsync(db);
        reviewer = Actor(fixture.Tenant.Id, fixture.Company.Id, "Reviewer");
        var service = Service(db);
        var evidence = Encoding.UTF8.GetBytes("exact B77 presenter authority evidence");
        var authorityRequest = new ExternalFilingHandoffEndpoints.AuthorityRequest(
            ExternalFilingWorkflow.CroB1,
            ExternalFilingAuthorityKind.CroElectronicFilingAgent,
            "Fixture Presenter Limited",
            "Fixture Presenter",
            "EFA-****42",
            "Prepare the manual B1 handoff for the retained company period.",
            "ENGAGEMENT-FILE001-01",
            "B77-FILE001-01",
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow.AddDays(30),
            evidence,
            Sha(evidence),
            "application/pdf",
            "b77-authority.pdf");

        var authorityPath = $"/api/companies/{fixture.Company.Id}/periods/{fixture.Period.Id}/external-filing-handoff/authorities";
        accessor.HttpContext = Context(reviewer, HttpMethods.Post, authorityPath, "authority-version-0001");
        var firstAuthority = await ExternalFilingHandoffEndpoints.RecordAuthorityEndpointAsync(
            fixture.Company.Id, fixture.Period.Id, authorityRequest, service, db, accessor.HttpContext, new IdempotencyService(db));
        Assert.Equal(StatusCodes.Status201Created, Assert.IsAssignableFrom<IStatusCodeHttpResult>(firstAuthority).StatusCode);
        var authority = Assert.IsType<ExternalFilingAuthoritySnapshot>(Assert.IsAssignableFrom<IValueHttpResult>(firstAuthority).Value);
        Assert.Equal(Sha(evidence), authority.AuthorityEvidenceSha256);

        accessor.HttpContext = Context(reviewer, HttpMethods.Post, authorityPath, "authority-version-0001");
        var replay = await ExternalFilingHandoffEndpoints.RecordAuthorityEndpointAsync(
            fixture.Company.Id, fixture.Period.Id, authorityRequest, service, db, accessor.HttpContext, new IdempotencyService(db));
        Assert.Equal(StatusCodes.Status201Created, Assert.IsAssignableFrom<IStatusCodeHttpResult>(replay).StatusCode);
        Assert.Equal("true", accessor.HttpContext.Response.Headers[IdempotencyHttpContract.ReplayedHeader]);
        Assert.Single(await db.FilingAuthorityEngagements.ToListAsync());
        Assert.DoesNotContain("exact B77", System.Text.Json.JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(firstAuthority).Value), StringComparison.Ordinal);

        var accountant = Actor(fixture.Tenant.Id, fixture.Company.Id, "Accountant");
        var snapshotPath = $"/api/companies/{fixture.Company.Id}/periods/{fixture.Period.Id}/external-filing-handoff/cro/snapshots";
        accessor.HttpContext = Context(accountant, HttpMethods.Post, snapshotPath, "cro-snapshot-0001");
        var initial = await ExternalFilingHandoffEndpoints.GenerateCroSnapshotEndpointAsync(
            fixture.Company.Id, fixture.Period.Id, CroInput(fixture.Director.Id, fixture.Secretary.Id), service, db, accessor.HttpContext, new IdempotencyService(db));
        Assert.Equal(StatusCodes.Status201Created, Assert.IsAssignableFrom<IStatusCodeHttpResult>(initial).StatusCode);
        var firstSnapshot = Assert.IsType<ExternalFilingHandoffService.SnapshotResponse>(Assert.IsAssignableFrom<IValueHttpResult>(initial).Value);
        Assert.True(
            firstSnapshot.Document.ReadyForManualHandoff,
            string.Join(" | ", firstSnapshot.Document.BlockingIssues));
        Assert.False(firstSnapshot.Document.DirectSubmissionSupported);
        Assert.False(firstSnapshot.Document.IsCompleteExternalReturn);

        accessor.HttpContext = Context(accountant, HttpMethods.Post, snapshotPath, "cro-amendment-0001");
        var amendmentInput = CroInput(fixture.Director.Id, fixture.Secretary.Id) with
        {
            SupersedesSnapshotId = firstSnapshot.Document.SnapshotId,
            AmendmentReason = "Correct the retained member evidence after independent review."
        };
        var amended = await ExternalFilingHandoffEndpoints.GenerateCroSnapshotEndpointAsync(
            fixture.Company.Id, fixture.Period.Id, amendmentInput, service, db, accessor.HttpContext, new IdempotencyService(db));
        var secondSnapshot = Assert.IsType<ExternalFilingHandoffService.SnapshotResponse>(Assert.IsAssignableFrom<IValueHttpResult>(amended).Value);
        Assert.Equal(2, secondSnapshot.Document.Version);
        Assert.Equal(firstSnapshot.Document.SnapshotId, secondSnapshot.Document.SupersedesSnapshotId);
        Assert.Equal(firstSnapshot.ArtifactSha256, secondSnapshot.Document.SupersedesArtifactSha256);

        var artifactContext = Context(reviewer, HttpMethods.Get, $"/api/companies/{fixture.Company.Id}/periods/{fixture.Period.Id}/external-filing-handoff/snapshots/{secondSnapshot.Document.SnapshotId}/artifact");
        accessor.HttpContext = artifactContext;
        var artifactResult = await ExternalFilingHandoffEndpoints.DownloadArtifactEndpointAsync(
            fixture.Company.Id, fixture.Period.Id, secondSnapshot.Document.SnapshotId, service, db, artifactContext);
        var file = Assert.IsType<FileContentHttpResult>(artifactResult);
        Assert.Equal(secondSnapshot.ArtifactSha256, Sha(file.FileContents.ToArray()));
        Assert.Equal(secondSnapshot.ArtifactSha256, artifactContext.Response.Headers["X-Artifact-SHA256"]);

        var outcomePath = $"/api/companies/{fixture.Company.Id}/periods/{fixture.Period.Id}/external-filing-handoff/snapshots/{secondSnapshot.Document.SnapshotId}/outcomes";
        accessor.HttpContext = Context(reviewer, HttpMethods.Post, outcomePath, "handoff-ready-0001");
        var ready = await ExternalFilingHandoffEndpoints.RecordOutcomeEndpointAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            secondSnapshot.Document.SnapshotId,
            new ExternalFilingHandoffService.OutcomeCommand(
                ExternalFilingOutcomeKind.ReadyForManualHandoff,
                null, null, null, null, null, null, null, null),
            service,
            db,
            accessor.HttpContext,
            new IdempotencyService(db));
        var readyEvent = Assert.IsType<ExternalFilingHandoffService.OutcomeResponse>(Assert.IsAssignableFrom<IValueHttpResult>(ready).Value);
        Assert.Equal(ExternalFilingOutcomeKind.ReadyForManualHandoff, readyEvent.Outcome);
        Assert.Null(readyEvent.ExternalReference);
        Assert.Null(readyEvent.EvidenceSha256);

        var workspace = await service.GetWorkspaceAsync(fixture.Company.Id, fixture.Period.Id);
        Assert.Equal(2, workspace.Snapshots.Count);
        Assert.Single(workspace.Outcomes);
        Assert.All(workspace.Snapshots, item => Assert.False(item.Document.IsCompleteExternalReturn));
    }

    [Fact]
    public void RolePolicy_MatchesEndpointSeparationOfDuties()
    {
        const string basePath = "/api/companies/1/periods/1/external-filing-handoff";
        Assert.True(RoleAuthorizationService.Authorize(Actor(1, 1, "Accountant"), basePath + "/workspace", HttpMethods.Get).IsAllowed);
        Assert.False(RoleAuthorizationService.Authorize(Actor(1, 1, "Client"), basePath + "/workspace", HttpMethods.Get).IsAllowed);
        Assert.True(RoleAuthorizationService.Authorize(Actor(1, 1, "Accountant"), basePath + "/cro/snapshots", HttpMethods.Post).IsAllowed);
        Assert.False(RoleAuthorizationService.Authorize(Actor(1, 1, "Reviewer"), basePath + "/cro/snapshots", HttpMethods.Post).IsAllowed);
        Assert.True(RoleAuthorizationService.Authorize(Actor(1, 1, "Reviewer"), basePath + "/authorities", HttpMethods.Post).IsAllowed);
        Assert.False(RoleAuthorizationService.Authorize(Actor(1, 1, "Accountant"), basePath + "/authorities", HttpMethods.Post).IsAllowed);
        Assert.True(RoleAuthorizationService.Authorize(Actor(1, 1, "Reviewer"), basePath + "/snapshots/00000000-0000-0000-0000-000000000001/outcomes", HttpMethods.Post).IsAllowed);
    }

    private static ExternalFilingHandoffService.CroSnapshotInput CroInput(int directorId, int secretaryId) => new(
        new DateOnly(2026, 9, 30),
        "RetainExistingAnnualReturnDate",
        true,
        true,
        "AUDIT-EXEMPTION-EVIDENCE-01",
        "EUR",
        false,
        0m,
        "POLITICAL-DONATIONS-BOARD-01",
        [
            new ExternalFilingHandoffService.CroOfficerInput(
                directorId,
                "Aoife",
                "Director",
                new ExternalHandoffAddress("1 Main Street", "Dublin 2", null, null, null, null, "D02 TEST"),
                "Protected CORE identity entry",
                "CORE-IDENTITY-EVIDENCE-01",
                new string('a', 64),
                null,
                "OTHER-DIRECTORSHIPS-01",
                true),
            new ExternalFilingHandoffService.CroOfficerInput(
                secretaryId,
                "Sean",
                "Secretary",
                new ExternalHandoffAddress("2 Main Street", "Dublin 2", null, null, null, null, "D02 TEST"),
                "Protected CORE identity entry",
                "CORE-IDENTITY-EVIDENCE-02",
                new string('b', 64),
                null,
                "OTHER-DIRECTORSHIPS-02",
                true)
        ],
        [new B1ShareholderHandoff(
            "MEMBER-01",
            "Fixture Member",
            new ExternalHandoffAddress("1 Main Street", "Dublin 2", null, null, null, null, "D02 TEST"),
            "Ordinary",
            "EUR",
            100m,
            100m,
            "100 Ordinary shares",
            "REGISTER-MEMBERS-01")],
        [],
        true,
        null,
        null,
        null);

    private static ExternalFilingHandoffService Service(AccountsDbContext db)
    {
        var statements = new FinancialStatementsService(db);
        var tax = new TaxComputationService(db, statements);
        var audit = new AuditService(db);
        var taxSupport = new CorporationTaxFilingSupportService(db, tax, audit);
        return new ExternalFilingHandoffService(
            db,
            new ExternalFilingHandoffRepository(db),
            ReleaseIdentity(),
            taxSupport,
            audit);
    }

    private static FilingReleaseIdentityProvider ReleaseIdentity()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FilingRelease:Candidate"] = Candidate })
            .Build();
        return new FilingReleaseIdentityProvider(configuration, new TestEnvironment());
    }

    private static async Task<Fixture> SeedAsync(AccountsDbContext db)
    {
        var tenant = new Tenant { Name = "FILE001 Firm", Slug = $"file001-{Guid.NewGuid():N}" };
        var company = new Company
        {
            Tenant = tenant,
            LegalName = "FILE001 Fixture Limited",
            CroNumber = "765432",
            TaxReference = "7654321F",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2020, 1, 2),
            AnnualReturnDate = new DateOnly(2026, 9, 30),
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Dublin 2",
            RegisteredOfficeEircode = "D02 TEST",
            IsTrading = true
        };
        var officer = new CompanyOfficer
        {
            Company = company,
            Name = "Aoife Director",
            Role = OfficerRole.Director,
            AppointedDate = new DateOnly(2020, 1, 2),
            Address = "1 Main Street, Dublin 2"
        };
        var secretary = new CompanyOfficer
        {
            Company = company,
            Name = "Sean Secretary",
            Role = OfficerRole.Secretary,
            AppointedDate = new DateOnly(2020, 1, 2),
            Address = "2 Main Street, Dublin 2"
        };
        company.Officers.Add(officer);
        company.Officers.Add(secretary);
        company.ShareCapitals.Add(new ShareCapital
        {
            Company = company,
            ShareClass = "Ordinary",
            NumberIssued = 100,
            NominalValue = 1m,
            TotalValue = 100m,
            IsFullyPaid = true
        });
        db.Add(company);
        await db.SaveChangesAsync();

        var accounts = Encoding.UTF8.GetBytes("retained final accounts PDF fixture bytes");
        var signature = Encoding.UTF8.GetBytes("retained signature page fixture bytes");
        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = false
        };
        db.Add(period);
        await db.SaveChangesAsync();
        db.Add(new CroFilingPackage
        {
            PeriodId = period.Id,
            AccountsPdfArtifact = accounts,
            AccountsPdfSha256 = Sha(accounts),
            AccountsPdfGenerated = true,
            SignaturePageArtifact = signature,
            SignaturePageSha256 = Sha(signature),
            SignaturePageGenerated = true,
            ApprovedBy = "Named Qualified Reviewer",
            ApprovedAt = DateTime.UtcNow,
            ApprovedArtifactManifestSha256 = new string('b', 64),
            ApprovedReleaseCandidate = Candidate,
            SignedByDirector = "Aoife Director",
            SignedBySecretary = "Sean Secretary",
            SignedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return new Fixture(tenant, company, period, officer, secretary);
    }

    private static AccountsDbContext CreateDb(IHttpContextAccessor accessor)
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase($"external-filing-handoff-{Guid.NewGuid():N}")
            .Options;
        return new AccountsDbContext(options, accessor);
    }

    private static DefaultHttpContext Context(AuthenticatedUser actor, string method, string path, string? key = null)
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        context.Items[AuthContext.ItemKey] = actor;
        context.Request.Method = method;
        context.Request.Path = path;
        context.TraceIdentifier = Guid.NewGuid().ToString("N");
        if (key is not null) context.Request.Headers[IdempotencyHttpContract.RequestHeader] = key;
        return context;
    }

    private static AuthenticatedUser Actor(int tenantId, int companyId, string role) => new(
        901,
        tenantId,
        "FILE001 Firm",
        $"{role.ToLowerInvariant()}@example.invalid",
        $"FILE001 {role}",
        role,
        new HashSet<int> { companyId });

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed record Fixture(
        Tenant Tenant,
        Company Company,
        AccountingPeriod Period,
        CompanyOfficer Director,
        CompanyOfficer Secretary);

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Accounts.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
