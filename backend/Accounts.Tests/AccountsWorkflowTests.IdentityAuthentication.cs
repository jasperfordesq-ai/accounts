using Accounts.Api.Endpoints;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Middleware;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuestPDF.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Accounts.Tests;

public partial class AccountsWorkflowTests
{
    [Fact]
    public async Task ClassificationRoutes_EnforceRuntimePeriodRoleAndApiAuthorization()
    {
        var databaseName = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<AccountsDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.Configure<SizeThresholdConfig>(_ => { });
        builder.Services.Configure<ApiAccessConfig>(config =>
        {
            config.Enabled = true;
            config.Keys =
            [
                new ApiAccessKeyConfig
                {
                    Name = "Reader",
                    Role = "Reader",
                    DevelopmentKey = "reader",
                    AllowedCompanyIds = []
                },
                new ApiAccessKeyConfig
                {
                    Name = "Writer",
                    Role = "Writer",
                    DevelopmentKey = "writer",
                    AllowedCompanyIds = []
                }
            ];
        });
        builder.Services.AddScoped<ApiAccessService>();
        builder.Services.AddScoped<AuditService>();
        builder.Services.AddScoped<DeadlineService>();
        builder.Services.AddScoped<DashboardDeadlineService>();
        builder.Services.AddScoped<SizeClassificationService>();
        builder.Services.AddScoped<FilingRegimeService>();

        await using var app = builder.Build();
        int tenantId;
        int allowedCompanyId;
        int allowedPeriodId;
        int restrictedPeriodId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var tenant = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
            var allowedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Allowed Client Limited");
            var restrictedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Restricted Client Limited");
            var allowedPeriod = new AccountingPeriod
            {
                CompanyId = allowedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true
            };
            var restrictedPeriod = new AccountingPeriod
            {
                CompanyId = restrictedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true
            };
            db.AccountingPeriods.AddRange(allowedPeriod, restrictedPeriod);
            await db.SaveChangesAsync();
            db.SizeClassifications.Add(new SizeClassification
            {
                PeriodId = allowedPeriod.Id,
                Turnover = 120_000m,
                BalanceSheetTotal = 40_000m,
                AvgEmployees = 3
            });
            await db.SaveChangesAsync();
            tenantId = tenant.Id;
            allowedCompanyId = allowedCompany.Id;
            allowedPeriodId = allowedPeriod.Id;
            restrictedPeriodId = restrictedPeriod.Id;
        }

