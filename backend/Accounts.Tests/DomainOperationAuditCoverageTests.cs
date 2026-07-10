using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounts.Tests;

public sealed class DomainOperationAuditCoverageTests
{
    [Fact]
    public async Task MasterDataHttpWritesEmitMeaningfulScopedAuditEventsAndRejectedWriteEmitsNone()
    {
        await using var fixture = await MasterDataFixture.StartAsync();
        const string correlationId = "corr-domain-master-001";
        var companyPayload = new
        {
            legalName = "Domain Audit Limited",
            tradingName = "Domain Audit",
            companyType = "Private",
            incorporationDate = "2025-01-01",
            financialYearStartMonth = 1,
            annualReturnDate = "2025-07-01",
            annualReturnDateEffectiveFrom = "2025-07-01",
            annualReturnDateSource = "CroRecord",
            annualReturnDateEvidenceReference = "CRO-AUDIT-MATRIX-001",
            isTrading = true
        };

        var companyId = await fixture.SendForIdAsync(
            HttpMethod.Post,
            "/api/companies",
            companyPayload,
            correlationId,
            "audit-company-create-0001");
        await fixture.SendAsync(HttpMethod.Put, $"/api/companies/{companyId}", new
        {
            legalName = "Domain Audit Limited",
            tradingName = "Domain Audit Updated",
            companyType = "Private",
            incorporationDate = "2025-01-01",
            financialYearStartMonth = 1,
            annualReturnDate = "2025-07-01",
            annualReturnDateEffectiveFrom = "2025-07-01",
            annualReturnDateSource = "CroRecord",
            annualReturnDateEvidenceReference = "CRO-AUDIT-MATRIX-001",
            isTrading = true
        }, correlationId);

        var officerId = await fixture.SendForIdAsync(
            HttpMethod.Post,
            $"/api/companies/{companyId}/officers",
            new { name = "Aoife Director", role = "Director", appointedDate = "2025-01-01" },
            correlationId);
        await fixture.SendAsync(
            HttpMethod.Put,
            $"/api/companies/{companyId}/officers/{officerId}",
            new { name = "Aoife Director Updated", role = "Director", appointedDate = "2025-01-01" },
            correlationId);

        var periodId = await fixture.SendForIdAsync(
            HttpMethod.Post,
            $"/api/companies/{companyId}/periods",
            new
            {
                periodStart = "2025-01-01",
                periodEnd = "2025-12-31",
                isFirstYear = true,
                goingConcernConfirmed = true
            },
            correlationId,
            "audit-period-create-0001");

        var bankId = await fixture.SendForIdAsync(
            HttpMethod.Post,
            $"/api/companies/{companyId}/bank-accounts",
            new { name = "Audit Current Account", iban = "IE12AUDI12345678901234", currency = "EUR", openingBalance = 0 },
            correlationId);
        await fixture.SendAsync(
            HttpMethod.Put,
            $"/api/companies/{companyId}/bank-accounts/{bankId}",
            new { name = "Audit Current Account Updated", iban = "IE12AUDI12345678901234", currency = "EUR", openingBalance = 0 },
            correlationId);

        await fixture.SendAsync(HttpMethod.Post, $"/api/companies/{companyId}/categories/seed", new { }, correlationId);
        var categoryId = await fixture.SendForIdAsync(
            HttpMethod.Post,
            $"/api/companies/{companyId}/categories",
            new { code = "AUD900", name = "Audit category", type = "Expense", taxTreatment = "Deductible" },
            correlationId);
        var ruleId = await fixture.SendForIdAsync(
            HttpMethod.Post,
            $"/api/companies/{companyId}/transaction-rules",
            new { pattern = "AUDIT MERCHANT", categoryId, priority = 10 },
            correlationId);
        await fixture.SendAsync(HttpMethod.Delete, $"/api/companies/{companyId}/transaction-rules/{ruleId}", null, correlationId);
        await fixture.SendAsync(HttpMethod.Delete, $"/api/companies/{companyId}/bank-accounts/{bankId}", null, correlationId);
        await fixture.SendAsync(HttpMethod.Delete, $"/api/companies/{companyId}/officers/{officerId}", null, correlationId);

        const string failedCorrelation = "corr-domain-rejected-001";
        var rejected = await fixture.SendAsync(
            HttpMethod.Post,
            $"/api/companies/{companyId}/officers",
            new { name = "", role = "Director" },
            failedCorrelation,
            ensureSuccess: false);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        await fixture.SendAsync(
            HttpMethod.Delete,
            $"/api/companies/{companyId}",
            new { confirmation = "Domain Audit Limited", reason = "Controlled quarantine requested for behavioral audit coverage." },
            correlationId);
        await fixture.SendAsync(
            HttpMethod.Post,
            $"/api/companies/{companyId}/recover",
            new { confirmation = "Domain Audit Limited", reason = "Controlled recovery requested after behavioral audit coverage." },
            correlationId);

        await using var scope = fixture.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var logs = await db.AuditLogs
            .IgnoreQueryFilters()
            .Where(log => log.CompanyId == companyId)
            .OrderBy(log => log.Id)
            .ToListAsync();
        var expectedCodes = new[]
        {
            AuditEventCodes.CompanyCreated,
            AuditEventCodes.CompanyUpdated,
            AuditEventCodes.CompanyOfficerCreated,
            AuditEventCodes.CompanyOfficerUpdated,
            AuditEventCodes.CompanyOfficerDeleted,
            AuditEventCodes.AccountingPeriodCreated,
            AuditEventCodes.BankAccountCreated,
            AuditEventCodes.BankAccountUpdated,
            AuditEventCodes.BankAccountDeleted,
            AuditEventCodes.AccountCategoriesSeeded,
            AuditEventCodes.AccountCategoryCreated,
            AuditEventCodes.TransactionRuleCreated,
            AuditEventCodes.TransactionRuleDeleted,
            AuditEventCodes.CompanyQuarantined,
            AuditEventCodes.CompanyRecovered
        };
        foreach (var code in expectedCodes)
            Assert.Contains(logs, log => log.Action == code);

        var covered = DomainAuditCoverage.Entries
            .Where(entry => expectedCodes.Contains(entry.EventCode, StringComparer.Ordinal))
            .ToArray();
        foreach (var entry in covered)
        {
            var log = logs.First(candidate => candidate.Action == entry.EventCode);
            Assert.Equal(fixture.TenantId, log.TenantId);
            Assert.Equal(companyId, log.CompanyId);
            Assert.Equal("user:7", log.UserId);
            Assert.Equal("Owner User", log.ActorDisplayName);
            Assert.Equal(correlationId, log.RequestId);
            Assert.Equal(entry.EntityType, log.EntityType);
            if (entry.PeriodScoped) Assert.Equal(periodId, log.PeriodId);
            if (entry.RequiresOldValue) Assert.False(string.IsNullOrWhiteSpace(log.OldValueJson));
            if (entry.RequiresNewValue) Assert.False(string.IsNullOrWhiteSpace(log.NewValueJson));
        }

        Assert.DoesNotContain(logs, log => log.RequestId == failedCorrelation);
        Assert.Contains("Domain Audit", logs.Single(log => log.Action == AuditEventCodes.CompanyUpdated).OldValueJson!, StringComparison.Ordinal);
        Assert.Contains("Domain Audit Updated", logs.Single(log => log.Action == AuditEventCodes.CompanyUpdated).NewValueJson!, StringComparison.Ordinal);
        Assert.Contains("Audit Current Account", logs.Single(log => log.Action == AuditEventCodes.BankAccountUpdated).OldValueJson!, StringComparison.Ordinal);
        Assert.DoesNotContain("IE12AUDI", logs.Single(log => log.Action == AuditEventCodes.BankAccountUpdated).NewValueJson!, StringComparison.Ordinal);
        Assert.All(logs, log => Assert.Equal(64, log.IntegrityHash?.Length));

        var integrity = await new AuditIntegrityService(db).VerifyCompanyAsync(companyId);
        Assert.True(integrity.IsValid);
        Assert.Empty(integrity.Issues);
    }

