using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounts.Tests;

public sealed class CompanyOnboardingTests
{
    [Fact]
    public async Task MissingIncorporationDateAndAnyInvalidOfficerLeaveNoPartialCompany()
    {
        var options = OptionsFor(Guid.NewGuid().ToString("N"));
        await using var db = new AccountsDbContext(options);
        var tenantId = await SeedTenantAsync(db);
        var input = ValidInput();
        input.Company!.IncorporationDate = default;
        input.Officers!.Add(new CompanyOfficerInput { Name = "", Role = OfficerRole.Secretary });

        var result = await InvokeAsync(db, tenantId, "onboard-invalid-0001", input);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCode(result));
        var problem = Assert.IsType<HttpValidationProblemDetails>(
            Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
        Assert.Contains("incorporationDate", problem.Errors.Keys);
        Assert.Contains("officers.2.name", problem.Errors.Keys);
        Assert.Empty(await db.Companies.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.IdempotencyRecords.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task SameKeyReturnsTheSameAtomicOutcomeAndCreatesOneCompleteAggregate()
    {
        var options = OptionsFor(Guid.NewGuid().ToString("N"));
        await using var db = new AccountsDbContext(options);
        var tenantId = await SeedTenantAsync(db);
        const string key = "onboard-retry-safe-0001";
        var input = ValidInput();

        var firstContext = Request(Actor(tenantId), key);
        var first = await CompanyOnboardingEndpoint.CreateAsync(
            input,
            firstContext,
            DisabledApiAccess(),
            Service(db));
        var firstOutcome = Assert.IsType<CompanyOnboardingOutcome>(
            Assert.IsAssignableFrom<IValueHttpResult>(first).Value);
        Assert.Equal(StatusCodes.Status201Created, StatusCode(first));
        Assert.Equal("false", firstContext.Response.Headers["Idempotency-Replayed"]);

        var replayContext = Request(Actor(tenantId), key);
        var replay = await CompanyOnboardingEndpoint.CreateAsync(
            input,
            replayContext,
            DisabledApiAccess(),
            Service(db));
        var replayOutcome = Assert.IsType<CompanyOnboardingOutcome>(
            Assert.IsAssignableFrom<IValueHttpResult>(replay).Value);

        Assert.Equal(StatusCodes.Status201Created, StatusCode(replay));
        Assert.Equal("true", replayContext.Response.Headers["Idempotency-Replayed"]);
        Assert.Equal(firstOutcome.CompanyId, replayOutcome.CompanyId);
        Assert.Equal(firstOutcome.FirstPeriodId, replayOutcome.FirstPeriodId);
        Assert.Equal(firstOutcome.OpeningBankAccountId, replayOutcome.OpeningBankAccountId);
        Assert.Equal(firstOutcome.Officers.Select(item => item.Id), replayOutcome.Officers.Select(item => item.Id));

        Assert.Single(await db.Companies.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await db.AccountingPeriods.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await db.BankAccounts.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(2, await db.CompanyOfficers.IgnoreQueryFilters().CountAsync());
        Assert.Equal(firstOutcome.CategoryCount, await db.AccountCategories.IgnoreQueryFilters().CountAsync());
        var retained = await db.IdempotencyRecords.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("Completed", retained.Status);
        Assert.Equal(firstOutcome.CompanyId.ToString(), retained.ResultResourceId);
        Assert.Equal(IdempotencyOperations.CompanyOnboard, retained.Operation);
        Assert.NotNull(retained.ResponseJson);
        Assert.Equal(64, retained.RequestFingerprintSha256.Length);
        Assert.Equal(64, retained.ResponseSha256?.Length);
        Assert.Single(await db.AuditLogs.Where(log => log.Action == AuditEventCodes.CompanyOnboarded).ToListAsync());
    }

    [Fact]
    public async Task ReusingAKeyWithDifferentPayloadConflictsWithoutCreatingADuplicate()
    {
        var options = OptionsFor(Guid.NewGuid().ToString("N"));
        await using var db = new AccountsDbContext(options);
        var tenantId = await SeedTenantAsync(db);
        const string key = "onboard-payload-conflict-0001";
        Assert.Equal(StatusCodes.Status201Created, StatusCode(await InvokeAsync(db, tenantId, key, ValidInput())));

        var changed = ValidInput("Different Legal Name Limited");
        var conflict = await InvokeAsync(db, tenantId, key, changed);

        Assert.Equal(StatusCodes.Status409Conflict, StatusCode(conflict));
        Assert.Single(await db.Companies.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await db.IdempotencyRecords.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task EndpointRequiresOwnerAndAValidIdempotencyKey()
    {
        var options = OptionsFor(Guid.NewGuid().ToString("N"));
        await using var db = new AccountsDbContext(options);
        var tenantId = await SeedTenantAsync(db);

        var reviewer = await CompanyOnboardingEndpoint.CreateAsync(
            ValidInput(),
            Request(Actor(tenantId, "Reviewer"), "onboard-reviewer-0001"),
            DisabledApiAccess(),
            Service(db));
        Assert.Equal(StatusCodes.Status403Forbidden, StatusCode(reviewer));

        var missingKey = await CompanyOnboardingEndpoint.CreateAsync(
            ValidInput(),
            Request(Actor(tenantId), null),
            DisabledApiAccess(),
            Service(db));
        Assert.Equal(StatusCodes.Status400BadRequest, StatusCode(missingKey));
        Assert.Empty(await db.Companies.IgnoreQueryFilters().ToListAsync());
    }

    private static Task<IResult> InvokeAsync(
        AccountsDbContext db,
        int tenantId,
        string key,
        CompanyOnboardingInput input) =>
        CompanyOnboardingEndpoint.CreateAsync(
            input,
            Request(Actor(tenantId), key),
            DisabledApiAccess(),
            Service(db));

    private static CompanyOnboardingService Service(AccountsDbContext db) => new(
        db,
        new PeriodChronologyService(db),
        new CategoryService(db),
        new AnnualReturnDateService(db, new AuditService(db)),
        new AuditService(db));

    private static CompanyOnboardingInput ValidInput(string legalName = "Atomic Onboarding Limited")
    {
        var incorporationDate = new DateOnly(2025, 1, 1);
        return new CompanyOnboardingInput
        {
            Company = new CompanyInput
            {
                LegalName = legalName,
                CompanyType = CompanyType.Private,
                IncorporationDate = incorporationDate,
                FinancialYearStartMonth = 1,
                AnnualReturnDate = new DateOnly(2025, 7, 1),
                AnnualReturnDateEffectiveFrom = new DateOnly(2025, 7, 1),
                AnnualReturnDateSource = AnnualReturnDateSource.CroRecord,
                AnnualReturnDateEvidenceReference = "CRO-CORE-TEST-ARD",
                IsTrading = true
            },
            Officers =
            [
                new CompanyOfficerInput { Name = "Aisling Director", Role = OfficerRole.Director },
                new CompanyOfficerInput { Name = "Seamus Secretary", Role = OfficerRole.Secretary }
            ],
            FirstPeriod = new AccountingPeriodInput
            {
                PeriodStart = incorporationDate,
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true,
                GoingConcernConfirmed = true
            },
            OpeningBankAccount = new BankAccountInput
            {
                Name = "Main Current Account",
                Currency = "EUR",
                OpeningBalance = 100m,
                OpeningBalanceDate = incorporationDate
            }
        };
    }

    private static async Task<int> SeedTenantAsync(AccountsDbContext db)
    {
        var tenant = new Tenant { Name = "Onboarding Test Firm", Slug = Guid.NewGuid().ToString("N") };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private static DefaultHttpContext Request(AuthenticatedUser actor, string? key)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/companies/onboard";
        context.TraceIdentifier = Guid.NewGuid().ToString("N");
        context.Items[AuthContext.ItemKey] = actor;
        if (key is not null)
            context.Request.Headers["Idempotency-Key"] = key;
        return context;
    }

    private static AuthenticatedUser Actor(int tenantId, string role = "Owner") => new(
        100,
        tenantId,
        "Onboarding Test Firm",
        role == "Owner" ? "owner@example.ie" : "reviewer@example.ie",
        role == "Owner" ? "Owner User" : "Reviewer User",
        role);

    private static ApiAccessService DisabledApiAccess() =>
        new(Options.Create(new ApiAccessConfig { Enabled = false }), new TestEnvironment());

    private static int StatusCode(IResult result) =>
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode
        ?? StatusCodes.Status200OK;

    private static DbContextOptions<AccountsDbContext> OptionsFor(string name) =>
        new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Accounts.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