        app.Use(async (context, next) =>
        {
            var role = context.Request.Headers["X-Test-Role"].FirstOrDefault() ?? "Accountant";
            context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
                UserId: 1,
                TenantId: tenantId,
                TenantName: "Tenant A",
                Email: $"{role.ToLowerInvariant()}@tenant-a.test",
                DisplayName: $"Tenant A {role}",
                Role: role,
                AllowedCompanyIds: new HashSet<int> { allowedCompanyId });
            await next();
        });
        app.MapClassificationEndpoints();
        await app.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(LoopbackBaseAddress(app)) };

            var mismatchedPeriod = await client.PutAsJsonAsync(
                $"/api/companies/{allowedCompanyId}/periods/{restrictedPeriodId}/member-audit-notice",
                new { received = true, noticeDate = "2025-02-01" });
            Assert.Equal(HttpStatusCode.NotFound, mismatchedPeriod.StatusCode);

            using var reviewerClassify = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/classify",
                role: "Reviewer",
                apiKey: "writer");
            var reviewerDenied = await client.SendAsync(reviewerClassify);
            Assert.Equal(HttpStatusCode.Forbidden, reviewerDenied.StatusCode);

            using var writerClassify = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/classify",
                role: "Accountant",
                apiKey: "writer");
            var classifyAllowed = await client.SendAsync(writerClassify);
            Assert.Equal(HttpStatusCode.OK, classifyAllowed.StatusCode);

            using var readerRegime = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/filing-regime",
                role: "Accountant",
                apiKey: "reader",
                body: new { electedRegime = ElectedRegime.Micro });
            var readerDenied = await client.SendAsync(readerRegime);
            Assert.Equal(HttpStatusCode.Unauthorized, readerDenied.StatusCode);

            using var writerRegime = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/filing-regime",
                role: "Accountant",
                apiKey: "writer",
                body: new { electedRegime = ElectedRegime.Micro });
            var regimeAllowed = await client.SendAsync(writerRegime);
            Assert.Equal(HttpStatusCode.OK, regimeAllowed.StatusCode);

            using var clientMemberAudit = Request(
                HttpMethod.Put,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/member-audit-notice",
                role: "Client",
                apiKey: "writer",
                body: new { received = true, noticeDate = "2025-02-01" });
            var clientDenied = await client.SendAsync(clientMemberAudit);
            Assert.Equal(HttpStatusCode.Forbidden, clientDenied.StatusCode);

            await using var verifyScope = app.Services.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var period = await verifyDb.AccountingPeriods.SingleAsync(p => p.Id == allowedPeriodId);
            Assert.False(period.MemberAuditNoticeReceived);
            Assert.Null(period.MemberAuditNoticeDate);
            Assert.NotNull(await verifyDb.FilingRegimes.SingleOrDefaultAsync(f => f.PeriodId == allowedPeriodId));
            Assert.Single(await verifyDb.AuditLogs.Where(a => a.Action == AuditEventCodes.SizeClassificationRun).ToListAsync());
            Assert.Single(await verifyDb.AuditLogs.Where(a => a.Action == AuditEventCodes.FilingRegimeDetermined).ToListAsync());
        }
        finally
        {
            await app.StopAsync();
        }

        static HttpRequestMessage Request(HttpMethod method, string path, string role, string? apiKey = null, object? body = null)
        {
            var request = new HttpRequestMessage(method, path);
            request.Headers.Add("X-Test-Role", role);
            if (apiKey is not null)
                request.Headers.Add("X-Accounts-Api-Key", apiKey);
            if (body is not null)
                request.Content = JsonContent.Create(body);
            return request;
        }

    }

    [Fact]
    public async Task DeadlineRoutes_EnforceRuntimeCompanyPeriodRoleAndApiAuthorization()
    {
        var databaseName = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<AccountsDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.Configure<ApiAccessConfig>(config =>
        {
            config.Enabled = true;
            config.Keys =
            [
                new ApiAccessKeyConfig
                {
                    Name = "Reader",
                    Role = "Reader",
                    DevelopmentKey = "reader",
                    AllowedCompanyIds = []
                },
                new ApiAccessKeyConfig
                {
                    Name = "Writer",
                    Role = "Writer",
                    DevelopmentKey = "writer",
                    AllowedCompanyIds = []
                }
            ];
        });
        builder.Services.AddScoped<ApiAccessService>();
        builder.Services.AddScoped<AuditService>();
        builder.Services.AddScoped<DeadlineService>();
        builder.Services.AddScoped<DashboardDeadlineService>();

        await using var app = builder.Build();
        int tenantId;
        int allowedCompanyId;
        int restrictedCompanyId;
        int allowedPeriodId;
        int restrictedPeriodId;
        var dueDate = new DateOnly(2025, 10, 1);
        var filedDate = new DateOnly(2025, 10, 2);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var tenant = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
            var allowedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Allowed Client Limited");
            var restrictedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Restricted Client Limited");
            var allowedPeriod = new AccountingPeriod
            {
                CompanyId = allowedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true
            };
            var restrictedPeriod = new AccountingPeriod
            {
                CompanyId = restrictedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true
            };
            db.AccountingPeriods.AddRange(allowedPeriod, restrictedPeriod);
            await db.SaveChangesAsync();
            db.FilingDeadlines.AddRange(
                new FilingDeadline
                {
                    CompanyId = allowedCompany.Id,
                    PeriodId = allowedPeriod.Id,
                    DeadlineType = DeadlineType.CRO,
                    DueDate = dueDate
                },
                new FilingDeadline
                {
                    CompanyId = restrictedCompany.Id,
                    PeriodId = restrictedPeriod.Id,
                    DeadlineType = DeadlineType.CRO,
                    DueDate = dueDate
                });
            await db.SaveChangesAsync();
            await SeedAcceptedCroFilingPackageAsync(db, allowedPeriod.Id);
            await SeedRevenueInternalIxbrlChecksPassedAsync(db, allowedPeriod.Id);
            tenantId = tenant.Id;
            allowedCompanyId = allowedCompany.Id;
            restrictedCompanyId = restrictedCompany.Id;
            allowedPeriodId = allowedPeriod.Id;
            restrictedPeriodId = restrictedPeriod.Id;
        }

        app.Use(async (context, next) =>
        {
            var role = context.Request.Headers["X-Test-Role"].FirstOrDefault() ?? "Client";
            context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
                UserId: 1,
                TenantId: tenantId,
                TenantName: "Tenant A",
                Email: $"{role.ToLowerInvariant()}@tenant-a.test",
                DisplayName: $"Tenant A {role}",
                Role: role,
                AllowedCompanyIds: new HashSet<int> { allowedCompanyId });
            await next();
        });
        app.MapDeadlineEndpoints();
        await app.StartAsync();

        try
        {
            var addresses = app.Services
                .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features
                .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(addresses!.Addresses)) };

            var hiddenCompanyRead = await client.GetAsync($"/api/companies/{restrictedCompanyId}/deadlines");
            Assert.Equal(HttpStatusCode.NotFound, hiddenCompanyRead.StatusCode);
            var hiddenCompanyUpcoming = await client.GetAsync($"/api/companies/{restrictedCompanyId}/deadlines/upcoming");
            Assert.Equal(HttpStatusCode.NotFound, hiddenCompanyUpcoming.StatusCode);
            var hiddenCompanyJeopardy = await client.GetAsync($"/api/companies/{restrictedCompanyId}/deadlines/jeopardy");
            Assert.Equal(HttpStatusCode.NotFound, hiddenCompanyJeopardy.StatusCode);

            using var mismatchedPeriodCalculate = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{restrictedPeriodId}/deadlines/calculate",
                role: "Client",
                apiKey: "writer");
            var mismatchedCalculateDenied = await client.SendAsync(mismatchedPeriodCalculate);
            Assert.Equal(HttpStatusCode.NotFound, mismatchedCalculateDenied.StatusCode);

            using var mismatchedPeriodMarkFiled = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{restrictedPeriodId}/mark-filed",
                role: "Client",
                apiKey: "writer",
                body: new { deadlineType = DeadlineType.CRO, filedDate });
            var mismatchedPeriodDenied = await client.SendAsync(mismatchedPeriodMarkFiled);
            Assert.Equal(HttpStatusCode.NotFound, mismatchedPeriodDenied.StatusCode);

            using var reviewerCalculate = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/deadlines/calculate",
                role: "Reviewer",
                apiKey: "writer");
            var reviewerDenied = await client.SendAsync(reviewerCalculate);
            Assert.Equal(HttpStatusCode.Forbidden, reviewerDenied.StatusCode);

            using var readerCalculate = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/deadlines/calculate",
                role: "Accountant",
                apiKey: "reader");
            var readerDenied = await client.SendAsync(readerCalculate);
            Assert.Equal(HttpStatusCode.Unauthorized, readerDenied.StatusCode);

            using var ownerCalculate = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/deadlines/calculate",
                role: "Owner",
                apiKey: "writer");
            var ownerCalculateAllowed = await client.SendAsync(ownerCalculate);
            Assert.Equal(HttpStatusCode.OK, ownerCalculateAllowed.StatusCode);

            using var writerCalculate = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/deadlines/calculate",
                role: "Accountant",
                apiKey: "writer");
            var calculateAllowed = await client.SendAsync(writerCalculate);
            Assert.Equal(HttpStatusCode.OK, calculateAllowed.StatusCode);

            using var accountantMarkFiled = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/mark-filed",
                role: "Accountant",
                apiKey: "writer",
                body: new { deadlineType = DeadlineType.CRO, filedDate });
            var accountantMarkDenied = await client.SendAsync(accountantMarkFiled);
            Assert.Equal(HttpStatusCode.Forbidden, accountantMarkDenied.StatusCode);

            using var emptyMarkFiled = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/mark-filed",
                role: "Reviewer",
                apiKey: "writer",
                body: new { });
            var emptyMarkDenied = await client.SendAsync(emptyMarkFiled);
            Assert.Equal(HttpStatusCode.BadRequest, emptyMarkDenied.StatusCode);

            await using (var invalidVerifyScope = app.Services.CreateAsyncScope())
            {
                var invalidVerifyDb = invalidVerifyScope.ServiceProvider.GetRequiredService<AccountsDbContext>();
                var unfiledCro = await invalidVerifyDb.FilingDeadlines.SingleAsync(d =>
                    d.CompanyId == allowedCompanyId && d.PeriodId == allowedPeriodId && d.DeadlineType == DeadlineType.CRO);
                Assert.Null(unfiledCro.FiledDate);
                Assert.Empty(await invalidVerifyDb.FilingHistories.Where(h =>
                    h.CompanyId == allowedCompanyId && h.PeriodId == allowedPeriodId && h.DeadlineType == DeadlineType.CRO).ToListAsync());
            }

            using var filedDateOnlyMarkFiled = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/mark-filed",
                role: "Reviewer",
                apiKey: "writer",
                body: new { filedDate });
            var filedDateOnlyDenied = await client.SendAsync(filedDateOnlyMarkFiled);
            Assert.Equal(HttpStatusCode.BadRequest, filedDateOnlyDenied.StatusCode);

            await using (var filedDateOnlyVerifyScope = app.Services.CreateAsyncScope())
            {
                var filedDateOnlyVerifyDb = filedDateOnlyVerifyScope.ServiceProvider.GetRequiredService<AccountsDbContext>();
                var unfiledCro = await filedDateOnlyVerifyDb.FilingDeadlines.SingleAsync(d =>
                    d.CompanyId == allowedCompanyId && d.PeriodId == allowedPeriodId && d.DeadlineType == DeadlineType.CRO);
                Assert.Null(unfiledCro.FiledDate);
                Assert.Empty(await filedDateOnlyVerifyDb.FilingHistories.Where(h =>
                    h.CompanyId == allowedCompanyId && h.PeriodId == allowedPeriodId && h.DeadlineType == DeadlineType.CRO).ToListAsync());
            }

            using var readerMarkFiled = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/mark-filed",
                role: "Reviewer",
                apiKey: "reader",
                body: new { deadlineType = DeadlineType.CRO, filedDate });
            var readerMarkDenied = await client.SendAsync(readerMarkFiled);
            Assert.Equal(HttpStatusCode.Unauthorized, readerMarkDenied.StatusCode);

            using var reviewerMarkFiled = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/mark-filed",
                role: "Reviewer",
                apiKey: "writer",
                body: new { deadlineType = DeadlineType.CRO, filedDate },
                idempotencyKey: "deadline-route-cro-filed-001");
            var markFiledAllowed = await client.SendAsync(reviewerMarkFiled);
            Assert.Equal(HttpStatusCode.OK, markFiledAllowed.StatusCode);

            await using var verifyScope = app.Services.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var allowedCro = await verifyDb.FilingDeadlines.SingleAsync(d =>
                d.CompanyId == allowedCompanyId && d.PeriodId == allowedPeriodId && d.DeadlineType == DeadlineType.CRO);
            var restrictedCro = await verifyDb.FilingDeadlines.SingleAsync(d =>
                d.CompanyId == restrictedCompanyId && d.PeriodId == restrictedPeriodId && d.DeadlineType == DeadlineType.CRO);
            Assert.Equal(filedDate, allowedCro.FiledDate);
            Assert.Null(restrictedCro.FiledDate);
            Assert.DoesNotContain(await verifyDb.FilingDeadlines.ToListAsync(), d =>
                d.CompanyId == restrictedCompanyId && d.PeriodId == restrictedPeriodId && d.DeadlineType == DeadlineType.Revenue);
            Assert.Single(await verifyDb.FilingHistories.Where(h =>
                h.CompanyId == allowedCompanyId && h.PeriodId == allowedPeriodId && h.DeadlineType == DeadlineType.CRO).ToListAsync());
            Assert.Empty(await verifyDb.FilingHistories.Where(h =>
                h.CompanyId == allowedCompanyId && h.PeriodId == allowedPeriodId && h.DeadlineType == DeadlineType.Revenue).ToListAsync());
            Assert.Empty(await verifyDb.FilingHistories.Where(h => h.CompanyId == restrictedCompanyId).ToListAsync());
        }
        finally
        {
            await app.StopAsync();
        }

        static HttpRequestMessage Request(
            HttpMethod method,
            string path,
            string role,
            string? apiKey = null,
            object? body = null,
            string? idempotencyKey = null)
        {
            var request = new HttpRequestMessage(method, path);
            request.Headers.Add("X-Test-Role", role);
            if (apiKey is not null)
                request.Headers.Add("X-Accounts-Api-Key", apiKey);
            if (idempotencyKey is not null)
                request.Headers.Add(IdempotencyHttpContract.RequestHeader, idempotencyKey);
            if (body is not null)
                request.Content = JsonContent.Create(body);
            return request;
        }

    }

    [Fact]
    public async Task DocumentEndpoints_PostCroDownloadsRequireCsrfAndRecordGeneratedFlags()
    {
        var databaseName = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<AccountsDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.AddScoped<FinancialStatementsService>();
        builder.Services.AddScoped<DocumentGeneratorService>();
        builder.Services.AddScoped<DirectorsReportService>();
        builder.Services.AddScoped<IxbrlService>();
        builder.Services.AddScoped<FilingWorkflowService>();
        builder.Services.Configure<AuthSessionConfig>(config => config.CsrfCookieName = "accounts_csrf");
        builder.Services.Configure<ApiAccessConfig>(config => config.Enabled = false);
        builder.Services.AddScoped<ApiAccessService>();

        await using var app = builder.Build();
        int companyId;
        int periodId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
            db.FilingRegimes.Add(new FilingRegime
            {
                PeriodId = period.Id,
                ElectedRegime = ElectedRegime.Micro,
                CanUseMicro = true,
                CanFileAbridged = true,
                AuditExempt = true
            });
            db.NotesDisclosures.Add(new NotesDisclosure
            {
                PeriodId = period.Id,
                NoteNumber = 1,
                Title = "Approval of Financial Statements",
                Content = "Approved by the directors.",
                IsRequired = true,
                IsIncluded = true
            });
            await db.SaveChangesAsync();
            await MakePeriodReadyForCroDocumentsAsync(db, period);
            companyId = period.CompanyId;
            periodId = period.Id;
        }

        app.Use(async (context, next) =>
        {
            context.Items[AuthContext.ItemKey] = AuthenticatedRole("Accountant") with
            {
                CsrfToken = "csrf-token-1"
            };
            await next();
        });
        app.UseMiddleware<CsrfProtectionMiddleware>();
        app.MapDocumentEndpoints();
        await app.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(LoopbackBaseAddress(app)) };
            var missingCsrf = await client.PostAsync(
                $"/api/companies/{companyId}/periods/{periodId}/documents/cro-filing-pack",
                content: null);

            Assert.Equal(HttpStatusCode.Forbidden, missingCsrf.StatusCode);

            await using (var rejectedScope = app.Services.CreateAsyncScope())
            {
                var db = rejectedScope.ServiceProvider.GetRequiredService<AccountsDbContext>();
                Assert.Null(await db.CroFilingPackages.SingleOrDefaultAsync(p => p.PeriodId == periodId));
            }

            const string croKey = "document-cro-replay-test-0001";
            var croPack = await SendCsrfPostAsync(
                client,
                $"/api/companies/{companyId}/periods/{periodId}/documents/cro-filing-pack",
                croKey);
            var croReplay = await SendCsrfPostAsync(
                client,
                $"/api/companies/{companyId}/periods/{periodId}/documents/cro-filing-pack",
                croKey);
            var signature = await SendCsrfPostAsync(
                client,
                $"/api/companies/{companyId}/periods/{periodId}/documents/signature-page");

            Assert.True(croPack.StatusCode == HttpStatusCode.OK, await croPack.Content.ReadAsStringAsync());
            Assert.True(croReplay.StatusCode == HttpStatusCode.OK, await croReplay.Content.ReadAsStringAsync());
            Assert.True(signature.StatusCode == HttpStatusCode.OK, await signature.Content.ReadAsStringAsync());
            Assert.Equal("application/pdf", croPack.Content.Headers.ContentType?.MediaType);
            Assert.Equal("application/pdf", signature.Content.Headers.ContentType?.MediaType);
            Assert.Equal("false", croPack.Headers.GetValues(IdempotencyHttpContract.ReplayedHeader).Single());
            Assert.Equal("true", croReplay.Headers.GetValues(IdempotencyHttpContract.ReplayedHeader).Single());
            Assert.Equal(await croPack.Content.ReadAsByteArrayAsync(), await croReplay.Content.ReadAsByteArrayAsync());

            await using var scope = app.Services.CreateAsyncScope();
            var finalDb = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var package = await finalDb.CroFilingPackages.SingleAsync(p => p.PeriodId == periodId);

            Assert.True(package.AccountsPdfGenerated);
            Assert.True(package.SignaturePageGenerated);
            Assert.Equal(FilingStatus.PackageGenerated, package.FilingStatus);
            Assert.Equal(2, await finalDb.IdempotencyRecords.IgnoreQueryFilters().CountAsync());
        }
        finally
        {
            await app.StopAsync();
        }

        static async Task<HttpResponseMessage> SendCsrfPostAsync(HttpClient client, string path, string? idempotencyKey = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Add(CsrfProtectionMiddleware.HeaderName, "csrf-token-1");
            request.Headers.Add(IdempotencyHttpContract.RequestHeader, idempotencyKey ?? $"document-test-{Guid.NewGuid():N}");
            request.Headers.Add("Cookie", "accounts_csrf=csrf-token-1");
            return await client.SendAsync(request);
        }
    }

    [Fact]
    public void ProductionSafety_BlocksWeakBootstrapOwnerPasswordOutsideDevelopment()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "accounts.example.ie",
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password;SSL Mode=VerifyFull;Root Certificate=/run/secrets/postgres_ca_certificate;Trust Server Certificate=false",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = StrongSessionSigningKey()
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Staging"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = false,
                SeedDemoData = false
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig
                {
                    Enabled = true,
                    RequireInProduction = true,
                    Keys =
                    [
                        new ApiAccessKeyConfig
                        {
                            Name = "Production firm",
                            KeyHash = ApiAccessService.HashKey("real-secret")
                        }
                    ]
                }),
                new TestEnvironment("Staging")),
            Options.Create(AuditIntegrityCheckpointOptions()),
            Options.Create(new BootstrapOwnerConfig
            {
                Enabled = true,
                TenantName = "Production Firm",
                TenantSlug = "production-firm",
                OwnerEmail = "owner@example.ie",
                OwnerDisplayName = "Owner User",
                OwnerInitialPassword = "short"
            }));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("BootstrapOwner:OwnerInitialPassword"));
    }

    [Fact]
    public void BootstrapOwnerPasswordPolicy_AllowsCiGeneratedPasswordShape()
    {
        var password = "CiOwner1!" + new string('a', 64);

        Assert.Null(BootstrapOwnerPasswordPolicy.Validate(password));
    }

    [Fact]
    public void ProductionSafety_AllowsStrongBootstrapOwnerConfigurationOutsideDevelopment()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "accounts.example.ie",
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password;SSL Mode=VerifyFull;Root Certificate=/run/secrets/postgres_ca_certificate;Trust Server Certificate=false",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = StrongSessionSigningKey()
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Staging"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = false,
                SeedDemoData = false
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig
                {
                    Enabled = true,
                    RequireInProduction = true,
                    Keys =
                    [
                        new ApiAccessKeyConfig
                        {
                            Name = "Production firm",
                            KeyHash = ApiAccessService.HashKey("real-secret")
                        }
                    ]
                }),
                new TestEnvironment("Staging")),
            Options.Create(AuditIntegrityCheckpointOptions()),
            Options.Create(new BootstrapOwnerConfig
            {
                Enabled = true,
                TenantName = "Production Firm",
                TenantSlug = "production-firm",
                OwnerEmail = "owner@example.ie",
                OwnerDisplayName = "Owner User",
                OwnerInitialPassword = "Correct Horse Battery Staple 1!"
            }),
            MonitoringOptions(),
            SecureDeadlineDeliveryOptions(),
            SecurePlatformMetricsOptions(),
            SecureDatabaseTenantIsolationOptions(),
            SecureIdentitySecurityOptions());

        Assert.Empty(service.Validate());
    }

    [Fact]
    public void ProductionSafety_BlocksWeakSessionSigningKeyInProduction()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password;SSL Mode=VerifyFull;Root Certificate=/run/secrets/postgres_ca_certificate;Trust Server Certificate=false",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = new string('a', 64)
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Production"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = false,
                SeedDemoData = false
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig
                {
                    Enabled = true,
                    RequireInProduction = true,
                    Keys =
                    [
                        new ApiAccessKeyConfig
                        {
                            Name = "Production firm",
                            KeyHash = ApiAccessService.HashKey("real-secret")
                        }
                    ]
                }),
                new TestEnvironment("Production")),
            Options.Create(AuditIntegrityCheckpointOptions()));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("AuthSession:SigningKey"));
    }

    [Fact]
    public void ProductionSafety_BlocksInvalidEncodedSessionSigningKeyInProduction()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password;SSL Mode=VerifyFull;Root Certificate=/run/secrets/postgres_ca_certificate;Trust Server Certificate=false",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = "not-a-base64-session-secret-value!!!!!"
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Production"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = false,
                SeedDemoData = false
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig
                {
                    Enabled = true,
                    RequireInProduction = true,
                    Keys =
                    [
                        new ApiAccessKeyConfig
                        {
                            Name = "Production firm",
                            KeyHash = ApiAccessService.HashKey("real-secret")
                        }
                    ]
                }),
                new TestEnvironment("Production")),
            Options.Create(AuditIntegrityCheckpointOptions()));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("AuthSession:SigningKey"));
    }

    [Fact]
    public void ProductionSafety_BlocksDevelopmentSessionSigningKeyInProduction()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password;SSL Mode=VerifyFull;Root Certificate=/run/secrets/postgres_ca_certificate;Trust Server Certificate=false",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = DevelopmentSessionSigningKey()
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Production"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = false,
                SeedDemoData = false
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig
                {
                    Enabled = true,
                    RequireInProduction = true,
                    Keys =
                    [
                        new ApiAccessKeyConfig
                        {
                            Name = "Production firm",
                            KeyHash = ApiAccessService.HashKey("real-secret")
                        }
                    ]
                }),
                new TestEnvironment("Production")),
            Options.Create(AuditIntegrityCheckpointOptions()));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("development session signing key", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProductionSafety_AllowsStrongSessionSigningConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "accounts.example.ie",
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password;SSL Mode=VerifyFull;Root Certificate=/run/secrets/postgres_ca_certificate;Trust Server Certificate=false",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = StrongSessionSigningKey()
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Production"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = false,
                SeedDemoData = false
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig
                {
                    Enabled = true,
                    RequireInProduction = true,
                    Keys =
                    [
                        new ApiAccessKeyConfig
                        {
                            Name = "Production firm",
                            KeyHash = ApiAccessService.HashKey("real-secret")
                        }
                    ]
                }),
                new TestEnvironment("Production")),
            Options.Create(AuditIntegrityCheckpointOptions()),
            monitoring: MonitoringOptions(),
            deadlineDelivery: SecureDeadlineDeliveryOptions(),
            platformMetrics: SecurePlatformMetricsOptions(),
            databaseTenantIsolation: SecureDatabaseTenantIsolationOptions(),
            identitySecurity: SecureIdentitySecurityOptions());

        Assert.Empty(service.Validate());
    }

    [Fact]
    public void ProductionSafety_AllowsStrongBase64UrlSessionSigningConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "accounts.example.ie",
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password;SSL Mode=VerifyFull;Root Certificate=/run/secrets/postgres_ca_certificate;Trust Server Certificate=false",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = StrongSessionSigningKeyBase64Url()
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Production"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = false,
                SeedDemoData = false
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig
                {
                    Enabled = true,
                    RequireInProduction = true,
                    Keys =
                    [
                        new ApiAccessKeyConfig
                        {
                            Name = "Production firm",
                            KeyHash = ApiAccessService.HashKey("real-secret")
                        }
                    ]
                }),
                new TestEnvironment("Production")),
            Options.Create(AuditIntegrityCheckpointOptions()),
            monitoring: MonitoringOptions(),
            deadlineDelivery: SecureDeadlineDeliveryOptions(),
            platformMetrics: SecurePlatformMetricsOptions(),
            databaseTenantIsolation: SecureDatabaseTenantIsolationOptions(),
            identitySecurity: SecureIdentitySecurityOptions());

        Assert.Empty(service.Validate());
    }

    [Theory]
    [InlineData(14)]
    [InlineData(1441)]
    public void ProductionSafety_BlocksSessionExpiryOutsideProductionRange(int expiryMinutes)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password;SSL Mode=VerifyFull;Root Certificate=/run/secrets/postgres_ca_certificate;Trust Server Certificate=false",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = StrongSessionSigningKey(),
                ["AuthSession:ExpiryMinutes"] = expiryMinutes.ToString()
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Production"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = false,
                SeedDemoData = false
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig
                {
                    Enabled = true,
                    RequireInProduction = true,
                    Keys =
                    [
                        new ApiAccessKeyConfig
                        {
                            Name = "Production firm",
                            KeyHash = ApiAccessService.HashKey("real-secret")
                        }
                    ]
                }),
                new TestEnvironment("Production")));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("AuthSession:ExpiryMinutes"));
    }

    [Fact]
    public void ProductionSafety_BlocksInsecureSessionCookiesInProduction()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password;SSL Mode=VerifyFull;Root Certificate=/run/secrets/postgres_ca_certificate;Trust Server Certificate=false",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = StrongSessionSigningKey(),
                ["AuthSession:SecureCookiesInProduction"] = "false"
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Production"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = false,
                SeedDemoData = false
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig
                {
                    Enabled = true,
                    RequireInProduction = true,
                    Keys =
                    [
                        new ApiAccessKeyConfig
                        {
                            Name = "Production firm",
                            KeyHash = ApiAccessService.HashKey("real-secret")
                        }
                    ]
                }),
                new TestEnvironment("Production")));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("AuthSession:SecureCookiesInProduction"));
    }

    [Fact]
    public async Task SeedData_DoesNotRewriteNonDemoUserCredentials()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db, "External Firm", "external-firm");
        var user = new UserAccount
        {
            TenantId = tenant.Id,
            Email = "real-user@example.ie",
            DisplayName = "Real User",
            Role = "Owner",
            PasswordHash = "legacy-hash",
            PasswordSalt = "legacy-salt",
            PasswordAlgorithm = "Legacy-SHA1",
            PasswordStrengthScore = 1,
            MustChangePassword = false,
            IsActive = true
        };
        db.UserAccounts.Add(user);
        await db.SaveChangesAsync();
        var userId = user.Id;

        await SeedData.SeedAsync(db);

        db.ChangeTracker.Clear();
        var reloaded = await db.UserAccounts.SingleAsync(u => u.Id == userId);
        Assert.Equal("legacy-hash", reloaded.PasswordHash);
        Assert.Equal("legacy-salt", reloaded.PasswordSalt);
        Assert.Equal("Legacy-SHA1", reloaded.PasswordAlgorithm);
        Assert.Equal(1, reloaded.PasswordStrengthScore);
        Assert.False(reloaded.MustChangePassword);
    }

    [Fact]
    public async Task SeedData_RemovesFixedDemoUsersWhenDemoUserSeedingIsDisabled()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db, "Accounts v2 Demo Tenant", "main-demo");
        var (hash, salt) = PasswordHasher.HashPassword("Harbour!Ledger-V2-Owner-2026-84Qm");
        db.UserAccounts.Add(new UserAccount
        {
            TenantId = tenant.Id,
            Email = "owner@accounts-demo.ie",
            DisplayName = "Legacy Demo Owner",
            Role = "Owner",
            PasswordHash = hash,
            PasswordSalt = salt,
            PasswordAlgorithm = AuthService.PasswordAlgorithm,
            PasswordStrengthScore = 5,
            MustChangePassword = false,
            IsActive = true,
            PasswordLastChangedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await SeedData.SeedAsync(db, seedDemoUsers: false);

        Assert.False(await db.UserAccounts.AnyAsync(u => u.Email.EndsWith("@accounts-demo.ie")));
    }

    [Fact]
    public async Task AuthService_LoginAcceptsValidPasswordAndReturnsTenantPrincipal()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        var user = await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!");
        var service = CreateAuthService(db);

        var result = await service.LoginAsync(" OWNER@EXAMPLE.IE ", "Correct Horse Battery Staple 1!");

        Assert.True(result.Succeeded);
        Assert.Null(result.FailureReason);
        Assert.NotNull(result.User);
        Assert.Equal(user.Id, result.User.UserId);
        Assert.Equal(tenant.Id, result.User.TenantId);
        Assert.Equal("Example Firm", result.User.TenantName);
        Assert.Equal("owner@example.ie", result.User.Email);
        Assert.Equal("Owner User", result.User.DisplayName);
        Assert.Equal("Owner", result.User.Role);
        var saved = await db.UserAccounts.FindAsync(user.Id);
        Assert.NotNull(saved?.LastLoginAt);
    }

    [Fact]
    public async Task AuthService_LoginSessionAndResponseCarryMustChangePasswordFlag()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        var user = await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!");
        user.MustChangePassword = true;
        await db.SaveChangesAsync();
        var service = CreateAuthService(db);

        var login = await service.LoginAsync("owner@example.ie", "Correct Horse Battery Staple 1!");
        var principalFlag = typeof(AuthenticatedUser).GetProperty("MustChangePassword");
        Assert.True(principalFlag is not null, "Authenticated users should carry MustChangePassword so middleware can enforce first-login password changes.");
        Assert.True((bool)principalFlag.GetValue(login.User!)!);

        var responseFlag = typeof(AuthResponse).GetProperty("MustChangePassword");
        Assert.True(responseFlag is not null, "Auth responses should tell the frontend when the user must change password.");
        Assert.True((bool)responseFlag.GetValue(AuthResponse.From(login.User!))!);

        var cookieValue = service.CreateSessionCookieValue(login.User!, DateTimeOffset.UtcNow);
        var roundTripped = await service.ReadSessionAsync(cookieValue, DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.NotNull(roundTripped);
        Assert.True((bool)principalFlag.GetValue(roundTripped)!);
    }

    [Fact]
    public async Task AuthService_ChangePasswordClearsMustChangePasswordAndInvalidatesOldSession()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        var user = await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!");
        user.MustChangePassword = true;
        await db.SaveChangesAsync();
        var service = CreateAuthService(db);
        var login = await service.LoginAsync("owner@example.ie", "Correct Horse Battery Staple 1!");
        var oldSession = service.CreateSessionCookieValue(login.User!, DateTimeOffset.UtcNow);
        var oldPasswordChangedAt = user.PasswordLastChangedAt;
        var result = await service.ChangePasswordAsync(
            user.Id,
            "Correct Horse Battery Staple 1!",
            "New Correct Horse Battery 2!");
        Assert.True(result.Succeeded);
        Assert.NotNull(result.User);
        var resultPasswordChangedAt = result.User.PasswordLastChangedAt;
        Assert.Equal(0, resultPasswordChangedAt.Ticks % 10);

        db.ChangeTracker.Clear();
        var reloaded = await db.UserAccounts.SingleAsync(u => u.Id == user.Id);
        Assert.False(reloaded.MustChangePassword);
        Assert.True(reloaded.PasswordLastChangedAt > oldPasswordChangedAt);
        Assert.Equal(resultPasswordChangedAt.Ticks, reloaded.PasswordLastChangedAt.Ticks);
        Assert.Null(await service.ReadSessionAsync(oldSession, DateTimeOffset.UtcNow.AddMinutes(1)));

        var oldLogin = await service.LoginAsync("owner@example.ie", "Correct Horse Battery Staple 1!");
        var newLogin = await service.LoginAsync("owner@example.ie", "New Correct Horse Battery 2!");
        Assert.False(oldLogin.Succeeded);
        Assert.True(newLogin.Succeeded);
    }

    [Fact]
    public async Task AuthService_LoginReturnsClientCompanyAssignments()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        var assignedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Assigned Client Limited");
        var otherCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Other Client Limited");
        var user = await SeedUserAsync(
            db,
            tenant,
            "client@example.ie",
            "Correct Horse Battery Staple 1!",
            role: "Client");
        db.UserCompanyAccesses.Add(new UserCompanyAccess
        {
            UserId = user.Id,
            CompanyId = assignedCompany.Id
        });
        await db.SaveChangesAsync();
        var service = CreateAuthService(db);

        var result = await service.LoginAsync("client@example.ie", "Correct Horse Battery Staple 1!");

        Assert.True(result.Succeeded);
        Assert.NotNull(result.User);
        Assert.Equal([assignedCompany.Id], result.User.AllowedCompanyIds.Order().ToArray());
        Assert.DoesNotContain(otherCompany.Id, result.User.AllowedCompanyIds);
    }

    [Fact]
    public async Task AuthService_LoginRejectsWrongPassword()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!");
        var service = CreateAuthService(db);

        var result = await service.LoginAsync("owner@example.ie", "wrong password");

        Assert.False(result.Succeeded);
        Assert.Null(result.User);
        Assert.Contains("Invalid email or password", result.FailureReason);
    }

    [Fact]
    public async Task AuthService_LoginRejectsInactiveUser()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!", isActive: false);
        var service = CreateAuthService(db);

        var result = await service.LoginAsync("owner@example.ie", "Correct Horse Battery Staple 1!");

        Assert.False(result.Succeeded);
        Assert.Null(result.User);
        Assert.Contains("inactive", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthService_LoginRejectsInactiveUserWithWrongPasswordAsInvalidCredentials()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!", isActive: false);
        var service = CreateAuthService(db);

        var result = await service.LoginAsync("owner@example.ie", "wrong password");

        Assert.False(result.Succeeded);
        Assert.Null(result.User);
        Assert.Equal("Invalid email or password.", result.FailureReason);
    }

    [Fact]
    public async Task AuthService_LoginRejectsMissingCredentialsWithFailureReason()
    {
        await using var db = CreateDbContext();
        var service = CreateAuthService(db);

        var result = await service.LoginAsync(" ", null);

        Assert.False(result.Succeeded);
        Assert.Null(result.User);
        Assert.Equal("Email and password are required.", result.FailureReason);
    }

    [Fact]
    public async Task AuthService_LoginRejectsUnsupportedPasswordAlgorithmAsInvalidCredentials()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        await SeedUserAsync(
            db,
            tenant,
            "owner@example.ie",
            "Correct Horse Battery Staple 1!",
            passwordAlgorithm: "PBKDF2-SHA1-1000");
        var service = CreateAuthService(db);

        var result = await service.LoginAsync("owner@example.ie", "Correct Horse Battery Staple 1!");

        Assert.False(result.Succeeded);
        Assert.Null(result.User);
        Assert.Equal("Invalid email or password.", result.FailureReason);
    }

    [Theory]
    [InlineData("missing@example.ie")]
    [InlineData("unsupported@example.ie")]
    [InlineData("malformed@example.ie")]
    public async Task AuthService_LoginRunsPasswordVerificationForInvalidCredentialShapes(string email)
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        await SeedUserAsync(
            db,
            tenant,
            "unsupported@example.ie",
            "Correct Horse Battery Staple 1!",
            passwordAlgorithm: "PBKDF2-SHA1-1000");
        var malformed = await SeedUserAsync(db, tenant, "malformed@example.ie", "Correct Horse Battery Staple 1!");
        malformed.PasswordSalt = "not base64";
        malformed.PasswordHash = "not base64";
        await db.SaveChangesAsync();
        var verifier = new CountingPasswordVerifier();
        var service = CreateAuthService(db, passwordVerifier: verifier);

        var result = await service.LoginAsync(email, "wrong password");

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid email or password.", result.FailureReason);
        Assert.Equal(1, verifier.CallCount);
    }

    [Fact]
    public async Task AuthService_SessionRoundTripReturnsActiveUser()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        var user = await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!");
        var service = CreateAuthService(db);
        var login = await service.LoginAsync("owner@example.ie", "Correct Horse Battery Staple 1!");
        var now = DateTimeOffset.UtcNow;

        var cookieValue = service.CreateSessionCookieValue(login.User!, now);
        var roundTripped = await service.ReadSessionAsync(cookieValue, now.AddMinutes(10));

        Assert.NotNull(roundTripped);
        Assert.Equal(user.Id, roundTripped.UserId);
        Assert.Equal(tenant.Id, roundTripped.TenantId);
        Assert.Equal("Example Firm", roundTripped.TenantName);
        Assert.Equal("owner@example.ie", roundTripped.Email);
        Assert.Equal("Owner User", roundTripped.DisplayName);
        Assert.Equal("Owner", roundTripped.Role);
    }

    [Fact]
    public async Task AuthService_LogoutRevokesExistingSessionCookie()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!");
        var service = CreateAuthService(db);
        var login = await service.LoginAsync("owner@example.ie", "Correct Horse Battery Staple 1!");
        var now = DateTimeOffset.UtcNow;
        var sessionCookie = service.CreateSessionCookieValue(login.User!, now);

        Assert.NotNull(await service.ReadSessionAsync(sessionCookie, now.AddMinutes(1)));

        await service.RevokeSessionAsync(login.User!.UserId);

        Assert.Null(await service.ReadSessionAsync(sessionCookie, now.AddMinutes(2)));
        var relogin = await service.LoginAsync("owner@example.ie", "Correct Horse Battery Staple 1!");
        var newSessionCookie = service.CreateSessionCookieValue(relogin.User!, now.AddMinutes(3));
        Assert.NotNull(await service.ReadSessionAsync(newSessionCookie, now.AddMinutes(4)));
    }

    [Fact]
    public async Task AuthService_LocksRepeatedPasswordFailuresForSameAccount()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        var user = await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!");
        var firstAttemptTime = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var service = CreateAuthService(
            db,
            timeProvider: new FixedUtcTimeProvider(firstAttemptTime));

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await service.LoginAsync("owner@example.ie", $"wrong password {attempt}");
            Assert.False(failed.Succeeded);
        }

        db.ChangeTracker.Clear();
        var locked = await db.UserAccounts.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(5, locked.FailedLoginCount);
        Assert.NotNull(locked.LastFailedLoginAt);
        Assert.True(locked.LockedUntilUtc > firstAttemptTime.UtcDateTime);

        var lockedOut = await service.LoginAsync("owner@example.ie", "Correct Horse Battery Staple 1!");
        Assert.False(lockedOut.Succeeded);

        db.ChangeTracker.Clear();
        var afterLockout = CreateAuthService(
            db,
            timeProvider: new FixedUtcTimeProvider(firstAttemptTime.AddMinutes(16)));
        var successful = await afterLockout.LoginAsync("owner@example.ie", "Correct Horse Battery Staple 1!");

        Assert.True(successful.Succeeded);
        db.ChangeTracker.Clear();
        var reset = await db.UserAccounts.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(0, reset.FailedLoginCount);
        Assert.Null(reset.LockedUntilUtc);
        Assert.Null(reset.LastFailedLoginAt);
    }

    [Fact]
    public async Task AuthService_ReportsLockoutStartedOnlyWhenFailureCrossesThreshold()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        var user = await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!");
        var now = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
        user.FailedLoginCount = 5;
        user.LastFailedLoginAt = now.UtcDateTime;
        user.LockedUntilUtc = null;
        await db.SaveChangesAsync();
        var service = CreateAuthService(
            db,
            timeProvider: new FixedUtcTimeProvider(now.AddMinutes(1)));

        var failed = await service.LoginAsync("owner@example.ie", "wrong password");

        Assert.False(failed.Succeeded);
        Assert.True(failed.AccountLocked);
        Assert.False(failed.LockoutStarted);
        Assert.Equal(6, failed.FailedLoginCount);
    }

    [Fact]
    public async Task AuthService_Base64UrlSigningKeyRoundTripsSessionCookie()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        var user = await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!");
        var service = CreateAuthService(db, StrongSessionSigningKeyBase64Url());
        var login = await service.LoginAsync("owner@example.ie", "Correct Horse Battery Staple 1!");
        var now = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

        var cookieValue = service.CreateSessionCookieValue(login.User!, now);
        var roundTripped = await service.ReadSessionAsync(cookieValue, now.AddMinutes(10));

        Assert.NotNull(roundTripped);
        Assert.Equal(user.Id, roundTripped.UserId);
        Assert.Equal(tenant.Id, roundTripped.TenantId);
    }

    [Fact]
    public void AuthService_CreateCookieOptionsClampsExpiryAndSecuresProductionCookies()
    {
        using var db = CreateDbContext();
        var now = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var lowExpiry = CreateAuthService(db, expiryMinutes: 5);
        var highExpiry = CreateAuthService(db, expiryMinutes: 2_000, environmentName: "Production", secureCookiesInProduction: false);

        var lowOptions = lowExpiry.CreateCookieOptions(now);
        var highOptions = highExpiry.CreateCookieOptions(now);
        var clearOptions = highExpiry.ClearCookieOptions();

        Assert.True(lowOptions.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, lowOptions.SameSite);
        Assert.Equal("/", lowOptions.Path);
        Assert.Equal(now.AddMinutes(15), lowOptions.Expires);
        Assert.False(lowOptions.Secure);
        Assert.Equal(now.AddMinutes(1_440), highOptions.Expires);
        Assert.True(highOptions.Secure);
        Assert.True(clearOptions.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, clearOptions.SameSite);
        Assert.Equal("/", clearOptions.Path);
        Assert.True(clearOptions.Secure);
        Assert.True(clearOptions.Expires < DateTimeOffset.UtcNow);
    }

    [Fact]
    public void AuthService_CookieOptionsAreSecureOutsideDevelopment()
    {
        using var db = CreateDbContext();
        var now = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var development = CreateAuthService(db, environmentName: "Development");
        var staging = CreateAuthService(db, environmentName: "Staging");

        Assert.False(development.CreateCookieOptions(now).Secure);
        Assert.False(development.CreateCsrfCookieOptions(now).Secure);
        Assert.False(development.ClearCookieOptions().Secure);
        Assert.False(development.ClearCsrfCookieOptions().Secure);
        Assert.True(staging.CreateCookieOptions(now).Secure);
        Assert.True(staging.CreateCsrfCookieOptions(now).Secure);
        Assert.True(staging.ClearCookieOptions().Secure);
        Assert.True(staging.ClearCsrfCookieOptions().Secure);
    }

    [Fact]
    public async Task AuthService_SessionPayloadHonoursConfiguredIdleTimeout()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!");
        var service = CreateAuthService(db, expiryMinutes: 5);
        var login = await service.LoginAsync("owner@example.ie", "Correct Horse Battery Staple 1!");
        var now = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

        var cookieValue = service.CreateSessionCookieValue(login.User!, now);
        var stillValidBeforeIdleExpiry = await service.ReadSessionAsync(cookieValue, now.AddMinutes(29));
        var expiredAfterIdleExpiry = await service.ReadSessionAsync(cookieValue, now.AddMinutes(31));

        Assert.NotNull(stillValidBeforeIdleExpiry);
        Assert.Null(expiredAfterIdleExpiry);
    }

    [Fact]
    public async Task AuthService_SessionCarriesCsrfTokenForUnsafeRequestChecks()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!");
        var service = CreateAuthService(db);
        var login = await service.LoginAsync("owner@example.ie", "Correct Horse Battery Staple 1!");
        var now = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var principal = login.User! with { CsrfToken = "csrf-token-1" };

        var cookieValue = service.CreateSessionCookieValue(principal, now);
        var readPrincipal = await service.ReadSessionAsync(cookieValue, now.AddMinutes(1));

        Assert.NotNull(readPrincipal);
        Assert.Equal("csrf-token-1", readPrincipal!.CsrfToken);
    }

    [Fact]
    public async Task AuthService_CsrfCookieIsReadableAndUsesProductionCookieSecurity()
    {
        await using var db = CreateDbContext();
        var service = CreateAuthService(db, environmentName: "Production");
        var now = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

        var cookieOptions = service.CreateCsrfCookieOptions(now);
        var clearOptions = service.ClearCsrfCookieOptions();

        Assert.Equal("accounts_csrf", service.CsrfCookieName);
        Assert.False(cookieOptions.HttpOnly);
        Assert.True(cookieOptions.Secure);
        Assert.Equal(SameSiteMode.Lax, cookieOptions.SameSite);
        Assert.Equal("/", cookieOptions.Path);
        Assert.Equal(now.AddMinutes(60), cookieOptions.Expires);
        Assert.False(clearOptions.HttpOnly);
        Assert.True(clearOptions.Secure);
        Assert.True(clearOptions.Expires < DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task UserSessionMiddleware_BlocksProtectedApiWithoutSession()
    {
        await using var db = CreateDbContext();
        var auth = CreateAuthService(db);
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.RequestServices = new ServiceCollection()
            .AddSingleton(auth)
            .BuildServiceProvider();
        context.Request.Path = "/api/companies";
        var middleware = new UserSessionMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task UserSessionMiddleware_AllowsLoginWithoutSession()
    {
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.RequestServices = new ServiceCollection().BuildServiceProvider();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/auth/login";
        var middleware = new UserSessionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task UserSessionMiddleware_AllowsLogoutWithoutSessionSoStaleCookiesCanBeCleared()
    {
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.RequestServices = new ServiceCollection().BuildServiceProvider();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/auth/logout";
        var middleware = new UserSessionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task UserSessionMiddleware_LoadsUserForLogoutWhenSessionIsValid()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        var user = await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!");
        var auth = CreateAuthService(db);
        var login = await auth.LoginAsync("owner@example.ie", "Correct Horse Battery Staple 1!");
        var now = DateTimeOffset.UtcNow;
        var sessionCookie = auth.CreateSessionCookieValue(login.User!, now);
        AuthenticatedUser? observedUser = null;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.RequestServices = new ServiceCollection()
            .AddSingleton(auth)
            .BuildServiceProvider();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/auth/logout";
        context.Request.Headers.Cookie = $"{auth.CookieName}={sessionCookie}";
        var middleware = new UserSessionMiddleware(innerContext =>
        {
            observedUser = AuthContext.GetUser(innerContext);
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.NotNull(observedUser);
        Assert.Equal(user.Id, observedUser!.UserId);
        Assert.Equal("owner@example.ie", observedUser.Email);
    }

    [Fact]
    public async Task CsrfProtectionMiddleware_BlocksUnsafeApiRequestWithoutMatchingToken()
    {
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/companies/1/periods";
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            7,
            1,
            "Firm A",
            "owner@example.ie",
            "Owner",
            "Owner",
            CsrfToken: "csrf-token-1");
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, Options.Create(new AuthSessionConfig()));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task CsrfProtectionMiddleware_AllowsUnsafeApiRequestWithMatchingHeaderAndCookie()
    {
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = "/api/companies/1";
        context.Request.Headers[CsrfProtectionMiddleware.HeaderName] = "csrf-token-1";
        context.Request.Headers.Cookie = "accounts_csrf=csrf-token-1";
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            7,
            1,
            "Firm A",
            "owner@example.ie",
            "Owner",
            "Owner",
            CsrfToken: "csrf-token-1");
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, Options.Create(new AuthSessionConfig()));

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("GET", "/api/companies")]
    [InlineData("POST", "/api/auth/login")]
    public async Task CsrfProtectionMiddleware_AllowsSafeMethodsAndLoginWithoutToken(string method, string path)
    {
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = method;
        context.Request.Path = path;
        var middleware = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, Options.Create(new AuthSessionConfig()));

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task CsrfRejectedUnsafeCompanyWrite_DoesNotReachCompanyAuditTrail()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/adjustments";
        context.Request.Headers["X-Correlation-ID"] = "csrf-rejected-001";
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            7,
            17,
            "Firm A",
            "reviewer@example.ie",
            "Maeve Reviewer",
            "Reviewer",
            CsrfToken: "csrf-token-1");
        var audit = new AuditTrailMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var csrf = new CsrfProtectionMiddleware(innerContext => audit.InvokeAsync(innerContext, db));

        await csrf.InvokeAsync(context, Options.Create(new AuthSessionConfig()));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task UserSessionMiddleware_AllowsLoginWithoutResolvingAuthService()
    {
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider(),
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/auth/login";
        var middleware = new UserSessionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task UserSessionMiddleware_ExceptionMiddlewareMasksAuthResolutionFailures()
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton<IHostEnvironment>(new TestEnvironment("Development"))
                .BuildServiceProvider(),
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Path = "/api/companies";
        var session = new UserSessionMiddleware(_ => Task.CompletedTask);
        var exception = new ExceptionMiddleware(
            innerContext => session.InvokeAsync(innerContext),
            NullLogger<ExceptionMiddleware>.Instance);

        await exception.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);
    }

    [Fact]
    public async Task BankingWriteEndpoints_EnforceDirectRequestAuthorizationBeforeMutation()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var category = AddCategory(db, period.CompanyId, "4007", "Sales", AccountCategoryType.Income);
        var bank = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        var transaction = new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = period.Id,
            Date = period.PeriodStart.AddDays(1),
            Description = "Receipt",
            Amount = 250m
        };
        db.ImportedTransactions.Add(transaction);
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var clientContext = AuthenticatedRequest("Client", HttpMethods.Put, $"/api/companies/{period.CompanyId}/periods/{period.Id}/transactions/{transaction.Id}/categorise");
        clientContext.Items[AuthContext.ItemKey] = AuthenticatedRole("Client") with
        {
            AllowedCompanyIds = new HashSet<int> { period.CompanyId }
        };

        var result = await BankingEndpoints.CategoriseTransactionEndpointAsync(
            period.CompanyId,
            period.Id,
            transaction.Id,
            new CategoriseInput(category.Id),
            db,
            audit,
            clientContext,
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status403Forbidden, ResultStatusCode(result));
        var unchanged = await db.ImportedTransactions.SingleAsync(t => t.Id == transaction.Id);
        Assert.Null(unchanged.CategoryId);
        Assert.False(unchanged.ManualOverride);
        Assert.Empty(await db.AuditLogs.Where(a => a.EntityType == "ImportedTransaction").ToListAsync());
    }

    [Fact]
    public async Task CreateCategory_ValidatesAndIgnoresOverPostedIdentityAndSystemFlag()
    {
        // data-input-validation-breadth: the category create must reject blank/over-long text and a
        // cross-company ParentId, and must ignore a client-supplied Id / IsSystem (over-posting).
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var companyId = period.CompanyId;
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var foreignParent = AddCategory(db, otherPeriod.CompanyId, "9999", "Other company", AccountCategoryType.Asset);
        await db.SaveChangesAsync();

        // Blank code/name is rejected.
        var blank = new AccountCategory { Code = "  ", Name = "", Type = AccountCategoryType.Expense };
        Assert.NotNull(await CategoryInputs.ValidateAndNormalizeAsync(db, companyId, blank));

        // A ParentId from another company is rejected.
        var crossParent = new AccountCategory { Code = "6001", Name = "Sub", Type = AccountCategoryType.Expense, ParentId = foreignParent.Id };
        Assert.NotNull(await CategoryInputs.ValidateAndNormalizeAsync(db, companyId, crossParent));

        // A valid category is normalised: client Id and IsSystem are ignored; CompanyId is forced.
        var input = new AccountCategory { Id = 4242, Code = "  6500 ", Name = "  Office costs ", Type = AccountCategoryType.Expense, IsSystem = true, CompanyId = 99999 };
        Assert.Null(await CategoryInputs.ValidateAndNormalizeAsync(db, companyId, input));
        Assert.Equal(0, input.Id);
        Assert.False(input.IsSystem);
        Assert.Equal(companyId, input.CompanyId);
        Assert.Equal("6500", input.Code);
        Assert.Equal("Office costs", input.Name);
    }

    [Fact]
    public async Task YearEndCreate_IgnoresClientSuppliedIdentityToPreventOverPosting()
    {
        // G3: a create must not let the client choose the primary key (entity over-posting).
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest("Accountant", HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/debtors");

        var created = await YearEndEndpoints.CreateDebtorEndpointAsync(
            period.CompanyId, period.Id,
            new Debtor { Id = 4242, Name = "Customer", Amount = 100m, Type = DebtorType.Trade },
            db, audit, context);

        Assert.Equal(StatusCodes.Status201Created, ResultStatusCode(created));
        var debtor = await db.Debtors.SingleAsync(d => d.PeriodId == period.Id);
        Assert.NotEqual(4242, debtor.Id);
        Assert.Equal(period.Id, debtor.PeriodId);
    }

    [Fact]
    public async Task UserSessionMiddleware_LoadsPrincipalFromValidSession()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        var user = await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!");
        var auth = CreateAuthService(db);
        var login = await auth.LoginAsync("owner@example.ie", "Correct Horse Battery Staple 1!");
        var cookieValue = auth.CreateSessionCookieValue(login.User!, DateTimeOffset.UtcNow);
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.RequestServices = new ServiceCollection()
            .AddSingleton(auth)
            .BuildServiceProvider();
        context.Request.Path = "/api/companies";
        context.Request.Headers.Cookie = $"{auth.CookieName}={cookieValue}";
        var middleware = new UserSessionMiddleware(innerContext =>
        {
            nextCalled = true;
            Assert.Equal(user.Id, AuthContext.RequireUser(innerContext).UserId);
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public void AuthenticatedIdentity_UsesPrincipalForReviewerDisplayName()
    {
        var reviewer = new AuthenticatedUser(7, 1, "Firm A", "reviewer@example.ie", "Maeve Reviewer", "Reviewer");

        Assert.Equal("Maeve Reviewer", AuthenticatedIdentity.ReviewerDisplayName(reviewer));
    }

    [Fact]
    public void AuthenticatedIdentity_UsesOpaquePrincipalKeyForAuditUserId()
    {
        var reviewer = new AuthenticatedUser(7, 1, "Firm A", "Reviewer@Example.IE", "Maeve Reviewer", "Reviewer");

        Assert.Equal("user:7", AuthenticatedIdentity.AuditUserId(reviewer));
    }

    [Fact]
    public void AuthenticatedIdentity_UsesOpaqueFallbackForBlankReviewerDisplayName()
    {
        var reviewer = new AuthenticatedUser(7, 1, "Firm A", " reviewer@example.ie ", "   ", "Reviewer");

        Assert.Equal("User 7", AuthenticatedIdentity.ReviewerDisplayName(reviewer));
    }

    [Fact]
    public void OpeningBalanceIdentity_IgnoresPayloadEnteredByAndUsesAuthenticatedReviewer()
    {
        var reviewer = new AuthenticatedUser(7, 1, "Firm A", "reviewer@example.ie", "Maeve Reviewer", "Reviewer");
        var input = new OpeningBalanceInput(100m, 0m, "Opening cash", "Spoofed payload reviewer", true);
        var balance = new OpeningBalance();
        var now = new DateTime(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);

        YearEndEndpoints.StampOpeningBalanceIdentity(balance, input, reviewer, now);

        Assert.Equal("Maeve Reviewer", balance.EnteredBy);
        Assert.Equal(now, balance.EnteredAt);
        Assert.True(balance.Reviewed);
        Assert.Equal("Maeve Reviewer", balance.ReviewedBy);
        Assert.Equal(now, balance.ReviewedAt);
    }

    [Fact]
    public void ApiAccess_AllowsBrowserAuthEndpointsWithoutServiceKeyOnlyInDevelopment()
    {
        var service = new ApiAccessService(
            Options.Create(new ApiAccessConfig
            {
                Enabled = true,
                Keys =
                [
                    new ApiAccessKeyConfig
                    {
                        Name = "Firm A",
                        KeyHash = ApiAccessService.HashKey("secret-a")
                    }
                ]
            }),
            new TestEnvironment("Development"));

        var authEndpoint = service.Authorize(null, new PathString("/api/auth/login"), HttpMethods.Post);
        var logoutEndpoint = service.Authorize(null, new PathString("/api/auth/logout"), HttpMethods.Post);
        var meEndpoint = service.Authorize(null, new PathString("/api/auth/me"), HttpMethods.Get);
        var ordinaryEndpoint = service.Authorize(null, new PathString("/api/companies"), HttpMethods.Get);

        Assert.True(authEndpoint.IsAllowed);
        Assert.True(logoutEndpoint.IsAllowed);
        Assert.True(meEndpoint.IsAllowed);
        Assert.False(ordinaryEndpoint.IsAllowed);
    }

    [Fact]
    public void ApiAccess_RequiresServiceKeyForBrowserAuthEndpointsOutsideDevelopment()
    {
        var service = new ApiAccessService(
            Options.Create(new ApiAccessConfig
            {
                Enabled = true,
                Keys =
                [
                    new ApiAccessKeyConfig
                    {
                        Name = "Frontend proxy",
                        Role = "Admin",
                        KeyHash = ApiAccessService.HashKey("secret-a")
                    }
                ]
            }),
            new TestEnvironment("Production"));

        var loginWithoutKey = service.Authorize(null, new PathString("/api/auth/login"), HttpMethods.Post);
        var logoutWithoutKey = service.Authorize(null, new PathString("/api/auth/logout"), HttpMethods.Post);
        var meWithoutKey = service.Authorize(null, new PathString("/api/auth/me"), HttpMethods.Get);
        var loginWithKey = service.Authorize("secret-a", new PathString("/api/auth/login"), HttpMethods.Post);

        Assert.False(loginWithoutKey.IsAllowed);
        Assert.False(logoutWithoutKey.IsAllowed);
        Assert.False(meWithoutKey.IsAllowed);
        Assert.Contains("Missing API access key", loginWithoutKey.DenialReason);
        Assert.True(loginWithKey.IsAllowed);
    }

    [Fact]
    public void ApiAccess_DeniesUnknownAuthEndpointWithoutServiceKey()
    {
        var service = new ApiAccessService(
            Options.Create(new ApiAccessConfig
            {
                Enabled = true,
                Keys =
                [
                    new ApiAccessKeyConfig
                    {
                        Name = "Firm A",
                        KeyHash = ApiAccessService.HashKey("secret-a")
                    }
                ]
            }),
            new TestEnvironment("Production"));

        var decision = service.Authorize(null, new PathString("/api/auth/anything-else"), HttpMethods.Get);

        Assert.False(decision.IsAllowed);
        Assert.Contains("Missing API access key", decision.DenialReason);
    }

    [Fact]
    public async Task AuthEndpoints_LoginRequiresNamedRateLimit()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<MfaSecurityService>();
        builder.Services.AddScoped<IdentityAccessService>();
        builder.Services.AddScoped<AuditService>();
        builder.Services.AddScoped<PrivacyGovernanceService>();
        builder.Services.AddScoped<UserLifecycleService>();
        await using var app = builder.Build();

        app.MapAuthEndpoints();

        var loginEndpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == "/api/auth/login");
        var metadata = loginEndpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>();

        Assert.NotNull(metadata);
        Assert.Equal(AuthEndpoints.LoginRateLimitPolicy, metadata.PolicyName);
    }

    [Fact]
    public void AuthEndpointIntegrationTests_BindEphemeralPortWithoutReservation()
    {
        var source = AccountsWorkflowTestSource();

        foreach (var methodName in new[]
        {
            nameof(AuthEndpoints_RecordLoginPasswordAndLogoutAuditEventsWithoutSecrets),
            nameof(AuthEndpoints_RecordAccountLockoutAuditEventWithoutSecrets),
            nameof(AuthEndpoints_AnonymousLogoutClearsSessionCookies)
        })
        {
            var snippet = TestMethodSnippet(source, methodName);
            Assert.Contains("UseUrls(\"http://127.0.0.1:0\")", snippet);
            Assert.Contains("LoopbackBaseAddress(app)", snippet);
            AssertOccursBefore(snippet, "await app.StartAsync();", "LoopbackBaseAddress(app)");
            Assert.DoesNotContain("Get" + "FreeLoopbackPort", snippet);
            Assert.DoesNotContain("Tcp" + "Listener", snippet);
        }
    }

    [Fact]
    public async Task AuthEndpoints_RecordLoginPasswordAndLogoutAuditEventsWithoutSecrets()
    {
        var databaseName = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<AccountsDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.Configure<AuthSessionConfig>(config =>
        {
            config.SigningKey = StrongSessionSigningKey();
            config.ExpiryMinutes = 60;
            config.RequirePrivilegedMfa = false;
        });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IPasswordVerifier, Pbkdf2PasswordVerifier>();
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<MfaSecurityService>();
        builder.Services.AddScoped<IdentityAccessService>();
        builder.Services.AddScoped<AuditService>();
        builder.Services.AddScoped<PrivacyGovernanceService>();
        builder.Services.AddScoped<UserLifecycleService>();

        await using var app = builder.Build();
        int tenantId;
        int userId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var tenant = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
            var user = await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!");
            tenantId = tenant.Id;
            userId = user.Id;
        }

        app.UseMiddleware<UserSessionMiddleware>();
        app.UseMiddleware<CsrfProtectionMiddleware>();
        app.MapAuthEndpoints();
        await app.StartAsync();

        try
        {
            var baseUri = new Uri(LoopbackBaseAddress(app));
            using var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true
            };
            using var client = new HttpClient(handler) { BaseAddress = baseUri };

            var failedLogin = await client.PostAsJsonAsync(
                "/api/auth/login",
                new { email = " OWNER@EXAMPLE.IE ", password = "wrong password one!" });
            Assert.Equal(HttpStatusCode.Unauthorized, failedLogin.StatusCode);

            var login = await client.PostAsJsonAsync(
                "/api/auth/login",
                new { email = "owner@example.ie", password = "Correct Horse Battery Staple 1!" });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);

            var csrfToken = RequiredCookieValue(handler.CookieContainer, baseUri, "accounts_csrf");
            using var badPasswordChange = new HttpRequestMessage(HttpMethod.Post, "/api/auth/password")
            {
                Content = JsonContent.Create(new
                {
                    currentPassword = "Wrong Current Password!",
                    newPassword = "New Correct Horse Battery 2!"
                })
            };
            badPasswordChange.Headers.Add(CsrfProtectionMiddleware.HeaderName, csrfToken);
            var rejectedPasswordChange = await client.SendAsync(badPasswordChange);
            Assert.Equal(HttpStatusCode.BadRequest, rejectedPasswordChange.StatusCode);

            csrfToken = RequiredCookieValue(handler.CookieContainer, baseUri, "accounts_csrf");
            using var goodPasswordChange = new HttpRequestMessage(HttpMethod.Post, "/api/auth/password")
            {
                Content = JsonContent.Create(new
                {
                    currentPassword = "Correct Horse Battery Staple 1!",
                    newPassword = "New Correct Horse Battery 2!"
                })
            };
            goodPasswordChange.Headers.Add(CsrfProtectionMiddleware.HeaderName, csrfToken);
            var acceptedPasswordChange = await client.SendAsync(goodPasswordChange);
            Assert.Equal(HttpStatusCode.OK, acceptedPasswordChange.StatusCode);

            csrfToken = RequiredCookieValue(handler.CookieContainer, baseUri, "accounts_csrf");
            using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
            logoutRequest.Headers.Add(CsrfProtectionMiddleware.HeaderName, csrfToken);
            var logout = await client.SendAsync(logoutRequest);
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

            await using var verifyScope = app.Services.CreateAsyncScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var audits = await db.AuditLogs
                .Where(a => a.TenantId == tenantId)
                .OrderBy(a => a.Id)
                .ToListAsync();

            var failedLoginAudit = Assert.Single(audits, a => a.Action == "AuthLoginFailed");
            var successfulLoginAudit = Assert.Single(audits, a => a.Action == "AuthLoginSucceeded");
            var rejectedPasswordAudit = Assert.Single(audits, a => a.Action == "AuthPasswordChangeFailed");
            var changedPasswordAudit = Assert.Single(audits, a => a.Action == "AuthPasswordChanged");
            var logoutAudit = Assert.Single(audits, a => a.Action == "AuthLogoutSucceeded");

            foreach (var entry in new[]
            {
                failedLoginAudit,
                successfulLoginAudit,
                rejectedPasswordAudit,
                changedPasswordAudit,
                logoutAudit
            })
            {
                Assert.Null(entry.CompanyId);
                Assert.Null(entry.PeriodId);
                Assert.Equal(userId, entry.EntityId);
                Assert.Equal(tenantId, entry.TenantId);
                Assert.Equal($"user:{userId}", entry.UserId);
                Assert.True(IsHex64(RequiredAuditLogString(entry, "IntegrityHash")));
                var payload = string.Concat(entry.OldValueJson, entry.NewValueJson);
                Assert.DoesNotContain("wrong password one!", payload);
                Assert.DoesNotContain("Wrong Current Password!", payload);
                Assert.DoesNotContain("Correct Horse Battery Staple 1!", payload);
                Assert.DoesNotContain("New Correct Horse Battery 2!", payload);
                Assert.DoesNotContain("accounts_session", payload, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("accounts_csrf", payload, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Equal("AuthSession", failedLoginAudit.EntityType);
            Assert.Equal("AuthSession", successfulLoginAudit.EntityType);
            Assert.Equal("UserAccount", rejectedPasswordAudit.EntityType);
            Assert.Equal("UserAccount", changedPasswordAudit.EntityType);
            Assert.Equal("AuthSession", logoutAudit.EntityType);
            Assert.Null(audits[0].PreviousIntegrityHash);
            Assert.Equal(audits[0].IntegrityHash, audits[1].PreviousIntegrityHash);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task AuthEndpoints_RecordAccountLockoutAuditEventWithoutSecrets()
    {
        var databaseName = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<AccountsDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.Configure<AuthSessionConfig>(config =>
        {
            config.SigningKey = StrongSessionSigningKey();
            config.RequirePrivilegedMfa = false;
        });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IPasswordVerifier, Pbkdf2PasswordVerifier>();
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<MfaSecurityService>();
        builder.Services.AddScoped<IdentityAccessService>();
        builder.Services.AddScoped<AuditService>();
        builder.Services.AddScoped<PrivacyGovernanceService>();
        builder.Services.AddScoped<UserLifecycleService>();

        await using var app = builder.Build();
        int tenantId;
        int userId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var tenant = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
            var user = await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!");
            tenantId = tenant.Id;
            userId = user.Id;
        }

        app.UseMiddleware<UserSessionMiddleware>();
        app.UseMiddleware<CsrfProtectionMiddleware>();
        app.MapAuthEndpoints();
        await app.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(LoopbackBaseAddress(app)) };

            for (var attempt = 1; attempt <= 5; attempt++)
            {
                var response = await client.PostAsJsonAsync(
                    "/api/auth/login",
                    new { email = "owner@example.ie", password = $"wrong password {attempt}!" });
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
            var alreadyLocked = await client.PostAsJsonAsync(
                "/api/auth/login",
                new { email = "owner@example.ie", password = "Correct Horse Battery Staple 1!" });
            Assert.Equal(HttpStatusCode.Unauthorized, alreadyLocked.StatusCode);

            await using var verifyScope = app.Services.CreateAsyncScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var audits = await db.AuditLogs
                .Where(a => a.TenantId == tenantId)
                .OrderBy(a => a.Id)
                .ToListAsync();
            var failedLoginAudits = audits.Where(a => a.Action == "AuthLoginFailed").ToList();
            var lockoutAudit = Assert.Single(audits, a => a.Action == "AuthAccountLocked");

            Assert.Equal(6, failedLoginAudits.Count);
            Assert.Equal("UserAccount", lockoutAudit.EntityType);
            Assert.Equal(userId, lockoutAudit.EntityId);
            Assert.Equal(tenantId, lockoutAudit.TenantId);
            Assert.Equal($"user:{userId}", lockoutAudit.UserId);
            Assert.True(IsHex64(RequiredAuditLogString(lockoutAudit, "IntegrityHash")));
            Assert.Contains("\"FailedLoginCount\":5", lockoutAudit.NewValueJson);
            Assert.DoesNotContain("wrong password", string.Concat(lockoutAudit.OldValueJson, lockoutAudit.NewValueJson), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task AuthEndpoints_AnonymousLogoutClearsSessionCookies()
    {
        var databaseName = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<AccountsDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.Configure<AuthSessionConfig>(config =>
        {
            config.SigningKey = StrongSessionSigningKey();
            config.RequirePrivilegedMfa = false;
        });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IPasswordVerifier, Pbkdf2PasswordVerifier>();
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<MfaSecurityService>();
        builder.Services.AddScoped<IdentityAccessService>();
        builder.Services.AddScoped<AuditService>();
        builder.Services.AddScoped<PrivacyGovernanceService>();
        builder.Services.AddScoped<UserLifecycleService>();

        await using var app = builder.Build();
        app.UseMiddleware<UserSessionMiddleware>();
        app.UseMiddleware<CsrfProtectionMiddleware>();
        app.MapAuthEndpoints();
        await app.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(LoopbackBaseAddress(app)) };

            var logout = await client.PostAsync("/api/auth/logout", content: null);

            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
            var setCookies = logout.Headers.TryGetValues("Set-Cookie", out var values)
                ? values.ToArray()
                : [];
            Assert.Contains(setCookies, value => value.StartsWith("accounts_session=", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(setCookies, value => value.StartsWith("accounts_csrf=", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(setCookies, value => value.Contains("expires=", StringComparison.OrdinalIgnoreCase));

            await using var verifyScope = app.Services.CreateAsyncScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            Assert.Empty(await db.AuditLogs.Where(a => a.Action == AuditEventCodes.AuthLogoutSucceeded).ToListAsync());
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public void EndpointRequestAuthorization_EnforcesApiAndRolePoliciesForDirectWrites()
    {
        var disabledApiAccess = new ApiAccessService(
            Options.Create(new ApiAccessConfig { Enabled = false }),
            new TestEnvironment("Development"));
        var clientContext = AuthenticatedRequest("Client", HttpMethods.Post, "/api/companies/1/periods");
        var ownerContext = AuthenticatedRequest("Owner", HttpMethods.Post, "/api/companies/1/periods");
        var enabledApiAccess = new ApiAccessService(
            Options.Create(new ApiAccessConfig
            {
                Enabled = true,
                Keys =
                [
                    new ApiAccessKeyConfig
                    {
                        Name = "Read only",
                        Role = "Reader",
                        DevelopmentKey = "reader",
                        AllowedCompanyIds = [10]
                    },
                    new ApiAccessKeyConfig
                    {
                        Name = "Writer",
                        Role = "Writer",
                        DevelopmentKey = "writer",
                        AllowedCompanyIds = [10]
                    }
                ]
            }),
            new TestEnvironment("Development"));
        var readerContext = AuthenticatedRequest("Owner", HttpMethods.Post, "/api/companies/10/periods");
        readerContext.Request.Headers["X-Accounts-Api-Key"] = "reader";
        var writerContext = AuthenticatedRequest("Owner", HttpMethods.Post, "/api/companies/10/periods");
        writerContext.Request.Headers["X-Accounts-Api-Key"] = "writer";

        var clientDenied = EndpointRequestAuthorization.AuthorizeCurrentRequest(clientContext, disabledApiAccess);
        var ownerAllowed = EndpointRequestAuthorization.AuthorizeCurrentRequest(ownerContext, disabledApiAccess);
        var readerDenied = EndpointRequestAuthorization.AuthorizeCurrentRequest(readerContext, enabledApiAccess);
        var writerAllowed = EndpointRequestAuthorization.AuthorizeCurrentRequest(writerContext, enabledApiAccess);

        Assert.NotNull(clientDenied);
        Assert.Equal(StatusCodes.Status403Forbidden, ResultStatusCode(clientDenied));
        Assert.Null(ownerAllowed);
        Assert.NotNull(readerDenied);
        Assert.Equal(StatusCodes.Status401Unauthorized, ResultStatusCode(readerDenied));
        Assert.Null(writerAllowed);
        Assert.Equal([10], Assert.IsAssignableFrom<IReadOnlyCollection<int>>(writerContext.Items["ApiAllowedCompanyIds"]));
    }

    [Theory]
    [InlineData("Accountant", "POST", "/api/companies", false)]
    [InlineData("Client", "POST", "/api/companies/1/periods", false)]
    [InlineData("Client", "POST", "/api/companies/1/periods/2/documents/cro-filing-pack", false)]
    [InlineData("Accountant", "POST", "/api/companies/1/periods", true)]
    [InlineData("Accountant", "POST", "/api/companies/1/periods/2/documents/cro-filing-pack", true)]
    [InlineData("Reviewer", "POST", "/api/companies/1/periods/2/adjustments/3/approve", true)]
    [InlineData("Reviewer", "PUT", "/api/companies/1/periods/2/filing/charity-status", true)]
    [InlineData("Accountant", "PUT", "/api/companies/1/periods/2/filing/charity-status", false)]
    [InlineData("Reviewer", "POST", "/api/companies/1/periods/2/documents/cro-filing-pack", false)]
    [InlineData("Reviewer", "POST", "/api/companies", false)]
    [InlineData("Owner", "DELETE", "/api/companies/1", true)]
    public void RoleAuthorization_EnforcesExpectedWritePermissions(string role, string method, string path, bool expected)
    {
        var decision = RoleAuthorizationService.Authorize(
            AuthenticatedRole(role),
            new PathString(path),
            method);

        Assert.Equal(expected, decision.IsAllowed);
    }

    [Theory]
    [InlineData("Owner", true)]
    [InlineData("Accountant", false)]
    [InlineData("Reviewer", false)]
    [InlineData("Client", false)]
    public void RoleAuthorization_AllowsCompanyCreationForOwnersOnly(string role, bool expected)
    {
        var decision = RoleAuthorizationService.Authorize(
            AuthenticatedRole(role),
            new PathString("/api/companies"),
            HttpMethods.Post);

        Assert.Equal(expected, decision.IsAllowed);
    }

    [Fact]
    public void RoleAuthorization_MustChangePasswordUserCanOnlyUsePasswordChangeSessionEndpoints()
    {
        var user = AuthenticatedRole("Owner");
        var flag = typeof(AuthenticatedUser).GetProperty("MustChangePassword");
        Assert.True(flag is not null, "AuthenticatedUser should expose MustChangePassword for authorization enforcement.");
        flag.SetValue(user, true);

        Assert.False(RoleAuthorizationService.Authorize(user, new PathString("/api/companies"), HttpMethods.Get).IsAllowed);
        Assert.False(RoleAuthorizationService.Authorize(user, new PathString("/api/companies/1/periods"), HttpMethods.Post).IsAllowed);
        Assert.True(RoleAuthorizationService.Authorize(user, new PathString("/api/auth/me"), HttpMethods.Get).IsAllowed);
        Assert.True(RoleAuthorizationService.Authorize(user, new PathString("/api/auth/logout"), HttpMethods.Post).IsAllowed);
        Assert.True(RoleAuthorizationService.Authorize(user, new PathString("/api/auth/password"), HttpMethods.Post).IsAllowed);
    }

    [Fact]
    public async Task RoleAuthorizationMiddleware_ReturnsForbiddenForDeniedWrite()
    {
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/companies/1/periods";
        context.Items[AuthContext.ItemKey] = AuthenticatedRole("Client");
        var middleware = new RoleAuthorizationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        var body = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Contains("read-only", body);
    }

    [Theory]
    [InlineData("Client")]
    [InlineData("Reviewer")]
    [InlineData("Accountant")]
    [InlineData("Owner")]
    public async Task RoleAuthorizationMiddleware_AllowsAuthenticatedLogoutForFirmRoles(string role)
    {
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/auth/logout";
        context.Items[AuthContext.ItemKey] = AuthenticatedRole(role);
        var middleware = new RoleAuthorizationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public void RoleAuthorization_AllowsClientRead()
    {
        var decision = RoleAuthorizationService.Authorize(
            AuthenticatedRole("Client"),
            new PathString("/api/companies/1/periods"),
            HttpMethods.Get);

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void RoleAuthorization_DeniesUnknownRoleWrite()
    {
        var decision = RoleAuthorizationService.Authorize(
            AuthenticatedRole("Mystery"),
            new PathString("/api/companies/1/periods"),
            HttpMethods.Post);

        Assert.False(decision.IsAllowed);
        Assert.Contains("not authorised", decision.DenialReason);
    }

    [Fact]
    public void RoleAuthorization_DeniesLegacyAdminHumanSessionWrite()
    {
        var decision = RoleAuthorizationService.Authorize(
            AuthenticatedRole("Admin"),
            new PathString("/api/companies/1/periods"),
            HttpMethods.Post);

        Assert.False(decision.IsAllowed);
        Assert.Contains("not authorised", decision.DenialReason);
    }

    [Fact]
    public void RoleAuthorization_TreatsPatchAsWriteForClient()
    {
        var decision = RoleAuthorizationService.Authorize(
            AuthenticatedRole("Client"),
            new PathString("/api/companies/1/periods/2/debtors/3"),
            HttpMethods.Patch);

        Assert.False(decision.IsAllowed);
        Assert.Contains("read-only", decision.DenialReason);
    }

    [Theory]
    [InlineData("POST", "/api/companies/1/periods/2/adjustments/3/approve")]
    [InlineData("PUT", "/api/companies/1/periods/2/year-end-reviews/debtors")]
    [InlineData("PUT", "/api/companies/1/periods/2/status")]
    [InlineData("PUT", "/api/companies/1/periods/2/filing/cro-status")]
    [InlineData("POST", "/api/companies/1/periods/2/filing/cro-payment")]
    [InlineData("POST", "/api/companies/1/periods/2/mark-filed")]
    [InlineData("DELETE", "/api/companies/1")]
    public void RoleAuthorization_DeniesAccountantReviewerAndOwnerOnlyActions(string method, string path)
    {
        var decision = RoleAuthorizationService.Authorize(
            AuthenticatedRole("Accountant"),
            new PathString(path),
            method);

        Assert.False(decision.IsAllowed);
    }

    [Theory]
    [InlineData("PUT", "/api/companies/1/periods/2/year-end-reviews/debtors")]
    [InlineData("PUT", "/api/companies/1/periods/2/status")]
    [InlineData("PUT", "/api/companies/1/periods/2/filing/cro-status")]
    [InlineData("POST", "/api/companies/1/periods/2/filing/cro-payment")]
    [InlineData("POST", "/api/companies/1/periods/2/mark-filed")]
    public void RoleAuthorization_AllowsReviewerWorkflowWrites(string method, string path)
    {
        var decision = RoleAuthorizationService.Authorize(
            AuthenticatedRole("Reviewer"),
            new PathString(path),
            method);

        Assert.True(decision.IsAllowed);
    }

    [Theory]
    [InlineData("POST", "/api/companies/1/periods/2/debtors")]
    [InlineData("POST", "/api/companies/1/bank-accounts")]
    [InlineData("POST", "/api/companies/1/periods/2/filing/mark-generated")]
    [InlineData("POST", "/api/companies/1/periods/2/filing/validate-ixbrl")]
    [InlineData("POST", "/api/companies/1/periods/2/documents/cro-filing-pack")]
    [InlineData("POST", "/api/companies/1/periods/2/documents/signature-page")]
    public void RoleAuthorization_DeniesReviewerPreparationWrites(string method, string path)
    {
        var decision = RoleAuthorizationService.Authorize(
            AuthenticatedRole("Reviewer"),
            new PathString(path),
            method);

        Assert.False(decision.IsAllowed);
    }

    [Theory]
    [InlineData("POST", "/api/companies/1/approve")]
    [InlineData("PUT", "/api/companies/1/status")]
    public void RoleAuthorization_DeniesReviewerFalsePositiveWorkflowNames(string method, string path)
    {
        var decision = RoleAuthorizationService.Authorize(
            AuthenticatedRole("Reviewer"),
            new PathString(path),
            method);

        Assert.False(decision.IsAllowed);
    }

    [Theory]
    [InlineData("POST", "/api/companies/1/periods/2/debtors")]
    [InlineData("POST", "/api/companies/1/bank-accounts")]
    [InlineData("POST", "/api/companies/1/periods/2/filing/mark-generated")]
    [InlineData("POST", "/api/companies/1/periods/2/filing/validate-ixbrl")]
    [InlineData("POST", "/api/companies/1/periods/2/documents/cro-filing-pack")]
    [InlineData("POST", "/api/companies/1/periods/2/documents/signature-page")]
    [InlineData("POST", "/api/companies/1/approve")]
    [InlineData("PUT", "/api/companies/1/status")]
    public void RoleAuthorization_AllowsAccountantPreparationAndFalsePositiveWrites(string method, string path)
    {
        var decision = RoleAuthorizationService.Authorize(
            AuthenticatedRole("Accountant"),
            new PathString(path),
            method);

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public async Task BootstrapOwnerService_CreatesFirstTenantOwnerAndClaimsOrphanCompanies()
    {
        await using var db = CreateDbContext();
        var company = new Company
        {
            LegalName = "Legacy Company Limited",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2025, 1, 1),
            AnnualReturnDate = new DateOnly(2024, 9, 15),
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var service = new BootstrapOwnerService(
            db,
            Options.Create(new BootstrapOwnerConfig
            {
                Enabled = true,
                TenantName = "Production Firm",
                TenantSlug = "production-firm",
                OwnerEmail = "owner@example.ie",
                OwnerDisplayName = "Owner User",
                OwnerInitialPassword = "Correct Horse Battery Staple 1!"
            }));

        await service.EnsureAsync();

        var tenant = await db.Tenants.SingleAsync(t => t.Slug == "production-firm");
        var owner = await db.UserAccounts.SingleAsync(u => u.Email == "owner@example.ie");
        var reloadedCompany = await db.Companies.SingleAsync(c => c.Id == company.Id);
        Assert.Equal(tenant.Id, owner.TenantId);
        Assert.Equal("Owner", owner.Role);
        Assert.True(owner.MustChangePassword);
        Assert.Equal(tenant.Id, reloadedCompany.TenantId);
    }

    [Fact]
    public async Task BootstrapOwnerService_HonorsConfiguredPasswordChangeGateForLocalOwner()
    {
        await using var db = CreateDbContext();
        var service = new BootstrapOwnerService(
            db,
            Options.Create(new BootstrapOwnerConfig
            {
                Enabled = true,
                TenantName = "Local Charity Workspace",
                TenantSlug = "local-charity",
                OwnerEmail = "admin@accounts.local",
                OwnerDisplayName = "Local Charity Admin",
                OwnerInitialPassword = "LocalAdmin!Accounts-2026-9Qx",
                OwnerMustChangePassword = false
            }));

        await service.EnsureAsync();

        var owner = await db.UserAccounts.SingleAsync(u => u.Email == "admin@accounts.local");
        Assert.False(owner.MustChangePassword);
    }

    [Fact]
    public async Task BootstrapOwnerService_CanRelaxExistingLocalOwnerPasswordChangeGate()
    {
        await using var db = CreateDbContext();
        var tenant = new Tenant
        {
            Name = "Local Charity Workspace",
            Slug = "local-charity"
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var (hash, salt) = PasswordHasher.HashPassword("LocalAdmin!Accounts-2026-9Qx");
        db.UserAccounts.Add(new UserAccount
        {
            TenantId = tenant.Id,
            Email = "admin@accounts.local",
            DisplayName = "Local Charity Admin",
            Role = "Owner",
            PasswordHash = hash,
            PasswordSalt = salt,
            PasswordAlgorithm = AuthService.PasswordAlgorithm,
            PasswordStrengthScore = 5,
            IsActive = true,
            MustChangePassword = true,
            PasswordLastChangedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var service = new BootstrapOwnerService(
            db,
            Options.Create(new BootstrapOwnerConfig
            {
                Enabled = true,
                TenantName = "Local Charity Workspace",
                TenantSlug = "local-charity",
                OwnerEmail = "admin@accounts.local",
                OwnerDisplayName = "Local Charity Admin",
                OwnerInitialPassword = "LocalAdmin!Accounts-2026-9Qx",
                OwnerMustChangePassword = false
            }));

        await service.EnsureAsync();

        var owner = await db.UserAccounts.SingleAsync(u => u.Email == "admin@accounts.local");
        Assert.False(owner.MustChangePassword);
    }

    [Fact]
    public async Task BootstrapOwnerService_RejectsInitialPasswordBelowProductionPolicy()
    {
        await using var db = CreateDbContext();
        var service = new BootstrapOwnerService(
            db,
            Options.Create(new BootstrapOwnerConfig
            {
                Enabled = true,
                TenantName = "Production Firm",
                TenantSlug = "production-firm",
                OwnerEmail = "owner@example.ie",
                OwnerDisplayName = "Owner User",
                OwnerInitialPassword = "ShortStrong1!abcd"
            }));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureAsync());

        Assert.Contains("BootstrapOwner:OwnerInitialPassword", error.Message);
        Assert.Contains("at least 20 characters", error.Message);
        Assert.False(await db.UserAccounts.AnyAsync());
    }

    [Fact]
    public async Task BootstrapOwnerService_RejectsConflictingOwnerEmailBeforeClaimingOrphans()
    {
        await using var db = CreateDbContext();
        var otherTenant = new Tenant
        {
            Name = "Other Firm",
            Slug = "other-firm"
        };
        db.Tenants.Add(otherTenant);
        await db.SaveChangesAsync();
        db.UserAccounts.Add(new UserAccount
        {
            TenantId = otherTenant.Id,
            Email = "owner@example.ie",
            DisplayName = "Other Owner",
            Role = "Owner",
            PasswordHash = "hash",
            PasswordSalt = "salt",
            IsActive = true
        });
        var orphanCompany = new Company
        {
            LegalName = "Legacy Company Limited",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2025, 1, 1),
            AnnualReturnDate = new DateOnly(2024, 9, 15),
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(orphanCompany);
        await db.SaveChangesAsync();
        var service = new BootstrapOwnerService(
            db,
            Options.Create(new BootstrapOwnerConfig
            {
                Enabled = true,
                TenantName = "Production Firm",
                TenantSlug = "production-firm",
                OwnerEmail = "owner@example.ie",
                OwnerDisplayName = "Owner User",
                OwnerInitialPassword = "Correct Horse Battery Staple 1!"
            }));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureAsync());

        Assert.Contains("BootstrapOwner:OwnerEmail", error.Message);
        Assert.Contains("configured tenant", error.Message);
        var reloadedCompany = await db.Companies.SingleAsync(c => c.Id == orphanCompany.Id);
        Assert.Null(reloadedCompany.TenantId);
    }

    [Fact]
    public async Task BootstrapOwnerService_RejectsCaseVariantOwnerEmailBeforeClaimingOrphans()
    {
        await using var db = CreateDbContext();
        var otherTenant = new Tenant
        {
            Name = "Other Firm",
            Slug = "other-firm"
        };
        db.Tenants.Add(otherTenant);
        await db.SaveChangesAsync();
        db.UserAccounts.Add(new UserAccount
        {
            TenantId = otherTenant.Id,
            Email = "Owner@Example.ie",
            DisplayName = "Other Owner",
            Role = "Owner",
            PasswordHash = "hash",
            PasswordSalt = "salt",
            IsActive = true
        });
        var orphanCompany = new Company
        {
            LegalName = "Legacy Company Limited",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2025, 1, 1),
            AnnualReturnDate = new DateOnly(2024, 9, 15),
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(orphanCompany);
        await db.SaveChangesAsync();
        var service = new BootstrapOwnerService(
            db,
            Options.Create(new BootstrapOwnerConfig
            {
                Enabled = true,
                TenantName = "Production Firm",
                TenantSlug = "production-firm",
                OwnerEmail = "owner@example.ie",
                OwnerDisplayName = "Owner User",
                OwnerInitialPassword = "Correct Horse Battery Staple 1!"
            }));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureAsync());

        Assert.Contains("BootstrapOwner:OwnerEmail", error.Message);
        var reloadedCompany = await db.Companies.SingleAsync(c => c.Id == orphanCompany.Id);
        Assert.Null(reloadedCompany.TenantId);
    }

    [Fact]
    public void EndpointInputs_PersistsReopenReasonAndIdentity()
    {
        var period = new AccountingPeriod
        {
            CompanyId = 1,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = true,
            Status = PeriodStatus.Finalised,
            LockedAt = DateTime.UtcNow,
            LockedBy = "Reviewer"
        };
        var user = AuthenticatedRole("Owner") with
        {
            Email = "owner@example.ie",
            DisplayName = "Owner User"
        };
        var now = new DateTime(2026, 6, 6, 14, 0, 0, DateTimeKind.Utc);

        EndpointInputs.ApplyPeriodStatusUpdate(period, new PeriodStatusUpdate(PeriodStatus.Review, null, "Material correction required"), user, now);

        Assert.Null(period.LockedAt);
        Assert.Equal("Material correction required", period.ReopenReason);
        Assert.Equal("Owner User", period.ReopenedBy);
        Assert.Equal(now, period.ReopenedAt);
    }

    [Fact]
    public void ManualAdjustment_PrepareOverwritesCallerSuppliedIdentityAndApprovalState()
    {
        var user = new AuthenticatedUser(7, 1, "Firm A", "reviewer@example.ie", "Maeve Reviewer", "Reviewer");
        var now = new DateTime(2026, 6, 6, 12, 30, 0, DateTimeKind.Utc);
        var forgedApprovedAt = new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var input = new Adjustment
        {
            PeriodId = 999,
            Description = "Manual accrual",
            Amount = 125m,
            Source = AdjustmentSource.Auto,
            CreatedBy = "Forged creator",
            CreatedAt = forgedApprovedAt,
            ApprovedBy = "Forged approver",
            ApprovedAt = forgedApprovedAt,
            IsAuto = true
        };

        AdjustmentInputs.PrepareManualAdjustment(input, periodId: 42, user, now);

        Assert.Equal(42, input.PeriodId);
        Assert.False(input.IsAuto);
        Assert.Equal(AdjustmentSource.Manual, input.Source);
        Assert.Equal("Maeve Reviewer", input.CreatedBy);
        Assert.Equal(now, input.CreatedAt);
        Assert.Null(input.ApprovedBy);
        Assert.Null(input.ApprovedAt);
    }

}