    [Fact]
    public async Task ProfessionalApprovalArtifactAndWorkflowWritesCarryActorCorrelationAndValidCheckpoint()
    {
        var root = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"), root)
            .Options;
        var context = RequestContext(1, "corr-professional-001");
        var accessor = new HttpContextAccessor { HttpContext = context };
        await using var db = new AccountsDbContext(options, accessor);
        var tenant = new Tenant { Name = "Professional Audit Practice", Slug = $"professional-audit-{Guid.NewGuid():N}" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        context.Items[AuthContext.ItemKey] = Actor(tenant.Id);
        var company = new Company
        {
            TenantId = tenant.Id,
            LegalName = "Professional Audit Limited",
            IncorporationDate = new DateOnly(2025, 1, 1),
            AnnualReturnDate = new DateOnly(2025, 7, 1)
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = true
        };
        db.AccountingPeriods.Add(period);
        var debit = new AccountCategory { CompanyId = company.Id, Code = "AUD-D", Name = "Audit debit", Type = AccountCategoryType.Expense };
        var credit = new AccountCategory { CompanyId = company.Id, Code = "AUD-C", Name = "Audit credit", Type = AccountCategoryType.Liability };
        db.AccountCategories.AddRange(debit, credit);
        await db.SaveChangesAsync();
        var adjustment = new Adjustment
        {
            PeriodId = period.Id,
            Description = "Professional approval test adjustment",
            DebitCategoryId = debit.Id,
            CreditCategoryId = credit.Id,
            Amount = 10m,
            Reason = "Evidence-backed adjustment",
            CreatedBy = "Preparer User"
        };
        db.Adjustments.Add(adjustment);
        await db.SaveChangesAsync();

        var audit = new AuditService(db, accessor);
        var approval = await AdjustmentEndpoints.ApproveAdjustmentEndpointAsync(
            company.Id,
            period.Id,
            adjustment.Id,
            db,
            audit,
            context,
            DisabledApiAccess());
        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(approval));

        var gate = new FilingReleaseGate(db, "audit-candidate", audit);
        await gate.RecordCroArtifactAsync(
            company.Id,
            period.Id,
            FilingReleaseArtifact.CroAccountsPdf,
            [37, 80, 68, 70, 45, 65, 85, 68],
            "user:7");
        var statements = new FinancialStatementsService(db);
        var workflow = new FilingWorkflowService(
            db,
            statements,
            new IxbrlService(db, statements, gate),
            audit,
            releaseGate: gate);
        await workflow.UpdateCroStatusAsync(
            company.Id,
            period.Id,
            FilingStatus.ReadyForReview,
            "Owner User",
            auditUserId: "user:7");
        var beforeFailedTransition = await db.AuditLogs.CountAsync(log => log.Action == AuditEventCodes.CroFilingStatusChanged);
        await Assert.ThrowsAsync<BusinessRuleException>(() => workflow.UpdateCroStatusAsync(
            company.Id,
            period.Id,
            FilingStatus.Submitted,
            "Owner User",
            submissionReference: "CORE-FAIL-001",
            auditUserId: "user:7"));
        Assert.Equal(beforeFailedTransition, await db.AuditLogs.CountAsync(log => log.Action == AuditEventCodes.CroFilingStatusChanged));

        var logs = await db.AuditLogs.OrderBy(log => log.Id).ToListAsync();
        foreach (var code in new[]
        {
            AuditEventCodes.AdjustmentApproved,
            AuditEventCodes.FilingArtifactGenerated,
            AuditEventCodes.CroFilingStatusChanged
        })
        {
            var log = Assert.Single(logs, candidate => candidate.Action == code);
            Assert.Equal(tenant.Id, log.TenantId);
            Assert.Equal(company.Id, log.CompanyId);
            Assert.Equal(period.Id, log.PeriodId);
            Assert.Equal("user:7", log.UserId);
            Assert.Equal("Owner User", log.ActorDisplayName);
            Assert.Equal("corr-professional-001", log.RequestId);
        }
        var approvalLog = logs.Single(log => log.Action == AuditEventCodes.AdjustmentApproved);
        Assert.NotNull(approvalLog.OldValueJson);
        Assert.NotNull(approvalLog.NewValueJson);
        Assert.Contains("ApprovedAt", approvalLog.NewValueJson!, StringComparison.Ordinal);
        var workflowLog = logs.Single(log => log.Action == AuditEventCodes.CroFilingStatusChanged);
        Assert.Contains("ReadyForReview", workflowLog.NewValueJson!, StringComparison.Ordinal);

        var integrity = await new AuditIntegrityService(db).VerifyCompanyAsync(company.Id);
        Assert.True(integrity.IsValid);
        var checkpointService = new AuditIntegrityCheckpointService(db, Options.Create(CheckpointConfig()));
        var checkpoint = await checkpointService.CreateCompanyCheckpointAsync(
            company.Id,
            "user:7",
            "Owner User",
            "corr-checkpoint-001");
        Assert.Equal(logs[^1].Id, checkpoint.LastAuditLogId);
        Assert.Equal(logs[^1].IntegrityHash, checkpoint.LastIntegrityHash);
        var verified = await checkpointService.VerifyLatestCompanyCheckpointAsync(company.Id);
        Assert.True(verified.IsValid);
        Assert.Equal(0, verified.IssueCount);
    }

    [Fact]
    public async Task TenantFiltersIsolateAuditRowsAndCrossTenantAuditAttributionFailsClosed()
    {
        var root = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"), root)
            .Options;
        int tenantOneId;
        int tenantTwoId;
        int companyOneId;
        int companyTwoId;
        await using (var setup = new AccountsDbContext(options))
        {
            var tenantOne = new Tenant { Name = "Tenant One", Slug = $"tenant-one-{Guid.NewGuid():N}" };
            var tenantTwo = new Tenant { Name = "Tenant Two", Slug = $"tenant-two-{Guid.NewGuid():N}" };
            setup.Tenants.AddRange(tenantOne, tenantTwo);
            await setup.SaveChangesAsync();
            var companyOne = new Company { TenantId = tenantOne.Id, LegalName = "Tenant One Limited", IncorporationDate = new DateOnly(2025, 1, 1) };
            var companyTwo = new Company { TenantId = tenantTwo.Id, LegalName = "Tenant Two Limited", IncorporationDate = new DateOnly(2025, 1, 1) };
            setup.Companies.AddRange(companyOne, companyTwo);
            await setup.SaveChangesAsync();
            (tenantOneId, tenantTwoId, companyOneId, companyTwoId) = (tenantOne.Id, tenantTwo.Id, companyOne.Id, companyTwo.Id);
        }

        var oneAccessor = new FixedHttpContextAccessor { HttpContext = RequestContext(tenantOneId, "corr-tenant-one") };
        var twoAccessor = new FixedHttpContextAccessor { HttpContext = RequestContext(tenantTwoId, "corr-tenant-two") };
        await using var oneDb = new AccountsDbContext(options, oneAccessor);
        await using var twoDb = new AccountsDbContext(options, twoAccessor);
        Assert.Equal(
            tenantOneId,
            await oneDb.Companies.IgnoreQueryFilters().Where(company => company.Id == companyOneId).Select(company => company.TenantId).SingleAsync());
        Assert.Equal(
            tenantTwoId,
            await twoDb.Companies.IgnoreQueryFilters().Where(company => company.Id == companyTwoId).Select(company => company.TenantId).SingleAsync());
        await new AuditService(oneDb, oneAccessor).LogAsync(
            companyOneId, null, "Company", companyOneId, AuditEventCodes.CompanyUpdated,
            newValue: new { LegalName = "Tenant One Limited" });
        await new AuditService(twoDb, twoAccessor).LogAsync(
            companyTwoId, null, "Company", companyTwoId, AuditEventCodes.CompanyUpdated,
            newValue: new { LegalName = "Tenant Two Limited" });

        Assert.Single(await oneDb.AuditLogs.ToListAsync());
        Assert.Single(await twoDb.AuditLogs.ToListAsync());
        Assert.All(await oneDb.AuditLogs.ToListAsync(), log => Assert.Equal(tenantOneId, log.TenantId));
        Assert.All(await twoDb.AuditLogs.ToListAsync(), log => Assert.Equal(tenantTwoId, log.TenantId));
        await Assert.ThrowsAsync<PersistenceOwnershipException>(() =>
            DomainAuditCoverage.LogAsync(
                new AuditService(oneDb, oneAccessor),
                oneAccessor.HttpContext!,
                companyTwoId,
                null,
                "Company",
                companyTwoId,
                AuditEventCodes.CompanyUpdated,
                new { LegalName = "Tenant Two Limited" },
                new { LegalName = "Cross tenant overwrite" }));
        Assert.Equal(2, await oneDb.AuditLogs.IgnoreQueryFilters().CountAsync());
    }

    private static DefaultHttpContext RequestContext(int tenantId, string correlationId)
    {
        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = Actor(tenantId);
        context.TraceIdentifier = $"trace-{correlationId}";
        context.Request.Headers["X-Correlation-ID"] = correlationId;
        return context;
    }

    private static AuthenticatedUser Actor(int tenantId) => new(
        7,
        tenantId,
        $"Tenant {tenantId}",
        "owner@example.ie",
        "Owner User",
        "Owner");

    private static ApiAccessService DisabledApiAccess() =>
        new(Options.Create(new ApiAccessConfig { Enabled = false }), new TestEnvironment());

    private static int ResultStatusCode(IResult result) =>
        (result as IStatusCodeHttpResult)?.StatusCode ?? StatusCodes.Status200OK;

    private static AuditIntegrityConfig CheckpointConfig() => new()
    {
        ActiveKeyId = "audit-matrix-key",
        SigningKeys =
        [
            new AuditIntegritySigningKeyConfig
            {
                KeyId = "audit-matrix-key",
                SigningKey = Convert.ToBase64String(SHA256.HashData("audit-matrix-signing-key"u8.ToArray()))
            }
        ]
    };

    private sealed class MasterDataFixture(WebApplication app, HttpClient client, int tenantId) : IAsyncDisposable
    {
        public WebApplication App { get; } = app;
        public int TenantId { get; } = tenantId;
        private HttpClient Client { get; } = client;

        public static async Task<MasterDataFixture> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
            builder.Services.AddHttpContextAccessor();
            var databaseName = Guid.NewGuid().ToString("N");
            builder.Services.AddDbContext<AccountsDbContext>(options => options.UseInMemoryDatabase(databaseName));
            builder.Services.Configure<ApiAccessConfig>(options => options.Enabled = false);
            builder.Services.Configure<ImportLimitConfig>(_ => { });
            builder.Services.Configure<IdempotencyConfig>(_ => { });
            builder.Services.AddSingleton<ApiAccessService>();
            builder.Services.AddScoped<AuditService>();
            builder.Services.AddScoped<AnnualReturnDateService>();
            builder.Services.AddScoped<IdempotencyService>();
            builder.Services.AddScoped<AccountingWriteGuard>();
            builder.Services.AddScoped<PeriodChronologyService>();
            builder.Services.AddScoped<CategoryService>();
            builder.Services.AddScoped<CompanyOnboardingService>();
            builder.Services.AddScoped<ImportService>();
            builder.Services.AddScoped<AccountingConcurrencyCoordinator>();
            builder.Services.AddScoped<DuplicateReviewService>();
            builder.Services.AddScoped<FinancialStatementsService>();
            builder.Services.AddScoped<FilingReadinessProfileService>();
            builder.Services.AddScoped<FilingReleaseGate>(services => new FilingReleaseGate(
                services.GetRequiredService<AccountsDbContext>(),
                "audit-matrix-candidate",
                services.GetRequiredService<AuditService>()));

            var app = builder.Build();
            int tenantId;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
                var tenant = new Tenant { Name = "Domain Audit Practice", Slug = $"domain-audit-{Guid.NewGuid():N}" };
                db.Tenants.Add(tenant);
                await db.SaveChangesAsync();
                tenantId = tenant.Id;
            }
            app.Use(async (context, next) =>
            {
                context.Items[AuthContext.ItemKey] = Actor(tenantId);
                await next();
            });
            app.MapCompanyEndpoints();
            app.MapBankingEndpoints();
            await app.StartAsync();
            var address = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new MasterDataFixture(app, new HttpClient { BaseAddress = new Uri(address) }, tenantId);
        }

        public async Task<HttpResponseMessage> SendAsync(
            HttpMethod method,
            string path,
            object? payload,
            string correlationId,
            string? idempotencyKey = null,
            bool ensureSuccess = true)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add("X-Correlation-ID", correlationId);
            if (idempotencyKey is not null)
                request.Headers.Add(IdempotencyHttpContract.RequestHeader, idempotencyKey);
            if (payload is not null)
                request.Content = JsonContent.Create(payload);
            var response = await Client.SendAsync(request);
            if (ensureSuccess && !response.IsSuccessStatusCode)
                throw new Xunit.Sdk.XunitException($"{method} {path} failed {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            return response;
        }

        public async Task<int> SendForIdAsync(
            HttpMethod method,
            string path,
            object payload,
            string correlationId,
            string? idempotencyKey = null)
        {
            using var response = await SendAsync(method, path, payload, correlationId, idempotencyKey);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.GetProperty("id").GetInt32();
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Accounts.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class FixedHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
