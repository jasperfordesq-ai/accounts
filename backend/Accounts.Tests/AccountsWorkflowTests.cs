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

public class AccountsWorkflowTests
{
    static AccountsWorkflowTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Fact]
    public async Task SizeClassification_FirstYearMicro_AllowsMicroAndAuditExemption()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            Turnover = 120_000m,
            BalanceSheetTotal = 30_000m,
            AvgEmployees = 2
        });
        await db.SaveChangesAsync();

        var service = new SizeClassificationService(db, Options.Create(new SizeThresholdConfig()));

        var result = await service.ClassifyAsync(period.CompanyId, period.Id);

        Assert.Equal(CompanySizeClass.Micro, result.CalculatedClass);
        Assert.True(result.CanUseMicro);
        Assert.True(result.AuditExempt);
        Assert.Contains("Micro", result.AvailableRegimes[0]);
    }

    [Fact]
    public async Task FilingRegime_MicroClassification_DefaultsToMicroRequirements()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            Turnover = 100_000m,
            BalanceSheetTotal = 20_000m,
            AvgEmployees = 1,
            CalculatedClass = CompanySizeClass.Micro
        });
        await db.SaveChangesAsync();

        var service = new FilingRegimeService(db);

        var result = await service.DetermineAsync(period.CompanyId, period.Id);

        Assert.Equal(ElectedRegime.Micro, result.Regime);
        Assert.True(result.CanUseMicro);
        Assert.Contains(result.RequiredStatements, s => s.Contains("s.280D"));
    }

    [Fact]
    public async Task FilingRegime_RecentRepeatedLateCroFilings_RemoveAuditExemption()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            Turnover = 100_000m,
            BalanceSheetTotal = 20_000m,
            AvgEmployees = 1,
            CalculatedClass = CompanySizeClass.Micro
        });
        var priorPeriod = new AccountingPeriod
        {
            CompanyId = period.CompanyId,
            PeriodStart = new DateOnly(2024, 1, 1),
            PeriodEnd = new DateOnly(2024, 12, 31),
            IsFirstYear = false
        };
        db.AccountingPeriods.Add(priorPeriod);
        await db.SaveChangesAsync();
        db.FilingHistories.AddRange(
            new FilingHistory
            {
                CompanyId = period.CompanyId,
                PeriodId = period.Id,
                DeadlineType = DeadlineType.CRO,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)),
                FiledDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1).AddDays(4)),
                DaysLate = 4,
                PenaltyAmount = 112m
            },
            new FilingHistory
            {
                CompanyId = period.CompanyId,
                PeriodId = priorPeriod.Id,
                DeadlineType = DeadlineType.CRO,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-2)),
                FiledDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-2).AddDays(1)),
                DaysLate = 1,
                PenaltyAmount = 103m
            });
        await db.SaveChangesAsync();

        var service = new FilingRegimeService(db);

        var result = await service.DetermineAsync(period.CompanyId, period.Id);

        Assert.False(result.AuditExempt);
        Assert.Contains("late CRO filings", result.Summary);
        var saved = await db.FilingRegimes.SingleAsync(f => f.PeriodId == period.Id);
        Assert.False(saved.AuditExempt);
    }

    [Fact]
    public async Task SizeClassificationService_RejectsMismatchedCompanyPeriodBeforeMutating()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = otherPeriod.Id,
            Turnover = 120_000m,
            BalanceSheetTotal = 40_000m,
            AvgEmployees = 3
        });
        await db.SaveChangesAsync();
        var before = await db.SizeClassifications
            .AsNoTracking()
            .SingleAsync(sc => sc.PeriodId == otherPeriod.Id);
        var service = new SizeClassificationService(db, Options.Create(new SizeThresholdConfig()));

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.ClassifyAsync(period.CompanyId, otherPeriod.Id));

        var unchanged = await db.SizeClassifications.SingleAsync(sc => sc.PeriodId == otherPeriod.Id);
        Assert.Equal(before.CalculatedClass, unchanged.CalculatedClass);
        Assert.Equal(before.CalculatedAt, unchanged.CalculatedAt);
        Assert.Null(unchanged.QualificationNotes);
    }

    [Fact]
    public async Task FilingRegimeService_RejectsMismatchedCompanyPeriodBeforeMutating()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = otherPeriod.Id,
            Turnover = 100_000m,
            BalanceSheetTotal = 20_000m,
            AvgEmployees = 1,
            CalculatedClass = CompanySizeClass.Micro
        });
        await db.SaveChangesAsync();
        var service = new FilingRegimeService(db);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.DetermineAsync(period.CompanyId, otherPeriod.Id));

        Assert.Empty(await db.FilingRegimes.Where(f => f.PeriodId == otherPeriod.Id).ToListAsync());
    }

    [Fact]
    public void ClassificationServices_RequireCompanyIdForPeriodMutations()
    {
        var classifyMethods = typeof(SizeClassificationService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name == nameof(SizeClassificationService.ClassifyAsync))
            .Select(m => m.GetParameters().Select(p => p.ParameterType).ToArray())
            .ToList();
        var regimeMethods = typeof(FilingRegimeService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name == nameof(FilingRegimeService.DetermineAsync))
            .Select(m => m.GetParameters().Select(p => p.ParameterType).ToArray())
            .ToList();

        Assert.Contains(classifyMethods, parameters =>
            parameters.Length >= 2
            && parameters[0] == typeof(int)
            && parameters[1] == typeof(int));
        Assert.DoesNotContain(classifyMethods, parameters =>
            parameters.Length >= 1
            && parameters[0] == typeof(int)
            && (parameters.Length == 1 || parameters[1] != typeof(int)));

        Assert.Contains(regimeMethods, parameters =>
            parameters.Length >= 2
            && parameters[0] == typeof(int)
            && parameters[1] == typeof(int));
        Assert.DoesNotContain(regimeMethods, parameters =>
            parameters.Length >= 1
            && parameters[0] == typeof(int)
            && (parameters.Length == 1 || parameters[1] != typeof(int)));
    }

    [Fact]
    public void ClassificationEndpoints_UseCompanyAwareServiceCallsAndReads()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "ClassificationEndpoints.cs"));

        Assert.Contains("ClassifyAsync(companyId, periodId", source);
        Assert.Contains("DetermineAsync(companyId, periodId", source);
        Assert.Contains("f.PeriodId == periodId && f.Period.CompanyId == companyId", source);
        Assert.DoesNotContain("ClassifyAsync(periodId", source);
        Assert.DoesNotContain("DetermineAsync(periodId", source);
        Assert.DoesNotContain("FirstOrDefaultAsync(f => f.PeriodId == periodId)", source);
    }

    [Fact]
    public void ClassificationEndpoints_UseDirectPeriodAccessAndRequestAuthorization()
    {
        var source = File
            .ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "ClassificationEndpoints.cs"))
            .Replace("\r\n", "\n");

        var filingRegimeRead = ClassificationEndpointSnippet(source, "group.MapGet(\"/filing-regime\"");
        Assert.Contains("AccountsDbContext db", filingRegimeRead);
        Assert.Contains("HttpContext context", filingRegimeRead);
        Assert.Contains("CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", filingRegimeRead);
        Assert.DoesNotContain("ApiAccessService apiAccess", filingRegimeRead);
        Assert.DoesNotContain("EndpointRequestAuthorization.AuthorizeCurrentRequest", filingRegimeRead);

        foreach (var endpoint in new[]
        {
            ("group.MapPost(\"/classify\"", "service.ClassifyAsync"),
            ("group.MapPost(\"/filing-regime\"", "service.DetermineAsync"),
            ("group.MapPut(\"/member-audit-notice\"", "var period = await db.AccountingPeriods"),
            ("public static async Task<IResult> SaveSizeClassificationEndpointAsync", "var period = await db.AccountingPeriods")
        })
        {
            var snippet = ClassificationEndpointSnippet(source, endpoint.Item1);
            Assert.Contains("AccountsDbContext db", snippet);
            Assert.Contains("ApiAccessService apiAccess", snippet);
            Assert.Contains("HttpContext context", snippet);
            Assert.Contains("CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", snippet);
            Assert.Contains("EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", snippet);
            AssertOccursBefore(snippet, "CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)");
            AssertOccursBefore(snippet, "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", endpoint.Item2);
        }

        static string ClassificationEndpointSnippet(string source, string marker)
        {
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Expected to find classification endpoint marker {marker}.");
            var next = Regex.Match(source[(start + marker.Length)..], @"\n\s*(?:group\.Map(?:Get|Post|Put|Delete)|public static|private static)", RegexOptions.None, TimeSpan.FromSeconds(1));
            return next.Success
                ? source[start..(start + marker.Length + next.Index)]
                : source[start..];
        }

        static void AssertOccursBefore(string snippet, string first, string second)
        {
            var firstIndex = snippet.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = snippet.IndexOf(second, StringComparison.Ordinal);
            Assert.True(firstIndex >= 0, $"Expected snippet to contain '{first}'.");
            Assert.True(secondIndex >= 0, $"Expected snippet to contain '{second}'.");
            Assert.True(firstIndex < secondIndex, $"Expected '{first}' before '{second}'.");
        }
    }

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
    public void DeadlineEndpoints_UseDirectCompanyPeriodAccessAndRequestAuthorization()
    {
        var source = File
            .ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "DeadlineEndpoints.cs"))
            .Replace("\r\n", "\n");

        foreach (var marker in new[]
        {
            "companyGroup.MapGet(\"/deadlines\"",
            "companyGroup.MapGet(\"/deadlines/upcoming\"",
            "companyGroup.MapGet(\"/deadlines/jeopardy\""
        })
        {
            var snippet = DeadlineEndpointSnippet(source, marker);
            Assert.Contains("AccountsDbContext db", snippet);
            Assert.Contains("HttpContext context", snippet);
            Assert.Contains("CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId)", snippet);
            Assert.DoesNotContain("ApiAccessService apiAccess", snippet);
            Assert.DoesNotContain("EndpointRequestAuthorization.AuthorizeCurrentRequest", snippet);
            AssertOccursBefore(snippet, "CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId)", "service.");
        }

        foreach (var marker in new[]
        {
            "periodGroup.MapPost(\"/deadlines/calculate\"",
            "periodGroup.MapPost(\"/mark-filed\""
        })
        {
            var snippet = DeadlineEndpointSnippet(source, marker);
            Assert.Contains("AccountsDbContext db", snippet);
            Assert.Contains("ApiAccessService apiAccess", snippet);
            Assert.Contains("HttpContext context", snippet);
            Assert.Contains("CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", snippet);
            Assert.Contains("EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", snippet);
            AssertOccursBefore(snippet, "CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)");
            AssertOccursBefore(snippet, "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", "service.");
        }

        static string DeadlineEndpointSnippet(string source, string marker)
        {
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Expected to find deadline endpoint marker {marker}.");
            var next = Regex.Match(source[(start + marker.Length)..], @"\n\s*(?:companyGroup|periodGroup)\.Map(?:Get|Post|Put|Delete)", RegexOptions.None, TimeSpan.FromSeconds(1));
            return next.Success
                ? source[start..(start + marker.Length + next.Index)]
                : source[start..];
        }

        static void AssertOccursBefore(string snippet, string first, string second)
        {
            var firstIndex = snippet.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = snippet.IndexOf(second, StringComparison.Ordinal);
            Assert.True(firstIndex >= 0, $"Expected snippet to contain '{first}'.");
            Assert.True(secondIndex >= 0, $"Expected snippet to contain '{second}'.");
            Assert.True(firstIndex < secondIndex, $"Expected '{first}' before '{second}'.");
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

            using var ownerMarkFiled = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/mark-filed",
                role: "Owner",
                apiKey: "writer",
                body: new { deadlineType = DeadlineType.Revenue, filedDate });
            var ownerMarkFiledAllowed = await client.SendAsync(ownerMarkFiled);
            Assert.Equal(HttpStatusCode.OK, ownerMarkFiledAllowed.StatusCode);

            using var reviewerMarkFiled = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/mark-filed",
                role: "Reviewer",
                apiKey: "writer",
                body: new { deadlineType = DeadlineType.CRO, filedDate });
            var markFiledAllowed = await client.SendAsync(reviewerMarkFiled);
            Assert.Equal(HttpStatusCode.OK, markFiledAllowed.StatusCode);

            await using var verifyScope = app.Services.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var allowedCro = await verifyDb.FilingDeadlines.SingleAsync(d =>
                d.CompanyId == allowedCompanyId && d.PeriodId == allowedPeriodId && d.DeadlineType == DeadlineType.CRO);
            var allowedRevenue = await verifyDb.FilingDeadlines.SingleAsync(d =>
                d.CompanyId == allowedCompanyId && d.PeriodId == allowedPeriodId && d.DeadlineType == DeadlineType.Revenue);
            var restrictedCro = await verifyDb.FilingDeadlines.SingleAsync(d =>
                d.CompanyId == restrictedCompanyId && d.PeriodId == restrictedPeriodId && d.DeadlineType == DeadlineType.CRO);
            Assert.Equal(filedDate, allowedCro.FiledDate);
            Assert.Equal(filedDate, allowedRevenue.FiledDate);
            Assert.Null(restrictedCro.FiledDate);
            Assert.DoesNotContain(await verifyDb.FilingDeadlines.ToListAsync(), d =>
                d.CompanyId == restrictedCompanyId && d.PeriodId == restrictedPeriodId && d.DeadlineType == DeadlineType.Revenue);
            Assert.Single(await verifyDb.FilingHistories.Where(h =>
                h.CompanyId == allowedCompanyId && h.PeriodId == allowedPeriodId && h.DeadlineType == DeadlineType.CRO).ToListAsync());
            Assert.Single(await verifyDb.FilingHistories.Where(h =>
                h.CompanyId == allowedCompanyId && h.PeriodId == allowedPeriodId && h.DeadlineType == DeadlineType.Revenue).ToListAsync());
            Assert.Empty(await verifyDb.FilingHistories.Where(h => h.CompanyId == restrictedCompanyId).ToListAsync());
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
    public async Task BalanceSheet_ExposesUnexplainedDifferenceInsteadOfPluggingReserves()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.BankAccounts.Add(new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 100m,
            OpeningBalanceDate = period.PeriodStart,
            Currency = "EUR"
        });
        db.ShareCapitals.Add(new ShareCapital
        {
            CompanyId = period.CompanyId,
            ShareClass = "Ordinary",
            NumberIssued = 1,
            NominalValue = 1m,
            TotalValue = 1m,
            IssueDate = period.PeriodStart
        });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var balanceSheet = await service.GetBalanceSheetAsync(period.CompanyId, period.Id);

        Assert.False(balanceSheet.Balances);
        Assert.Equal(99m, balanceSheet.CapitalAndReserves.UnexplainedDifference);
        Assert.Equal(0m, balanceSheet.CapitalAndReserves.RetainedEarnings);
        Assert.Equal(1m, balanceSheet.CapitalAndReserves.Total);
    }

    [Fact]
    public async Task BalanceSheet_AccrualDueAfterYearIsNotDoubleCounted()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.Creditors.Add(new Creditor
        {
            PeriodId = period.Id,
            Name = "Long-term accrual",
            Amount = 1_000m,
            Type = CreditorType.Accrual,
            DueWithinYear = false
        });
        await db.SaveChangesAsync();

        var balanceSheet = await new FinancialStatementsService(db).GetBalanceSheetAsync(period.CompanyId, period.Id);

        // A non-current accrual belongs only in creditors due after more than one year,
        // not also in accruals due within the year.
        Assert.Equal(0m, balanceSheet.CreditorsWithinYear.Accruals);
        Assert.Equal(1_000m, balanceSheet.CreditorsAfterYear.Other);
        // Counted once across the two creditor sections, not twice.
        Assert.Equal(1_000m, balanceSheet.CreditorsWithinYear.Total + balanceSheet.CreditorsAfterYear.Total);
    }

    [Fact]
    public async Task BalanceSheet_GroupsFixedAssetDepreciationPerAssetNotPerCategoryTotal()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var laptop = new FixedAsset
        {
            CompanyId = period.CompanyId,
            Name = "Laptop",
            Category = "Computer Equipment",
            Cost = 100m,
            AcquisitionDate = period.PeriodStart,
            UsefulLifeYears = 3
        };
        var server = new FixedAsset
        {
            CompanyId = period.CompanyId,
            Name = "Server",
            Category = "Computer Equipment",
            Cost = 200m,
            AcquisitionDate = period.PeriodStart,
            UsefulLifeYears = 4
        };
        db.FixedAssets.AddRange(laptop, server);
        await db.SaveChangesAsync();

        db.DepreciationEntries.AddRange(
            new DepreciationEntry
            {
                AssetId = laptop.Id,
                PeriodId = period.Id,
                OpeningNbv = 100m,
                Charge = 30m,
                ClosingNbv = 70m
            },
            new DepreciationEntry
            {
                AssetId = server.Id,
                PeriodId = period.Id,
                OpeningNbv = 200m,
                Charge = 50m,
                ClosingNbv = 150m
            });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var balanceSheet = await service.GetBalanceSheetAsync(period.CompanyId, period.Id);
        var computerEquipment = balanceSheet.FixedAssets.Categories.Single(c => c.Category == "Computer Equipment");

        Assert.Equal(300m, computerEquipment.Cost);
        Assert.Equal(80m, computerEquipment.Depreciation);
        Assert.Equal(220m, computerEquipment.Nbv);
    }

    [Fact]
    public async Task BalanceSheet_IgnoresFixedAssetsAndBankOpeningsAfterPeriodEnd()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var currentAsset = new FixedAsset
        {
            CompanyId = period.CompanyId,
            Name = "Laptop",
            Category = "Computer Equipment",
            Cost = 100m,
            AcquisitionDate = period.PeriodStart,
            UsefulLifeYears = 3
        };
        db.FixedAssets.AddRange(
            currentAsset,
            new FixedAsset
            {
                CompanyId = period.CompanyId,
                Name = "Future server",
                Category = "Computer Equipment",
                Cost = 900m,
                AcquisitionDate = period.PeriodEnd.AddDays(1),
                UsefulLifeYears = 4
            });
        db.BankAccounts.AddRange(
            new BankAccount
            {
                CompanyId = period.CompanyId,
                Name = "Current account",
                OpeningBalance = 100m,
                OpeningBalanceDate = period.PeriodStart
            },
            new BankAccount
            {
                CompanyId = period.CompanyId,
                Name = "Future account",
                OpeningBalance = 900m,
                OpeningBalanceDate = period.PeriodEnd.AddDays(1)
            });
        await db.SaveChangesAsync();
        db.DepreciationEntries.Add(new DepreciationEntry
        {
            AssetId = currentAsset.Id,
            PeriodId = period.Id,
            OpeningNbv = 100m,
            Charge = 20m,
            ClosingNbv = 80m
        });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var balanceSheet = await service.GetBalanceSheetAsync(period.CompanyId, period.Id);

        Assert.Equal(80m, balanceSheet.FixedAssets.Total);
        Assert.Equal(100m, balanceSheet.CurrentAssets.Cash);
    }

    [Fact]
    public async Task BalanceSheet_IgnoresFutureLoansAndShareIssues()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var currentLoan = new Loan
        {
            CompanyId = period.CompanyId,
            Lender = "Current Bank",
            OriginalAmount = 50m,
            Balance = 50m,
            DueWithinYear = 10m,
            DueAfterYear = 40m
        };
        var futureLoan = new Loan
        {
            CompanyId = period.CompanyId,
            Lender = "Future Bank",
            OriginalAmount = 900m,
            Balance = 900m,
            DueWithinYear = 90m,
            DueAfterYear = 810m
        };
        SetRequiredDate(currentLoan, "DrawdownDate", period.PeriodStart);
        SetRequiredDate(currentLoan, "BalanceAsOfDate", period.PeriodEnd);
        SetRequiredDate(futureLoan, "DrawdownDate", period.PeriodEnd.AddDays(1));
        SetRequiredDate(futureLoan, "BalanceAsOfDate", period.PeriodEnd.AddDays(1));
        db.Loans.AddRange(currentLoan, futureLoan);

        var issuedShare = new ShareCapital
        {
            CompanyId = period.CompanyId,
            ShareClass = "Ordinary",
            NumberIssued = 1,
            NominalValue = 1m,
            TotalValue = 1m
        };
        var futureShare = new ShareCapital
        {
            CompanyId = period.CompanyId,
            ShareClass = "Future Ordinary",
            NumberIssued = 900,
            NominalValue = 1m,
            TotalValue = 900m
        };
        SetRequiredDate(issuedShare, "IssueDate", period.PeriodStart);
        SetRequiredDate(futureShare, "IssueDate", period.PeriodEnd.AddDays(1));
        db.ShareCapitals.AddRange(issuedShare, futureShare);
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var balanceSheet = await service.GetBalanceSheetAsync(period.CompanyId, period.Id);
        var cashFlow = await service.GetCashFlowStatementAsync(period.CompanyId, period.Id);
        var equity = await service.GetEquityChangesAsync(period.CompanyId, period.Id);

        Assert.Equal(10m, balanceSheet.CreditorsWithinYear.OtherCreditors);
        Assert.Equal(40m, balanceSheet.CreditorsAfterYear.Loans);
        Assert.Equal(1m, balanceSheet.CapitalAndReserves.ShareCapital);
        Assert.Equal(50m, cashFlow.LoanDrawdowns);
        Assert.Equal(1m, equity.ClosingShareCapital);
    }

    [Fact]
    public async Task CompanyLevelAccountingFacts_AreIgnoredUntilEffectiveForCurrentPeriodOutputs()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Small,
            CanUseMicro = false,
            CanFileAbridged = true,
            AuditExempt = true
        });
        db.FixedAssets.Add(new FixedAsset
        {
            CompanyId = period.CompanyId,
            Name = "Future server",
            Category = "Computer Equipment",
            Cost = 8_000m,
            AcquisitionDate = period.PeriodEnd.AddDays(1),
            UsefulLifeYears = 4,
            DepreciationMethod = DepreciationMethod.StraightLine
        });

        var futureLoan = new Loan
        {
            CompanyId = period.CompanyId,
            Lender = "Future Bank",
            OriginalAmount = 20_000m,
            Balance = 20_000m,
            DueWithinYear = 2_000m,
            DueAfterYear = 18_000m
        };
        SetRequiredDate(futureLoan, "DrawdownDate", period.PeriodEnd.AddDays(1));
        SetRequiredDate(futureLoan, "BalanceAsOfDate", period.PeriodEnd.AddDays(1));

        var futureShare = new ShareCapital
        {
            CompanyId = period.CompanyId,
            ShareClass = "Future Ordinary",
            NumberIssued = 20_000,
            NominalValue = 1m,
            TotalValue = 20_000m
        };
        SetRequiredDate(futureShare, "IssueDate", period.PeriodEnd.AddDays(1));
        db.Loans.Add(futureLoan);
        db.ShareCapitals.Add(futureShare);
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(period.CompanyId, period.Id);
        var tax = await new TaxComputationService(db, new FinancialStatementsService(db)).ComputeAsync(period.CompanyId, period.Id);
        var ct1 = await new TaxComputationService(db, new FinancialStatementsService(db)).GetCt1SupportDataAsync(period.CompanyId, period.Id);
        var notes = await new NotesDisclosureService(db).GenerateNotesAsync(period.CompanyId, period.Id);

        Assert.Empty(await db.DepreciationEntries.Where(d => d.PeriodId == period.Id).ToListAsync());
        Assert.DoesNotContain(await db.Adjustments.Where(a => a.PeriodId == period.Id).ToListAsync(), a =>
            a.Description.Contains("Future server", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(tax.Adjustments, a =>
            a.Description.Contains("Capital allowances", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0m, ct1.CapitalAllowances);
        Assert.DoesNotContain(notes, n => n.Title == "Tangible Fixed Assets");
        Assert.DoesNotContain(notes, n => n.Title == "Creditors: Amounts Falling Due After More Than One Year");
        var shareNote = Assert.Single(notes, n => n.Title == "Share Capital");
        Assert.DoesNotContain("Future Ordinary", shareNote.Content);
        Assert.DoesNotContain("Future Bank", string.Join("\n", notes.Select(n => n.Content)));
    }

    [Fact]
    public async Task CapitalAllowances_AreProRatedForShortAccountingPeriod()
    {
        await using var db = CreateDbContext();
        var company = new Company
        {
            LegalName = "Short Period Limited",
            CroNumber = "654321",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2025, 1, 1),
            ArdMonth = 6,
            IsTrading = true,
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        // First accounting period of 181 days (1 Jan – 30 Jun 2025), shorter than 12 months.
        var shortPeriod = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 6, 30),
            IsFirstYear = true
        };
        db.AccountingPeriods.Add(shortPeriod);
        db.FixedAssets.Add(new FixedAsset
        {
            CompanyId = company.Id,
            Name = "Laptop fleet",
            Category = "Computer Equipment",
            Cost = 8_000m,
            AcquisitionDate = new DateOnly(2025, 1, 15),
            UsefulLifeYears = 4,
            DepreciationMethod = DepreciationMethod.StraightLine
        });
        await db.SaveChangesAsync();

        var ct1 = await new TaxComputationService(db, new FinancialStatementsService(db))
            .GetCt1SupportDataAsync(company.Id, shortPeriod.Id);

        // s.284 TCA: 8000 * 12.5% * (181/365) = 495.89, not the full-year 1000.
        Assert.Equal(495.89m, ct1.CapitalAllowances);
        Assert.True(ct1.CapitalAllowances < 1_000m);
    }

    [Fact]
    public async Task CapitalAllowances_FullTwelveMonthPeriodGivesFullWearAndTear()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true); // 1 Jan – 31 Dec 2025
        db.FixedAssets.Add(new FixedAsset
        {
            CompanyId = period.CompanyId,
            Name = "Laptop fleet",
            Category = "Computer Equipment",
            Cost = 8_000m,
            AcquisitionDate = new DateOnly(2025, 1, 15),
            UsefulLifeYears = 4,
            DepreciationMethod = DepreciationMethod.StraightLine
        });
        await db.SaveChangesAsync();

        var ct1 = await new TaxComputationService(db, new FinancialStatementsService(db))
            .GetCt1SupportDataAsync(period.CompanyId, period.Id);

        // A full 12-month period attracts the full 12.5% wear and tear: 8000 * 12.5% = 1000.
        Assert.Equal(1_000m, ct1.CapitalAllowances);
    }

    [Fact]
    public async Task CapitalAllowances_ClaimPersistedWhenAdjustmentsGenerated()
    {
        // BL-06: generating a period's adjustments records the actual wear-and-tear claim per asset,
        // so later periods can read the real cumulative claim instead of re-estimating it.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FixedAssets.Add(new FixedAsset
        {
            CompanyId = period.CompanyId,
            Name = "Laptop fleet",
            Category = "Computer Equipment",
            Cost = 8_000m,
            AcquisitionDate = new DateOnly(2025, 1, 15),
            UsefulLifeYears = 4,
            DepreciationMethod = DepreciationMethod.StraightLine
        });
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(period.CompanyId, period.Id);

        var claim = await db.CapitalAllowanceClaims.SingleAsync(c => c.PeriodId == period.Id);
        Assert.Equal(1_000m, claim.Claim);   // 8000 * 12.5%, full year
        Assert.Equal(8_000m, claim.Cost);
    }

    [Fact]
    public async Task CapitalAllowances_CapCumulativeClaimUsingPersistedPriorClaims()
    {
        // BL-06: prior claims come from persisted records, not re-estimated from period length or
        // depreciation entries. An asset already 7,500/8,000 claimed leaves only 500 this period,
        // even with no depreciation entries from which the old code could re-derive the prior claim.
        await using var db = CreateDbContext();
        var company = new Company
        {
            LegalName = "Capital Allowance Limited",
            CroNumber = "778899",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2018, 1, 1),
            ArdMonth = 12,
            IsTrading = true,
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var priorPeriod = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2024, 1, 1),
            PeriodEnd = new DateOnly(2024, 12, 31),
            IsFirstYear = false
        };
        var currentPeriod = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = false
        };
        db.AccountingPeriods.AddRange(priorPeriod, currentPeriod);
        var asset = new FixedAsset
        {
            CompanyId = company.Id,
            Name = "Machine",
            Category = "Plant & Machinery",
            Cost = 8_000m,
            AcquisitionDate = new DateOnly(2018, 6, 1),
            UsefulLifeYears = 4,
            DepreciationMethod = DepreciationMethod.StraightLine
        };
        db.FixedAssets.Add(asset);
        await db.SaveChangesAsync();

        // 7,500 already claimed in a prior period — deliberately with no depreciation entries.
        db.CapitalAllowanceClaims.Add(new CapitalAllowanceClaim
        {
            AssetId = asset.Id,
            PeriodId = priorPeriod.Id,
            Cost = 8_000m,
            Claim = 7_500m
        });
        await db.SaveChangesAsync();

        var ct1 = await new TaxComputationService(db, new FinancialStatementsService(db))
            .GetCt1SupportDataAsync(company.Id, currentPeriod.Id);

        // Only 500 of cost remains, so the claim is capped at 500, not the full-year 1,000.
        Assert.Equal(500m, ct1.CapitalAllowances);
    }

    [Fact]
    public async Task Depreciation_ReducingBalanceFullyWritesDownByEndOfUsefulLife()
    {
        // BL-21: a reducing-balance asset must be fully written down by the end of its useful life,
        // not leave an indefinite reducing-balance residual.
        await using var db = CreateDbContext();
        var company = new Company
        {
            LegalName = "Reducing Balance Limited",
            CroNumber = "112233",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2025, 1, 1),
            ArdMonth = 12,
            IsTrading = true,
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var periods = new List<AccountingPeriod>();
        for (var year = 2025; year <= 2027; year++)
            periods.Add(new AccountingPeriod { CompanyId = company.Id, PeriodStart = new DateOnly(year, 1, 1), PeriodEnd = new DateOnly(year, 12, 31), IsFirstYear = year == 2025 });
        db.AccountingPeriods.AddRange(periods);
        var asset = new FixedAsset
        {
            CompanyId = company.Id,
            Name = "Van",
            Category = "Motor Vehicles",
            Cost = 8_000m,
            AcquisitionDate = new DateOnly(2025, 1, 1),
            UsefulLifeYears = 3,
            DepreciationMethod = DepreciationMethod.ReducingBalance
        };
        db.FixedAssets.Add(asset);
        await db.SaveChangesAsync();

        var service = new AdjustmentService(db);
        foreach (var p in periods)
            await service.GenerateAutoAdjustmentsAsync(company.Id, p.Id);

        var finalEntry = await db.DepreciationEntries.SingleAsync(d => d.AssetId == asset.Id && d.PeriodId == periods[2].Id);
        Assert.Equal(0m, finalEntry.ClosingNbv);
        var totalCharge = await db.DepreciationEntries.Where(d => d.AssetId == asset.Id).SumAsync(d => d.Charge);
        Assert.Equal(8_000m, totalCharge);
    }

    [Fact]
    public async Task EquityChanges_FirstYearShowsIncorporationCapitalAsOpeningNotIssuedInYear()
    {
        // BL-22: capital subscribed at incorporation (on the period start date) is the opening balance
        // of the statement of changes in equity, not mis-stated as issued during the first year.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.ShareCapitals.Add(new ShareCapital
        {
            CompanyId = period.CompanyId,
            ShareClass = "Ordinary",
            NumberIssued = 100,
            NominalValue = 1m,
            TotalValue = 100m,
            IssueDate = period.PeriodStart
        });
        await db.SaveChangesAsync();

        var equity = await new FinancialStatementsService(db).GetEquityChangesAsync(period.CompanyId, period.Id);

        Assert.Equal(100m, equity.OpeningShareCapital);
        Assert.Equal(0m, equity.SharesIssued);
        Assert.Equal(100m, equity.ClosingShareCapital);
    }

    [Fact]
    public async Task Adjustments_PrepaymentIncreasesProfitAndShowsAsCurrentAsset()
    {
        // BL-32: figure-level proof that a prepayment increases profit (defers an expense) and is
        // carried as a current asset.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.Debtors.Add(new Debtor { PeriodId = period.Id, Name = "Insurance prepaid", Amount = 600m, Type = DebtorType.Prepayment });
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(period.CompanyId, period.Id);

        var prepaymentAdj = await db.Adjustments.SingleAsync(a => a.PeriodId == period.Id && a.Description.Contains("Prepayment", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(600m, prepaymentAdj.ImpactOnProfit);
        var bs = await new FinancialStatementsService(db).GetBalanceSheetAsync(period.CompanyId, period.Id);
        Assert.Equal(600m, bs.CurrentAssets.Prepayments);
    }

    [Fact]
    public async Task BalanceSheet_IncludesPayeAndRctTaxBalancesInCreditors()
    {
        // BL-32: PAYE/PRSI and RCT liabilities are creditors falling due within one year.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.TaxBalances.AddRange(
            new TaxBalance { PeriodId = period.Id, TaxType = TaxType.Paye, Liability = 500m, Paid = 0m, Balance = 500m },
            new TaxBalance { PeriodId = period.Id, TaxType = TaxType.Rct, Liability = 300m, Paid = 0m, Balance = 300m });
        await db.SaveChangesAsync();

        var bs = await new FinancialStatementsService(db).GetBalanceSheetAsync(period.CompanyId, period.Id);
        Assert.Equal(800m, bs.CreditorsWithinYear.TaxCreditors);
    }

    [Fact]
    public async Task BalanceSheet_DoesNotDoubleCountTaxCreditorAndTaxBalance()
    {
        // accounting-tax-creditor-double-count [HUMAN DECISION: TaxBalances is the single source of tax].
        // The same €125 CT liability is recorded BOTH as a TaxBalance and (redundantly) as a
        // Creditors.Type==Tax row. Tax owed is taken from TaxBalances only, so the tax-creditor line is
        // €125 (not €250) and the balance sheet still balances.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var companyId = period.CompanyId;
        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(companyId);
        int Cat(string code) => categories.Single(c => c.Code == code).Id;

        db.ShareCapitals.Add(new ShareCapital
        {
            CompanyId = companyId,
            ShareClass = "Ordinary",
            NumberIssued = 100,
            NominalValue = 1m,
            TotalValue = 100m,
            IssueDate = period.PeriodStart
        });
        var bank = new BankAccount
        {
            CompanyId = companyId,
            Name = "Current Account",
            OpeningBalance = 100m,
            OpeningBalanceDate = period.PeriodStart
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = Cat("3000"),
            Credit = 100m,
            SourceNote = "Share capital subscribed at incorporation",
            EnteredBy = "Accounts reviewer",
            Reviewed = true
        });
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = period.Id,
            Date = new DateOnly(2025, 6, 1),
            Description = "Sales invoice INV001",
            Amount = 1_000m,
            CategoryId = Cat("4000")
        });
        db.TaxBalances.Add(new TaxBalance
        {
            PeriodId = period.Id,
            TaxType = TaxType.CorporationTax,
            Liability = 125m,
            Paid = 0m,
            Balance = 125m
        });
        db.Creditors.Add(new Creditor
        {
            PeriodId = period.Id,
            Name = "Corporation tax payable",
            Amount = 125m,
            Type = CreditorType.Tax,
            DueWithinYear = true
        });
        await db.SaveChangesAsync();

        var bs = await new FinancialStatementsService(db).GetBalanceSheetAsync(companyId, period.Id);

        Assert.Equal(125m, bs.CreditorsWithinYear.TaxCreditors); // X, not 2X
        Assert.Equal(0m, bs.CapitalAndReserves.UnexplainedDifference);
        Assert.True(bs.Balances);
    }

    [Fact]
    public async Task ProfitAndLoss_TreatsCapexAsCapitalNotRevenueExpense()
    {
        // BL-32: a fixed-asset purchase coded to an asset account is capital — it does not reduce
        // profit; only the depreciation charge does.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var cats = await new CategoryService(db).SeedDefaultCategoriesAsync(period.CompanyId);
        int Cat(string code) => cats.Single(c => c.Code == code).Id;
        var bank = new BankAccount { CompanyId = period.CompanyId, Name = "Current", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        db.ImportedTransactions.AddRange(
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 2, 1), Description = "Sales", Amount = 10_000m, CategoryId = Cat("4000") },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 1), Description = "Laptop purchase", Amount = -2_000m, CategoryId = Cat("0050") });
        await db.SaveChangesAsync();

        var pl = await new FinancialStatementsService(db).GetProfitAndLossAsync(period.CompanyId, period.Id);
        Assert.Equal(10_000m, pl.Turnover);
        Assert.Equal(0m, pl.TotalOverheads);
        Assert.Equal(10_000m, pl.OperatingProfit);
    }

    [Fact]
    public async Task BalanceSheet_RollsPriorPeriodProfitIntoOpeningRetainedEarnings()
    {
        // BL-32: retained earnings brought forward equal the prior period's profit after tax.
        await using var db = CreateDbContext();
        var company = new Company
        {
            LegalName = "Roll Forward Limited",
            CroNumber = "556677",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2024, 1, 1),
            ArdMonth = 12,
            IsTrading = true,
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var prior = new AccountingPeriod { CompanyId = company.Id, PeriodStart = new DateOnly(2024, 1, 1), PeriodEnd = new DateOnly(2024, 12, 31), IsFirstYear = true };
        var current = new AccountingPeriod { CompanyId = company.Id, PeriodStart = new DateOnly(2025, 1, 1), PeriodEnd = new DateOnly(2025, 12, 31), IsFirstYear = false };
        db.AccountingPeriods.AddRange(prior, current);
        var sales = AddCategory(db, company.Id, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var bank = new BankAccount { CompanyId = company.Id, Name = "Current", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        db.ImportedTransactions.Add(new ImportedTransaction { BankAccountId = bank.Id, PeriodId = prior.Id, Date = new DateOnly(2024, 6, 1), Description = "Prior sales", Amount = 5_000m, CategoryId = sales.Id });
        await db.SaveChangesAsync();

        var bs = await new FinancialStatementsService(db).GetBalanceSheetAsync(company.Id, current.Id);
        Assert.Equal(5_000m, bs.CapitalAndReserves.OpeningRetainedEarnings);
    }

    [Fact]
    public async Task BalanceSheet_MultiYearRetainedEarningsRollForwardAccumulatesProfitsLessDividends()
    {
        // G2 (money correct over multiple years): reserves brought forward across a three-year chain
        // accumulate prior profits and subtract prior dividends. Proves the roll-forward figure
        // (BL-20/BL-32) is correct year on year, including the dividend reduction.
        await using var db = CreateDbContext();
        var company = new Company
        {
            LegalName = "Roll Forward Chain Limited",
            CroNumber = "778899",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2023, 1, 1),
            ArdMonth = 12,
            IsTrading = true,
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var y2023 = new AccountingPeriod { CompanyId = company.Id, PeriodStart = new DateOnly(2023, 1, 1), PeriodEnd = new DateOnly(2023, 12, 31), IsFirstYear = true };
        var y2024 = new AccountingPeriod { CompanyId = company.Id, PeriodStart = new DateOnly(2024, 1, 1), PeriodEnd = new DateOnly(2024, 12, 31), IsFirstYear = false };
        var y2025 = new AccountingPeriod { CompanyId = company.Id, PeriodStart = new DateOnly(2025, 1, 1), PeriodEnd = new DateOnly(2025, 12, 31), IsFirstYear = false };
        db.AccountingPeriods.AddRange(y2023, y2024, y2025);
        var sales = AddCategory(db, company.Id, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var bank = new BankAccount { CompanyId = company.Id, Name = "Current", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        // Profits: 2023 +5,000, 2024 +3,000, 2025 +2,000. A €1,000 dividend is paid in 2024.
        db.ImportedTransactions.AddRange(
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = y2023.Id, Date = new DateOnly(2023, 6, 1), Description = "2023 sales", Amount = 5_000m, CategoryId = sales.Id },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = y2024.Id, Date = new DateOnly(2024, 6, 1), Description = "2024 sales", Amount = 3_000m, CategoryId = sales.Id },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = y2025.Id, Date = new DateOnly(2025, 6, 1), Description = "2025 sales", Amount = 2_000m, CategoryId = sales.Id });
        db.Dividends.Add(new Dividend { PeriodId = y2024.Id, Amount = 1_000m, DateDeclared = new DateOnly(2024, 12, 1), DatePaid = new DateOnly(2024, 12, 15) });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var bs2024 = await statements.GetBalanceSheetAsync(company.Id, y2024.Id);
        var bs2025 = await statements.GetBalanceSheetAsync(company.Id, y2025.Id);

        // Opening reserves carried into 2024 = 2023 profit.
        Assert.Equal(5_000m, bs2024.CapitalAndReserves.OpeningRetainedEarnings);
        // Opening reserves carried into 2025 = 2023 profit + 2024 profit - 2024 dividend.
        Assert.Equal(7_000m, bs2025.CapitalAndReserves.OpeningRetainedEarnings);
        // Closing reserves at end of 2025 = brought forward 7,000 + 2025 profit 2,000.
        Assert.Equal(9_000m, bs2025.CapitalAndReserves.RetainedEarnings);
    }

    [Fact]
    public async Task FixedAssets_PeriodMembershipRespectsAcquisitionAndDisposalBoundaries()
    {
        // BL-27: an asset is in the balance sheet if acquired on or before the period end and not
        // disposed on or before it. Test the exact-date boundaries.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true); // ends 2025-12-31
        db.FixedAssets.AddRange(
            new FixedAsset { CompanyId = period.CompanyId, Name = "Acquired on year-end", Category = "Equipment", Cost = 1_000m, AcquisitionDate = new DateOnly(2025, 12, 31), UsefulLifeYears = 5, DepreciationMethod = DepreciationMethod.StraightLine },
            new FixedAsset { CompanyId = period.CompanyId, Name = "Acquired after year-end", Category = "Equipment", Cost = 2_000m, AcquisitionDate = new DateOnly(2026, 1, 1), UsefulLifeYears = 5, DepreciationMethod = DepreciationMethod.StraightLine },
            new FixedAsset { CompanyId = period.CompanyId, Name = "Disposed on year-end", Category = "Equipment", Cost = 4_000m, AcquisitionDate = new DateOnly(2025, 1, 1), DisposalDate = new DateOnly(2025, 12, 31), UsefulLifeYears = 5, DepreciationMethod = DepreciationMethod.StraightLine },
            new FixedAsset { CompanyId = period.CompanyId, Name = "Disposed after year-end", Category = "Equipment", Cost = 8_000m, AcquisitionDate = new DateOnly(2025, 1, 1), DisposalDate = new DateOnly(2026, 1, 1), UsefulLifeYears = 5, DepreciationMethod = DepreciationMethod.StraightLine });
        await db.SaveChangesAsync();

        var bs = await new FinancialStatementsService(db).GetBalanceSheetAsync(period.CompanyId, period.Id);
        // Included: acquired-on-year-end (1,000) + disposed-after-year-end (8,000). Excluded: future
        // acquisition and disposed-on-year-end.
        Assert.Equal(9_000m, bs.FixedAssets.Categories.Sum(c => c.Cost));
    }

    [Fact]
    public async Task ShareCapital_PeriodMembershipRespectsIssueAndCancellationBoundaries()
    {
        // BL-27: share capital is in existence at the period end if issued on or before it and not
        // cancelled on or before it.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.ShareCapitals.AddRange(
            new ShareCapital { CompanyId = period.CompanyId, ShareClass = "On year-end", NumberIssued = 100, NominalValue = 1m, TotalValue = 100m, IssueDate = new DateOnly(2025, 12, 31) },
            new ShareCapital { CompanyId = period.CompanyId, ShareClass = "After year-end", NumberIssued = 200, NominalValue = 1m, TotalValue = 200m, IssueDate = new DateOnly(2026, 1, 1) },
            new ShareCapital { CompanyId = period.CompanyId, ShareClass = "Cancelled on year-end", NumberIssued = 50, NominalValue = 1m, TotalValue = 50m, IssueDate = new DateOnly(2025, 1, 1), CancelledDate = new DateOnly(2025, 12, 31) });
        await db.SaveChangesAsync();

        var bs = await new FinancialStatementsService(db).GetBalanceSheetAsync(period.CompanyId, period.Id);
        Assert.Equal(100m, bs.CapitalAndReserves.ShareCapital);
    }

    [Fact]
    public async Task TaxComputation_SurfacesTradingLossInsteadOfDiscardingIt()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var salesCat = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var expenseCat = AddCategory(db, period.CompanyId, "6000", "Office costs", AccountCategoryType.Expense);
        var bank = new BankAccount { CompanyId = period.CompanyId, Name = "Current Account", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        // Loss-making period: 1,000 income vs 5,000 expense => 4,000 trading loss.
        db.ImportedTransactions.AddRange(
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 1), Description = "Sale", Amount = 1_000m, CategoryId = salesCat.Id },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 2), Description = "Office cost", Amount = -5_000m, CategoryId = expenseCat.Id });
        await db.SaveChangesAsync();

        var tax = await new TaxComputationService(db, new FinancialStatementsService(db)).ComputeAsync(period.CompanyId, period.Id);

        Assert.Equal(0m, tax.TaxableProfit);
        Assert.Equal(0m, tax.TotalCorporationTax);
        Assert.Equal(4_000m, tax.TradingLossAvailable);
        Assert.Contains("carry forward", tax.Notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TaxComputation_AppliesTwentyFivePercentToNonTradingIncome()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var trading = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var rental = AddCategory(db, period.CompanyId, "4500", "Rental income", AccountCategoryType.Income);
        rental.IsNonTradingIncome = true;
        var bank = new BankAccount { CompanyId = period.CompanyId, Name = "Current Account", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        // 10,000 trading income (12.5%) + 4,000 non-trading rental income (25%).
        db.ImportedTransactions.AddRange(
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 1), Description = "Trading sales", Amount = 10_000m, CategoryId = trading.Id },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 2), Description = "Rent received", Amount = 4_000m, CategoryId = rental.Id });
        await db.SaveChangesAsync();

        var tax = await new TaxComputationService(db, new FinancialStatementsService(db)).ComputeAsync(period.CompanyId, period.Id);

        Assert.Equal(14_000m, tax.TaxableProfit);
        Assert.Equal(1_250m, tax.CorporationTaxAt125); // 10,000 trading @ 12.5%
        Assert.Equal(1_000m, tax.CorporationTaxAt25);  // 4,000 non-trading @ 25% (s.21A TCA 1997)
        Assert.Equal(2_250m, tax.TotalCorporationTax);
    }

    [Fact]
    public async Task TaxComputation_TradingLossDoesNotShelterNonTradingIncomeFrom25Percent()
    {
        // BL-04: a trading loss must not silently absorb passive (Case III/V) income. Absent an
        // elected s.396A claim, the non-trading income is charged at 25% in full and the trading
        // loss is surfaced for carry-forward — taxing in the high-stakes (do-not-under-tax) direction.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var trading = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var expense = AddCategory(db, period.CompanyId, "6000", "Office costs", AccountCategoryType.Expense);
        var rental = AddCategory(db, period.CompanyId, "4500", "Rental income", AccountCategoryType.Income);
        rental.IsNonTradingIncome = true;
        var bank = new BankAccount { CompanyId = period.CompanyId, Name = "Current Account", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        // Trading: 1,000 sales vs 11,000 costs => 10,000 trading loss.
        // Non-trading: 4,000 rental profit, which must remain taxable at 25%.
        db.ImportedTransactions.AddRange(
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 1), Description = "Trading sales", Amount = 1_000m, CategoryId = trading.Id },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 2), Description = "Office costs", Amount = -11_000m, CategoryId = expense.Id },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 3), Description = "Rent received", Amount = 4_000m, CategoryId = rental.Id });
        await db.SaveChangesAsync();

        var tax = await new TaxComputationService(db, new FinancialStatementsService(db)).ComputeAsync(period.CompanyId, period.Id);

        // The 4,000 of passive income is taxed at 25% even though the trade is loss-making.
        Assert.Equal(1_000m, tax.CorporationTaxAt25);
        Assert.Equal(0m, tax.CorporationTaxAt125);
        Assert.Equal(1_000m, tax.TotalCorporationTax);
        // The full 10,000 trading loss is carried forward — the rental does not reduce it.
        Assert.Equal(10_000m, tax.TradingLossAvailable);
        // Only the non-trading income is taxable this period.
        Assert.Equal(4_000m, tax.TaxableProfit);
        Assert.Contains("25%", tax.Notes);
        Assert.Contains("carry forward", tax.Notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BalanceSheet_MixedCashAccrualScenario_BalancesWithZeroUnexplainedDifference()
    {
        // BL-01: a realistic mixed cash/accrual set must reconcile. Net assets are built from the
        // entity tables (debtors/creditors/stock/fixed assets/loans) + bank cash, while reserves come
        // from the P&L. The auto-adjustment engine posts the accrual contras (trade debtors -> turnover,
        // trade creditors/accruals -> expense, prepayments, stock, depreciation) that keep the two sides
        // in step, so UnexplainedDifference must be exactly zero.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var companyId = period.CompanyId;

        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(companyId);
        int Cat(string code) => categories.Single(c => c.Code == code).Id;

        // Share capital of €100 funded by an opening bank balance of €100 (cash the members paid in).
        db.ShareCapitals.Add(new ShareCapital
        {
            CompanyId = companyId,
            ShareClass = "Ordinary",
            NumberIssued = 100,
            NominalValue = 1m,
            TotalValue = 100m,
            IssueDate = new DateOnly(2025, 1, 1)
        });
        var bank = new BankAccount
        {
            CompanyId = companyId,
            Name = "Current Account",
            OpeningBalance = 100m,
            OpeningBalanceDate = new DateOnly(2025, 1, 1)
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        // Cash movements: +10,000 trading sales, -3,000 rent, -4,000 capex (asset — outside the P&L),
        // +5,000 loan drawdown. Capex and loan are coded to balance-sheet categories, so they move cash
        // without touching profit.
        db.ImportedTransactions.AddRange(
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 2, 1), Description = "Sales", Amount = 10_000m, CategoryId = Cat("4000") },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 1), Description = "Rent", Amount = -3_000m, CategoryId = Cat("6100") },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 4, 1), Description = "Plant purchase", Amount = -4_000m, CategoryId = Cat("0020") },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 5, 1), Description = "Bank loan drawdown", Amount = 5_000m, CategoryId = Cat("2600") });

        // Accrual-basis facts entered as year-end entity rows.
        db.Debtors.AddRange(
            new Debtor { PeriodId = period.Id, Name = "Customer X", Amount = 2_000m, Type = DebtorType.Trade },
            new Debtor { PeriodId = period.Id, Name = "Insurance prepaid", Amount = 300m, Type = DebtorType.Prepayment });
        db.Creditors.AddRange(
            new Creditor { PeriodId = period.Id, Name = "Supplier Y", Amount = 1_500m, Type = CreditorType.Trade, DueWithinYear = true },
            new Creditor { PeriodId = period.Id, Name = "Accountancy fees", Amount = 500m, Type = CreditorType.Accrual, DueWithinYear = true });
        db.Inventories.Add(new Inventory { PeriodId = period.Id, Description = "Closing stock", Value = 800m, ValuationMethod = ValuationMethod.Cost });

        // Fixed asset matching the €4,000 capex, depreciated straight-line over 4 years (€1,000/yr).
        db.FixedAssets.Add(new FixedAsset
        {
            CompanyId = companyId,
            Name = "Plant",
            Category = "Plant & Machinery",
            Cost = 4_000m,
            AcquisitionDate = new DateOnly(2025, 4, 1),
            UsefulLifeYears = 4,
            DepreciationMethod = DepreciationMethod.StraightLine
        });

        // Loan: €5,000 drawn in-period, €1,000 due within a year and €4,000 after.
        db.Loans.Add(new Loan
        {
            CompanyId = companyId,
            Lender = "Bank",
            OriginalAmount = 5_000m,
            Balance = 5_000m,
            DueWithinYear = 1_000m,
            DueAfterYear = 4_000m,
            DrawdownDate = new DateOnly(2025, 5, 1),
            BalanceAsOfDate = period.PeriodEnd
        });
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(companyId, period.Id);

        var balanceSheet = await new FinancialStatementsService(db).GetBalanceSheetAsync(companyId, period.Id);

        // The whole point of BL-01: a correct mixed cash/accrual set balances exactly.
        Assert.Equal(0m, balanceSheet.CapitalAndReserves.UnexplainedDifference);
        Assert.True(balanceSheet.Balances);

        // Headline figures match the hand-computed scenario.
        Assert.Equal(7_200m, balanceSheet.NetAssets);
        Assert.Equal(7_200m, balanceSheet.CapitalAndReserves.Total);
        Assert.Equal(100m, balanceSheet.CapitalAndReserves.ShareCapital);
        Assert.Equal(7_100m, balanceSheet.CapitalAndReserves.RetainedEarnings);
        Assert.Equal(2_000m, balanceSheet.CurrentAssets.Debtors);
        Assert.Equal(1_500m, balanceSheet.CreditorsWithinYear.TradeCreditors);
        Assert.Equal(3_000m, balanceSheet.FixedAssets.Total);
    }

    // ----------------------------------------------------------------------------------------------
    // Golden-path end-to-end tests (Trust guarantee #1). Each drives the WHOLE pipeline with the real
    // services — onboard -> import a real CSV -> categorise -> year-end facts -> generate adjustments ->
    // statements that BALANCE -> accounts PDF -> iXBRL — for a shipped regime, and proves the period
    // clears the readiness gate so the final outputs actually generate.
    // ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task GoldenPath_MicroAuditExemptCompany_OnboardToBalancedStatementsPdfAndIxbrl()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var companyId = period.CompanyId;

        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(companyId);
        int Cat(string code) => categories.Single(c => c.Code == code).Id;

        // Onboarding: €500 share capital subscribed at incorporation, funded by the opening bank
        // balance. The matching opening-balance entry keeps the opening trial balance in step.
        db.ShareCapitals.Add(new ShareCapital
        {
            CompanyId = companyId,
            ShareClass = "Ordinary",
            NumberIssued = 500,
            NominalValue = 1m,
            TotalValue = 500m,
            IssueDate = period.PeriodStart
        });
        var bank = new BankAccount
        {
            CompanyId = companyId,
            Name = "Current Account",
            OpeningBalance = 500m,
            OpeningBalanceDate = period.PeriodStart
        };
        db.BankAccounts.Add(bank);
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            Turnover = 6_500m,
            BalanceSheetTotal = 6_250m,
            AvgEmployees = 1
        });
        await db.SaveChangesAsync();

        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = Cat("3000"),
            Credit = 500m,
            SourceNote = "Share capital subscribed at incorporation",
            EnteredBy = "Accounts reviewer",
            Reviewed = true,
            ReviewedBy = "Accounts reviewer",
            ReviewedAt = DateTime.UtcNow
        });
        // Categorisation rules so the import auto-codes every row (no manual tidy-up needed).
        db.TransactionRules.AddRange(
            new TransactionRule { CompanyId = companyId, Pattern = "Sales", CategoryId = Cat("4000"), Priority = 1 },
            new TransactionRule { CompanyId = companyId, Pattern = "Office", CategoryId = Cat("6500"), Priority = 2 },
            new TransactionRule { CompanyId = companyId, Pattern = "Light", CategoryId = Cat("6300"), Priority = 3 });
        await db.SaveChangesAsync();

        // Import a real bank-statement CSV through the real ImportService (generic format).
        var csv = MakeGenericCsv(
            ("01/03/2025", "Sales invoice INV001", 4_000m),
            ("10/06/2025", "Sales invoice INV002", 2_500m),
            ("15/04/2025", "Office Supplies purchase", -300m),
            ("20/09/2025", "Light and Heat ESB", -450m));
        var import = await new ImportService(db).ImportCsvAsync(
            companyId, bank.Id, period.Id, new MemoryStream(Encoding.UTF8.GetBytes(csv)), "statement.csv");

        Assert.Equal(4, import.ImportedRows);
        Assert.Equal(4, import.AutoCategorised);
        Assert.Equal(0, import.DuplicatesSkipped);

        // Year-end questionnaire — a nil-trading micro with no debtors/creditors/etc. confirms each section.
        db.YearEndReviewConfirmations.AddRange(
            NilReview(period.Id, "debtors"), NilReview(period.Id, "creditors"),
            NilReview(period.Id, "payroll"), NilReview(period.Id, "tax"),
            NilReview(period.Id, "dividends"), NilReview(period.Id, "post-balance-sheet-events"),
            NilReview(period.Id, "related-parties"), NilReview(period.Id, "contingent-liabilities"),
            NilReview(period.Id, "going-concern"));
        await db.SaveChangesAsync();

        var emit = await ClassifyAdjustNotesAndEmitAsync(db, companyId, period.Id, ElectedRegime.Micro);

        // Regime is Micro, audit-exempt.
        Assert.Equal(ElectedRegime.Micro, emit.Regime.Regime);
        Assert.True(emit.Regime.AuditExempt);

        // Readiness gate is fully satisfied — nothing missing, nothing warned, balance sheet balances.
        Assert.Empty(emit.Readiness.MissingItems);
        Assert.Empty(emit.Readiness.Warnings);
        Assert.True(emit.Readiness.BalanceSheetBalances);
        Assert.Equal(100, emit.Readiness.FilingReadinessPercent);

        // Money is correct and the statements BALANCE.
        Assert.True(emit.BalanceSheet.Balances);
        Assert.Equal(0m, emit.BalanceSheet.CapitalAndReserves.UnexplainedDifference);
        Assert.Equal(6_250m, emit.BalanceSheet.NetAssets);
        Assert.Equal(500m, emit.BalanceSheet.CapitalAndReserves.ShareCapital);
        Assert.Equal(5_750m, emit.BalanceSheet.CapitalAndReserves.ProfitForYear);
        Assert.Equal(6_250m, emit.BalanceSheet.CurrentAssets.Cash);

        // P&L stage runs and reconciles to the worked figures.
        Assert.Equal(6_500m, emit.ProfitAndLoss.Turnover);
        Assert.Equal(5_750m, emit.ProfitAndLoss.ProfitAfterTax);

        // The accounts PDF generated past the readiness gate and is a real PDF.
        Assert.True(emit.Pdf.Length > 1_000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(emit.Pdf, 0, 4));

        // The iXBRL is well-formed XML carrying the entity name.
        AssertWellFormedXml(emit.Ixbrl);
        Assert.Contains("Example Micro Limited", emit.Ixbrl);

        // tests-pdf-content-verified: parse the PDF text (not just the %PDF header) and assert the
        // real figures/names/wording — company legal name, the period-end date, the computed
        // net-assets total, and the micro s.280D statutory statement.
        var pdfText = ExtractPdfText(emit.Pdf);
        Assert.Contains("Example Micro Limited", pdfText);
        Assert.Contains(period.PeriodEnd.ToString("dd MMMM yyyy"), pdfText);
        Assert.Contains(emit.BalanceSheet.NetAssets.ToString("N0"), pdfText); // 6,250 == computed BalanceSheet
        Assert.Contains("280D", pdfText);
    }

    [Fact]
    public async Task GoldenPath_SmallAuditExemptCompany_MixedAccrualSetBalancesThroughPdfAndIxbrl()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var companyId = period.CompanyId;
        // A small audit-exempt LTD that owns assets, holds stock and has a bank loan.
        var company = await db.Companies.FirstAsync(c => c.Id == companyId);
        company.LegalName = "Connacht Digital Solutions Limited";
        company.OwnsAssets = true;
        company.HasStock = true;
        company.HasBorrowings = true;
        await db.SaveChangesAsync();

        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(companyId);
        int Cat(string code) => categories.Single(c => c.Code == code).Id;

        db.ShareCapitals.Add(new ShareCapital
        {
            CompanyId = companyId,
            ShareClass = "Ordinary",
            NumberIssued = 100,
            NominalValue = 1m,
            TotalValue = 100m,
            IssueDate = period.PeriodStart
        });
        var bank = new BankAccount
        {
            CompanyId = companyId,
            Name = "Current Account",
            OpeningBalance = 100m,
            OpeningBalanceDate = period.PeriodStart
        };
        db.BankAccounts.Add(bank);
        // Size-classification interview is input-driven (REQUIREMENTS §B); these are representative
        // Small-company figures, independent of the worked demo ledger below.
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            Turnover = 2_000_000m,
            BalanceSheetTotal = 1_200_000m,
            AvgEmployees = 25
        });
        await db.SaveChangesAsync();

        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = Cat("3000"),
            Credit = 100m,
            SourceNote = "Share capital subscribed at incorporation",
            EnteredBy = "Accounts reviewer",
            Reviewed = true,
            ReviewedBy = "Accounts reviewer",
            ReviewedAt = DateTime.UtcNow
        });
        db.TransactionRules.AddRange(
            new TransactionRule { CompanyId = companyId, Pattern = "Sales", CategoryId = Cat("4000"), Priority = 1 },
            new TransactionRule { CompanyId = companyId, Pattern = "Rent", CategoryId = Cat("6100"), Priority = 2 },
            new TransactionRule { CompanyId = companyId, Pattern = "Plant", CategoryId = Cat("0020"), Priority = 3 },
            new TransactionRule { CompanyId = companyId, Pattern = "Loan", CategoryId = Cat("2600"), Priority = 4 });
        await db.SaveChangesAsync();

        // Cash movements: +10,000 trading, -3,000 rent, -4,000 capex (asset — outside the P&L),
        // +5,000 loan drawdown. Capex and loan are coded to balance-sheet categories.
        var csv = MakeGenericCsv(
            ("01/02/2025", "Sales receipts", 10_000m),
            ("01/03/2025", "Rent paid", -3_000m),
            ("01/04/2025", "Plant and machinery purchase", -4_000m),
            ("01/05/2025", "Bank Loan drawdown", 5_000m));
        var import = await new ImportService(db).ImportCsvAsync(
            companyId, bank.Id, period.Id, new MemoryStream(Encoding.UTF8.GetBytes(csv)), "aib-statement.csv");
        Assert.Equal(4, import.ImportedRows);
        Assert.Equal(4, import.AutoCategorised);

        // Accrual-basis year-end facts entered as entity rows.
        db.Debtors.AddRange(
            new Debtor { PeriodId = period.Id, Name = "Customer X", Amount = 2_000m, Type = DebtorType.Trade },
            new Debtor { PeriodId = period.Id, Name = "Insurance prepaid", Amount = 300m, Type = DebtorType.Prepayment });
        db.Creditors.AddRange(
            new Creditor { PeriodId = period.Id, Name = "Supplier Y", Amount = 1_500m, Type = CreditorType.Trade, DueWithinYear = true },
            new Creditor { PeriodId = period.Id, Name = "Accountancy fees", Amount = 500m, Type = CreditorType.Accrual, DueWithinYear = true });
        db.Inventories.Add(new Inventory { PeriodId = period.Id, Description = "Closing stock", Value = 800m, ValuationMethod = ValuationMethod.Cost });
        db.FixedAssets.Add(new FixedAsset
        {
            CompanyId = companyId,
            Name = "Plant",
            Category = "Plant & Machinery",
            Cost = 4_000m,
            AcquisitionDate = new DateOnly(2025, 4, 1),
            UsefulLifeYears = 4,
            DepreciationMethod = DepreciationMethod.StraightLine
        });
        db.Loans.Add(new Loan
        {
            CompanyId = companyId,
            Lender = "Bank",
            OriginalAmount = 5_000m,
            Balance = 5_000m,
            DueWithinYear = 1_000m,
            DueAfterYear = 4_000m,
            DrawdownDate = new DateOnly(2025, 5, 1),
            BalanceAsOfDate = period.PeriodEnd
        });
        db.YearEndReviewConfirmations.AddRange(
            NilReview(period.Id, "payroll"), NilReview(period.Id, "tax"),
            NilReview(period.Id, "dividends"), NilReview(period.Id, "post-balance-sheet-events"),
            NilReview(period.Id, "related-parties"), NilReview(period.Id, "contingent-liabilities"),
            NilReview(period.Id, "going-concern"));
        await db.SaveChangesAsync();

        var emit = await ClassifyAdjustNotesAndEmitAsync(db, companyId, period.Id, ElectedRegime.Small);

        // Small audit-exempt regime.
        Assert.Equal(ElectedRegime.Small, emit.Regime.Regime);
        Assert.True(emit.Regime.AuditExempt);

        // Readiness gate satisfied across the richer year-end set.
        Assert.Empty(emit.Readiness.MissingItems);
        Assert.Empty(emit.Readiness.Warnings);
        Assert.True(emit.Readiness.BalanceSheetBalances);

        // The mixed cash/accrual set BALANCES exactly.
        Assert.True(emit.BalanceSheet.Balances);
        Assert.Equal(0m, emit.BalanceSheet.CapitalAndReserves.UnexplainedDifference);
        Assert.Equal(7_200m, emit.BalanceSheet.NetAssets);
        Assert.Equal(100m, emit.BalanceSheet.CapitalAndReserves.ShareCapital);
        Assert.Equal(7_100m, emit.BalanceSheet.CapitalAndReserves.RetainedEarnings);
        Assert.Equal(3_000m, emit.BalanceSheet.FixedAssets.Total);

        // PDF generates past the gate; iXBRL is well-formed.
        Assert.True(emit.Pdf.Length > 1_000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(emit.Pdf, 0, 4));
        AssertWellFormedXml(emit.Ixbrl);
        Assert.Contains("Connacht Digital Solutions Limited", emit.Ixbrl);

        // tests-pdf-content-verified: the parsed PDF carries the legal name, period-end date and the
        // computed net-assets total for the richer accrual-basis small company.
        var pdfText = ExtractPdfText(emit.Pdf);
        Assert.Contains("Connacht Digital Solutions Limited", pdfText);
        Assert.Contains(period.PeriodEnd.ToString("dd MMMM yyyy"), pdfText);
        Assert.Contains(emit.BalanceSheet.NetAssets.ToString("N0"), pdfText); // 7,200 == computed BalanceSheet
    }

    private sealed record GoldenPathEmission(
        byte[] Pdf,
        string Ixbrl,
        FilingRegimeService.FilingRequirements Regime,
        FinancialStatementsService.ReadinessScore Readiness,
        FinancialStatementsService.BalanceSheet BalanceSheet,
        FinancialStatementsService.ProfitAndLoss ProfitAndLoss);

    // Shared tail of the golden path: classify -> determine regime -> generate + approve adjustments ->
    // generate notes -> compute statements -> generate the accounts PDF and iXBRL. The PDF call itself
    // asserts final-output readiness, so this only succeeds when the whole pipeline is filing-ready.
    private static async Task<GoldenPathEmission> ClassifyAdjustNotesAndEmitAsync(
        AccountsDbContext db, int companyId, int periodId, ElectedRegime electedRegime)
    {
        await new SizeClassificationService(db, Options.Create(new SizeThresholdConfig()))
            .ClassifyAsync(companyId, periodId);
        var regime = await new FilingRegimeService(db).DetermineAsync(companyId, periodId, electedRegime);
        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(companyId, periodId);

        // A nil-adjustment period still needs at least one adjustment row for the readiness check.
        if (!await db.Adjustments.AnyAsync(a => a.PeriodId == periodId))
        {
            db.Adjustments.Add(new Adjustment
            {
                PeriodId = periodId,
                Description = "No year-end adjustment required",
                Amount = 0m,
                ImpactOnProfit = 0m,
                Source = AdjustmentSource.Manual,
                CreatedBy = "Accounts reviewer"
            });
            await db.SaveChangesAsync();
        }
        // Reviewer approves all proposed adjustments.
        foreach (var adj in await db.Adjustments.Where(a => a.PeriodId == periodId && a.ApprovedAt == null).ToListAsync())
        {
            adj.ApprovedBy = "Accounts reviewer";
            adj.ApprovedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();

        await new NotesDisclosureService(db).GenerateNotesAsync(companyId, periodId);

        var statements = new FinancialStatementsService(db);
        var readiness = await statements.GetReadinessScoreAsync(companyId, periodId);
        var bs = await statements.GetBalanceSheetAsync(companyId, periodId);
        var pl = await statements.GetProfitAndLossAsync(companyId, periodId);
        var pdf = await new DocumentGeneratorService(db, statements).GenerateAccountsPackageAsync(companyId, periodId);
        var ixbrl = Encoding.UTF8.GetString(await new IxbrlService(db, statements).GenerateIxbrlAsync(companyId, periodId));
        return new GoldenPathEmission(pdf, ixbrl, regime, readiness, bs, pl);
    }

    // Extract the rendered text from a generated PDF so tests can assert on real figures, names and
    // statutory wording (tests-pdf-content-verified), not just the %PDF magic bytes. PdfPig is pure
    // managed (no native deps), so this runs on Linux CI. GetWords() reconstructs words from glyphs;
    // joining them with single spaces yields stable, kerning-independent tokens to match against.
    private static string ExtractPdfText(byte[] pdf)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
            sb.Append(' ').Append(string.Join(' ', page.GetWords().Select(w => w.Text)));
        return sb.ToString();
    }

    private static string MakeGenericCsv(params (string Date, string Description, decimal Amount)[] rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Date,Description,Amount");
        foreach (var row in rows)
            sb.AppendLine($"{row.Date},{row.Description},{row.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}");
        return sb.ToString();
    }

    private static void AssertWellFormedXml(string xhtml)
    {
        // DOCTYPE tolerated; no undeclared HTML entities. Throws if the iXBRL is not well-formed XML.
        var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore };
        using var reader = System.Xml.XmlReader.Create(new StringReader(xhtml), settings);
        var doc = System.Xml.Linq.XDocument.Load(reader);
        Assert.NotNull(doc.Root);
    }

    [Fact]
    public async Task ExceptionMiddleware_LogsCorrelationIdAndDoesNotLeakSecretsInProduction()
    {
        // G6 (failures diagnosable): an unhandled error must be triageable from a support ticket
        // without a repro — the response carries a correlation id that also appears in the server log,
        // while no exception detail (which may carry secrets/PII) leaks to the client in production.
        var logger = new CapturingLogger<ExceptionMiddleware>();
        const string secret = "Server=db;Password=hunter2-SECRET";
        RequestDelegate next = _ => throw new InvalidOperationException(secret);
        var middleware = new ExceptionMiddleware(next, logger);

        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/companies/1/periods/2/adjustments/generate";
        context.TraceIdentifier = "corr-id-7f3a";
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestEnvironment("Production"));
        context.RequestServices = services.BuildServiceProvider();
        using var body = new MemoryStream();
        context.Response.Body = body;

        await middleware.InvokeAsync(context);

        Assert.Equal(500, context.Response.StatusCode);

        body.Position = 0;
        var responseJson = await new StreamReader(body).ReadToEndAsync();
        using var payload = JsonDocument.Parse(responseJson);
        // Client gets the correlation id and a generic message — never the exception or secret.
        Assert.Equal("corr-id-7f3a", payload.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal("An internal error occurred. Please try again.", payload.RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain("hunter2", responseJson);
        Assert.DoesNotContain(secret, responseJson);

        // Server log is enough to triage: Error level, the request method+path, the same correlation id,
        // and the full exception (the secret is allowed server-side).
        var logged = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("corr-id-7f3a", logged.Message);
        Assert.Contains("POST", logged.Message);
        Assert.Contains("/api/companies/1/periods/2/adjustments/generate", logged.Message);
        Assert.NotNull(logged.Exception);
        Assert.Contains(secret, logged.Exception!.Message);
    }

    [Fact]
    public async Task Adjustments_AccrueLoanInterestAndKeepBalanceSheetBalanced()
    {
        // BL-07: the engine accrues interest on outstanding loans. It posts an interest expense and a
        // matching accrual liability, so the balance sheet stays balanced.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var companyId = period.CompanyId;
        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(companyId);
        int Cat(string code) => categories.Single(c => c.Code == code).Id;

        db.ShareCapitals.Add(new ShareCapital { CompanyId = companyId, ShareClass = "Ordinary", NumberIssued = 100, NominalValue = 1m, TotalValue = 100m, IssueDate = new DateOnly(2025, 1, 1) });
        var bank = new BankAccount { CompanyId = companyId, Name = "Current Account", OpeningBalance = 100m, OpeningBalanceDate = new DateOnly(2025, 1, 1) };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        // €10,000 loan drawn (cash in, coded to the loan liability so it is outside the P&L), 5% rate.
        db.ImportedTransactions.Add(new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 1, 2), Description = "Loan drawdown", Amount = 10_000m, CategoryId = Cat("2700") });
        db.Loans.Add(new Loan
        {
            CompanyId = companyId,
            Lender = "Bank of Ireland",
            OriginalAmount = 10_000m,
            Balance = 10_000m,
            InterestRate = 5m,
            DueWithinYear = 0m,
            DueAfterYear = 10_000m,
            DrawdownDate = new DateOnly(2025, 1, 2),
            BalanceAsOfDate = period.PeriodEnd
        });
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(companyId, period.Id);

        // The interest accrual exists as a creditor and as an interest expense adjustment of €500.
        var interestAccrual = await db.Creditors.SingleAsync(c => c.PeriodId == period.Id && c.Name.Contains("interest", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(500m, interestAccrual.Amount); // 10,000 * 5% * full year
        Assert.Equal(CreditorType.Accrual, interestAccrual.Type);
        Assert.Contains(await db.Adjustments.Where(a => a.PeriodId == period.Id).ToListAsync(),
            a => a.Description.Contains("interest", StringComparison.OrdinalIgnoreCase) && a.ImpactOnProfit == -500m);

        var balanceSheet = await new FinancialStatementsService(db).GetBalanceSheetAsync(companyId, period.Id);
        Assert.Equal(0m, balanceSheet.CapitalAndReserves.UnexplainedDifference);
        Assert.True(balanceSheet.Balances);
        Assert.Equal(500m, balanceSheet.CreditorsWithinYear.Accruals);
    }

    [Fact]
    public async Task Adjustments_ReclassifyOverdrawnDirectorLoanAsReceivable()
    {
        // BL-07: an overdrawn director's loan account (director owes the company) is reclassified to a
        // receivable. The adjustment is P&L-neutral — it only moves a balance between presentation accounts.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var director = await db.CompanyOfficers.FirstAsync(o => o.CompanyId == period.CompanyId && o.Role == OfficerRole.Director);
        db.DirectorLoans.Add(new DirectorLoan
        {
            PeriodId = period.Id,
            DirectorId = director.Id,
            OpeningBalance = 0m,
            Advances = 3_000m,
            Repayments = 0m,
            ClosingBalance = 3_000m
        });
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(period.CompanyId, period.Id);

        var reclass = await db.Adjustments.SingleAsync(a =>
            a.PeriodId == period.Id && a.Description.Contains("Director loan reclassification", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3_000m, reclass.Amount);
        Assert.Equal(0m, reclass.ImpactOnProfit);
        Assert.NotNull(reclass.DebitCategoryId);
        Assert.NotNull(reclass.CreditCategoryId);
        Assert.NotEqual(reclass.DebitCategoryId, reclass.CreditCategoryId);
    }

    [Fact]
    public async Task ProfitAndLoss_IncludesNonTurnoverIncomeAsOtherIncomeAndTaxesIt()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var sales = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var rental = AddCategory(db, period.CompanyId, "8000", "Rental income", AccountCategoryType.Income);
        rental.IsNonTradingIncome = true;
        var bank = new BankAccount { CompanyId = period.CompanyId, Name = "Current Account", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        // Trading sales (4xxx turnover) plus rental income coded outside turnover (8xxx).
        db.ImportedTransactions.AddRange(
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 1), Description = "Trading sales", Amount = 10_000m, CategoryId = sales.Id },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 2), Description = "Rent received", Amount = 4_000m, CategoryId = rental.Id });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var pl = await statements.GetProfitAndLossAsync(period.CompanyId, period.Id);

        Assert.Equal(10_000m, pl.Turnover);        // only 4xxx counts as turnover
        Assert.Equal(4_000m, pl.OtherIncome);      // 8xxx rental is other income, not dropped
        Assert.Equal(14_000m, pl.ProfitBeforeTax); // both reach profit before tax

        // The non-turnover, non-trading income is taxed at 25%, the trading balance at 12.5%.
        var tax = await new TaxComputationService(db, statements).ComputeAsync(period.CompanyId, period.Id);
        Assert.Equal(14_000m, tax.TaxableProfit);
        Assert.Equal(1_250m, tax.CorporationTaxAt125);
        Assert.Equal(1_000m, tax.CorporationTaxAt25);
    }

    [Fact]
    public async Task LoanSnapshots_ForFutureEffectiveLoansDoNotLeakIntoCurrentPeriodReporting()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Small,
            CanUseMicro = false,
            CanFileAbridged = true,
            AuditExempt = true
        });
        var futureLoan = new Loan
        {
            CompanyId = period.CompanyId,
            Lender = "Future Bank",
            OriginalAmount = 20_000m,
            Balance = 20_000m,
            DueWithinYear = 2_000m,
            DueAfterYear = 18_000m
        };
        SetRequiredDate(futureLoan, "DrawdownDate", period.PeriodEnd.AddDays(1));
        SetRequiredDate(futureLoan, "BalanceAsOfDate", period.PeriodEnd.AddDays(1));
        db.Loans.Add(futureLoan);
        await db.SaveChangesAsync();
        db.LoanBalanceSnapshots.Add(new LoanBalanceSnapshot
        {
            LoanId = futureLoan.Id,
            PeriodId = period.Id,
            OpeningBalance = 0m,
            Drawdowns = 20_000m,
            Repayments = 0m,
            ClosingBalance = 20_000m,
            DueWithinYear = 2_000m,
            DueAfterYear = 18_000m
        });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);

        var balanceSheet = await statements.GetBalanceSheetAsync(period.CompanyId, period.Id);
        var cashFlow = await statements.GetCashFlowStatementAsync(period.CompanyId, period.Id);
        var notes = await new NotesDisclosureService(db).GenerateNotesAsync(period.CompanyId, period.Id);

        Assert.Equal(0m, balanceSheet.CreditorsWithinYear.OtherCreditors);
        Assert.Equal(0m, balanceSheet.CreditorsAfterYear.Loans);
        Assert.Equal(0m, cashFlow.LoanDrawdowns);
        Assert.DoesNotContain(notes, n => n.Title == "Creditors: Amounts Falling Due After More Than One Year");
        Assert.DoesNotContain("Future Bank", string.Join("\n", notes.Select(n => n.Content)));
    }

    [Fact]
    public async Task Statements_UseLoanBalanceSnapshotsForPeriodLiabilitiesAndRepayments()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: false);
        var loan = new Loan
        {
            CompanyId = period.CompanyId,
            Lender = "Current Bank",
            OriginalAmount = 100m,
            Balance = 60m,
            DueWithinYear = 15m,
            DueAfterYear = 45m,
            DrawdownDate = period.PeriodStart.AddYears(-1),
            BalanceAsOfDate = period.PeriodEnd
        };
        db.Loans.Add(loan);
        await db.SaveChangesAsync();

        var snapshotType = Type.GetType("Accounts.Api.Entities.LoanBalanceSnapshot, Accounts.Api")
            ?? throw new InvalidOperationException("LoanBalanceSnapshot is required for period-specific loan cash flow.");
        var snapshot = Activator.CreateInstance(snapshotType)!;
        SetRequiredValue(snapshot, "LoanId", loan.Id);
        SetRequiredValue(snapshot, "PeriodId", period.Id);
        SetRequiredValue(snapshot, "OpeningBalance", 70m);
        SetRequiredValue(snapshot, "Drawdowns", 0m);
        SetRequiredValue(snapshot, "Repayments", 10m);
        SetRequiredValue(snapshot, "ClosingBalance", 60m);
        SetRequiredValue(snapshot, "DueWithinYear", 15m);
        SetRequiredValue(snapshot, "DueAfterYear", 45m);
        db.Add(snapshot);
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var balanceSheet = await service.GetBalanceSheetAsync(period.CompanyId, period.Id);
        var cashFlow = await service.GetCashFlowStatementAsync(period.CompanyId, period.Id);

        Assert.Equal(15m, balanceSheet.CreditorsWithinYear.OtherCreditors);
        Assert.Equal(45m, balanceSheet.CreditorsAfterYear.Loans);
        Assert.Equal(10m, cashFlow.LoanRepayments);
    }

    [Fact]
    public async Task CashFlow_DoesNotInferRepaymentsForPriorLoansWithoutSnapshot()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: false);
        db.Loans.Add(new Loan
        {
            CompanyId = period.CompanyId,
            Lender = "Current Bank",
            OriginalAmount = 100m,
            Balance = 60m,
            DueWithinYear = 15m,
            DueAfterYear = 45m,
            DrawdownDate = period.PeriodStart.AddYears(-1),
            BalanceAsOfDate = period.PeriodEnd
        });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var cashFlow = await service.GetCashFlowStatementAsync(period.CompanyId, period.Id);

        Assert.Equal(0m, cashFlow.LoanRepayments);
    }

    [Fact]
    public async Task Readiness_UsesActualBalanceSheetEquation()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            CalculatedClass = CompanySizeClass.Micro,
            Turnover = 0m,
            BalanceSheetTotal = 100m,
            AvgEmployees = 0
        });
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        db.BankAccounts.Add(new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 100m,
            OpeningBalanceDate = period.PeriodStart
        });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var readiness = await service.GetReadinessScoreAsync(period.CompanyId, period.Id);

        Assert.False(readiness.BalanceSheetBalances);
        Assert.Contains(readiness.Warnings, w => w.Contains("Balance sheet does not balance"));
    }

    [Fact]
    public async Task Readiness_RequiresGoingConcernAssessmentAndReviewedNotes()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.GoingConcernConfirmed = false;
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            CalculatedClass = CompanySizeClass.Micro,
            Turnover = 0m,
            BalanceSheetTotal = 1m,
            AvgEmployees = 0
        });
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var readiness = await service.GetReadinessScoreAsync(period.CompanyId, period.Id);

        Assert.Contains("Going concern assessment not completed", readiness.MissingItems);
        Assert.Contains("Notes to the financial statements not generated or reviewed", readiness.MissingItems);
    }

    [Fact]
    public async Task Readiness_RequiresExplicitReviewOfNilYearEndSections()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);

        var service = new FinancialStatementsService(db);

        var readiness = await service.GetReadinessScoreAsync(period.CompanyId, period.Id);

        Assert.Contains("Debtors and other receivables not reviewed", readiness.MissingItems);
        Assert.Contains("Creditors, accruals and payables not reviewed", readiness.MissingItems);
        Assert.Contains("Payroll and staff status not confirmed", readiness.MissingItems);
        Assert.Contains("Dividends not reviewed", readiness.MissingItems);
        Assert.Contains("Post balance sheet events, related parties, or contingencies not reviewed", readiness.MissingItems);
        Assert.Contains("Going concern assessment not completed", readiness.MissingItems);
    }

    [Fact]
    public async Task Readiness_AcceptsExplicitNilReviewConfirmations()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.YearEndReviewConfirmations.AddRange(
            NilReview(period.Id, "debtors"),
            NilReview(period.Id, "creditors"),
            NilReview(period.Id, "payroll"),
            NilReview(period.Id, "tax"),
            NilReview(period.Id, "dividends"),
            NilReview(period.Id, "post-balance-sheet-events"),
            NilReview(period.Id, "related-parties"),
            NilReview(period.Id, "contingent-liabilities"),
            NilReview(period.Id, "going-concern"));
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var readiness = await service.GetReadinessScoreAsync(period.CompanyId, period.Id);

        Assert.DoesNotContain("Debtors and other receivables not reviewed", readiness.MissingItems);
        Assert.DoesNotContain("Creditors, accruals and payables not reviewed", readiness.MissingItems);
        Assert.DoesNotContain("Payroll and staff status not confirmed", readiness.MissingItems);
        Assert.DoesNotContain("Tax balances not reviewed", readiness.MissingItems);
        Assert.DoesNotContain("Dividends not reviewed", readiness.MissingItems);
        Assert.DoesNotContain("Post balance sheet events, related parties, or contingencies not reviewed", readiness.MissingItems);
        Assert.DoesNotContain("Going concern assessment not completed", readiness.MissingItems);
    }

    [Fact]
    public async Task MicroCroPack_IsDistinctFromApprovalPack()
    {
        await using var db = CreateDbContext();
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

        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);

        var approvalPack = await documents.GenerateAccountsPackageAsync(period.CompanyId, period.Id);
        var croPack = await documents.GenerateCroFilingPackAsync(period.CompanyId, period.Id);

        Assert.NotEmpty(approvalPack);
        Assert.NotEmpty(croPack);
        Assert.NotEqual(approvalPack.Length, croPack.Length);
    }

    [Fact]
    public async Task AbridgedSmallCroPack_PdfContainsSection352WordingNameAndPeriodEnd()
    {
        // tests-pdf-content-verified (abridged branch): a SmallAbridged CRO filing pack must carry the
        // s.352 abridged-filing exemption wording, the company legal name and the period-end date.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.SmallAbridged,
            CanUseMicro = false,
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

        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);
        var croPack = await documents.GenerateCroFilingPackAsync(period.CompanyId, period.Id);

        var pdfText = ExtractPdfText(croPack);
        Assert.Contains("Example Micro Limited", pdfText);
        Assert.Contains(period.PeriodEnd.ToString("dd MMMM yyyy"), pdfText);
        Assert.Contains("352", pdfText); // s.352 Companies Act 2014 abridged-filing exemption
    }

    [Fact]
    public void AuditExemptionStatement_IsPrintedOnlyWhenConfirmedAvailable()
    {
        Assert.True(DocumentGeneratorService.ShouldIncludeAuditExemptionStatement(ElectedRegime.Micro, auditExempt: true));
        Assert.True(DocumentGeneratorService.ShouldIncludeAuditExemptionStatement(ElectedRegime.SmallAbridged, auditExempt: true));
        Assert.False(DocumentGeneratorService.ShouldIncludeAuditExemptionStatement(ElectedRegime.Micro, auditExempt: false));
        Assert.False(DocumentGeneratorService.ShouldIncludeAuditExemptionStatement(ElectedRegime.Medium, auditExempt: true));
    }

    [Theory]
    [InlineData(ElectedRegime.Medium)]
    [InlineData(ElectedRegime.Full)]
    public void AccountsPackage_MediumAndFullIncludeEveryRequiredPrimaryStatement(ElectedRegime regime)
    {
        // BL-02 / BL-03: Medium and Full packages must render a Cash Flow Statement, a Statement of
        // Changes in Equity and an Auditor's Report — the sections the Small PDF omitted. The rendered
        // section set must cover every primary statement the filing regime requires.
        var rendered = DocumentGeneratorService.GetIncludedPrimaryStatements(regime, DocumentPackagePurpose.StatutoryApproval, auditExempt: false);
        Assert.Contains("Cash Flow Statement", rendered);
        Assert.Contains("Statement of Changes in Equity", rendered);
        Assert.Contains("Independent Auditor's Report", rendered);
        Assert.Contains("Profit and Loss Account", rendered);
        Assert.Contains("Balance Sheet", rendered);

        // Cross-check against the regime contract: every required primary statement is covered.
        var required = FilingRegimeService.GetRequiredStatements(regime, CompanySizeClass.Medium);
        foreach (var heading in new[] { "Cash Flow Statement", "Statement of Changes in Equity", "Auditor's Report" })
        {
            Assert.Contains(required, r => r.Contains(heading, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(rendered, r => r.Contains(heading, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void AccountsPackage_SmallAuditExemptOmitsCashFlowEquityAndAuditorsReport()
    {
        // BL-02 / BL-03 negative: the small audit-exempt package must NOT carry the Medium/Full-only
        // sections, and an audit-exempt company gets no auditor's report.
        var rendered = DocumentGeneratorService.GetIncludedPrimaryStatements(ElectedRegime.Small, DocumentPackagePurpose.StatutoryApproval, auditExempt: true);
        Assert.DoesNotContain("Cash Flow Statement", rendered);
        Assert.DoesNotContain("Statement of Changes in Equity", rendered);
        Assert.DoesNotContain("Independent Auditor's Report", rendered);
    }

    [Fact]
    public void AccountsPackage_AuditorsReportTogglesWithAuditExemption()
    {
        // BL-03: the auditor's report is present exactly when the company is not audit-exempt.
        var audited = DocumentGeneratorService.GetIncludedPrimaryStatements(ElectedRegime.Medium, DocumentPackagePurpose.StatutoryApproval, auditExempt: false);
        var exempt = DocumentGeneratorService.GetIncludedPrimaryStatements(ElectedRegime.Medium, DocumentPackagePurpose.StatutoryApproval, auditExempt: true);
        Assert.Contains("Independent Auditor's Report", audited);
        Assert.DoesNotContain("Independent Auditor's Report", exempt);
    }

    [Fact]
    public async Task MediumAccountsPdf_RendersExtraStatementsAndIsLargerThanSmall()
    {
        // BL-18: the Medium PDF actually generates past the readiness gate (so the new section
        // composers don't throw) and, carrying the extra statements, is larger than the small PDF
        // for the same underlying data.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var regime = new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Medium,
            CanUseMicro = false,
            CanFileAbridged = false,
            AuditExempt = false
        };
        db.FilingRegimes.Add(regime);
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

        var documents = new DocumentGeneratorService(db, new FinancialStatementsService(db));

        var mediumPdf = await documents.GenerateAccountsPackageAsync(period.CompanyId, period.Id);
        Assert.NotEmpty(mediumPdf);

        // Regenerate the same data as a small audit-exempt pack (no cash flow, SOCIE or auditor's report).
        regime.ElectedRegime = ElectedRegime.Small;
        regime.AuditExempt = true;
        await db.SaveChangesAsync();
        var smallPdf = await documents.GenerateAccountsPackageAsync(period.CompanyId, period.Id);
        Assert.NotEmpty(smallPdf);

        Assert.True(mediumPdf.Length > smallPdf.Length,
            $"Expected the Medium pack ({mediumPdf.Length} bytes) to be larger than the small pack ({smallPdf.Length} bytes) because it carries the cash flow, equity and auditor's report sections.");
    }

    [Fact]
    public async Task Notes_MediumRegimeAddsFullerDisclosureSetBeyondSmall()
    {
        // BL-13: Medium/Full regimes require notes a small company does not — turnover analysis, tax
        // on profit, financial instruments and capital commitments — rendered even when nil.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Medium,
            CanUseMicro = false,
            CanFileAbridged = false,
            AuditExempt = false
        });
        await db.SaveChangesAsync();

        var mediumNotes = await new NotesDisclosureService(db).GenerateNotesAsync(period.CompanyId, period.Id);
        foreach (var title in new[] { "Turnover", "Tax on Profit on Ordinary Activities", "Financial Instruments", "Capital Commitments" })
            Assert.Contains(mediumNotes, n => n.Title == title);

        // The same company on the small regime does not get the Medium/Full-only notes.
        var regime = await db.FilingRegimes.SingleAsync(r => r.PeriodId == period.Id);
        regime.ElectedRegime = ElectedRegime.Small;
        await db.SaveChangesAsync();
        var smallNotes = await new NotesDisclosureService(db).GenerateNotesAsync(period.CompanyId, period.Id);
        Assert.DoesNotContain(smallNotes, n => n.Title == "Financial Instruments");
        Assert.DoesNotContain(smallNotes, n => n.Title == "Capital Commitments");
    }

    [Fact]
    public void ApprovalPack_IncludesProfitAndLossForSmallAbridgedButCroPackDoesNot()
    {
        Assert.True(DocumentGeneratorService.ShouldIncludeProfitAndLoss(ElectedRegime.SmallAbridged, DocumentPackagePurpose.StatutoryApproval));
        Assert.True(DocumentGeneratorService.ShouldIncludeProfitAndLoss(ElectedRegime.SmallAbridged, DocumentPackagePurpose.AgmApproval));
        Assert.False(DocumentGeneratorService.ShouldIncludeProfitAndLoss(ElectedRegime.SmallAbridged, DocumentPackagePurpose.CroFiling));
        Assert.False(DocumentGeneratorService.ShouldIncludeProfitAndLoss(ElectedRegime.Micro, DocumentPackagePurpose.StatutoryApproval));

        var approvalSubtitle = DocumentGeneratorService.PackageRegimeSubtitle(ElectedRegime.SmallAbridged, DocumentPackagePurpose.AgmApproval);
        var croSubtitle = DocumentGeneratorService.PackageRegimeSubtitle(ElectedRegime.SmallAbridged, DocumentPackagePurpose.CroFiling);

        Assert.DoesNotContain("Abridged", approvalSubtitle);
        Assert.DoesNotContain("CRO", approvalSubtitle);
        Assert.Contains("Abridged", croSubtitle);
        Assert.Contains("CRO", croSubtitle);
    }

    [Fact]
    public async Task CroFilingPack_RequiresConfirmedFilingRegime()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => documents.GenerateCroFilingPackAsync(period.CompanyId, period.Id));

        Assert.Contains("Confirm the filing regime", error.Message);
    }

    [Fact]
    public async Task CroFilingPack_BlocksWhenReadinessItemsRemainOpen()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => documents.GenerateCroFilingPackAsync(period.CompanyId, period.Id));

        Assert.Contains("Cannot generate final CRO filing pack", error.Message);
        Assert.Contains("balance sheet does not balance", error.Message);
    }

    [Fact]
    public async Task SignaturePage_BlocksWhenReadinessItemsRemainOpen()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        await db.SaveChangesAsync();
        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => documents.GenerateSignaturePageAsync(period.CompanyId, period.Id));

        Assert.Contains("Cannot generate final CRO signature page", error.Message);
        Assert.Contains("balance sheet does not balance", error.Message);
    }

    [Fact]
    public async Task SignaturePage_RejectsResignedOfficerSignatories()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var officers = await db.CompanyOfficers
            .Where(o => o.CompanyId == period.CompanyId)
            .ToListAsync();
        foreach (var officer in officers)
            officer.ResignedDate = period.PeriodStart.AddDays(-1);
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
        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => documents.GenerateSignaturePageAsync(period.CompanyId, period.Id));

        Assert.Contains("active director", error.Message);
    }

    [Theory]
    [InlineData("accounts package")]
    [InlineData("AGM approval pack")]
    public async Task FinalApprovalPacks_BlockWhenReadinessItemsRemainOpen(string packageName)
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        await db.SaveChangesAsync();
        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);

        var error = packageName == "accounts package"
            ? await Assert.ThrowsAsync<BusinessRuleException>(() => documents.GenerateAccountsPackageAsync(period.CompanyId, period.Id))
            : await Assert.ThrowsAsync<BusinessRuleException>(() => documents.GenerateAgmApprovalPackAsync(period.CompanyId, period.Id));

        Assert.Contains($"Cannot generate final {packageName}", error.Message);
        Assert.Contains("balance sheet does not balance", error.Message);
        Assert.Contains("No transactions imported", error.Message);
    }

    [Fact]
    public async Task FinalOutputs_BlockWhenReadinessWarningsRemainOpen()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        await MakePeriodReadyForCroDocumentsAsync(db, period);
        var uncategorisedBankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Uncategorised current account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(uncategorisedBankAccount);
        await db.SaveChangesAsync();
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = uncategorisedBankAccount.Id,
            PeriodId = period.Id,
            Date = period.PeriodStart.AddDays(10),
            Description = "Uncategorised filing blocker",
            Amount = 42m
        });
        await db.SaveChangesAsync();
        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => documents.GenerateAccountsPackageAsync(period.CompanyId, period.Id));

        Assert.Contains("transactions not yet categorised", error.Message);
    }

    [Fact]
    public void DocumentEndpoints_UseGuardedFinalDocumentMethods()
    {
        var source = File
            .ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "DocumentEndpoints.cs"))
            .Replace("\r\n", "\n");

        Assert.Contains("GenerateAccountsPackageAsync(companyId, periodId)", source);
        Assert.Contains("GenerateAgmApprovalPackAsync(companyId, periodId)", source);
        Assert.Contains("GenerateCroFilingPackAsync(companyId, periodId)", source);
        Assert.Contains("GenerateSignaturePageAsync(companyId, periodId)", source);
        Assert.DoesNotContain("GenerateAccountsPackageAsync(periodId)", source);
        Assert.DoesNotContain("GenerateAgmApprovalPackAsync(periodId)", source);
        Assert.DoesNotContain("GenerateCroFilingPackAsync(periodId)", source);
        Assert.DoesNotContain("GenerateSignaturePageAsync(periodId)", source);
        Assert.DoesNotContain("GenerateAccountsPackageAsync(periodId, DocumentPackagePurpose", source);
        Assert.DoesNotContain("GenerateIxbrlAsync", source);

        foreach (var marker in new[] { "group.MapPost(\"/cro-filing-pack\"", "group.MapPost(\"/signature-page\"" })
        {
            var snippet = DocumentEndpointSnippet(source, marker);
            Assert.Contains("ApiAccessService apiAccess", snippet);
            Assert.Contains("CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", snippet);
            Assert.Contains("EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", snippet);
            Assert.Contains("RecordCroDocumentGeneratedAsync", snippet);
            AssertOccursBefore(snippet, "CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)");
            AssertOccursBefore(snippet, "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", "RecordCroDocumentGeneratedAsync");
        }

        foreach (var marker in new[] { "group.MapGet(\"/cro-filing-pack\"", "group.MapGet(\"/signature-page\"" })
        {
            var snippet = DocumentEndpointSnippet(source, marker);
            Assert.DoesNotContain("ApiAccessService apiAccess", snippet);
            Assert.DoesNotContain("EndpointRequestAuthorization.AuthorizeCurrentRequest", snippet);
            Assert.DoesNotContain("RecordCroDocumentGeneratedAsync", snippet);
        }

        static string DocumentEndpointSnippet(string source, string marker)
        {
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Expected to find document endpoint marker {marker}.");
            var next = Regex.Match(source[(start + marker.Length)..], @"\n\s*(?:group\.Map(?:Get|Post|Put|Delete)|private static)", RegexOptions.None, TimeSpan.FromSeconds(1));
            return next.Success
                ? source[start..(start + marker.Length + next.Index)]
                : source[start..];
        }

        static void AssertOccursBefore(string snippet, string first, string second)
        {
            var firstIndex = snippet.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = snippet.IndexOf(second, StringComparison.Ordinal);
            Assert.True(firstIndex >= 0, $"Expected snippet to contain '{first}'.");
            Assert.True(secondIndex >= 0, $"Expected snippet to contain '{second}'.");
            Assert.True(firstIndex < secondIndex, $"Expected '{first}' before '{second}'.");
        }
    }

    [Theory]
    [InlineData(nameof(DocumentGeneratorService.GenerateAccountsPackageAsync))]
    [InlineData(nameof(DocumentGeneratorService.GenerateAgmApprovalPackAsync))]
    [InlineData(nameof(DocumentGeneratorService.GenerateCroFilingPackAsync))]
    [InlineData(nameof(DocumentGeneratorService.GenerateSignaturePageAsync))]
    public async Task FinalDocumentServices_RejectMismatchedCompanyPeriod(string methodName)
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var documents = new DocumentGeneratorService(db, new FinancialStatementsService(db));
        var method = typeof(DocumentGeneratorService).GetMethod(methodName, [typeof(int), typeof(int)]);

        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task<byte[]>>(method.Invoke(documents, [period.CompanyId, otherPeriod.Id]));
        await Assert.ThrowsAsync<ResourceNotFoundException>(async () => await task);
    }

    [Fact]
    public void DocumentGeneratorService_RequiresCompanyIdForFinalDocumentMethods()
    {
        var methodNames = new HashSet<string>
        {
            nameof(DocumentGeneratorService.GenerateAccountsPackageAsync),
            nameof(DocumentGeneratorService.GenerateAgmApprovalPackAsync),
            nameof(DocumentGeneratorService.GenerateCroFilingPackAsync),
            nameof(DocumentGeneratorService.GenerateSignaturePageAsync)
        };
        var methods = typeof(DocumentGeneratorService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => methodNames.Contains(m.Name))
            .Select(m => new { m.Name, Parameters = m.GetParameters().Select(p => p.ParameterType).ToArray() })
            .ToList();

        foreach (var methodName in methodNames)
        {
            Assert.Contains(methods, m =>
                m.Name == methodName
                && m.Parameters.Length == 2
                && m.Parameters[0] == typeof(int)
                && m.Parameters[1] == typeof(int));
            Assert.DoesNotContain(methods, m =>
                m.Name == methodName
                && m.Parameters.Length == 1
                && m.Parameters[0] == typeof(int));
        }
    }

    [Theory]
    [InlineData(nameof(FinancialStatementsService.GetTrialBalanceAsync), typeof(List<FinancialStatementsService.TrialBalanceLine>))]
    [InlineData(nameof(FinancialStatementsService.GetProfitAndLossAsync), typeof(FinancialStatementsService.ProfitAndLoss))]
    [InlineData(nameof(FinancialStatementsService.GetBalanceSheetAsync), typeof(FinancialStatementsService.BalanceSheet))]
    [InlineData(nameof(FinancialStatementsService.GetReadinessScoreAsync), typeof(FinancialStatementsService.ReadinessScore))]
    [InlineData(nameof(FinancialStatementsService.GetStatementSourcesAsync), typeof(List<FinancialStatementsService.StatementSourceSummary>))]
    [InlineData(nameof(FinancialStatementsService.GetCashFlowStatementAsync), typeof(FinancialStatementsService.CashFlowStatement))]
    [InlineData(nameof(FinancialStatementsService.GetEquityChangesAsync), typeof(FinancialStatementsService.EquityChanges))]
    public async Task StatementServices_RejectMismatchedCompanyPeriod(string methodName, Type resultType)
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var service = new FinancialStatementsService(db);
        var method = typeof(FinancialStatementsService).GetMethod(methodName, [typeof(int), typeof(int)]);

        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method.Invoke(service, [period.CompanyId, otherPeriod.Id]));
        Assert.True(typeof(Task<>).MakeGenericType(resultType).IsInstanceOfType(task));
        await Assert.ThrowsAsync<ResourceNotFoundException>(async () => await task);
    }

    [Fact]
    public void StatementEndpoints_UseCompanyAwareServiceCalls()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "StatementsEndpoints.cs"));

        Assert.Contains("GetTrialBalanceAsync(companyId, periodId)", source);
        Assert.Contains("GetProfitAndLossAsync(companyId, periodId)", source);
        Assert.Contains("GetBalanceSheetAsync(companyId, periodId)", source);
        Assert.Contains("GetReadinessScoreAsync(companyId, periodId)", source);
        Assert.Contains("GetStatementSourcesAsync(companyId, periodId)", source);
        Assert.Contains("GetCashFlowStatementAsync(companyId, periodId)", source);
        Assert.Contains("GetEquityChangesAsync(companyId, periodId)", source);
        Assert.DoesNotContain("GetTrialBalanceAsync(periodId)", source);
        Assert.DoesNotContain("GetProfitAndLossAsync(periodId)", source);
        Assert.DoesNotContain("GetBalanceSheetAsync(periodId)", source);
        Assert.DoesNotContain("GetReadinessScoreAsync(periodId)", source);
        Assert.DoesNotContain("GetStatementSourcesAsync(periodId)", source);
        Assert.DoesNotContain("GetCashFlowStatementAsync(periodId)", source);
        Assert.DoesNotContain("GetEquityChangesAsync(periodId)", source);
    }

    [Fact]
    public void FinancialStatementsService_RequiresCompanyIdForPublicStatementOutputs()
    {
        var methodNames = new HashSet<string>
        {
            nameof(FinancialStatementsService.GetTrialBalanceAsync),
            nameof(FinancialStatementsService.GetProfitAndLossAsync),
            nameof(FinancialStatementsService.GetBalanceSheetAsync),
            nameof(FinancialStatementsService.GetReadinessScoreAsync),
            nameof(FinancialStatementsService.GetStatementSourcesAsync),
            nameof(FinancialStatementsService.GetCashFlowStatementAsync),
            nameof(FinancialStatementsService.GetEquityChangesAsync)
        };
        var methods = typeof(FinancialStatementsService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => methodNames.Contains(m.Name))
            .Select(m => new { m.Name, Parameters = m.GetParameters().Select(p => p.ParameterType).ToArray() })
            .ToList();

        foreach (var methodName in methodNames)
        {
            Assert.Contains(methods, m =>
                m.Name == methodName
                && m.Parameters.Length == 2
                && m.Parameters[0] == typeof(int)
                && m.Parameters[1] == typeof(int));
            Assert.DoesNotContain(methods, m =>
                m.Name == methodName
                && m.Parameters.Length == 1
                && m.Parameters[0] == typeof(int));
        }
    }

    [Fact]
    public async Task TrialBalance_PostsImportedTransactionsWithImplicitBankSide()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankCategory = AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var salesCategory = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bankAccount.Id,
            PeriodId = period.Id,
            Date = new DateOnly(2025, 3, 1),
            Description = "Customer receipt",
            Amount = 100m,
            CategoryId = salesCategory.Id
        });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var trialBalance = await service.GetTrialBalanceAsync(period.CompanyId, period.Id);

        Assert.Equal(100m, trialBalance.Single(l => l.Code == bankCategory.Code).Debit);
        Assert.Equal(100m, trialBalance.Single(l => l.Code == salesCategory.Code).Credit);
        Assert.Equal(trialBalance.Sum(l => l.Debit), trialBalance.Sum(l => l.Credit));
    }

    [Fact]
    public async Task TrialBalance_IncludesReviewedOpeningBalancesAndBankOpeningSide()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankCategory = AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var retainedCategory = AddCategory(db, period.CompanyId, "3100", "Retained Earnings", AccountCategoryType.Equity);
        db.BankAccounts.Add(new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 500m,
            OpeningBalanceDate = period.PeriodStart
        });
        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = retainedCategory.Id,
            Credit = 500m,
            SourceNote = "Prior-year signed accounts",
            EnteredBy = "Accounts reviewer",
            Reviewed = true
        });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var trialBalance = await service.GetTrialBalanceAsync(period.CompanyId, period.Id);

        Assert.Equal(500m, trialBalance.Single(l => l.Code == bankCategory.Code).Debit);
        Assert.Equal(500m, trialBalance.Single(l => l.Code == retainedCategory.Code).Credit);
        Assert.Equal(trialBalance.Sum(l => l.Debit), trialBalance.Sum(l => l.Credit));
    }

    [Fact]
    public async Task StatementSources_ExposeOpeningTransactionsAndAdjustments()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var salesCategory = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var retainedCategory = AddCategory(db, period.CompanyId, "3100", "Retained Earnings", AccountCategoryType.Equity);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 500m,
            OpeningBalanceDate = period.PeriodStart
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();
        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = retainedCategory.Id,
            Credit = 500m,
            SourceNote = "Prior accounts",
            EnteredBy = "Accounts reviewer",
            Reviewed = true
        });
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bankAccount.Id,
            PeriodId = period.Id,
            Date = new DateOnly(2025, 4, 1),
            Description = "Customer receipt",
            Amount = 250m,
            CategoryId = salesCategory.Id
        });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var sources = await service.GetStatementSourcesAsync(period.CompanyId, period.Id);
        var bank = sources.Single(s => s.Code == "1400");
        var sales = sources.Single(s => s.Code == "4000");
        var retained = sources.Single(s => s.Code == "3100");

        Assert.Equal(500m, bank.OpeningDebit);
        Assert.Equal(250m, bank.TransactionDebit);
        Assert.Equal(250m, sales.TransactionCredit);
        Assert.Equal(500m, retained.OpeningCredit);
        Assert.Contains(bank.SourceNotes, n => n.Contains("Bank opening balance"));
    }

    [Fact]
    public async Task ProfitAndLoss_IncludesUnpostedYearEndAdjustmentsButNotTaxProvisionTwice()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var salesCategory = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var sundryCategory = AddCategory(db, period.CompanyId, "7900", "Sundry Expenses", AccountCategoryType.Expense);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        db.ImportedTransactions.AddRange(
            new ImportedTransaction
            {
                BankAccountId = bankAccount.Id,
                PeriodId = period.Id,
                Date = new DateOnly(2025, 3, 1),
                Description = "Customer receipt",
                Amount = 1_000m,
                CategoryId = salesCategory.Id
            },
            new ImportedTransaction
            {
                BankAccountId = bankAccount.Id,
                PeriodId = period.Id,
                Date = new DateOnly(2025, 3, 2),
                Description = "Sundry expense",
                Amount = -200m,
                CategoryId = sundryCategory.Id
            });
        db.Adjustments.AddRange(
            new Adjustment
            {
                PeriodId = period.Id,
                Description = "Manual year-end correction",
                Amount = 50m,
                ImpactOnProfit = -50m,
                Source = AdjustmentSource.Manual,
                IsAuto = false
            },
            new Adjustment
            {
                PeriodId = period.Id,
                Description = "Corporation tax provision",
                Amount = 100m,
                ImpactOnProfit = -100m,
                Source = AdjustmentSource.Auto,
                IsAuto = true
            });
        db.TaxBalances.Add(new TaxBalance
        {
            PeriodId = period.Id,
            TaxType = TaxType.CorporationTax,
            Liability = 100m,
            Paid = 0m,
            Balance = 100m
        });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var profitAndLoss = await service.GetProfitAndLossAsync(period.CompanyId, period.Id);

        Assert.Equal(1_000m, profitAndLoss.Turnover);
        Assert.Equal(200m, profitAndLoss.TotalOverheads);
        Assert.Equal(-50m, profitAndLoss.TotalYearEndAdjustments);
        Assert.Equal(750m, profitAndLoss.ProfitBeforeTax);
        Assert.Equal(100m, profitAndLoss.TaxCharge);
        Assert.Equal(650m, profitAndLoss.ProfitAfterTax);
    }

    [Fact]
    public async Task AutoAdjustments_PostToDebitAndCreditCategories()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FixedAssets.Add(new FixedAsset
        {
            CompanyId = period.CompanyId,
            Name = "Laptop",
            Category = "Computer Equipment",
            Cost = 1_200m,
            AcquisitionDate = new DateOnly(2025, 1, 1),
            UsefulLifeYears = 3,
            DepreciationMethod = DepreciationMethod.StraightLine
        });
        db.Creditors.Add(new Creditor
        {
            PeriodId = period.Id,
            Name = "Accountancy fee",
            Amount = 500m,
            Type = CreditorType.Accrual,
            DueWithinYear = true
        });
        await db.SaveChangesAsync();

        var service = new AdjustmentService(db);

        await service.GenerateAutoAdjustmentsAsync(period.CompanyId, period.Id);

        var postedAdjustments = await db.Adjustments
            .Where(a => a.PeriodId == period.Id && a.Amount > 0)
            .ToListAsync();
        Assert.NotEmpty(postedAdjustments);
        Assert.All(postedAdjustments, adjustment =>
        {
            Assert.True(adjustment.DebitCategoryId.HasValue, $"{adjustment.Description} missing debit category");
            Assert.True(adjustment.CreditCategoryId.HasValue, $"{adjustment.Description} missing credit category");
        });
    }

    [Fact]
    public async Task AutoAdjustmentService_RejectsMismatchedCompanyPeriodBeforeMutating()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FixedAssets.Add(new FixedAsset
        {
            CompanyId = otherPeriod.CompanyId,
            Name = "Other company laptop",
            Category = "Computer Equipment",
            Cost = 1_200m,
            AcquisitionDate = otherPeriod.PeriodStart,
            UsefulLifeYears = 3,
            DepreciationMethod = DepreciationMethod.StraightLine
        });
        db.Creditors.Add(new Creditor
        {
            PeriodId = otherPeriod.Id,
            Name = "Other company accrual",
            Amount = 500m,
            Type = CreditorType.Accrual,
            DueWithinYear = true
        });
        await db.SaveChangesAsync();
        var service = new AdjustmentService(db);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.GenerateAutoAdjustmentsAsync(period.CompanyId, otherPeriod.Id));

        Assert.Empty(await db.Adjustments.Where(a => a.PeriodId == otherPeriod.Id).ToListAsync());
        Assert.Empty(await db.DepreciationEntries.Where(d => d.PeriodId == otherPeriod.Id).ToListAsync());
    }

    [Fact]
    public void AdjustmentService_RequiresCompanyIdForAutoGeneration()
    {
        var methods = typeof(AdjustmentService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name == nameof(AdjustmentService.GenerateAutoAdjustmentsAsync))
            .Select(m => m.GetParameters().Select(p => p.ParameterType).ToArray())
            .ToList();

        Assert.Contains(methods, parameters =>
            parameters.Length == 2
            && parameters[0] == typeof(int)
            && parameters[1] == typeof(int));
        Assert.DoesNotContain(methods, parameters =>
            parameters.Length == 1
            && parameters[0] == typeof(int));
    }

    [Fact]
    public void AdjustmentEndpoints_UseCompanyAwareAutoGeneration()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "AdjustmentEndpoints.cs"));

        Assert.Contains("GenerateAutoAdjustmentsAsync(companyId, periodId)", source);
        Assert.DoesNotContain("GenerateAutoAdjustmentsAsync(periodId)", source);
    }

    [Fact]
    public void AdjustmentEndpoints_UseDirectAccessAndRequestAuthorizationGuards()
    {
        var source = File
            .ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "AdjustmentEndpoints.cs"))
            .Replace("\r\n", "\n");

        foreach (var marker in new[]
        {
            "public static async Task<IResult> ListAdjustmentsEndpointAsync",
            "public static async Task<IResult> GetAdjustmentSummaryEndpointAsync"
        })
        {
            var snippet = AdjustmentMemberSnippet(source, marker);
            Assert.Contains("HttpContext context", snippet);
            Assert.Contains("CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", snippet);
            Assert.DoesNotContain("ApiAccessService apiAccess", snippet);
            Assert.DoesNotContain("EndpointRequestAuthorization.AuthorizeCurrentRequest", snippet);
        }

        foreach (var marker in new[]
        {
            "public static async Task<IResult> GenerateAdjustmentsEndpointAsync",
            "public static async Task<IResult> CreateAdjustmentEndpointAsync",
            "public static async Task<IResult> UpdateAdjustmentEndpointAsync",
            "public static async Task<IResult> ApproveAdjustmentEndpointAsync",
            "public static async Task<IResult> DeleteAdjustmentEndpointAsync"
        })
        {
            var snippet = AdjustmentMemberSnippet(source, marker);
            Assert.Contains("HttpContext context", snippet);
            Assert.Contains("ApiAccessService apiAccess", snippet);
            Assert.Contains("CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", snippet);
            Assert.Contains("EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", snippet);
            AssertOccursBefore(snippet, "CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)");
        }

        foreach (var marker in new[]
        {
            "auditGroup.MapGet(\"/\"",
            "auditGroup.MapGet(\"/integrity\"",
            "auditGroup.MapGet(\"/integrity/checkpoints/latest\""
        })
        {
            var snippet = AdjustmentMapSnippet(source, marker);
            Assert.Contains("AccountsDbContext db", snippet);
            Assert.Contains("HttpContext context", snippet);
            Assert.Contains("CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId)", snippet);
            Assert.DoesNotContain("EndpointRequestAuthorization.AuthorizeCurrentRequest", snippet);
        }

        var checkpointSnippet = AdjustmentMapSnippet(source, "auditGroup.MapPost(\"/integrity/checkpoints\"");
        Assert.Contains("AccountsDbContext db", checkpointSnippet);
        Assert.Contains("ApiAccessService apiAccess", checkpointSnippet);
        Assert.Contains("HttpContext context", checkpointSnippet);
        Assert.Contains("CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId)", checkpointSnippet);
        Assert.Contains("EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", checkpointSnippet);
        AssertOccursBefore(checkpointSnippet, "CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId)", "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)");
        AssertOccursBefore(checkpointSnippet, "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", "AuditCheckpointInputs.RequireOwner");

        static string AdjustmentMemberSnippet(string source, string marker)
        {
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Expected to find adjustment endpoint marker {marker}.");
            var nextMember = Regex.Match(source[(start + marker.Length)..], @"\n    (?:public|private) static", RegexOptions.None, TimeSpan.FromSeconds(1));
            return nextMember.Success
                ? source[start..(start + marker.Length + nextMember.Index)]
                : source[start..];
        }

        static string AdjustmentMapSnippet(string source, string marker)
        {
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Expected to find adjustment map marker {marker}.");
            var nextMap = Regex.Match(source[(start + marker.Length)..], @"\n\s*(?:auditGroup\.Map(?:Get|Post|Put|Delete)|public static)", RegexOptions.None, TimeSpan.FromSeconds(1));
            return nextMap.Success
                ? source[start..(start + marker.Length + nextMap.Index)]
                : source[start..];
        }

        static void AssertOccursBefore(string snippet, string first, string second)
        {
            var firstIndex = snippet.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = snippet.IndexOf(second, StringComparison.Ordinal);
            Assert.True(firstIndex >= 0, $"Expected snippet to contain '{first}'.");
            Assert.True(secondIndex >= 0, $"Expected snippet to contain '{second}'.");
            Assert.True(firstIndex < secondIndex, $"Expected '{first}' before '{second}'.");
        }
    }

    [Fact]
    public async Task AdjustmentRoutes_EnforceRuntimeCompanyPeriodRoleApiAndLockGuards()
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
                },
                new ApiAccessKeyConfig
                {
                    Name = "Admin",
                    Role = "Admin",
                    DevelopmentKey = "admin",
                    AllowedCompanyIds = []
                }
            ];
        });
        builder.Services.Configure<AuditIntegrityConfig>(config =>
        {
            config.ActiveKeyId = "audit-key-2026";
            config.SigningKeys =
            [
                new AuditIntegritySigningKeyConfig
                {
                    KeyId = "audit-key-2026",
                    SigningKey = StrongAuditCheckpointSigningKey()
                }
            ];
        });
        builder.Services.AddScoped<ApiAccessService>();
        builder.Services.AddScoped<AccountingWriteGuard>();
        builder.Services.AddScoped<AuditService>();
        builder.Services.AddScoped<AdjustmentService>();
        builder.Services.AddScoped<AuditIntegrityService>();
        builder.Services.AddScoped<AuditIntegrityCheckpointService>();

        await using var app = builder.Build();
        int tenantId;
        int allowedCompanyId;
        int restrictedCompanyId;
        int lockedCompanyId;
        int allowedPeriodId;
        int restrictedPeriodId;
        int lockedPeriodId;
        int debitCategoryId;
        int creditCategoryId;
        int approvalAdjustmentId;
        int deleteAdjustmentId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var tenant = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
            var allowedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Allowed Client Limited");
            var lockedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Locked Client Limited");
            var restrictedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Restricted Client Limited");
            var allowedPeriod = new AccountingPeriod
            {
                CompanyId = allowedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true
            };
            var lockedPeriod = new AccountingPeriod
            {
                CompanyId = lockedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true,
                Status = PeriodStatus.Finalised,
                LockedAt = DateTime.UtcNow,
                LockedBy = "Reviewer"
            };
            var restrictedPeriod = new AccountingPeriod
            {
                CompanyId = restrictedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true
            };
            db.AccountingPeriods.AddRange(allowedPeriod, lockedPeriod, restrictedPeriod);
            await db.SaveChangesAsync();
            var debit = AddCategory(db, allowedCompany.Id, "6813", "Audit fees", AccountCategoryType.Expense);
            var credit = AddCategory(db, allowedCompany.Id, "2103", "Accruals", AccountCategoryType.Liability);
            var approvalAdjustment = new Adjustment
            {
                PeriodId = allowedPeriod.Id,
                Description = "Approval accrual",
                DebitCategoryId = debit.Id,
                CreditCategoryId = credit.Id,
                Amount = 500m,
                Reason = "Year-end",
                LegalBasis = "FRS 102",
                ImpactOnProfit = -500m,
                ImpactOnAssets = 0m,
                CreatedBy = "Accountant"
            };
            var deleteAdjustment = new Adjustment
            {
                PeriodId = allowedPeriod.Id,
                Description = "Delete accrual",
                DebitCategoryId = debit.Id,
                CreditCategoryId = credit.Id,
                Amount = 300m,
                Reason = "Year-end",
                LegalBasis = "FRS 102",
                ImpactOnProfit = -300m,
                ImpactOnAssets = 0m,
                CreatedBy = "Accountant"
            };
            db.Adjustments.AddRange(approvalAdjustment, deleteAdjustment);
            for (var i = 0; i < 125; i++)
            {
                db.AuditLogs.Add(new AuditLog
                {
                    CompanyId = allowedCompany.Id,
                    PeriodId = allowedPeriod.Id,
                    EntityType = "SyntheticAuditEvidence",
                    EntityId = i + 1,
                    Action = "SyntheticAudit",
                    OldValueJson = "{\"internal\":\"old\"}",
                    NewValueJson = "{\"internal\":\"new\"}",
                    UserId = "accountant@tenant-a.test",
                    RequestId = $"audit-{i + 1}",
                    ActorDisplayName = "Internal Accountant",
                    IntegrityHash = $"hash-{i + 1}",
                    Timestamp = DateTime.UtcNow.AddMinutes(-i)
                });
            }
            await db.SaveChangesAsync();
            tenantId = tenant.Id;
            allowedCompanyId = allowedCompany.Id;
            restrictedCompanyId = restrictedCompany.Id;
            lockedCompanyId = lockedCompany.Id;
            allowedPeriodId = allowedPeriod.Id;
            restrictedPeriodId = restrictedPeriod.Id;
            lockedPeriodId = lockedPeriod.Id;
            debitCategoryId = debit.Id;
            creditCategoryId = credit.Id;
            approvalAdjustmentId = approvalAdjustment.Id;
            deleteAdjustmentId = deleteAdjustment.Id;
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
                AllowedCompanyIds: new HashSet<int> { allowedCompanyId, lockedCompanyId });
            await next();
        });
        app.MapAdjustmentEndpoints();
        await app.StartAsync();

        try
        {
            var addresses = app.Services
                .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features
                .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(addresses!.Addresses)) };
            var createBody = new
            {
                description = "Manual accrual",
                debitCategoryId,
                creditCategoryId,
                amount = 100m,
                reason = "Year-end",
                legalBasis = "FRS 102",
                impactOnProfit = -100m,
                impactOnAssets = 0m
            };

            using var hiddenListRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/periods/{restrictedPeriodId}/adjustments/",
                role: "Client");
            var hiddenList = await client.SendAsync(hiddenListRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenList.StatusCode);

            using var hiddenAuditRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/audit-log/",
                role: "Client");
            var hiddenAudit = await client.SendAsync(hiddenAuditRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenAudit.StatusCode);

            using var clientAuditRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{allowedCompanyId}/audit-log/",
                role: "Client");
            var clientAuditDenied = await client.SendAsync(clientAuditRequest);
            Assert.Equal(HttpStatusCode.Forbidden, clientAuditDenied.StatusCode);

            using var clientIntegrityRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{allowedCompanyId}/audit-log/integrity",
                role: "Client");
            var clientIntegrityDenied = await client.SendAsync(clientIntegrityRequest);
            Assert.Equal(HttpStatusCode.Forbidden, clientIntegrityDenied.StatusCode);

            using var clientCheckpointRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{allowedCompanyId}/audit-log/integrity/checkpoints/latest",
                role: "Client");
            var clientCheckpointDenied = await client.SendAsync(clientCheckpointRequest);
            Assert.Equal(HttpStatusCode.Forbidden, clientCheckpointDenied.StatusCode);

            using var oversizedAuditRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{allowedCompanyId}/audit-log/?page=0&pageSize=5000",
                role: "Accountant");
            var oversizedAuditResponse = await client.SendAsync(oversizedAuditRequest);
            Assert.Equal(HttpStatusCode.OK, oversizedAuditResponse.StatusCode);
            var oversizedAuditJson = await oversizedAuditResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(100, oversizedAuditJson.GetProperty("items").GetArrayLength());
            Assert.Equal(125, oversizedAuditJson.GetProperty("total").GetInt32());

            using var hugePageAuditRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{allowedCompanyId}/audit-log/?page=2147483647&pageSize=100",
                role: "Accountant");
            var hugePageAuditResponse = await client.SendAsync(hugePageAuditRequest);
            Assert.Equal(HttpStatusCode.OK, hugePageAuditResponse.StatusCode);
            var hugePageAuditJson = await hugePageAuditResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Empty(hugePageAuditJson.GetProperty("items").EnumerateArray());
            Assert.Equal(125, hugePageAuditJson.GetProperty("total").GetInt32());

            using var reviewerCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/adjustments/",
                role: "Reviewer",
                apiKey: "writer",
                body: createBody);
            var reviewerCreateDenied = await client.SendAsync(reviewerCreate);
            Assert.Equal(HttpStatusCode.Forbidden, reviewerCreateDenied.StatusCode);

            using var readerCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/adjustments/",
                role: "Accountant",
                apiKey: "reader",
                body: createBody);
            var readerCreateDenied = await client.SendAsync(readerCreate);
            Assert.Equal(HttpStatusCode.Unauthorized, readerCreateDenied.StatusCode);

            using var writerCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/adjustments/",
                role: "Accountant",
                apiKey: "writer",
                body: createBody);
            var writerCreateAllowed = await client.SendAsync(writerCreate);
            Assert.Equal(HttpStatusCode.Created, writerCreateAllowed.StatusCode);

            using var accountantApprove = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/adjustments/{approvalAdjustmentId}/approve",
                role: "Accountant",
                apiKey: "writer");
            var accountantApproveDenied = await client.SendAsync(accountantApprove);
            Assert.Equal(HttpStatusCode.Forbidden, accountantApproveDenied.StatusCode);

            using var reviewerApprove = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/adjustments/{approvalAdjustmentId}/approve",
                role: "Reviewer",
                apiKey: "writer");
            var reviewerApproveAllowed = await client.SendAsync(reviewerApprove);
            Assert.Equal(HttpStatusCode.OK, reviewerApproveAllowed.StatusCode);

            using var writerDelete = Request(
                HttpMethod.Delete,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/adjustments/{deleteAdjustmentId}",
                role: "Accountant",
                apiKey: "writer");
            var writerDeleteDenied = await client.SendAsync(writerDelete);
            Assert.Equal(HttpStatusCode.Unauthorized, writerDeleteDenied.StatusCode);

            using var lockedGenerate = Request(
                HttpMethod.Post,
                $"/api/companies/{lockedCompanyId}/periods/{lockedPeriodId}/adjustments/generate",
                role: "Accountant",
                apiKey: "writer");
            var lockedGenerateDenied = await client.SendAsync(lockedGenerate);
            Assert.Equal(HttpStatusCode.Conflict, lockedGenerateDenied.StatusCode);

            using var adminDelete = Request(
                HttpMethod.Delete,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/adjustments/{deleteAdjustmentId}",
                role: "Accountant",
                apiKey: "admin");
            var adminDeleteAllowed = await client.SendAsync(adminDelete);
            Assert.Equal(HttpStatusCode.NoContent, adminDeleteAllowed.StatusCode);

            await using var verifyScope = app.Services.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            Assert.Equal(2, await verifyDb.Adjustments.CountAsync(a => a.PeriodId == allowedPeriodId));
            Assert.False(await verifyDb.Adjustments.AnyAsync(a => a.PeriodId == lockedPeriodId));
            var approved = await verifyDb.Adjustments.SingleAsync(a => a.Id == approvalAdjustmentId);
            Assert.Equal("Tenant A Reviewer", approved.ApprovedBy);
            Assert.False(await verifyDb.Adjustments.AnyAsync(a => a.Id == deleteAdjustmentId));
            Assert.Equal(3, await verifyDb.AuditLogs.CountAsync(a => a.EntityType == "Adjustment"));
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
    public async Task FilingWorkflow_BlocksWhenCroCertificationSignatoriesAreMissing()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.CompanyOfficers.RemoveRange(db.CompanyOfficers.Where(o => o.CompanyId == period.CompanyId));
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);

        var status = await workflow.GetStatusAsync(period.CompanyId, period.Id);

        Assert.Contains("No active director recorded for CRO accounts certification.", status.BlockingIssues);
        Assert.Contains("No active company secretary recorded for CRO accounts certification.", status.BlockingIssues);
    }

    [Fact]
    public async Task FilingWorkflow_DoesNotApproveCroFilingWhileBlockersRemain()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            workflow.UpdateCroStatusAsync(period.CompanyId, period.Id, FilingStatus.Approved, "Reviewer"));

        Assert.Contains("Cannot approve CRO filing while blockers remain", error.Message);
    }

    [Fact]
    public async Task Ixbrl_ProfitAndLossIncludesInterestAndProfitBeforeTax()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var sales = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var bankCharges = AddCategory(db, period.CompanyId, "6900", "Bank Charges & Interest", AccountCategoryType.Expense);
        var bank = new BankAccount { CompanyId = period.CompanyId, Name = "Current Account", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        db.ImportedTransactions.AddRange(
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 1), Description = "Sales", Amount = 10_000m, CategoryId = sales.Id },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 2), Description = "Bank interest", Amount = -500m, CategoryId = bankCharges.Id });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var bytes = await new IxbrlService(db, statements).GenerateIxbrlAsync(period.CompanyId, period.Id);
        var xhtml = System.Text.Encoding.UTF8.GetString(bytes);

        // The filed P&L must show interest payable and a profit-before-tax subtotal so it
        // reconciles: operating profit 10,000 - interest 500 = profit before tax 9,500.
        Assert.Contains("core:InterestPayableSimilarChargesFinanceCosts", xhtml);
        Assert.Contains("core:ProfitLossOnOrdinaryActivitiesBeforeTax", xhtml);
        Assert.Contains("Profit before taxation", xhtml);
        Assert.Contains(">9500<", xhtml);
    }

    [Fact]
    public async Task Ixbrl_EmitsPriorYearComparativesEntityMetadataAndWellFormedXml()
    {
        // BL-08 / BL-09: the filed iXBRL must carry prior-year comparatives and entity/report
        // metadata, parse as well-formed XML, and tag values that equal the statement figures.
        await using var db = CreateDbContext();
        var company = new Company
        {
            LegalName = "Comparatives Limited",
            CroNumber = "445566",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2024, 1, 1),
            ArdMonth = 12,
            IsTrading = true,
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var priorPeriod = new AccountingPeriod { CompanyId = company.Id, PeriodStart = new DateOnly(2024, 1, 1), PeriodEnd = new DateOnly(2024, 12, 31), IsFirstYear = true };
        var currentPeriod = new AccountingPeriod { CompanyId = company.Id, PeriodStart = new DateOnly(2025, 1, 1), PeriodEnd = new DateOnly(2025, 12, 31), IsFirstYear = false };
        db.AccountingPeriods.AddRange(priorPeriod, currentPeriod);
        var sales = AddCategory(db, company.Id, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var bank = new BankAccount { CompanyId = company.Id, Name = "Current Account", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        db.ImportedTransactions.AddRange(
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = priorPeriod.Id, Date = new DateOnly(2024, 6, 1), Description = "Prior sales", Amount = 6_000m, CategoryId = sales.Id },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = currentPeriod.Id, Date = new DateOnly(2025, 6, 1), Description = "Current sales", Amount = 9_000m, CategoryId = sales.Id });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var xhtml = Encoding.UTF8.GetString(await new IxbrlService(db, statements).GenerateIxbrlAsync(company.Id, currentPeriod.Id));

        // Well-formed XML: DOCTYPE tolerated, no undeclared HTML entities such as &nbsp;.
        var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore };
        using var reader = System.Xml.XmlReader.Create(new StringReader(xhtml), settings);
        var doc = System.Xml.Linq.XDocument.Load(reader);

        // Prior-year comparatives.
        Assert.Contains("xbrli:context id=\"prior\"", xhtml);
        Assert.Contains("xbrli:context id=\"priorInstant\"", xhtml);
        Assert.Contains("contextRef=\"prior\"", xhtml);

        // Entity/report metadata.
        Assert.Contains("bus:EntityCurrentLegalOrRegisteredName", xhtml);
        Assert.Contains("bus:StartDateForPeriodCoveredByReport", xhtml);
        Assert.Contains("Comparatives Limited", xhtml);

        // Tagged values equal the statement figures (current and prior).
        var currentBs = await statements.GetBalanceSheetAsync(company.Id, currentPeriod.Id);
        var currentPl = await statements.GetProfitAndLossAsync(company.Id, currentPeriod.Id);
        var priorPl = await statements.GetProfitAndLossAsync(company.Id, priorPeriod.Id);

        System.Xml.Linq.XNamespace ix = "http://www.xbrl.org/2013/inlineXBRL";
        decimal Fact(string concept, string ctx) =>
            decimal.Parse(doc.Descendants(ix + "nonFraction")
                .Single(e => (string?)e.Attribute("name") == concept && (string?)e.Attribute("contextRef") == ctx).Value,
                System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(Math.Round(currentPl.Turnover, 0), Fact("core:TurnoverGrossRevenue", "current"));
        Assert.Equal(Math.Round(currentBs.NetAssets, 0), Fact("core:NetAssetsLiabilities", "instant"));
        Assert.Equal(Math.Round(priorPl.Turnover, 0), Fact("core:TurnoverGrossRevenue", "prior"));
    }

    [Fact]
    public async Task FilingWorkflow_MarkGeneratedEndpointCannotSatisfyCroDocumentReadiness()
    {
        var databaseName = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<AccountsDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.Configure<ApiAccessConfig>(config => config.Enabled = false);
        builder.Services.AddScoped<ApiAccessService>();
        builder.Services.AddScoped<FinancialStatementsService>();
        builder.Services.AddScoped<IxbrlService>();
        builder.Services.AddScoped<FilingWorkflowService>();

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
            context.Items[AuthContext.ItemKey] = AuthenticatedRole("Accountant");
            await next();
        });
        app.MapFilingWorkflowEndpoints();
        await app.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(LoopbackBaseAddress(app)) };
            var accounts = await client.PostAsJsonAsync(
                $"/api/companies/{companyId}/periods/{periodId}/filing/mark-generated",
                new { documentType = "accounts" });
            var signature = await client.PostAsJsonAsync(
                $"/api/companies/{companyId}/periods/{periodId}/filing/mark-generated",
                new { documentType = "signature" });

            Assert.NotEqual(HttpStatusCode.OK, accounts.StatusCode);
            Assert.NotEqual(HttpStatusCode.OK, signature.StatusCode);

            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var statements = scope.ServiceProvider.GetRequiredService<FinancialStatementsService>();
            var ixbrl = scope.ServiceProvider.GetRequiredService<IxbrlService>();
            var workflow = new FilingWorkflowService(db, statements, ixbrl);
            var status = await workflow.GetStatusAsync(companyId, periodId);

            Assert.Contains("CRO accounts PDF not generated", status.BlockingIssues);
            Assert.Contains("CRO signature page not generated", status.BlockingIssues);
            Assert.False(status.ReadyToFile);
            Assert.Null(await db.CroFilingPackages.SingleOrDefaultAsync(p => p.PeriodId == periodId));
        }
        finally
        {
            await app.StopAsync();
        }

    }

    [Fact]
    public async Task DocumentEndpoints_GetCroDownloadsDoNotRecordGeneratedFlags()
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
            context.Items[AuthContext.ItemKey] = AuthenticatedRole("Accountant");
            await next();
        });
        app.MapDocumentEndpoints();
        await app.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(LoopbackBaseAddress(app)) };
            var croPack = await client.GetAsync($"/api/companies/{companyId}/periods/{periodId}/documents/cro-filing-pack");
            var signature = await client.GetAsync($"/api/companies/{companyId}/periods/{periodId}/documents/signature-page");

            Assert.True(croPack.StatusCode == HttpStatusCode.OK, await croPack.Content.ReadAsStringAsync());
            Assert.True(signature.StatusCode == HttpStatusCode.OK, await signature.Content.ReadAsStringAsync());

            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var package = await db.CroFilingPackages.SingleOrDefaultAsync(p => p.PeriodId == periodId);

            Assert.Null(package);
        }
        finally
        {
            await app.StopAsync();
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

            var croPack = await SendCsrfPostAsync(
                client,
                $"/api/companies/{companyId}/periods/{periodId}/documents/cro-filing-pack");
            var signature = await SendCsrfPostAsync(
                client,
                $"/api/companies/{companyId}/periods/{periodId}/documents/signature-page");

            Assert.True(croPack.StatusCode == HttpStatusCode.OK, await croPack.Content.ReadAsStringAsync());
            Assert.True(signature.StatusCode == HttpStatusCode.OK, await signature.Content.ReadAsStringAsync());
            Assert.Equal("application/pdf", croPack.Content.Headers.ContentType?.MediaType);
            Assert.Equal("application/pdf", signature.Content.Headers.ContentType?.MediaType);

            await using var scope = app.Services.CreateAsyncScope();
            var finalDb = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var package = await finalDb.CroFilingPackages.SingleAsync(p => p.PeriodId == periodId);

            Assert.True(package.AccountsPdfGenerated);
            Assert.True(package.SignaturePageGenerated);
            Assert.Equal(FilingStatus.PackageGenerated, package.FilingStatus);
        }
        finally
        {
            await app.StopAsync();
        }

        static async Task<HttpResponseMessage> SendCsrfPostAsync(HttpClient client, string path)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Add(CsrfProtectionMiddleware.HeaderName, "csrf-token-1");
            request.Headers.Add("Cookie", "accounts_csrf=csrf-token-1");
            return await client.SendAsync(request);
        }
    }

    [Fact]
    public async Task DocumentEndpoints_ReadOnlyDownloadsDoNotAdvanceCroGeneratedFlags()
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
            context.Items[AuthContext.ItemKey] = AuthenticatedRole("Client") with
            {
                AllowedCompanyIds = new HashSet<int> { companyId }
            };
            await next();
        });
        app.MapDocumentEndpoints();
        await app.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(LoopbackBaseAddress(app)) };
            var croPack = await client.GetAsync($"/api/companies/{companyId}/periods/{periodId}/documents/cro-filing-pack");
            var signature = await client.GetAsync($"/api/companies/{companyId}/periods/{periodId}/documents/signature-page");

            Assert.True(croPack.StatusCode == HttpStatusCode.OK, await croPack.Content.ReadAsStringAsync());
            Assert.True(signature.StatusCode == HttpStatusCode.OK, await signature.Content.ReadAsStringAsync());

            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            Assert.Null(await db.CroFilingPackages.SingleOrDefaultAsync(p => p.PeriodId == periodId));
        }
        finally
        {
            await app.StopAsync();
        }

    }

    [Fact]
    public async Task FilingWorkflow_TreatsOverdueDeadlinesAsWarningsNotReadinessBlockers()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingDeadlines.Add(new FilingDeadline
        {
            CompanyId = period.CompanyId,
            PeriodId = period.Id,
            DeadlineType = DeadlineType.CRO,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)
        });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);

        var status = await workflow.GetStatusAsync(period.CompanyId, period.Id);

        Assert.Contains(status.WarningIssues, w => w.Contains("CRO deadline passed"));
        Assert.DoesNotContain(status.BlockingIssues, b => b.Contains("CRO deadline passed"));
    }

    [Fact]
    public async Task FilingWorkflow_RequiresPaymentBeforeCroAcceptance()
    {
        await using var db = CreateDbContext();
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

        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);
        await workflow.RecordCroDocumentGeneratedAsync(period.CompanyId, period.Id, "accounts");
        await workflow.RecordCroDocumentGeneratedAsync(period.CompanyId, period.Id, "signature");
        await workflow.UpdateCroStatusAsync(period.CompanyId, period.Id, FilingStatus.Approved, "Reviewer");
        await workflow.UpdateCroStatusAsync(
            period.CompanyId,
            period.Id,
            FilingStatus.Submitted,
            "Reviewer",
            submissionReference: "  CORE-2026-0001  ");

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            workflow.UpdateCroStatusAsync(period.CompanyId, period.Id, FilingStatus.Accepted, "Reviewer"));
        Assert.Contains("Confirm CORE payment", error.Message);

        await workflow.ConfirmCroPaymentAsync(period.CompanyId, period.Id, "Reviewer");
        var accepted = await workflow.UpdateCroStatusAsync(period.CompanyId, period.Id, FilingStatus.Accepted, "Reviewer");

        Assert.Equal(FilingStatus.Accepted, accepted.FilingStatus);
        Assert.True(accepted.PaymentCompleted);
    }

    [Fact]
    public async Task FilingWorkflow_RejectsCroSubmissionWithoutCoreReferenceBeforeMutatingPackage()
    {
        await using var db = CreateDbContext();
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

        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);
        await workflow.RecordCroDocumentGeneratedAsync(period.CompanyId, period.Id, "accounts");
        await workflow.RecordCroDocumentGeneratedAsync(period.CompanyId, period.Id, "signature");
        await workflow.UpdateCroStatusAsync(period.CompanyId, period.Id, FilingStatus.Approved, "Reviewer");

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            workflow.UpdateCroStatusAsync(
                period.CompanyId,
                period.Id,
                FilingStatus.Submitted,
                "Reviewer",
                submissionReference: "   "));

        var package = await db.CroFilingPackages.SingleAsync(p => p.PeriodId == period.Id);
        Assert.Contains("CORE submission reference is required", error.Message);
        Assert.Equal(FilingStatus.Approved, package.FilingStatus);
        Assert.Null(package.SubmittedBy);
        Assert.Null(package.SubmittedAt);
        Assert.Null(package.CroSubmissionReference);
    }

    [Fact]
    public async Task FilingWorkflow_RejectsCroAcceptanceWhenSubmittedPackageHasNoCoreReference()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.CroFilingPackages.Add(new CroFilingPackage
        {
            PeriodId = period.Id,
            FilingStatus = FilingStatus.Submitted,
            AccountsPdfGenerated = true,
            SignaturePageGenerated = true,
            PaymentCompleted = true,
            SubmittedBy = "Reviewer",
            SubmittedAt = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
            CroSubmissionReference = "   "
        });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            workflow.UpdateCroStatusAsync(period.CompanyId, period.Id, FilingStatus.Accepted, "Reviewer"));

        var package = await db.CroFilingPackages.SingleAsync(p => p.PeriodId == period.Id);
        Assert.Contains("CORE submission reference is required", error.Message);
        Assert.Equal(FilingStatus.Submitted, package.FilingStatus);
        Assert.Equal(FilingPackageStatus.Draft, package.Status);
    }

    [Fact]
    public async Task FilingWorkflow_RequiresCharityReportsReferenceAndAcceptanceBeforeDeadlineFiling()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2023, 1, 1);
        period.PeriodEnd = new DateOnly(2023, 12, 31);
        period.Status = PeriodStatus.Review;
        var company = await db.Companies.SingleAsync(c => c.Id == period.CompanyId);
        company.IsCharitableOrganisation = true;
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
        db.CharityInfos.Add(new CharityInfo
        {
            CompanyId = period.CompanyId,
            CharityNumber = "CHY-12345",
            CharityType = "CLG",
            GrossIncome = 100_000m,
            CharitableObjectives = "Community education",
            PrincipalActivities = "Training and support"
        });
        db.FundBalances.Add(new FundBalance
        {
            PeriodId = period.Id,
            FundName = "General fund",
            FundType = "Unrestricted",
            OpeningBalance = 100m,
            IncomingResources = 1_000m,
            ResourcesExpended = 900m,
            ClosingBalance = 200m
        });
        await db.SaveChangesAsync();
        await MakePeriodReadyForCroDocumentsAsync(db, period);

        var audit = new AuditService(db);
        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl, audit);
        var deadlines = await new DeadlineService(db).CalculateDeadlinesAsync(period.CompanyId, period.Id);
        var charityDeadline = deadlines.Single(d => d.DeadlineType == DeadlineType.Charity);

        var approveBeforeReports = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            workflow.UpdateCharityStatusAsync(period.CompanyId, period.Id, FilingStatus.Approved, "Reviewer"));
        Assert.Contains("Generate the Charity SoFA", approveBeforeReports.Message);

        await workflow.RecordCharityReportGeneratedAsync(period.CompanyId, period.Id, "sofa", "reviewer@example.ie");
        await workflow.RecordCharityReportGeneratedAsync(period.CompanyId, period.Id, "trustees-report", "reviewer@example.ie");
        var approved = await workflow.UpdateCharityStatusAsync(period.CompanyId, period.Id, FilingStatus.Approved, "Reviewer", auditUserId: "reviewer@example.ie");
        var approvedStatus = approved.FilingStatus;

        var submitWithoutReference = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            workflow.UpdateCharityStatusAsync(period.CompanyId, period.Id, FilingStatus.Submitted, "Reviewer", auditUserId: "reviewer@example.ie"));
        Assert.Contains("Charity annual return reference is required", submitWithoutReference.Message);

        var submitted = await workflow.UpdateCharityStatusAsync(
            period.CompanyId,
            period.Id,
            FilingStatus.Submitted,
            "Reviewer",
            annualReturnReference: "  CRA-AR-2025-001  ",
            auditUserId: "reviewer@example.ie");
        var submittedStatus = submitted.FilingStatus;
        var accepted = await workflow.UpdateCharityStatusAsync(period.CompanyId, period.Id, FilingStatus.Accepted, "Reviewer", auditUserId: "reviewer@example.ie");
        var status = await workflow.GetStatusAsync(period.CompanyId, period.Id);

        await new DeadlineService(db, audit).MarkFiledAsync(
            period.CompanyId,
            period.Id,
            DeadlineType.Charity,
            charityDeadline.DueDate.AddDays(3),
            "reviewer@example.ie");

        var history = await db.FilingHistories.SingleAsync(h =>
            h.CompanyId == period.CompanyId && h.PeriodId == period.Id && h.DeadlineType == DeadlineType.Charity);
        var reportAudits = await db.AuditLogs.Where(a => a.Action == AuditEventCodes.CharityReportGenerated).ToListAsync();
        var statusAudits = await db.AuditLogs.Where(a => a.Action == AuditEventCodes.CharityFilingStatusChanged).ToListAsync();

        Assert.Equal(FilingStatus.Approved, approvedStatus);
        Assert.Equal(FilingStatus.Submitted, submittedStatus);
        Assert.Equal(FilingStatus.Accepted, accepted.FilingStatus);
        Assert.Equal(FilingPackageStatus.Accepted, accepted.Status);
        Assert.Equal("CRA-AR-2025-001", accepted.AnnualReturnReference);
        Assert.Equal(FilingStatus.Accepted, status.Charity.Status);
        Assert.True(status.Charity.SofaGenerated);
        Assert.True(status.Charity.TrusteesReportGenerated);
        Assert.Equal("CRA-AR-2025-001", status.Charity.AnnualReturnReference);
        Assert.Equal("CRA-AR-2025-001", history.FilingReference);
        Assert.Equal(2, reportAudits.Count);
        Assert.Equal(3, statusAudits.Count);
    }

    [Fact]
    public async Task FilingWorkflow_RechecksFinalReadinessBeforeCroSubmission()
    {
        await using var db = CreateDbContext();
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

        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);
        await workflow.RecordCroDocumentGeneratedAsync(period.CompanyId, period.Id, "accounts");
        await workflow.RecordCroDocumentGeneratedAsync(period.CompanyId, period.Id, "signature");
        await workflow.UpdateCroStatusAsync(period.CompanyId, period.Id, FilingStatus.Approved, "Reviewer");

        db.Adjustments.Add(new Adjustment
        {
            PeriodId = period.Id,
            Description = "Late unapproved adjustment",
            Amount = 100m,
            ImpactOnProfit = 100m,
            Source = AdjustmentSource.Manual,
            CreatedBy = "Reviewer"
        });
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            workflow.UpdateCroStatusAsync(
                period.CompanyId,
                period.Id,
                FilingStatus.Submitted,
                "Reviewer",
                submissionReference: "CORE-READINESS-TEST"));

        var package = await db.CroFilingPackages.SingleAsync(p => p.PeriodId == period.Id);
        Assert.Contains("Cannot generate final CRO submission", error.Message);
        Assert.Contains("adjustments pending approval", error.Message);
        Assert.Equal(FilingStatus.Approved, package.FilingStatus);
        Assert.Null(package.SubmittedBy);
        Assert.Null(package.SubmittedAt);
    }

    [Fact]
    public async Task FilingWorkflow_RechecksCroCertificationBlockersBeforeSubmission()
    {
        await using var db = CreateDbContext();
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

        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);
        await workflow.RecordCroDocumentGeneratedAsync(period.CompanyId, period.Id, "accounts");
        await workflow.RecordCroDocumentGeneratedAsync(period.CompanyId, period.Id, "signature");
        await workflow.UpdateCroStatusAsync(period.CompanyId, period.Id, FilingStatus.Approved, "Reviewer");

        var secretary = await db.CompanyOfficers.SingleAsync(o =>
            o.CompanyId == period.CompanyId
            && (o.Role == OfficerRole.Secretary || o.Role == OfficerRole.CompanySecretary));
        secretary.ResignedDate = period.PeriodEnd;
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            workflow.UpdateCroStatusAsync(
                period.CompanyId,
                period.Id,
                FilingStatus.Submitted,
                "Reviewer",
                submissionReference: "CORE-BLOCKER-TEST"));

        var package = await db.CroFilingPackages.SingleAsync(p => p.PeriodId == period.Id);
        Assert.Contains("Cannot submit CRO filing while blockers remain", error.Message);
        Assert.Contains("No active company secretary", error.Message);
        Assert.Equal(FilingStatus.Approved, package.FilingStatus);
        Assert.Null(package.SubmittedBy);
        Assert.Null(package.SubmittedAt);
    }

    [Fact]
    public void Deadline_MoveToNextWorkingDay_SkipsIrishPublicHolidays()
    {
        Assert.Equal(new DateOnly(2026, 3, 18), DeadlineService.MoveToNextWorkingDay(new DateOnly(2026, 3, 17)));
        Assert.Equal(new DateOnly(2026, 4, 7), DeadlineService.MoveToNextWorkingDay(new DateOnly(2026, 4, 6)));
        Assert.Equal(new DateOnly(2026, 12, 29), DeadlineService.MoveToNextWorkingDay(new DateOnly(2026, 12, 25)));
    }

    [Fact]
    public async Task ImportCsv_RejectsBankAccountFromAnotherCompanyPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        using var csv = new MemoryStream(Encoding.UTF8.GetBytes("Date,Description,Amount\n01/01/2026,Receipt,100\n"));
        var service = new ImportService(db, Options.Create(new ImportLimitConfig()));

        var error = await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.ImportCsvAsync(period.CompanyId, bankAccount.Id, otherPeriod.Id, csv, "bank.csv"));

        Assert.Contains($"Period {otherPeriod.Id} not found", error.Message);
        Assert.Empty(await db.ImportBatches.ToListAsync());
        Assert.Empty(await db.ImportedTransactions.ToListAsync());
    }

    [Theory]
    [InlineData("Posted Account, Posted Transactions Date, Description, Debit Amount, Credit Amount, Balance", "AIB")]
    [InlineData("Date, Transaction Details, Amount, Balance - Bank of Ireland", "BOI")]
    [InlineData("Type, Started Date, Completed Date, Description, Amount, Balance", "Revolut")]
    [InlineData("id, created, amount, currency, description, balance_transaction", "Stripe")]
    [InlineData("Date, Description, Amount", "Generic")]
    public async Task ImportService_DetectsBankFormatFromHeader(string header, string expected)
    {
        // BL-14: the AIB/BOI/Revolut/Stripe auto-detection was completely untested.
        await using var db = CreateDbContext();
        var service = new ImportService(db, Options.Create(new ImportLimitConfig()));
        Assert.Equal(expected, service.DetectFormat(header).Name);
    }

    [Fact]
    public async Task ImportService_AutoDetectsRevolutAndParsesColumnsPerMapping()
    {
        // BL-14: end-to-end proof that auto-detection picks the format and reads each column per the
        // detected mapping (Revolut: date col 0 yyyy-MM-dd, description col 1, amount col 2).
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bank = new BankAccount { CompanyId = period.CompanyId, Name = "Revolut", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        var csv = "Started Date,Description,Amount,Balance\n2025-03-01,Coffee shop,-4.50,995.50\n2025-03-02,Client payment,1200.00,2195.50\n";
        var service = new ImportService(db, Options.Create(new ImportLimitConfig()));

        var result = await service.ImportCsvAsync(
            period.CompanyId, bank.Id, period.Id,
            new MemoryStream(Encoding.UTF8.GetBytes(csv)), "revolut.csv");

        Assert.Equal(2, result.ImportedRows);
        var txns = await db.ImportedTransactions.Where(t => t.BankAccountId == bank.Id).OrderBy(t => t.Date).ToListAsync();
        Assert.Equal(new DateOnly(2025, 3, 1), txns[0].Date);
        Assert.Equal("Coffee shop", txns[0].Description);
        Assert.Equal(-4.50m, txns[0].Amount);
        Assert.Equal(1_200.00m, txns[1].Amount);

        // Re-importing the identical file detects every row as a duplicate.
        var second = await service.ImportCsvAsync(
            period.CompanyId, bank.Id, period.Id,
            new MemoryStream(Encoding.UTF8.GetBytes(csv)), "revolut.csv");
        Assert.Equal(2, second.DuplicatesSkipped);
        Assert.Equal(0, second.ImportedRows);
    }

    [Fact]
    public async Task CategoryService_SeedsDefaultIrishChartOfAccounts()
    {
        // BL-17: the default chart of accounts was untested.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var cats = await new CategoryService(db).SeedDefaultCategoriesAsync(period.CompanyId);

        Assert.True(cats.Count >= 50, $"expected a full chart of accounts, got {cats.Count}");
        Assert.All(cats, c => Assert.Equal(period.CompanyId, c.CompanyId));
        Assert.Contains(cats, c => c.Code == "4000" && c.Type == AccountCategoryType.Income);
        Assert.Contains(cats, c => c.Code == "1400" && c.Type == AccountCategoryType.Asset);
        Assert.Contains(cats, c => c.Code == "2000" && c.Type == AccountCategoryType.Liability);
        Assert.Contains(cats, c => c.Code == "3000" && c.Type == AccountCategoryType.Equity);
        Assert.Contains(cats, c => c.Code == "7000" && c.TaxTreatment == TaxTreatment.NonDeductible);

        // Re-seeding is idempotent — it returns the existing set without duplicating.
        var again = await new CategoryService(db).SeedDefaultCategoriesAsync(period.CompanyId);
        Assert.Equal(cats.Count, again.Count);
        Assert.Equal(cats.Count, await db.AccountCategories.CountAsync(c => c.CompanyId == period.CompanyId));
    }

    [Fact]
    public async Task CategoryService_AutoCategorisesByRuleThenFuzzyNameWithConfidence()
    {
        // BL-17: confidence-scored auto-categorisation was untested.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var service = new CategoryService(db);
        var cats = await service.SeedDefaultCategoriesAsync(period.CompanyId);
        var rent = cats.Single(c => c.Code == "6100");

        // A matching transaction rule wins with high (0.85) confidence.
        db.TransactionRules.Add(new TransactionRule { CompanyId = period.CompanyId, Pattern = "ACME LANDLORD", CategoryId = rent.Id, Priority = 1 });
        await db.SaveChangesAsync();
        var ruled = await service.AutoCategoriseAsync(period.CompanyId, "Payment to ACME LANDLORD Ltd");
        Assert.Equal(rent.Id, ruled.categoryId);
        Assert.Equal(0.85m, ruled.confidence);

        // No rule: fall back to a fuzzy category-name match at lower (0.5) confidence.
        var fuzzy = await service.AutoCategoriseAsync(period.CompanyId, "Monthly insurance premium");
        Assert.NotNull(fuzzy.categoryId);
        Assert.Equal(0.5m, fuzzy.confidence);

        // Nothing matches: no category, zero confidence.
        var none = await service.AutoCategoriseAsync(period.CompanyId, "zzzz qqqq");
        Assert.Null(none.categoryId);
        Assert.Equal(0m, none.confidence);
    }

    [Theory]
    [InlineData(999, false)]   // below 10% of net assets
    [InlineData(1000, false)]  // exactly 10% does not exceed (the coded test is strict >)
    [InlineData(1001, true)]   // above 10% triggers the SAP requirement
    public async Task DirectorLoanCompliance_TenPercentNetAssetsThresholdBoundary(decimal closingBalance, bool exceeds)
    {
        // BL-16: boundary test for the 10%-of-net-assets director-loan threshold. Net assets are
        // pinned to €10,000 so the threshold is exactly €1,000.
        // NOTE: the code names this s.239 (and the warning cites s.239); the docs say s.236. This test
        // asserts the coded s.239 behaviour — the section discrepancy is flagged for legal confirmation.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var sales = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var bank = new BankAccount { CompanyId = period.CompanyId, Name = "Current", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        var director = await db.CompanyOfficers.FirstAsync(o => o.CompanyId == period.CompanyId && o.Role == OfficerRole.Director);
        await db.SaveChangesAsync();

        db.ImportedTransactions.Add(new ImportedTransaction { BankAccountId = bank.Id, PeriodId = period.Id, Date = new DateOnly(2025, 3, 1), Description = "Sales", Amount = 10_000m, CategoryId = sales.Id });
        db.DirectorLoans.Add(new DirectorLoan { PeriodId = period.Id, DirectorId = director.Id, OpeningBalance = 0m, Advances = closingBalance, Repayments = 0m, ClosingBalance = closingBalance, IsDocumented = true });
        await db.SaveChangesAsync();

        var result = await new DirectorLoanComplianceService(db, new FinancialStatementsService(db))
            .GetComplianceStatusAsync(period.CompanyId, period.Id);

        Assert.Equal(10_000m, result.NetAssets);
        Assert.Equal(1_000m, result.ThresholdAmount);
        Assert.Equal(exceeds, result.ExceedsThreshold);
        Assert.Equal(exceeds, result.SapRequired);
        if (exceeds)
            Assert.Contains("s.239", result.Warning);
    }

    [Fact]
    public async Task ImportCsv_RejectsCallerCompanyMismatchBeforeImporting()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherBankAccount = new BankAccount
        {
            CompanyId = otherPeriod.CompanyId,
            Name = "Other current account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(otherBankAccount);
        await db.SaveChangesAsync();
        using var csv = new MemoryStream(Encoding.UTF8.GetBytes("Date,Description,Amount\n01/01/2025,Receipt,100\n"));
        var service = new ImportService(db, Options.Create(new ImportLimitConfig()));

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.ImportCsvAsync(period.CompanyId, otherBankAccount.Id, otherPeriod.Id, csv, "bank.csv"));

        Assert.Empty(await db.ImportBatches.ToListAsync());
        Assert.Empty(await db.ImportedTransactions.ToListAsync());
    }

    [Fact]
    public async Task ImportCsv_EnforcesConfiguredRowLimit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        using var csv = new MemoryStream(Encoding.UTF8.GetBytes("Date,Description,Amount\n01/01/2026,Receipt,100\n02/01/2026,Receipt,200\n"));
        var service = new ImportService(db, Options.Create(new ImportLimitConfig { MaxRows = 1 }));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.ImportCsvAsync(period.CompanyId, bankAccount.Id, period.Id, csv, "bank.csv"));

        Assert.Contains("too many rows", error.Message);
    }

    [Fact]
    public async Task ImportCsv_SkipsRowsOutsideAccountingPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        using var csv = new MemoryStream(Encoding.UTF8.GetBytes("Date,Description,Amount\n31/12/2024,Prior year receipt,100\n01/01/2025,Current receipt,200\n01/01/2026,Future receipt,300\n"));
        var service = new ImportService(db, Options.Create(new ImportLimitConfig()));

        var result = await service.ImportCsvAsync(period.CompanyId, bankAccount.Id, period.Id, csv, "bank.csv");

        Assert.Equal(3, result.TotalRows);
        Assert.Equal(1, result.ImportedRows);
        Assert.Equal(2, result.Warnings.Count(w => w.Contains("outside accounting period")));
        Assert.Equal(3, await db.ImportBatches.Select(b => b.RowCount).SingleAsync());
        var saved = await db.ImportedTransactions.SingleAsync();
        Assert.Equal(new DateOnly(2025, 1, 1), saved.Date);
    }

    [Fact]
    public async Task ImportCsv_ParsesQuotedMultilineBankDescriptionsWithEscapedQuotes()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        var csvText = "Date,Description,Amount\n01/01/2025,\"Customer said \"\"thanks\"\"\nInvoice 42\",123.45\n";
        using var csv = new MemoryStream(Encoding.UTF8.GetBytes(csvText));
        var service = new ImportService(db, Options.Create(new ImportLimitConfig()));

        var result = await service.ImportCsvAsync(period.CompanyId, bankAccount.Id, period.Id, csv, "bank.csv");

        Assert.Equal(1, result.TotalRows);
        Assert.Equal(1, result.ImportedRows);
        Assert.Empty(result.Warnings);
        var saved = await db.ImportedTransactions.SingleAsync();
        Assert.Equal("Customer said \"thanks\"\nInvoice 42", saved.Description);
        Assert.Equal(123.45m, saved.Amount);
    }

    [Fact]
    public async Task ImportCsv_NeutralisesSpreadsheetFormulaInjectionInStoredText()
    {
        // import-csv-formula-injection: a bank memo/reference that begins with = + - @ (or a leading
        // tab/CR/LF) is a CSV-injection vector — it executes as a formula when the imported
        // transactions are later exported to Excel/Sheets. Stored text must be neutralised, while
        // numeric fields (incl. a legitimate negative amount) must still parse.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        // Columns: Date(0), Description(1), Reference(2), Amount(3).
        var csvText =
            "Date,Description,Reference,Amount\n" +
            "01/01/2025,=1+2,@evil,100\n" +
            "02/01/2025,Normal payment,REF123,-12.50\n" +
            "03/01/2025,-Refund issued,+447700,5\n";
        using var csv = new MemoryStream(Encoding.UTF8.GetBytes(csvText));
        var mapping = new ImportService.ColumnMapping(
            DateColumn: 0, DescriptionColumn: 1, AmountColumn: 3, BalanceColumn: null, ReferenceColumn: 2);
        var service = new ImportService(db, Options.Create(new ImportLimitConfig()));

        var result = await service.ImportCsvAsync(period.CompanyId, bankAccount.Id, period.Id, csv, "bank.csv", mapping);

        Assert.Equal(3, result.ImportedRows);
        var txns = await db.ImportedTransactions.OrderBy(t => t.Date).ToListAsync();
        Assert.Equal(3, txns.Count);

        // Formula triggers in stored text are neutralised with a leading apostrophe.
        Assert.Equal("'=1+2", txns[0].Description);
        Assert.Equal("'@evil", txns[0].Reference);
        Assert.Equal(100m, txns[0].Amount);

        // Ordinary text is stored unchanged; the negative AMOUNT is parsed, not neutralised.
        Assert.Equal("Normal payment", txns[1].Description);
        Assert.Equal("REF123", txns[1].Reference);
        Assert.Equal(-12.50m, txns[1].Amount);

        // Leading '-' and '+' in stored text are neutralised even though they are valid number signs.
        Assert.Equal("'-Refund issued", txns[2].Description);
        Assert.Equal("'+447700", txns[2].Reference);
        Assert.Equal(5m, txns[2].Amount);
    }

    [Fact]
    public async Task ImportCsv_WarningsDoNotEchoRawCsvFieldValues()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        var csvText = "Date,Description,Amount\nnot-a-real-date-SECRET,Private card payment,100\n01/01/2025,Private card payment,amount-SECRET\n";
        using var csv = new MemoryStream(Encoding.UTF8.GetBytes(csvText));
        var service = new ImportService(db, Options.Create(new ImportLimitConfig()));

        var result = await service.ImportCsvAsync(period.CompanyId, bankAccount.Id, period.Id, csv, "bank.csv");
        var warningText = string.Join("\n", result.Warnings);

        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains("Row 1", warningText);
        Assert.Contains("Row 2", warningText);
        Assert.Contains("could not parse date", warningText);
        Assert.Contains("could not parse amount", warningText);
        Assert.DoesNotContain("not-a-real-date-SECRET", warningText);
        Assert.DoesNotContain("amount-SECRET", warningText);
        Assert.Empty(await db.ImportedTransactions.ToListAsync());
    }

    [Fact]
    public async Task ImportCsv_ReadFailuresReturnClientSafeBusinessRule()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();
        var service = new ImportService(db, Options.Create(new ImportLimitConfig()));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.ImportCsvAsync(
                period.CompanyId,
                bankAccount.Id,
                period.Id,
                new ThrowingReadStream("raw-upload-SECRET"),
                "bank.csv"));

        Assert.Equal("CSV file could not be read. Upload a valid CSV bank statement.", error.Message);
        Assert.DoesNotContain("raw-upload-SECRET", error.Message);
        Assert.Empty(await db.ImportedTransactions.ToListAsync());
    }

    [Fact]
    public void ImportService_RequiresCompanyIdForCsvImport()
    {
        var methods = typeof(ImportService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name == nameof(ImportService.ImportCsvAsync))
            .Select(m => m.GetParameters().Select(p => p.ParameterType).ToArray())
            .ToList();

        Assert.Contains(methods, parameters =>
            parameters.Length >= 4
            && parameters[0] == typeof(int)
            && parameters[1] == typeof(int)
            && parameters[2] == typeof(int)
            && parameters[3] == typeof(Stream));
        Assert.DoesNotContain(methods, parameters =>
            parameters.Length >= 3
            && parameters[0] == typeof(int)
            && parameters[1] == typeof(int)
            && parameters[2] == typeof(Stream));
    }

    [Fact]
    public void BankingImportEndpoint_UsesCompanyAwareImportService()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "BankingEndpoints.cs"));

        Assert.Contains("ImportCsvAsync(companyId, bankAccountId, periodId", source);
        Assert.DoesNotContain("ImportCsvAsync(bankAccountId, periodId", source);
    }

    [Fact]
    public async Task BankingImportEndpoint_ReturnsBadRequestForMalformedMultipartUploads()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bank = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/bank-accounts/{bank.Id}/import");
        context.Request.ContentType = "multipart/form-data";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("raw-multipart-SECRET"));
        var importService = new ImportService(db, Options.Create(new ImportLimitConfig()));

        var result = await BankingEndpoints.ImportCsvEndpointAsync(
            period.CompanyId,
            bank.Id,
            period.Id,
            context.Request,
            importService,
            new AuditService(db),
            Options.Create(new ImportLimitConfig()),
            db,
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(result));
        var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var message = valueResult.Value?.GetType().GetProperty("error")?.GetValue(valueResult.Value)?.ToString();
        Assert.Equal("Upload a valid multipart CSV bank statement.", message);
        Assert.DoesNotContain("raw-multipart-SECRET", message);
        Assert.Empty(await db.ImportBatches.ToListAsync());
        Assert.Empty(await db.ImportedTransactions.ToListAsync());
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == "BankCsvImported" || a.EntityType == "ImportBatch").ToListAsync());
    }

    [Fact]
    public async Task BankingImportEndpoint_ReturnsBadRequestForImportLimitFailures()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bank = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/bank-accounts/{bank.Id}/import");
        var csvText = "Date,Description,Amount\n01/01/2025,Receipt,100\n02/01/2025,Receipt,200\n";
        var formFile = new FormFile(
            new MemoryStream(Encoding.UTF8.GetBytes(csvText)),
            0,
            csvText.Length,
            "file",
            "bank.csv")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/csv"
        };
        context.Request.ContentType = "multipart/form-data; boundary=test";
        context.Request.Form = new FormCollection([], new FormFileCollection { formFile });
        var limits = Options.Create(new ImportLimitConfig { MaxRows = 1 });
        var importService = new ImportService(db, limits);

        var result = await BankingEndpoints.ImportCsvEndpointAsync(
            period.CompanyId,
            bank.Id,
            period.Id,
            context.Request,
            importService,
            new AuditService(db),
            limits,
            db,
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(result));
        var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var message = valueResult.Value?.GetType().GetProperty("error")?.GetValue(valueResult.Value)?.ToString();
        Assert.Contains("too many rows", message);
        Assert.Empty(await db.ImportBatches.ToListAsync());
        Assert.Empty(await db.ImportedTransactions.ToListAsync());
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == "BankCsvImported" || a.EntityType == "ImportBatch").ToListAsync());
    }

    [Theory]
    [InlineData("route-company-bank-wrong-period")]
    [InlineData("route-company-wrong-bank-wrong-period")]
    public async Task BankingImportEndpoint_RejectsMismatchedRouteCompanyBeforeWrites(string scenario)
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bank = new BankAccount
        {
            CompanyId = scenario == "route-company-bank-wrong-period" ? period.CompanyId : otherPeriod.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = AuthenticatedRole("Reviewer");
        var formFile = new FormFile(
            new MemoryStream(Encoding.UTF8.GetBytes("Date,Description,Amount\n01/01/2025,Receipt,100\n")),
            0,
            "Date,Description,Amount\n01/01/2025,Receipt,100\n".Length,
            "file",
            "bank.csv")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/csv"
        };
        context.Request.ContentType = "multipart/form-data; boundary=test";
        context.Request.Form = new FormCollection([], new FormFileCollection { formFile });
        var importService = new ImportService(db, Options.Create(new ImportLimitConfig()));
        var audit = new AuditService(db);

        var result = await BankingEndpoints.ImportCsvEndpointAsync(
            period.CompanyId,
            bank.Id,
            otherPeriod.Id,
            context.Request,
            importService,
            audit,
            Options.Create(new ImportLimitConfig()),
            db,
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(result));
        Assert.Empty(await db.ImportBatches.ToListAsync());
        Assert.Empty(await db.ImportedTransactions.ToListAsync());
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == "BankCsvImported" || a.EntityType == "ImportBatch").ToListAsync());
    }

    [Fact]
    public void CorsOriginConfig_UsesConfiguredOriginsAndDevelopmentFallbackOnlyInDevelopment()
    {
        var configured = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AllowedOrigins:1"] = "https://app.accounts.example.ie"
            })
            .Build();
        var empty = new ConfigurationBuilder().Build();

        Assert.Equal(
            ["https://accounts.example.ie", "https://app.accounts.example.ie"],
            CorsOriginConfig.Resolve(configured, new TestEnvironment("Production")));
        Assert.Equal(
            ["http://localhost:3000", "http://localhost:5173", "http://localhost:5174"],
            CorsOriginConfig.Resolve(empty, new TestEnvironment("Development")));
        Assert.Empty(CorsOriginConfig.Resolve(empty, new TestEnvironment("Staging")));
    }

    [Fact]
    public void BackendCors_UsesExplicitConfiguredOriginsWithoutWildcardPolicy()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Program.cs"));

        Assert.Contains("CorsOriginConfig.Resolve", program);
        Assert.Contains("policy.WithOrigins(origins)", program);
        Assert.DoesNotContain("AllowAnyOrigin", program);
        Assert.DoesNotContain("?? [\"http://localhost:3000\"]", program);
    }

    [Fact]
    public void BackendBaseConfig_DoesNotBakeDevelopmentOnlyRuntimeDefaults()
    {
        var apiPath = Path.Combine(RepositoryRoot(), "backend", "Accounts.Api");
        var appsettings = File.ReadAllText(Path.Combine(apiPath, "appsettings.json"));
        var developmentSettings = File.ReadAllText(Path.Combine(apiPath, "appsettings.Development.json"));
        var baseConfig = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(apiPath, "appsettings.json"), optional: false)
            .Build();
        var developmentConfig = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(apiPath, "appsettings.json"), optional: false)
            .AddJsonFile(Path.Combine(apiPath, "appsettings.Development.json"), optional: false)
            .Build();

        Assert.DoesNotContain("\"AllowedOrigins\"", appsettings);
        Assert.DoesNotContain("localhost:3000", appsettings);
        Assert.True(string.IsNullOrWhiteSpace(baseConfig["AllowedHosts"]));
        Assert.True(string.IsNullOrWhiteSpace(baseConfig.GetConnectionString("DefaultConnection")));
        Assert.False(new DatabaseStartupConfig().AutoMigrateOnStartup);
        Assert.False(new DatabaseStartupConfig().SeedDemoData);
        Assert.DoesNotContain("accounts_dev", appsettings);
        Assert.DoesNotContain("\"AllowedHosts\": \"*\"", appsettings);
        Assert.DoesNotContain("\"DatabaseStartup\"", appsettings);
        Assert.DoesNotContain("\"ApiAccess\"", appsettings);
        Assert.DoesNotContain("\"AuditIntegrity\"", appsettings);
        Assert.Contains("\"AllowedOrigins\"", developmentSettings);
        Assert.Contains("http://localhost:3000", developmentSettings);
        Assert.Contains("\"DatabaseStartup\"", developmentSettings);
        Assert.Contains("\"ApiAccess\"", developmentSettings);
        Assert.Contains("\"AuditIntegrity\"", developmentSettings);
        Assert.Equal("*", developmentConfig["AllowedHosts"]);
        Assert.Contains("accounts_dev", developmentConfig.GetConnectionString("DefaultConnection"));
        Assert.True(developmentConfig.GetValue<bool>("DatabaseStartup:AutoMigrateOnStartup"));
        Assert.True(developmentConfig.GetValue<bool>("DatabaseStartup:SeedDemoData"));
        Assert.False(developmentConfig.GetValue<bool>("DatabaseStartup:SeedDemoUsers"));
        Assert.False(developmentConfig.GetValue<bool>("DatabaseStartup:SeedSampleCompanies"));
        Assert.False(developmentConfig.GetValue<bool>("ApiAccess:Enabled"));
    }

    [Fact]
    public void LocalDevelopmentConfig_BootstrapsSingleAdminForSeededCharityWorkspace()
    {
        var apiPath = Path.Combine(RepositoryRoot(), "backend", "Accounts.Api");
        var developmentConfig = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(apiPath, "appsettings.json"), optional: false)
            .AddJsonFile(Path.Combine(apiPath, "appsettings.Development.json"), optional: false)
            .Build();

        Assert.True(developmentConfig.GetValue<bool>("BootstrapOwner:Enabled"));
        Assert.Equal("Accounts v2 Demo Tenant", developmentConfig["BootstrapOwner:TenantName"]);
        Assert.Equal("main-demo", developmentConfig["BootstrapOwner:TenantSlug"]);
        Assert.Equal("admin@accounts.local", developmentConfig["BootstrapOwner:OwnerEmail"]);
        Assert.Equal("Local Charity Admin", developmentConfig["BootstrapOwner:OwnerDisplayName"]);
        Assert.Equal("LocalAdmin!Accounts-2026-9Qx", developmentConfig["BootstrapOwner:OwnerInitialPassword"]);
        Assert.False(developmentConfig.GetValue<bool>("BootstrapOwner:OwnerMustChangePassword"));
        Assert.Null(BootstrapOwnerPasswordPolicy.Validate(developmentConfig["BootstrapOwner:OwnerInitialPassword"]!));
    }

    [Fact]
    public void LocalCompose_UsesNoExternalServicesAndPassesLocalAdminBootstrapDefaults()
    {
        var compose = File.ReadAllText(Path.Combine(RepositoryRoot(), "compose.yml"));
        var frontendDockerfile = File.ReadAllText(Path.Combine(RepositoryRoot(), "Dockerfile.frontend"));

        Assert.Contains("postgres:16.4-alpine", compose);
        Assert.DoesNotContain("stripe", compose, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("s3", compose, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("azure", compose, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gcs", compose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BootstrapOwner__Enabled: \"true\"", compose);
        Assert.Contains("BootstrapOwner__TenantSlug: \"main-demo\"", compose);
        Assert.Contains("BootstrapOwner__OwnerEmail: \"admin@accounts.local\"", compose);
        Assert.Contains("BootstrapOwner__OwnerDisplayName: \"Local Charity Admin\"", compose);
        Assert.Contains("BootstrapOwner__OwnerInitialPassword: \"LocalAdmin!Accounts-2026-9Qx\"", compose);
        Assert.Contains("BootstrapOwner__OwnerMustChangePassword: \"false\"", compose);
        Assert.Contains("DatabaseStartup__SeedSampleCompanies: \"false\"", compose);
        Assert.Contains("NEXT_PUBLIC_DEMO_LOGIN_EMAIL: \"admin@accounts.local\"", compose);
        Assert.Contains("ARG NEXT_PUBLIC_DEMO_LOGIN_EMAIL", frontendDockerfile);
    }

    [Fact]
    public void DatabaseConnectionConfig_UsesDevelopmentFallbackOnlyInDevelopment()
    {
        var empty = new ConfigurationBuilder().Build();
        var configured = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = " Host=db;Database=accounts "
            })
            .Build();

        Assert.Contains("accounts_dev", DatabaseConnectionConfig.Resolve(empty, new TestEnvironment("Development")));
        Assert.Equal(
            "Host=db;Database=accounts",
            DatabaseConnectionConfig.Resolve(configured, new TestEnvironment("Production")));
        var error = Assert.Throws<InvalidOperationException>(() =>
            DatabaseConnectionConfig.Resolve(empty, new TestEnvironment("Staging")));
        Assert.Contains("ConnectionStrings:DefaultConnection", error.Message);
        Assert.Contains("outside Development", error.Message);
    }

    [Fact]
    public void ProductionSafety_BlocksDemoDatabaseStartupInProduction()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=accounts_dev",
                ["AllowedOrigins:0"] = "http://localhost:3000"
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Production"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = true,
                SeedDemoData = true
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig { Enabled = false, RequireInProduction = true }),
                new TestEnvironment("Production")));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("AutoMigrateOnStartup"));
        Assert.Contains(failures, f => f.Contains("SeedDemoData"));
        Assert.Contains(failures, f => f.Contains("development database password"));
        Assert.Contains(failures, f => f.Contains("localhost"));
        Assert.Contains(failures, f => f.Contains("AuthSession:SigningKey"));
        Assert.Contains(failures, f => f.Contains("AuditIntegrity:SigningKeys"));
        Assert.Contains(failures, f => f.Contains("ApiAccess:Enabled"));
    }

    [Fact]
    public void ProductionSafety_BlocksDevelopmentDefaultsInStaging()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "*",
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=accounts_dev",
                ["AllowedOrigins:0"] = "http://localhost:3000",
                ["AuthSession:SigningKey"] = DevelopmentSessionSigningKey()
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Staging"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = true,
                SeedDemoData = true
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig { Enabled = false, RequireInProduction = true }),
                new TestEnvironment("Staging")),
            Options.Create(new AuditIntegrityConfig
            {
                ActiveKeyId = "development-audit-checkpoint",
                SigningKeys =
                [
                    new AuditIntegritySigningKeyConfig
                    {
                        KeyId = "development-audit-checkpoint",
                        SigningKey = AuditIntegrityCheckpointService.DevelopmentSigningKeyBase64
                    }
                ]
            }));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("AutoMigrateOnStartup"));
        Assert.Contains(failures, f => f.Contains("SeedDemoData"));
        Assert.Contains(failures, f => f.Contains("development database password"));
        Assert.Contains(failures, f => f.Contains("localhost"));
        Assert.Contains(failures, f => f.Contains("AllowedHosts"));
        Assert.Contains(failures, f => f.Contains("development session signing key"));
        Assert.Contains(failures, f => f.Contains("development audit checkpoint key"));
        Assert.Contains(failures, f => f.Contains("ApiAccess:Enabled"));
    }

    [Fact]
    public void ProductionSafety_AllowsDevelopmentDefaultsInDevelopment()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "*",
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Password=accounts_dev",
                ["AllowedOrigins:0"] = "http://localhost:3000",
                ["AuthSession:SigningKey"] = DevelopmentSessionSigningKey()
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Development"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = true,
                SeedDemoData = true
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig { Enabled = false, RequireInProduction = true }),
                new TestEnvironment("Development")),
            Options.Create(new AuditIntegrityConfig()));

        Assert.Empty(service.Validate());
    }

    [Fact]
    public void ProductionSafety_BlocksDemoSeedingOutsideDevelopmentEvenWhenExplicitlyAllowed()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "accounts.example.ie",
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
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
                SeedDemoData = true,
                AllowDemoSeedInProduction = true
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
            Options.Create(AuditIntegrityCheckpointOptions()));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("SeedDemoData must be disabled"));
    }

    [Fact]
    public void ProductionSafety_BlocksMissingConnectionStringInProduction()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "accounts.example.ie",
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
            Options.Create(AuditIntegrityCheckpointOptions()));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("DefaultConnection must be explicitly configured"));
    }

    [Fact]
    public void ProductionSafety_BlocksWeakBootstrapOwnerPasswordOutsideDevelopment()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "accounts.example.ie",
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
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
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
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
            }));

        Assert.Empty(service.Validate());
    }

    [Fact]
    public void ProductionSafety_BlocksMissingAuditIntegritySigningKeyInProduction()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "accounts.example.ie",
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
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
                new TestEnvironment("Production")));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("AuditIntegrity:SigningKeys"));
    }

    [Fact]
    public void ProductionSafety_BlocksWeakAuditIntegritySigningKeyInProduction()
    {
        var failures = AuditIntegrityCheckpointService.ValidateConfiguration(new AuditIntegrityConfig
        {
            ActiveKeyId = "weak",
            SigningKeys =
            [
                new AuditIntegritySigningKeyConfig
                {
                    KeyId = "weak",
                    SigningKey = new string('a', 64)
                }
            ]
        });

        Assert.Contains(failures, f => f.Contains("AuditIntegrity:SigningKeys[weak]:SigningKey"));
    }

    [Fact]
    public void ProductionSafety_BlocksDevelopmentAuditIntegritySigningKeyInProduction()
    {
        var failures = AuditIntegrityCheckpointService.ValidateConfiguration(new AuditIntegrityConfig
        {
            ActiveKeyId = "development-audit-checkpoint",
            SigningKeys =
            [
                new AuditIntegritySigningKeyConfig
                {
                    KeyId = "development-audit-checkpoint",
                    SigningKey = AuditIntegrityCheckpointService.DevelopmentSigningKeyBase64
                }
            ]
        });

        Assert.Contains(failures, f => f.Contains("committed development audit checkpoint key"));
    }

    [Fact]
    public void ProductionSafety_AllowsDeliberateProductionConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "accounts.example.ie",
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = StrongSessionSigningKey(),
                ["RateLimits:TrustForwardedFor"] = "true",
                ["TRUST_PROXY_HEADERS"] = "true"
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

        Assert.Empty(service.Validate());
    }

    [Fact]
    public void ProductionSafety_BlocksTrustedForwardedForWithoutIngressAcknowledgement()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "accounts.example.ie",
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = StrongSessionSigningKey(),
                ["RateLimits:TrustForwardedFor"] = "true"
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

        Assert.Contains(failures, f => f.Contains("TRUST_PROXY_HEADERS"));
    }

    [Fact]
    public void ProductionSafety_BlocksBlankAllowedHostsInProduction()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = " ",
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
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
            Options.Create(AuditIntegrityCheckpointOptions()));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("AllowedHosts must be explicitly configured"));
    }

    [Fact]
    public void ProductionSafety_BlocksWildcardAllowedHostsInProduction()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "*",
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
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
            Options.Create(AuditIntegrityCheckpointOptions()));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("AllowedHosts"));
    }

    [Fact]
    public void ProductionSafety_BlocksHttpAllowedOriginsInProduction()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "accounts.example.ie",
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
                ["AllowedOrigins:0"] = "http://accounts.example.ie",
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
            Options.Create(AuditIntegrityCheckpointOptions()));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("AllowedOrigins"));
    }

    [Fact]
    public void ProductionSafety_BlocksWeakSessionSigningKeyInProduction()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
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
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
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
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
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
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
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
            Options.Create(AuditIntegrityCheckpointOptions()));

        Assert.Empty(service.Validate());
    }

    [Fact]
    public void ProductionSafety_AllowsStrongBase64UrlSessionSigningConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "accounts.example.ie",
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
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
            Options.Create(AuditIntegrityCheckpointOptions()));

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
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
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
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
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
    public async Task SeedData_RemovesNonCharitySampleCompaniesWhenSampleCompanySeedingIsDisabled()
    {
        await using var db = CreateDbContext();

        await SeedData.SeedAsync(db, seedDemoUsers: false, seedSampleCompanies: true);
        Assert.True(await db.Companies.AnyAsync(c => c.CroNumber == "654321"));
        Assert.True(await db.Companies.AnyAsync(c => c.CroNumber == "789012"));

        db.ChangeTracker.Clear();
        await SeedData.SeedAsync(db, seedDemoUsers: false, seedSampleCompanies: false);

        Assert.True(await db.Companies.AnyAsync(c =>
            c.CroNumber == "567890"
            && c.LegalName == "Green Valley Community Development CLG"
            && c.IsCharitableOrganisation));
        Assert.False(await db.Companies.AnyAsync(c => c.CroNumber == "654321"));
        Assert.False(await db.Companies.AnyAsync(c => c.CroNumber == "789012"));
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
        var method = typeof(AuthService).GetMethod("ChangePasswordAsync", [typeof(int), typeof(string), typeof(string)]);
        Assert.True(method is not null, "AuthService should expose ChangePasswordAsync for first-login password rotation.");

        var task = Assert.IsAssignableFrom<Task>(method.Invoke(service, [user.Id, "Correct Horse Battery Staple 1!", "New Correct Horse Battery 2!"]));
        await task;
        var result = task.GetType().GetProperty("Result")?.GetValue(task);
        Assert.NotNull(result);
        var succeeded = (bool)(result.GetType().GetProperty("Succeeded")?.GetValue(result) ?? false);
        Assert.True(succeeded);
        var resultUser = result.GetType().GetProperty("User")?.GetValue(result);
        Assert.NotNull(resultUser);
        var resultPasswordChangedAt = Assert.IsType<DateTime>(
            typeof(AuthenticatedUser).GetProperty("PasswordLastChangedAt")?.GetValue(resultUser));
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
    public void AuthService_UsesSetBasedUpdatesForConcurrentAuthStateMutations()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Services", "AuthService.cs"));

        Assert.Contains("ExecuteUpdateAsync", source);
        Assert.Contains("SetProperty(u => u.SessionVersion, u => u.SessionVersion + 1)", source);
        Assert.Contains("SetProperty(u => u.FailedLoginCount", source);
        Assert.DoesNotContain("user.SessionVersion += 1", source);
        Assert.DoesNotContain("user.FailedLoginCount += 1", source);
    }

    [Fact]
    public void AuthService_PasswordChangePrincipalUsesWrittenSessionVersionNotPostUpdateReload()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Services", "AuthService.cs"));

        Assert.Contains("var nextSessionVersion = user", source);
        Assert.Contains("SessionVersion + 1", source);
        Assert.Contains("u.SessionVersion == user.SessionVersion", source);
        Assert.Contains("SetProperty(u => u.SessionVersion, nextSessionVersion)", source);
        Assert.DoesNotContain("LoadUserForAuthAsync", source);
    }

    [Fact]
    public void AuthService_RelationalFailedLoginRepairLocksThresholdRowsWithoutActiveLock()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Services", "AuthService.cs"));

        Assert.Contains("u.LockedUntilUtc == null || u.LockedUntilUtc <= now", source);
        Assert.Contains("previousFailedLoginCount < MaxFailedLoginAttempts", source);
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
    public async Task AuthService_SessionExpiryUsesClampedExpiryMinutes()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!");
        var service = CreateAuthService(db, expiryMinutes: 5);
        var login = await service.LoginAsync("owner@example.ie", "Correct Horse Battery Staple 1!");
        var now = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

        var cookieValue = service.CreateSessionCookieValue(login.User!, now);
        var stillValidAtClampedExpiry = await service.ReadSessionAsync(cookieValue, now.AddMinutes(14));
        var expiredAfterClampedExpiry = await service.ReadSessionAsync(cookieValue, now.AddMinutes(16));

        Assert.NotNull(stillValidAtClampedExpiry);
        Assert.Null(expiredAfterClampedExpiry);
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

    [Fact]
    public async Task TenantIsolation_CompanyAndPeriodAccessIsScopedToCallersTenant()
    {
        // BL-10: behavioural guard that company/period access is filtered to the caller's tenant at the
        // data-access query layer, so a cross-tenant id is invisible (the endpoint then returns 404).
        await using var db = CreateDbContext();
        var companyA = new Company { TenantId = 1, LegalName = "Tenant A Ltd", CroNumber = "100001", CompanyType = CompanyType.Private, IncorporationDate = new DateOnly(2025, 1, 1), ArdMonth = 12, RegisteredOfficeAddress1 = "1 A Street", RegisteredOfficeCity = "Dublin", RegisteredOfficeCounty = "Dublin" };
        var companyB = new Company { TenantId = 2, LegalName = "Tenant B Ltd", CroNumber = "100002", CompanyType = CompanyType.Private, IncorporationDate = new DateOnly(2025, 1, 1), ArdMonth = 12, RegisteredOfficeAddress1 = "1 B Street", RegisteredOfficeCity = "Cork", RegisteredOfficeCounty = "Cork" };
        db.Companies.AddRange(companyA, companyB);
        await db.SaveChangesAsync();
        var periodB = new AccountingPeriod { CompanyId = companyB.Id, PeriodStart = new DateOnly(2025, 1, 1), PeriodEnd = new DateOnly(2025, 12, 31), IsFirstYear = true };
        db.AccountingPeriods.Add(periodB);
        await db.SaveChangesAsync();

        var tenantAContext = new DefaultHttpContext();
        tenantAContext.Items[AuthContext.ItemKey] = new AuthenticatedUser(7, 1, "Firm A", "owner@a.ie", "Owner", "Owner");

        Assert.True(await CompanyEndpointAccess.CanAccessCompanyAsync(tenantAContext, db, companyA.Id));
        Assert.False(await CompanyEndpointAccess.CanAccessCompanyAsync(tenantAContext, db, companyB.Id));
        Assert.False(await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(tenantAContext, db, companyB.Id, periodB.Id));
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
    public void AuditTrailMiddleware_RunsAfterSessionAndBeforeCsrfGuard()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Program.cs"));
        var sessionIndex = program.IndexOf("UseMiddleware<Accounts.Api.Middleware.UserSessionMiddleware>", StringComparison.Ordinal);
        var auditIndex = program.IndexOf("UseMiddleware<Accounts.Api.Middleware.AuditTrailMiddleware>", StringComparison.Ordinal);
        var csrfIndex = program.IndexOf("UseMiddleware<Accounts.Api.Middleware.CsrfProtectionMiddleware>", StringComparison.Ordinal);

        Assert.NotEqual(-1, sessionIndex);
        Assert.NotEqual(-1, auditIndex);
        Assert.NotEqual(-1, csrfIndex);
        Assert.True(sessionIndex < auditIndex);
        Assert.True(auditIndex < csrfIndex);
    }

    [Fact]
    public async Task AuditTrailMiddleware_LogsCsrfRejectedUnsafeCompanyWrite()
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
        var csrf = new CsrfProtectionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var audit = new AuditTrailMiddleware(innerContext =>
            csrf.InvokeAsync(innerContext, Options.Create(new AuthSessionConfig())));

        await audit.InvokeAsync(context, db);

        var entry = await db.AuditLogs.SingleAsync(a => a.EntityType == "ApiRequest");
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("ApiWriteRejected", entry.Action);
        Assert.Equal(period.CompanyId, entry.CompanyId);
        Assert.Equal(period.Id, entry.PeriodId);
        Assert.Equal("csrf-rejected-001", entry.RequestId);
        Assert.Equal("reviewer@example.ie", entry.UserId);
        Assert.Contains("\"statusCode\":403", entry.NewValueJson);
    }

    [Fact]
    public async Task AuditTrailMiddleware_LogsSuccessfulUnsafeCompanyPeriodRequest()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/filing/cro-status";
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            7,
            1,
            "Firm A",
            "reviewer@example.ie",
            "Maeve Reviewer",
            "Reviewer");
        var middleware = new AuditTrailMiddleware(innerContext =>
        {
            innerContext.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        var entry = await db.AuditLogs.SingleAsync(a => a.EntityType == "ApiRequest");
        Assert.Equal(period.CompanyId, entry.CompanyId);
        Assert.Equal(period.Id, entry.PeriodId);
        Assert.Equal(period.Id, entry.EntityId);
        Assert.Equal("ApiWriteSucceeded", entry.Action);
        Assert.Equal("reviewer@example.ie", entry.UserId);
        Assert.Contains("cro-status", entry.NewValueJson);
    }

    [Fact]
    public async Task AuditService_LogAsync_PersistsEvidenceMetadataAndIntegrityHash()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);

        await LogAuditWithEvidenceMetadataAsync(
            audit,
            period.CompanyId,
            period.Id,
            "FilingRegime",
            period.Id,
            "FilingRegimeDetermined",
            new { Regime = "Unknown" },
            new { Regime = "Micro" },
            "reviewer@example.ie",
            tenantId: 17,
            requestId: "req-audit-001",
            actorDisplayName: "Maeve Reviewer");

        var entry = await db.AuditLogs.SingleAsync();
        AssertAuditLogValue(entry, "TenantId", 17);
        AssertAuditLogValue(entry, "RequestId", "req-audit-001");
        AssertAuditLogValue(entry, "ActorDisplayName", "Maeve Reviewer");
        Assert.Null(ReadAuditLogValue(entry, "PreviousIntegrityHash"));
        Assert.True(IsHex64(RequiredAuditLogString(entry, "IntegrityHash")));
    }

    [Fact]
    public async Task AuditService_LogAsync_RedactsSensitivePayloadFieldsBeforeStorage()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);

        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "UserAccount",
            10,
            "UserUpdated",
            oldValue: new
            {
                Email = "user@example.ie",
                Password = "old-password",
                PasswordHash = "old-hash",
                PasswordSalt = "old-salt",
                Profile = new
                {
                    SectionKey = "tax",
                    CsrfToken = "old-csrf-token"
                }
            },
            newValue: new
            {
                Email = "user@example.ie",
                SectionKey = "tax",
                ApiKey = "new-api-key",
                Authorization = "Bearer new-token",
                Cookie = "accounts_session=secret-cookie",
                Items = new[]
                {
                    new
                    {
                        Secret = "nested-secret",
                        Description = "Safe audit evidence"
                    }
                }
            },
            userId: "reviewer@example.ie");

        var entry = await db.AuditLogs.SingleAsync();
        Assert.Contains("\"Password\":\"[REDACTED]\"", entry.OldValueJson);
        Assert.Contains("\"PasswordHash\":\"[REDACTED]\"", entry.OldValueJson);
        Assert.Contains("\"PasswordSalt\":\"[REDACTED]\"", entry.OldValueJson);
        Assert.Contains("\"CsrfToken\":\"[REDACTED]\"", entry.OldValueJson);
        Assert.Contains("\"ApiKey\":\"[REDACTED]\"", entry.NewValueJson);
        Assert.Contains("\"Authorization\":\"[REDACTED]\"", entry.NewValueJson);
        Assert.Contains("\"Cookie\":\"[REDACTED]\"", entry.NewValueJson);
        Assert.Contains("\"Secret\":\"[REDACTED]\"", entry.NewValueJson);
        Assert.Contains("\"SectionKey\":\"tax\"", entry.OldValueJson);
        Assert.Contains("\"SectionKey\":\"tax\"", entry.NewValueJson);
        Assert.Contains("Safe audit evidence", entry.NewValueJson);
        Assert.DoesNotContain("old-password", entry.OldValueJson);
        Assert.DoesNotContain("old-hash", entry.OldValueJson);
        Assert.DoesNotContain("old-salt", entry.OldValueJson);
        Assert.DoesNotContain("old-csrf-token", entry.OldValueJson);
        Assert.DoesNotContain("new-api-key", entry.NewValueJson);
        Assert.DoesNotContain("Bearer new-token", entry.NewValueJson);
        Assert.DoesNotContain("secret-cookie", entry.NewValueJson);
        Assert.DoesNotContain("nested-secret", entry.NewValueJson);
        Assert.True(IsHex64(RequiredAuditLogString(entry, "IntegrityHash")));
    }

    [Fact]
    public async Task AuditService_LogAsync_ChainsRowsForSameCompany()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);

        await LogAuditWithEvidenceMetadataAsync(
            audit,
            period.CompanyId,
            period.Id,
            "FilingRegime",
            period.Id,
            "FilingRegimeDetermined",
            null,
            new { Regime = "Micro" },
            "reviewer@example.ie",
            tenantId: 17,
            requestId: "req-chain-001",
            actorDisplayName: "Maeve Reviewer");
        await LogAuditWithEvidenceMetadataAsync(
            audit,
            period.CompanyId,
            period.Id,
            "CroFilingPackage",
            period.Id,
            "CroDocumentGenerated",
            null,
            new { DocumentType = "AccountsPackage" },
            "reviewer@example.ie",
            tenantId: 17,
            requestId: "req-chain-002",
            actorDisplayName: "Maeve Reviewer");

        var entries = await db.AuditLogs.OrderBy(a => a.Id).ToListAsync();
        var firstHash = RequiredAuditLogString(entries[0], "IntegrityHash");
        var secondHash = RequiredAuditLogString(entries[1], "IntegrityHash");
        Assert.True(IsHex64(firstHash));
        Assert.True(IsHex64(secondHash));
        Assert.NotEqual(firstHash, secondHash);
        Assert.Equal(firstHash, RequiredAuditLogString(entries[1], "PreviousIntegrityHash"));
    }

    [Fact]
    public void AuditService_UsesPostgresAdvisoryTransactionLockBeforeReadingHashChainTail()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "backend",
            "Accounts.Api",
            "Services",
            "AuditService.cs"));

        Assert.Contains("pg_advisory_xact_lock", source);
        Assert.Contains("AcquireDistributedChainLockAsync", source);
        Assert.Contains("db.Database.IsRelational()", source);
        Assert.Contains("Database.ProviderName", source);
        Assert.Contains("Npgsql.EntityFrameworkCore.PostgreSQL", source);
        AssertOccursBefore(
            source,
            "AcquireDistributedChainLockAsync(companyId, tenantId, auditCancellationToken)",
            "GetPreviousIntegrityHashAsync(companyId, tenantId, auditCancellationToken)");

        static void AssertOccursBefore(string source, string first, string second)
        {
            var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
            Assert.True(firstIndex >= 0, $"Expected source to contain '{first}'.");
            Assert.True(secondIndex >= 0, $"Expected source to contain '{second}'.");
            Assert.True(firstIndex < secondIndex, $"Expected '{first}' before '{second}'.");
        }
    }

    [Fact]
    public async Task AuditIntegrityService_VerifiesValidCompanyHashChain()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "FilingRegime",
            period.Id,
            AuditEventCodes.FilingRegimeDetermined,
            newValue: new { Regime = "Micro" },
            userId: "reviewer@example.ie");
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "CroFilingPackage",
            period.Id,
            AuditEventCodes.CroDocumentGenerated,
            newValue: new { DocumentType = "AccountsPackage" },
            userId: "reviewer@example.ie");
        var entries = await db.AuditLogs.OrderBy(a => a.Id).ToListAsync();
        var verifier = new AuditIntegrityService(db);
        var beforeCheck = DateTime.UtcNow;

        var report = await verifier.VerifyCompanyAsync(period.CompanyId);

        Assert.True(report.IsValid);
        Assert.Equal(2, report.CheckedEntries);
        Assert.Equal(0, report.UncheckedLegacyEntries);
        Assert.Equal(0, report.IssueCount);
        Assert.Empty(report.Issues);
        Assert.Equal(entries[0].Id, report.FirstAuditLogId);
        Assert.Equal(entries[1].Id, report.LastAuditLogId);
        Assert.Equal(entries[0].IntegrityHash, report.FirstHash);
        Assert.Equal(entries[1].IntegrityHash, report.LastHash);
        Assert.True(report.CheckedAtUtc >= beforeCheck);
    }

    [Fact]
    public async Task AuditIntegrityService_DetectsPayloadTamperingAndChainBreak()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "FilingRegime",
            period.Id,
            AuditEventCodes.FilingRegimeDetermined,
            newValue: new { Regime = "Micro" },
            userId: "reviewer@example.ie");
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "CroFilingPackage",
            period.Id,
            AuditEventCodes.CroDocumentGenerated,
            newValue: new { DocumentType = "AccountsPackage" },
            userId: "reviewer@example.ie");
        var entries = await db.AuditLogs.OrderBy(a => a.Id).ToListAsync();
        entries[0].NewValueJson = "{\"Regime\":\"Full\"}";
        entries[1].PreviousIntegrityHash = new string('0', 64);
        await db.SaveChangesAsync();
        var verifier = new AuditIntegrityService(db);

        var report = await verifier.VerifyCompanyAsync(period.CompanyId);

        Assert.False(report.IsValid);
        Assert.Equal(2, report.CheckedEntries);
        Assert.Equal(0, report.UncheckedLegacyEntries);
        Assert.Contains(report.Issues, issue =>
            issue.AuditLogId == entries[0].Id
            && issue.Code == AuditIntegrityIssueCodes.HashMismatch
            && issue.Timestamp == entries[0].Timestamp);
        Assert.Contains(report.Issues, issue =>
            issue.AuditLogId == entries[1].Id
            && issue.Code == AuditIntegrityIssueCodes.ChainBreak
            && issue.Expected == entries[0].IntegrityHash
            && issue.Actual == new string('0', 64));
    }

    [Fact]
    public async Task AuditIntegrityService_ReportsLegacyUnhashedEntriesSeparately()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.AuditLogs.Add(new AuditLog
        {
            CompanyId = period.CompanyId,
            PeriodId = period.Id,
            EntityType = "LegacySeed",
            EntityId = period.Id,
            Action = "Seeded",
            NewValueJson = "{\"Legacy\":true}"
        });
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "FilingRegime",
            period.Id,
            AuditEventCodes.FilingRegimeDetermined,
            newValue: new { Regime = "Micro" },
            userId: "reviewer@example.ie");
        var entries = await db.AuditLogs.OrderBy(a => a.Id).ToListAsync();
        var verifier = new AuditIntegrityService(db);

        var report = await verifier.VerifyCompanyAsync(period.CompanyId);

        Assert.False(report.IsValid);
        Assert.Equal(1, report.CheckedEntries);
        Assert.Equal(1, report.UncheckedLegacyEntries);
        Assert.Equal(entries[1].Id, report.FirstAuditLogId);
        Assert.Equal(entries[1].Id, report.LastAuditLogId);
        Assert.Contains(report.Issues, issue =>
            issue.AuditLogId == entries[0].Id
            && issue.Code == AuditIntegrityIssueCodes.MissingHash
            && issue.Timestamp == entries[0].Timestamp);
    }

    [Fact]
    public async Task AuditIntegrityCheckpointService_CreatesSignedCheckpointForValidCompanyChain()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "FilingRegime",
            period.Id,
            AuditEventCodes.FilingRegimeDetermined,
            newValue: new { Regime = "Micro" },
            userId: "reviewer@example.ie");
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "CroFilingPackage",
            period.Id,
            AuditEventCodes.CroDocumentGenerated,
            newValue: new { DocumentType = "AccountsPackage" },
            userId: "reviewer@example.ie");
        var entries = await db.AuditLogs.OrderBy(a => a.Id).ToListAsync();
        var service = new AuditIntegrityCheckpointService(db, Options.Create(AuditIntegrityCheckpointOptions()));
        var beforeCheckpoint = DateTime.UtcNow;

        var checkpoint = await service.CreateCompanyCheckpointAsync(
            period.CompanyId,
            createdByUserId: "owner@example.ie",
            createdByDisplayName: "Owner User",
            requestId: "req-anchor-001");

        Assert.Equal(period.CompanyId, checkpoint.CompanyId);
        Assert.Equal(entries[1].Id, checkpoint.LastAuditLogId);
        Assert.Equal(entries[1].IntegrityHash, checkpoint.LastIntegrityHash);
        Assert.Equal(2, checkpoint.CheckedEntries);
        Assert.Equal("audit-key-2026", checkpoint.KeyId);
        Assert.Equal("owner@example.ie", checkpoint.CreatedByUserId);
        Assert.Equal("Owner User", checkpoint.CreatedByDisplayName);
        Assert.Equal("req-anchor-001", checkpoint.RequestId);
        Assert.True(checkpoint.CreatedAtUtc >= beforeCheckpoint);
        Assert.True(IsHex64(checkpoint.Signature));
        Assert.Same(checkpoint, await db.AuditIntegrityCheckpoints.SingleAsync());

        var verification = await service.VerifyLatestCompanyCheckpointAsync(period.CompanyId);

        Assert.True(verification.IsValid);
        Assert.True(verification.HasCheckpoint);
        Assert.Equal(checkpoint.Id, verification.CheckpointId);
        Assert.Equal(checkpoint.LastAuditLogId, verification.LastAuditLogId);
        Assert.Equal(checkpoint.LastIntegrityHash, verification.LastIntegrityHash);
        Assert.Equal(checkpoint.KeyId, verification.KeyId);
        Assert.Equal(0, verification.IssueCount);
        Assert.Empty(verification.Issues);
    }

    [Fact]
    public async Task AuditIntegrityCheckpointService_DetectsTamperedCheckpointSignature()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "FilingRegime",
            period.Id,
            AuditEventCodes.FilingRegimeDetermined,
            newValue: new { Regime = "Micro" },
            userId: "reviewer@example.ie");
        var service = new AuditIntegrityCheckpointService(db, Options.Create(AuditIntegrityCheckpointOptions()));
        var checkpoint = await service.CreateCompanyCheckpointAsync(
            period.CompanyId,
            createdByUserId: "owner@example.ie",
            createdByDisplayName: "Owner User",
            requestId: "req-anchor-002");
        checkpoint.Signature = new string('0', 64);
        await db.SaveChangesAsync();

        var verification = await service.VerifyLatestCompanyCheckpointAsync(period.CompanyId);

        Assert.False(verification.IsValid);
        Assert.Contains(verification.Issues, issue =>
            issue.CheckpointId == checkpoint.Id
            && issue.Code == AuditIntegrityCheckpointIssueCodes.SignatureMismatch
            && issue.Expected is null
            && issue.Actual is null);
    }

    [Fact]
    public async Task AuditIntegrityCheckpointService_DetectsRewrittenAnchoredAuditLog()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "FilingRegime",
            period.Id,
            AuditEventCodes.FilingRegimeDetermined,
            newValue: new { Regime = "Micro" },
            userId: "reviewer@example.ie");
        var service = new AuditIntegrityCheckpointService(db, Options.Create(AuditIntegrityCheckpointOptions()));
        var checkpoint = await service.CreateCompanyCheckpointAsync(
            period.CompanyId,
            createdByUserId: "owner@example.ie",
            createdByDisplayName: "Owner User",
            requestId: "req-anchor-003");
        var anchoredEntry = await db.AuditLogs.SingleAsync(a => a.Id == checkpoint.LastAuditLogId);
        anchoredEntry.IntegrityHash = new string('a', 64);
        await db.SaveChangesAsync();

        var verification = await service.VerifyLatestCompanyCheckpointAsync(period.CompanyId);

        Assert.False(verification.IsValid);
        Assert.Contains(verification.Issues, issue =>
            issue.CheckpointId == checkpoint.Id
            && issue.AuditLogId == checkpoint.LastAuditLogId
            && issue.Code == AuditIntegrityCheckpointIssueCodes.AnchoredHashMismatch
            && issue.Expected == checkpoint.LastIntegrityHash
            && issue.Actual == new string('a', 64));
    }

    [Fact]
    public async Task AuditIntegrityCheckpointService_DetectsMissingAnchoredAuditLog()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "FilingRegime",
            period.Id,
            AuditEventCodes.FilingRegimeDetermined,
            newValue: new { Regime = "Micro" },
            userId: "reviewer@example.ie");
        var service = new AuditIntegrityCheckpointService(db, Options.Create(AuditIntegrityCheckpointOptions()));
        var checkpoint = await service.CreateCompanyCheckpointAsync(
            period.CompanyId,
            createdByUserId: "owner@example.ie",
            createdByDisplayName: "Owner User",
            requestId: "req-anchor-missing");
        var anchoredEntry = await db.AuditLogs.SingleAsync(a => a.Id == checkpoint.LastAuditLogId);
        db.AuditLogs.Remove(anchoredEntry);
        await db.SaveChangesAsync();

        var verification = await service.VerifyLatestCompanyCheckpointAsync(period.CompanyId);

        Assert.False(verification.IsValid);
        Assert.Contains(verification.Issues, issue =>
            issue.CheckpointId == checkpoint.Id
            && issue.AuditLogId == checkpoint.LastAuditLogId
            && issue.Code == AuditIntegrityCheckpointIssueCodes.AnchoredAuditLogMissing);
    }

    [Fact]
    public async Task AuditIntegrityCheckpointService_VerifiesLatestCheckpointWhenMultipleExist()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "FilingRegime",
            period.Id,
            AuditEventCodes.FilingRegimeDetermined,
            newValue: new { Regime = "Micro" },
            userId: "reviewer@example.ie");
        var service = new AuditIntegrityCheckpointService(db, Options.Create(AuditIntegrityCheckpointOptions()));
        var firstCheckpoint = await service.CreateCompanyCheckpointAsync(
            period.CompanyId,
            createdByUserId: "owner@example.ie",
            createdByDisplayName: "Owner User",
            requestId: "req-anchor-005-a");
        await audit.LogAsync(
            period.CompanyId,
            period.Id,
            "CroFilingPackage",
            period.Id,
            AuditEventCodes.CroDocumentGenerated,
            newValue: new { DocumentType = "AccountsPackage" },
            userId: "reviewer@example.ie");

        var latestCheckpoint = await service.CreateCompanyCheckpointAsync(
            period.CompanyId,
            createdByUserId: "owner@example.ie",
            createdByDisplayName: "Owner User",
            requestId: "req-anchor-005-b");
        var verification = await service.VerifyLatestCompanyCheckpointAsync(period.CompanyId);

        Assert.True(verification.IsValid);
        Assert.NotEqual(firstCheckpoint.Id, verification.CheckpointId);
        Assert.Equal(latestCheckpoint.Id, verification.CheckpointId);
        Assert.Equal(latestCheckpoint.LastAuditLogId, verification.LastAuditLogId);
        Assert.Equal(2, latestCheckpoint.CheckedEntries);
    }

    [Fact]
    public async Task AuditIntegrityCheckpointService_RefusesCheckpointForInvalidCompanyChain()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.AuditLogs.Add(new AuditLog
        {
            CompanyId = period.CompanyId,
            PeriodId = period.Id,
            EntityType = "LegacySeed",
            EntityId = period.Id,
            Action = "Seeded"
        });
        await db.SaveChangesAsync();
        var service = new AuditIntegrityCheckpointService(db, Options.Create(AuditIntegrityCheckpointOptions()));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateCompanyCheckpointAsync(
                period.CompanyId,
                createdByUserId: "owner@example.ie",
                createdByDisplayName: "Owner User",
                requestId: "req-anchor-004"));

        Assert.Contains("valid audit integrity chain", error.Message);
        Assert.Empty(db.AuditIntegrityCheckpoints);
    }

    [Fact]
    public void AuditIntegrityEndpoint_IsExposedWithAuditLogEndpoints()
    {
        var endpoints = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "backend",
            "Accounts.Api",
            "Endpoints",
            "AdjustmentEndpoints.cs"));
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Program.cs"));

        Assert.Contains("/integrity", endpoints);
        Assert.Contains("/integrity/checkpoints", endpoints);
        Assert.Contains("AuditIntegrityService", endpoints);
        Assert.Contains("AuditIntegrityCheckpointService", endpoints);
        Assert.Contains("RequireOwner", endpoints);
        Assert.Contains("AddScoped<AuditIntegrityService>", program);
        Assert.Contains("AddScoped<AuditIntegrityCheckpointService>", program);
    }

    [Fact]
    public async Task AuditCheckpointInputs_AllowsOnlyOwnersToCreateCheckpointsWithPlainForbiddenResponse()
    {
        Assert.Null(AuditCheckpointInputs.RequireOwner(AuthenticatedRole("Owner")));
        var denial = AuditCheckpointInputs.RequireOwner(AuthenticatedRole("Accountant"));
        Assert.NotNull(denial);
        Assert.NotNull(AuditCheckpointInputs.RequireOwner(AuthenticatedRole("Reviewer")));
        Assert.NotNull(AuditCheckpointInputs.RequireOwner(AuthenticatedRole("Client")));

        using var provider = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = provider
        };
        await denial!.ExecuteAsync(context);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task AuditTrailMiddleware_RecordsRequestCorrelationAndActorMetadataForSucceededAndRejectedWrites()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var successContext = WriteAuditContext(
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/filing/cro-status",
            "corr-success-001");
        var rejectedContext = WriteAuditContext(
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/adjustments",
            "corr-rejected-001");

        var successMiddleware = new AuditTrailMiddleware(innerContext =>
        {
            innerContext.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });
        var rejectedMiddleware = new AuditTrailMiddleware(innerContext =>
        {
            innerContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        });

        await successMiddleware.InvokeAsync(successContext, db);
        await rejectedMiddleware.InvokeAsync(rejectedContext, db);

        var entries = await db.AuditLogs.Where(a => a.EntityType == "ApiRequest").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal("ApiWriteSucceeded", entries[0].Action);
        Assert.Equal("ApiWriteRejected", entries[1].Action);
        AssertAuditLogValue(entries[0], "TenantId", 17);
        AssertAuditLogValue(entries[0], "RequestId", "corr-success-001");
        AssertAuditLogValue(entries[0], "ActorDisplayName", "Maeve Reviewer");
        AssertAuditLogValue(entries[1], "TenantId", 17);
        AssertAuditLogValue(entries[1], "RequestId", "corr-rejected-001");
        AssertAuditLogValue(entries[1], "ActorDisplayName", "Maeve Reviewer");
    }

    [Fact]
    public async Task AuditTrailMiddleware_DoesNotPersistPendingBusinessChangesWhenAuditingRejectedWrite()
    {
        var databaseName = Guid.NewGuid().ToString();
        var root = new InMemoryDatabaseRoot();
        await using (var seedDb = CreateDbContext(databaseName, root))
        {
            await SeedCompanyPeriodAsync(seedDb, isFirstYear: true);
        }

        await using var requestDb = CreateDbContext(databaseName, root);
        var period = await requestDb.AccountingPeriods.SingleAsync();
        var originalStatus = period.Status;
        var context = WriteAuditContext(
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/adjustments",
            "corr-rejected-pending");
        var middleware = new AuditTrailMiddleware(innerContext =>
        {
            period.Status = PeriodStatus.Finalised;
            innerContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, requestDb);

        await using var verifyDb = CreateDbContext(databaseName, root);
        var persisted = await verifyDb.AccountingPeriods.AsNoTracking().SingleAsync();
        Assert.Equal(originalStatus, persisted.Status);
        Assert.Equal("ApiWriteRejected", (await verifyDb.AuditLogs.SingleAsync()).Action);
    }

    [Fact]
    public async Task AuditService_LogAsync_UsesDatabaseStableTimestampPrecisionForHash()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);

        for (var i = 0; i < 25; i++)
        {
            await audit.LogAsync(
                period.CompanyId,
                period.Id,
                "FilingRegime",
                period.Id,
                "FilingRegimeDetermined",
                newValue: new { Sequence = i });
        }

        var entries = await db.AuditLogs.ToListAsync();
        Assert.All(entries, entry => Assert.Equal(0, entry.Timestamp.Ticks % 10));
    }

    [Fact]
    public void AuditLogModel_PreventsForkedIntegrityChains()
    {
        var context = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "backend",
            "Accounts.Api",
            "Data",
            "AccountsDbContext.cs"));
        var migration = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "backend",
            "Accounts.Api",
            "Data",
            "Migrations",
            "20260607005841_AddAuditLogEvidenceMetadata.cs"));

        Assert.Contains("HasIndex(a => a.PreviousIntegrityHash).IsUnique()", context);
        Assert.Contains("IX_audit_logs_PreviousIntegrityHash", migration);
        Assert.Contains("unique: true", migration);
    }

    [Fact]
    public void AuditTrailMiddleware_UsesDurableCancellationForCommittedAuditEvidence()
    {
        var middleware = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "backend",
            "Accounts.Api",
            "Middleware",
            "AuditTrailMiddleware.cs"));

        Assert.Contains("CommitAsync(CancellationToken.None)", middleware);
        Assert.Contains("RollbackAsync(CancellationToken.None)", middleware);
        Assert.Contains("durableAudit: true", middleware);
        Assert.Contains("cancellationToken: CancellationToken.None", middleware);
        Assert.DoesNotContain("cancellationToken: context.RequestAborted", middleware);
    }

    [Fact]
    public void AuditTrailMiddleware_UsesFreshScopeForPostRollbackAuditEvidence()
    {
        var middleware = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "backend",
            "Accounts.Api",
            "Middleware",
            "AuditTrailMiddleware.cs"));

        Assert.Contains("IServiceScopeFactory", middleware);
        Assert.Contains("CreateScope()", middleware);
        Assert.Contains("useFreshScope: true", middleware);
    }

    [Fact]
    public async Task AuditTrailMiddleware_DoesNotLogSafeReadsButLogsRejectedAndFailedWrites()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var getContext = new DefaultHttpContext();
        getContext.Request.Method = HttpMethods.Get;
        getContext.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/statements/readiness";
        var failedContext = new DefaultHttpContext();
        failedContext.Request.Method = HttpMethods.Post;
        failedContext.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/adjustments";
        failedContext.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            7,
            1,
            "Firm A",
            "reviewer@example.ie",
            "Maeve Reviewer",
            "Reviewer");
        var readMiddleware = new AuditTrailMiddleware(innerContext =>
        {
            innerContext.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });
        var failedMiddleware = new AuditTrailMiddleware(innerContext =>
        {
            innerContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        });
        var exceptionContext = new DefaultHttpContext();
        exceptionContext.Request.Method = HttpMethods.Post;
        exceptionContext.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/filing/cro-status";
        exceptionContext.Items[AuthContext.ItemKey] = failedContext.Items[AuthContext.ItemKey];
        var exceptionMiddleware = new AuditTrailMiddleware(_ =>
            throw new InvalidOperationException("database password=secret failure"));

        await readMiddleware.InvokeAsync(getContext, db);
        await failedMiddleware.InvokeAsync(failedContext, db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => exceptionMiddleware.InvokeAsync(exceptionContext, db));

        var entries = await db.AuditLogs.Where(a => a.EntityType == "ApiRequest").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal("ApiWriteRejected", entries[0].Action);
        Assert.Equal(StatusCodes.Status400BadRequest, JsonDocument.Parse(entries[0].NewValueJson!).RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal("ApiWriteFailed", entries[1].Action);
        Assert.Contains("InvalidOperationException", entries[1].NewValueJson);
        Assert.DoesNotContain("password=secret", entries[1].NewValueJson);
    }

    [Fact]
    public async Task AuditTrailMiddleware_ExtractsPeriodIdFromImportQueryString()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = $"/api/companies/{period.CompanyId}/bank-accounts/4/import";
        context.Request.QueryString = QueryString.Create("periodId", period.Id.ToString());
        var middleware = new AuditTrailMiddleware(innerContext =>
        {
            innerContext.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        var entry = await db.AuditLogs.SingleAsync(a => a.EntityType == "ApiRequest");
        Assert.Equal(period.Id, entry.PeriodId);
        Assert.Equal(period.Id, entry.EntityId);
    }

    [Fact]
    public async Task AuditTrailMiddleware_IgnoresQueryPeriodIdOutsideVerifiedImportRoute()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = $"/api/companies/{period.CompanyId}";
        context.Request.QueryString = QueryString.Create("periodId", period.Id.ToString());
        var middleware = new AuditTrailMiddleware(innerContext =>
        {
            innerContext.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        var entry = await db.AuditLogs.SingleAsync(a => a.EntityType == "ApiRequest");
        Assert.Null(entry.PeriodId);
        Assert.Equal(period.CompanyId, entry.EntityId);
    }

    [Fact]
    public void AuditTrailMiddleware_UsesRelationalTransactionForAuditedWrites()
    {
        var middleware = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "backend",
            "Accounts.Api",
            "Middleware",
            "AuditTrailMiddleware.cs"));

        Assert.Contains("BeginTransactionAsync", middleware);
        Assert.Contains("CommitAsync", middleware);
        Assert.Contains("RollbackAsync", middleware);
        Assert.Contains("responseBuffer", middleware);
        Assert.Contains("CopyToAsync", middleware);
        Assert.Contains("transactionCompleted", middleware);
    }

    [Fact]
    public void AuditTrailMiddleware_RunsBeforeAuthorizationAndLockGuards()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Program.cs"));
        var auditIndex = program.IndexOf("UseMiddleware<Accounts.Api.Middleware.AuditTrailMiddleware>", StringComparison.Ordinal);

        Assert.True(auditIndex < program.IndexOf("UseMiddleware<Accounts.Api.Middleware.TenantAccessMiddleware>", StringComparison.Ordinal));
        Assert.True(auditIndex < program.IndexOf("UseMiddleware<Accounts.Api.Middleware.RoleAuthorizationMiddleware>", StringComparison.Ordinal));
        Assert.True(auditIndex < program.IndexOf("UseMiddleware<Accounts.Api.Middleware.PeriodLockMiddleware>", StringComparison.Ordinal));
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
    public async Task ExceptionMiddleware_ReturnsClientSafeBusinessRuleMessages()
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton<IHostEnvironment>(new TestEnvironment("Production"))
                .BuildServiceProvider(),
            Response =
            {
                Body = new MemoryStream()
            }
        };
        var exception = new ExceptionMiddleware(
            _ => throw new BusinessRuleException("Confirm the filing regime before generating the CRO filing pack."),
            NullLogger<ExceptionMiddleware>.Instance);

        await exception.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var payload = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("Confirm the filing regime before generating the CRO filing pack.", payload.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ExceptionMiddleware_MasksUnexpectedExceptionsInProduction()
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton<IHostEnvironment>(new TestEnvironment("Production"))
                .BuildServiceProvider(),
            Response =
            {
                Body = new MemoryStream()
            }
        };
        var exception = new ExceptionMiddleware(
            _ => throw new InvalidOperationException("Npgsql failure for password=secret on cro_filing_packages"),
            NullLogger<ExceptionMiddleware>.Instance);

        await exception.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var payload = await JsonDocument.ParseAsync(context.Response.Body);
        var error = payload.RootElement.GetProperty("error").GetString();
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("An internal error occurred. Please try again.", error);
        Assert.DoesNotContain("Npgsql", error);
        Assert.DoesNotContain("secret", error);
    }

    [Fact]
    public void SensitiveFilingEndpoints_DoNotReturnRawExceptionMessages()
    {
        var root = RepositoryRoot();
        var endpointFiles = new[]
        {
            "backend/Accounts.Api/Endpoints/AdjustmentEndpoints.cs",
            "backend/Accounts.Api/Endpoints/DocumentEndpoints.cs",
            "backend/Accounts.Api/Endpoints/RevenueEndpoints.cs",
            "backend/Accounts.Api/Endpoints/FilingWorkflowEndpoints.cs",
            "backend/Accounts.Api/Endpoints/CharityEndpoints.cs",
            "backend/Accounts.Api/Endpoints/YearEndEndpoints.cs"
        };

        foreach (var relativePath in endpointFiles)
        {
            var source = File.ReadAllText(Path.Combine(root, relativePath));
            Assert.DoesNotContain("catch (Exception ex)", source);
            Assert.DoesNotContain("error = ex.Message", source);
        }
    }

    [Fact]
    public async Task FilingWorkflow_IxbrlGenerationFailureDoesNotPersistGeneratedSuccess()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.TaxBalances.Add(new TaxBalance
        {
            PeriodId = period.Id,
            TaxType = TaxType.CorporationTax,
            Paid = 0m
        });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var ixbrl = new FailingIxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);

        var result = await workflow.ValidateIxbrlAsync(period.CompanyId, period.Id);

        Assert.False(result.IxbrlGenerated);
        Assert.False(result.IxbrlValidated);
        Assert.Contains("iXBRL generation failed. Check server logs and retry.", result.IxbrlValidationErrors);
        Assert.DoesNotContain("password=secret", result.IxbrlValidationErrors);
    }

    [Fact]
    public async Task IxbrlService_UsesCurrentIrishRevenueTaxonomyAndCorrectPeriodContexts()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        await MakePeriodReadyForCroDocumentsAsync(db, period);

        var statements = new FinancialStatementsService(db);
        var service = new IxbrlService(db, statements);

        var xhtml = Encoding.UTF8.GetString(await service.GenerateIxbrlAsync(period.CompanyId, period.Id));

        Assert.Contains("ie-FRS-102-2026-01-01.xsd", xhtml);
        Assert.Contains("xmlns:ie-common=\"https://xbrl.frc.org.uk/ireland/common/2026-01-01\"", xhtml);
        Assert.Contains("name=\"core:NetAssetsLiabilities\" contextRef=\"instant\"", xhtml);
        Assert.Contains("name=\"core:TurnoverGrossRevenue\" contextRef=\"current\"", xhtml);
        Assert.Contains("name=\"core:ProfitLossForPeriod\" contextRef=\"current\"", xhtml);
        Assert.DoesNotContain("name=\"core:TurnoverGrossRevenue\" contextRef=\"instant\"", xhtml);
        Assert.DoesNotContain("name=\"core:ProfitLossForPeriod\" contextRef=\"instant\"", xhtml);
    }

    [Fact]
    public async Task IxbrlService_RejectsMismatchedCompanyPeriodForRawGeneration()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var service = new IxbrlService(db, new FinancialStatementsService(db));
        var method = typeof(IxbrlService).GetMethod(nameof(IxbrlService.GenerateIxbrlAsync), [typeof(int), typeof(int)]);

        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task<byte[]>>(method.Invoke(service, [period.CompanyId, otherPeriod.Id]));
        await Assert.ThrowsAsync<ResourceNotFoundException>(async () => await task);
    }

    [Fact]
    public void IxbrlService_RequiresCompanyIdForRawGeneration()
    {
        var methods = typeof(IxbrlService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name == nameof(IxbrlService.GenerateIxbrlAsync))
            .Select(m => m.GetParameters().Select(p => p.ParameterType).ToArray())
            .ToList();

        Assert.Contains(methods, parameters =>
            parameters.Length == 2
            && parameters[0] == typeof(int)
            && parameters[1] == typeof(int));
        Assert.DoesNotContain(methods, parameters =>
            parameters.Length == 1
            && parameters[0] == typeof(int));
    }

    [Fact]
    public async Task FilingWorkflow_InternalIxbrlChecksDoNotMarkExternalValidationPassed()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        await MakePeriodReadyForCroDocumentsAsync(db, period);
        db.TaxBalances.Add(new TaxBalance
        {
            PeriodId = period.Id,
            TaxType = TaxType.CorporationTax,
            Paid = 0m
        });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);

        var result = await workflow.ValidateIxbrlAsync(period.CompanyId, period.Id);

        Assert.True(result.IxbrlGenerated);
        Assert.False(result.IxbrlValidated);
        Assert.Equal("Internal checks passed. External ROS/iXBRL validation is still required before Revenue filing.", result.IxbrlValidationErrors);
    }

    [Fact]
    public async Task FinalIxbrlDownload_BlocksUntilReadinessAndInternalChecksPass()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        await db.SaveChangesAsync();
        var statements = new FinancialStatementsService(db);
        var service = new IxbrlService(db, statements);
        var method = typeof(IxbrlService).GetMethod("GenerateFinalIxbrlAsync", [typeof(int), typeof(int)]);
        Assert.True(method is not null, "Public iXBRL downloads should use a final-filing method that enforces readiness and internal checks.");

        var task = Assert.IsAssignableFrom<Task>(method.Invoke(service, [period.CompanyId, period.Id]));
        var error = await Assert.ThrowsAsync<BusinessRuleException>(async () => await task);

        Assert.Contains("Cannot generate final iXBRL", error.Message);
        Assert.Contains("balance sheet does not balance", error.Message);
        Assert.Contains("Internal iXBRL checks have not passed", error.Message);
    }

    [Fact]
    public async Task FinalIxbrlDownload_RejectsMismatchedCompanyPeriodBeforeReadinessChecks()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var statements = new FinancialStatementsService(db);
        var service = new IxbrlService(db, statements);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.GenerateFinalIxbrlAsync(period.CompanyId, otherPeriod.Id));
    }

    [Fact]
    public void RevenueIxbrlEndpoint_UsesFinalIxbrlReadinessGuard()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "RevenueEndpoints.cs"));

        Assert.Contains("GenerateFinalIxbrlAsync(companyId, periodId)", source);
        Assert.DoesNotContain("GenerateIxbrlAsync(periodId)", source);
    }

    [Theory]
    [InlineData(nameof(TaxComputationService.ComputeAsync), typeof(TaxComputationService.TaxComputation))]
    [InlineData(nameof(TaxComputationService.GetCt1SupportDataAsync), typeof(TaxComputationService.Ct1SupportData))]
    public async Task RevenueServices_RejectMismatchedCompanyPeriod(string methodName, Type resultType)
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var service = new TaxComputationService(db, new FinancialStatementsService(db));
        var method = typeof(TaxComputationService).GetMethod(methodName, [typeof(int), typeof(int)]);

        Assert.NotNull(method);
        var taskType = typeof(Task<>).MakeGenericType(resultType);
        var task = Assert.IsAssignableFrom<Task>(method.Invoke(service, [period.CompanyId, otherPeriod.Id]));
        Assert.IsAssignableFrom(taskType, task);
        await Assert.ThrowsAsync<ResourceNotFoundException>(async () => await task);
    }

    [Fact]
    public void RevenueEndpoints_UseCompanyAwareTaxServiceCalls()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "RevenueEndpoints.cs"));

        Assert.Contains("ComputeAsync(companyId, periodId)", source);
        Assert.Contains("GetCt1SupportDataAsync(companyId, periodId)", source);
        Assert.DoesNotContain("ComputeAsync(periodId)", source);
        Assert.DoesNotContain("GetCt1SupportDataAsync(periodId)", source);
    }

    [Fact]
    public void TaxComputationService_RequiresCompanyIdForRevenueOutputs()
    {
        var methods = typeof(TaxComputationService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name is nameof(TaxComputationService.ComputeAsync) or nameof(TaxComputationService.GetCt1SupportDataAsync))
            .Select(m => new { m.Name, Parameters = m.GetParameters().Select(p => p.ParameterType).ToArray() })
            .ToList();

        foreach (var methodName in new[] { nameof(TaxComputationService.ComputeAsync), nameof(TaxComputationService.GetCt1SupportDataAsync) })
        {
            Assert.Contains(methods, m =>
                m.Name == methodName
                && m.Parameters.Length == 2
                && m.Parameters[0] == typeof(int)
                && m.Parameters[1] == typeof(int));
            Assert.DoesNotContain(methods, m =>
                m.Name == methodName
                && m.Parameters.Length == 1
                && m.Parameters[0] == typeof(int));
        }
    }

    [Fact]
    public async Task ValidateIxbrlEndpoint_ReturnsRevenueWorkflowStatusContract()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        await MakePeriodReadyForCroDocumentsAsync(db, period);
        db.TaxBalances.Add(new TaxBalance
        {
            PeriodId = period.Id,
            TaxType = TaxType.CorporationTax,
            Paid = 0m
        });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/filing/validate-ixbrl");

        var result = await FilingWorkflowEndpoints.ValidateIxbrlEndpointAsync(
            period.CompanyId,
            period.Id,
            workflow,
            db,
            context,
            DisabledApiAccess());

        var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var revenue = Assert.IsType<FilingWorkflowService.RevenueFilingStatus>(valueResult.Value);
        Assert.True(revenue.IxbrlReady);
        Assert.True(revenue.IxbrlInternalChecksPassed);
        Assert.False(revenue.IxbrlValid);
        Assert.Contains("External ROS/iXBRL validation", revenue.ValidationErrors);
    }

    [Fact]
    public async Task ValidateIxbrlEndpoint_DoesNotMutateRevenuePackageForMismatchedCompanyPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/filing/validate-ixbrl");

        var result = await FilingWorkflowEndpoints.ValidateIxbrlEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            workflow,
            db,
            context,
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(result));
        Assert.Empty(await db.RevenueFilingPackages.ToListAsync());
    }

    [Fact]
    public async Task ValidateIxbrlEndpoint_DeniesReviewerBeforeRevenueMutation()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);
        var context = AuthenticatedRequest(
            "Reviewer",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/filing/validate-ixbrl");

        var result = await FilingWorkflowEndpoints.ValidateIxbrlEndpointAsync(
            period.CompanyId,
            period.Id,
            workflow,
            db,
            context,
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status403Forbidden, ResultStatusCode(result));
        Assert.Empty(await db.RevenueFilingPackages.ToListAsync());
    }

    [Fact]
    public void FilingWorkflowService_RequiresCompanyIdForIxbrlValidation()
    {
        var methods = typeof(FilingWorkflowService)
            .GetMethods()
            .Where(m => m.Name == nameof(FilingWorkflowService.ValidateIxbrlAsync))
            .Select(m => m.GetParameters().Select(p => p.ParameterType).ToArray())
            .ToList();

        Assert.Contains(methods, parameters =>
            parameters.Length >= 2
            && parameters[0] == typeof(int)
            && parameters[1] == typeof(int));
        Assert.DoesNotContain(methods, parameters =>
            parameters.Length >= 1
            && parameters[0] == typeof(int)
            && (parameters.Length == 1 || parameters[1] != typeof(int)));
    }

    [Fact]
    public void PeriodWorkspace_LabelsIxbrlActionAsInternalChecks()
    {
        var page = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "frontend/src/app/companies/[companyId]/periods/[periodId]/page.tsx"));

        Assert.Contains("Run iXBRL Checks", page);
        Assert.Contains("result.ixbrlInternalChecksPassed", page);
        Assert.Contains("Internal iXBRL checks passed; external ROS validation is still required", page);
        Assert.DoesNotContain("toast.success(\"iXBRL validation passed\")", page);
        Assert.DoesNotContain(">Validate iXBRL<", page);
    }

    [Fact]
    public void PeriodWorkspace_CapturesFilingReferencesBeforeMarkFiled()
    {
        var root = RepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "frontend/src/app/companies/[companyId]/periods/[periodId]/page.tsx"));
        var api = File.ReadAllText(Path.Combine(root, "frontend/src/lib/api.ts"));
        var endpoint = File.ReadAllText(Path.Combine(root, "backend/Accounts.Api/Endpoints/DeadlineEndpoints.cs"));

        Assert.Contains("filingReference?: string", api);
        Assert.Contains("string? FilingReference", endpoint);
        Assert.Contains("input.FilingReference", endpoint);
        Assert.Contains("Revenue ROS or CT1 filing reference", page);
        Assert.Contains("ROS/CT1 reference", page);
        Assert.Contains("Revenue filing reference is required", page);
        Assert.Contains("Charities Regulator annual return reference", page);
        Assert.Contains("Annual return reference", page);
        Assert.Contains("Charity annual return reference is required", page);
        Assert.Contains("filingReference }", page);
    }

    [Fact]
    public void PeriodWorkspace_CapturesCoreSubmissionReferenceBeforeCroSubmit()
    {
        var root = RepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "frontend/src/app/companies/[companyId]/periods/[periodId]/page.tsx"));
        var api = File.ReadAllText(Path.Combine(root, "frontend/src/lib/api.ts"));

        Assert.Contains("submissionReference?: string", api);
        Assert.Contains("croSubmissionReference", page);
        Assert.Contains("CORE submission reference", page);
        Assert.Contains("CORE submission reference is required", page);
        Assert.Contains("submissionReference: coreReference", page);
    }

    [Fact]
    public void CharityPage_RecordsAnnualReturnWorkflowBeforeDeadlineFiling()
    {
        var root = RepositoryRoot();
        var charityPage = File.ReadAllText(Path.Combine(root, "frontend/src/app/companies/[companyId]/periods/[periodId]/charity/page.tsx"));
        var api = File.ReadAllText(Path.Combine(root, "frontend/src/lib/api.ts"));

        Assert.Contains("recordCharityReportGenerated", api);
        Assert.Contains("updateCharityFilingStatus", api);
        Assert.Contains("charity-report-generated", api);
        Assert.Contains("charity-status", api);
        Assert.Contains("Annual Return Workflow", charityPage);
        Assert.Contains("recordCharityReportGenerated(cId, pId, \"sofa\")", charityPage);
        Assert.Contains("recordCharityReportGenerated(cId, pId, \"trustees-report\")", charityPage);
        Assert.Contains("annualReturnReference: reference", charityPage);
        Assert.Contains("status: \"Accepted\"", charityPage);
    }

    [Fact]
    public async Task DirectorsReportData_RejectsMismatchedCompanyPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var service = new DirectorsReportService(db, new FinancialStatementsService(db));

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.GenerateAsync(period.CompanyId, otherPeriod.Id));
    }

    [Fact]
    public async Task DirectorsReportData_SurfacesProfitAndLossFailures()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var service = new DirectorsReportService(db, new FailingProfitAndLossStatementsService(db));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.GenerateAsync(period.CompanyId, period.Id));

        Assert.Contains("profit and loss", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("€0", error.Message);
    }

    [Fact]
    public void DirectorsReportEndpoint_UsesCompanyAwareServiceCall()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "DocumentEndpoints.cs"));

        Assert.Contains("GenerateAsync(companyId, periodId)", source);
        Assert.DoesNotContain("GenerateAsync(periodId)", source);
    }

    [Fact]
    public void PeriodWorkspace_AuditTrailDisplaysOldAndNewPayloads()
    {
        var page = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "frontend/src/app/companies/[companyId]/periods/[periodId]/page.tsx"));

        Assert.Contains("getAuditLog(cId, pId, 1, 50)", page);
        Assert.Contains("Audit Details", page);
        Assert.Contains("entry.oldValueJson", page);
        Assert.Contains("entry.newValueJson", page);
        Assert.Contains("whitespace-pre-wrap", page);
    }

    [Fact]
    public async Task SizeClassificationSaveEndpoint_LogsDomainAuditWithOldAndNewValues()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Owner",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/size-classification");

        var result = await ClassificationEndpoints.SaveSizeClassificationEndpointAsync(
            period.CompanyId,
            period.Id,
            new SizeClassificationInput(120_000m, 40_000m, 3, null),
            db,
            audit,
            context,
            DisabledApiAccess());

        Assert.IsAssignableFrom<IResult>(result);
        var entry = await db.AuditLogs.SingleAsync(a => a.Action == AuditEventCodes.SizeClassificationDataSaved);
        Assert.Equal(period.CompanyId, entry.CompanyId);
        Assert.Equal(period.Id, entry.PeriodId);
        Assert.Equal("SizeClassification", entry.EntityType);
        Assert.Equal("user@example.ie", entry.UserId);
        Assert.Null(entry.OldValueJson);
        Assert.Contains("\"Turnover\":120000", entry.NewValueJson);
        Assert.Contains("\"AvgEmployees\":3", entry.NewValueJson);
    }

    [Fact]
    public async Task SizeClassificationSaveEndpoint_DeniesClientBeforeMutation()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Client",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/size-classification");
        context.Items[AuthContext.ItemKey] = AuthenticatedRole("Client") with
        {
            AllowedCompanyIds = new HashSet<int> { period.CompanyId }
        };

        var result = await ClassificationEndpoints.SaveSizeClassificationEndpointAsync(
            period.CompanyId,
            period.Id,
            new SizeClassificationInput(120_000m, 40_000m, 3, null),
            db,
            audit,
            context,
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status403Forbidden, ResultStatusCode(result));
        Assert.Empty(await db.SizeClassifications.ToListAsync());
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == AuditEventCodes.SizeClassificationDataSaved).ToListAsync());
    }

    [Fact]
    public async Task ClassificationAndFilingRegime_LogStatutoryDecisionAudits()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            Turnover = 120_000m,
            BalanceSheetTotal = 40_000m,
            AvgEmployees = 3
        });
        await db.SaveChangesAsync();

        var audit = new AuditService(db);
        var classification = new SizeClassificationService(db, Options.Create(new SizeThresholdConfig()), audit);
        await classification.ClassifyAsync(period.CompanyId, period.Id, "reviewer@example.ie");
        var filingRegime = new FilingRegimeService(db, new DeadlineService(db), audit);
        await filingRegime.DetermineAsync(period.CompanyId, period.Id, ElectedRegime.Micro, "reviewer@example.ie");

        var classifyAudit = await db.AuditLogs.SingleAsync(a => a.Action == AuditEventCodes.SizeClassificationRun);
        Assert.Equal(period.CompanyId, classifyAudit.CompanyId);
        Assert.Equal(period.Id, classifyAudit.PeriodId);
        Assert.Equal("reviewer@example.ie", classifyAudit.UserId);
        Assert.Contains("\"CalculatedClass\":\"Micro\"", classifyAudit.NewValueJson);
        Assert.Contains("\"AuditExempt\":true", classifyAudit.NewValueJson);

        var regimeAudit = await db.AuditLogs.SingleAsync(a => a.Action == AuditEventCodes.FilingRegimeDetermined);
        Assert.Equal(period.CompanyId, regimeAudit.CompanyId);
        Assert.Equal(period.Id, regimeAudit.PeriodId);
        Assert.Contains("\"ElectedRegime\":\"Micro\"", regimeAudit.NewValueJson);
        Assert.Contains("\"RequiredStatements\"", regimeAudit.NewValueJson);
    }

    [Fact]
    public async Task DeadlineMarkFiled_LogsPenaltyAndFiledDateAudit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2023, 1, 1);
        period.PeriodEnd = new DateOnly(2023, 12, 31);
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var service = new DeadlineService(db, audit);
        var deadlines = await service.CalculateDeadlinesAsync(period.CompanyId, period.Id, "reviewer@example.ie");
        var croDeadline = deadlines.Single(d => d.DeadlineType == DeadlineType.CRO);
        await SeedAcceptedCroFilingPackageAsync(db, period.Id);

        await service.MarkFiledAsync(period.CompanyId, period.Id, DeadlineType.CRO, croDeadline.DueDate.AddDays(3), "reviewer@example.ie");

        var entry = await db.AuditLogs.SingleAsync(a => a.Action == AuditEventCodes.DeadlineMarkedFiled);
        Assert.Equal(period.CompanyId, entry.CompanyId);
        Assert.Equal(period.Id, entry.PeriodId);
        Assert.Contains("\"FiledDate\":null", entry.OldValueJson);
        Assert.Contains("\"IsLate\":true", entry.NewValueJson);
        Assert.Contains("\"PenaltyAmount\":109", entry.NewValueJson);
    }

    [Fact]
    public async Task DeadlineMarkFiled_UpsertsFilingHistorySoDuplicateCallsDoNotRemoveAuditExemption()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2023, 1, 1);
        period.PeriodEnd = new DateOnly(2023, 12, 31);
        await db.SaveChangesAsync();
        var service = new DeadlineService(db);
        var deadlines = await service.CalculateDeadlinesAsync(period.CompanyId, period.Id);
        var croDeadline = deadlines.Single(d => d.DeadlineType == DeadlineType.CRO);
        var firstFiledDate = croDeadline.DueDate.AddDays(3);
        var correctedFiledDate = croDeadline.DueDate.AddDays(4);
        await SeedAcceptedCroFilingPackageAsync(db, period.Id);

        await service.MarkFiledAsync(period.CompanyId, period.Id, DeadlineType.CRO, firstFiledDate);
        await service.MarkFiledAsync(period.CompanyId, period.Id, DeadlineType.CRO, firstFiledDate);
        await service.MarkFiledAsync(period.CompanyId, period.Id, DeadlineType.CRO, correctedFiledDate);

        var histories = await db.FilingHistories
            .Where(h => h.CompanyId == period.CompanyId && h.PeriodId == period.Id && h.DeadlineType == DeadlineType.CRO)
            .ToListAsync();
        var jeopardy = await service.CheckAuditExemptionJeopardyAsync(period.CompanyId);

        var history = Assert.Single(histories);
        Assert.Equal(correctedFiledDate, history.FiledDate);
        Assert.Equal(4, history.DaysLate);
        Assert.False(jeopardy.HasLostExemption);
        Assert.Equal(1, jeopardy.LateFilingCount);
    }

    [Fact]
    public async Task DeadlineMarkFiled_RejectsCroBeforeAcceptedWorkflowWithoutMutatingDeadlineOrHistory()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2023, 1, 1);
        period.PeriodEnd = new DateOnly(2023, 12, 31);
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var service = new DeadlineService(db, audit);
        var deadlines = await service.CalculateDeadlinesAsync(period.CompanyId, period.Id, "reviewer@example.ie");
        var croDeadline = deadlines.Single(d => d.DeadlineType == DeadlineType.CRO);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.MarkFiledAsync(period.CompanyId, period.Id, DeadlineType.CRO, croDeadline.DueDate.AddDays(3), "reviewer@example.ie"));

        var deadline = await db.FilingDeadlines.SingleAsync(d =>
            d.CompanyId == period.CompanyId && d.PeriodId == period.Id && d.DeadlineType == DeadlineType.CRO);
        Assert.Contains("CRO filing", error.Message);
        Assert.Contains("accepted", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(deadline.FiledDate);
        Assert.False(deadline.IsLate);
        Assert.Equal(0, deadline.PenaltyAmount);
        Assert.Empty(await db.FilingHistories.Where(h =>
            h.CompanyId == period.CompanyId && h.PeriodId == period.Id && h.DeadlineType == DeadlineType.CRO).ToListAsync());
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), a => a.Action == AuditEventCodes.DeadlineMarkedFiled);
    }

    [Fact]
    public async Task DeadlineMarkFiled_RejectsAcceptedCroPackageWithoutCoreReferenceBeforeMutatingDeadlineOrHistory()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2023, 1, 1);
        period.PeriodEnd = new DateOnly(2023, 12, 31);
        db.CroFilingPackages.Add(new CroFilingPackage
        {
            PeriodId = period.Id,
            FilingStatus = FilingStatus.Accepted,
            AccountsPdfGenerated = true,
            SignaturePageGenerated = true,
            PaymentCompleted = true,
            SubmittedBy = "reviewer@example.ie",
            SubmittedAt = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
            CroSubmissionReference = " "
        });
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var service = new DeadlineService(db, audit);
        var deadlines = await service.CalculateDeadlinesAsync(period.CompanyId, period.Id, "reviewer@example.ie");
        var croDeadline = deadlines.Single(d => d.DeadlineType == DeadlineType.CRO);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.MarkFiledAsync(period.CompanyId, period.Id, DeadlineType.CRO, croDeadline.DueDate.AddDays(3), "reviewer@example.ie"));

        var deadline = await db.FilingDeadlines.SingleAsync(d =>
            d.CompanyId == period.CompanyId && d.PeriodId == period.Id && d.DeadlineType == DeadlineType.CRO);
        Assert.Contains("CORE submission reference is required", error.Message);
        Assert.Null(deadline.FiledDate);
        Assert.Null(deadline.FilingReference);
        Assert.False(deadline.IsLate);
        Assert.Equal(0, deadline.PenaltyAmount);
        Assert.Empty(await db.FilingHistories.Where(h =>
            h.CompanyId == period.CompanyId && h.PeriodId == period.Id && h.DeadlineType == DeadlineType.CRO).ToListAsync());
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), a => a.Action == AuditEventCodes.DeadlineMarkedFiled);
    }

    [Fact]
    public async Task DeadlineMarkFiled_RejectsRevenueBeforeInternalIxbrlChecksWithoutMutatingDeadlineOrHistory()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2023, 1, 1);
        period.PeriodEnd = new DateOnly(2023, 12, 31);
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var service = new DeadlineService(db, audit);
        var deadlines = await service.CalculateDeadlinesAsync(period.CompanyId, period.Id, "reviewer@example.ie");
        var revenueDeadline = deadlines.Single(d => d.DeadlineType == DeadlineType.Revenue);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.MarkFiledAsync(period.CompanyId, period.Id, DeadlineType.Revenue, revenueDeadline.DueDate.AddDays(3), "reviewer@example.ie"));

        var deadline = await db.FilingDeadlines.SingleAsync(d =>
            d.CompanyId == period.CompanyId && d.PeriodId == period.Id && d.DeadlineType == DeadlineType.Revenue);
        Assert.Contains("internal iXBRL checks", error.Message);
        Assert.Null(deadline.FiledDate);
        Assert.False(deadline.IsLate);
        Assert.Equal(0, deadline.PenaltyAmount);
        Assert.Empty(await db.FilingHistories.Where(h =>
            h.CompanyId == period.CompanyId && h.PeriodId == period.Id && h.DeadlineType == DeadlineType.Revenue).ToListAsync());
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), a => a.Action == AuditEventCodes.DeadlineMarkedFiled);
    }

    [Fact]
    public async Task DeadlineMarkFiled_RejectsRevenueWithoutFilingReferenceBeforeMutatingDeadlineOrHistory()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2023, 1, 1);
        period.PeriodEnd = new DateOnly(2023, 12, 31);
        await db.SaveChangesAsync();
        await SeedRevenueInternalIxbrlChecksPassedAsync(db, period.Id, ct1Reference: null);
        var audit = new AuditService(db);
        var service = new DeadlineService(db, audit);
        var deadlines = await service.CalculateDeadlinesAsync(period.CompanyId, period.Id, "reviewer@example.ie");
        var revenueDeadline = deadlines.Single(d => d.DeadlineType == DeadlineType.Revenue);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.MarkFiledAsync(period.CompanyId, period.Id, DeadlineType.Revenue, revenueDeadline.DueDate.AddDays(3), "reviewer@example.ie"));

        var deadline = await db.FilingDeadlines.SingleAsync(d =>
            d.CompanyId == period.CompanyId && d.PeriodId == period.Id && d.DeadlineType == DeadlineType.Revenue);
        var package = await db.RevenueFilingPackages.SingleAsync(p => p.PeriodId == period.Id);
        Assert.Contains("Revenue filing reference", error.Message);
        Assert.Null(deadline.FiledDate);
        Assert.False(deadline.IsLate);
        Assert.Equal(0, deadline.PenaltyAmount);
        Assert.Null(package.Ct1Reference);
        Assert.Empty(await db.FilingHistories.Where(h =>
            h.CompanyId == period.CompanyId && h.PeriodId == period.Id && h.DeadlineType == DeadlineType.Revenue).ToListAsync());
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), a => a.Action == AuditEventCodes.DeadlineMarkedFiled);
    }

    [Fact]
    public async Task DeadlineMarkFiled_RejectsCharityBeforeReportingEvidenceWithoutMutatingDeadlineOrHistory()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2023, 1, 1);
        period.PeriodEnd = new DateOnly(2023, 12, 31);
        var company = await db.Companies.SingleAsync(c => c.Id == period.CompanyId);
        company.IsCharitableOrganisation = true;
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var service = new DeadlineService(db, audit);
        var deadlines = await service.CalculateDeadlinesAsync(period.CompanyId, period.Id, "reviewer@example.ie");
        var charityDeadline = deadlines.Single(d => d.DeadlineType == DeadlineType.Charity);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.MarkFiledAsync(period.CompanyId, period.Id, DeadlineType.Charity, charityDeadline.DueDate.AddDays(3), "reviewer@example.ie"));

        var deadline = await db.FilingDeadlines.SingleAsync(d =>
            d.CompanyId == period.CompanyId && d.PeriodId == period.Id && d.DeadlineType == DeadlineType.Charity);
        Assert.Contains("Charity annual return", error.Message);
        Assert.Contains("accepted", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(deadline.FiledDate);
        Assert.False(deadline.IsLate);
        Assert.Equal(0, deadline.PenaltyAmount);
        Assert.Empty(await db.FilingHistories.Where(h =>
            h.CompanyId == period.CompanyId && h.PeriodId == period.Id && h.DeadlineType == DeadlineType.Charity).ToListAsync());
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), a => a.Action == AuditEventCodes.DeadlineMarkedFiled);
    }

    [Fact]
    public async Task DeadlineMarkFiled_PersistsRevenueFilingReferenceWhenMarkingRevenueFiled()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2023, 1, 1);
        period.PeriodEnd = new DateOnly(2023, 12, 31);
        await db.SaveChangesAsync();
        await SeedRevenueInternalIxbrlChecksPassedAsync(db, period.Id, ct1Reference: null);
        var audit = new AuditService(db);
        var service = new DeadlineService(db, audit);
        var deadlines = await service.CalculateDeadlinesAsync(period.CompanyId, period.Id, "reviewer@example.ie");
        var revenueDeadline = deadlines.Single(d => d.DeadlineType == DeadlineType.Revenue);
        var filedDate = revenueDeadline.DueDate.AddDays(3);

        await service.MarkFiledAsync(
            period.CompanyId,
            period.Id,
            DeadlineType.Revenue,
            filedDate,
            "reviewer@example.ie",
            "  ROS-CT1-2025-0001  ");

        var deadline = await db.FilingDeadlines.SingleAsync(d =>
            d.CompanyId == period.CompanyId && d.PeriodId == period.Id && d.DeadlineType == DeadlineType.Revenue);
        var history = await db.FilingHistories.SingleAsync(h =>
            h.CompanyId == period.CompanyId && h.PeriodId == period.Id && h.DeadlineType == DeadlineType.Revenue);
        var package = await db.RevenueFilingPackages.SingleAsync(p => p.PeriodId == period.Id);
        var entry = await db.AuditLogs.SingleAsync(a => a.Action == AuditEventCodes.DeadlineMarkedFiled);

        Assert.Equal(filedDate, deadline.FiledDate);
        Assert.True(deadline.IsLate);
        Assert.Equal(filedDate, history.FiledDate);
        Assert.Equal(3, history.DaysLate);
        Assert.Equal("ROS-CT1-2025-0001", package.Ct1Reference);
        Assert.Equal(period.CompanyId, entry.CompanyId);
        Assert.Equal(period.Id, entry.PeriodId);
    }

    [Fact]
    public async Task DeadlineMarkFiled_PersistsCharityFilingReferenceWhenAcceptedAnnualReturnPackageExists()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2023, 1, 1);
        period.PeriodEnd = new DateOnly(2023, 12, 31);
        var company = await db.Companies.SingleAsync(c => c.Id == period.CompanyId);
        company.IsCharitableOrganisation = true;
        db.CharityInfos.Add(new CharityInfo
        {
            CompanyId = period.CompanyId,
            CharityNumber = "CHY-12345",
            CharityType = "CLG",
            GrossIncome = 100_000m,
            CharitableObjectives = "Community education",
            PrincipalActivities = "Training and support"
        });
        db.FundBalances.Add(new FundBalance
        {
            PeriodId = period.Id,
            FundName = "General fund",
            FundType = "Unrestricted",
            OpeningBalance = 100m,
            IncomingResources = 1_000m,
            ResourcesExpended = 900m,
            ClosingBalance = 200m
        });
        db.CharityFilingPackages.Add(new CharityFilingPackage
        {
            PeriodId = period.Id,
            FilingStatus = FilingStatus.Accepted,
            Status = FilingPackageStatus.Accepted,
            SofaGenerated = true,
            TrusteesReportGenerated = true,
            AnnualReturnReference = "CRA-AR-2025-001",
            SubmittedBy = "Reviewer",
            SubmittedAt = new DateTime(2026, 1, 31, 10, 0, 0, DateTimeKind.Utc),
            AcceptedBy = "Reviewer",
            AcceptedAt = new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var service = new DeadlineService(db, audit);
        var deadlines = await service.CalculateDeadlinesAsync(period.CompanyId, period.Id, "reviewer@example.ie");
        var charityDeadline = deadlines.Single(d => d.DeadlineType == DeadlineType.Charity);
        var filedDate = charityDeadline.DueDate.AddDays(3);

        await service.MarkFiledAsync(
            period.CompanyId,
            period.Id,
            DeadlineType.Charity,
            filedDate,
            "reviewer@example.ie");

        var deadline = await db.FilingDeadlines.SingleAsync(d =>
            d.CompanyId == period.CompanyId && d.PeriodId == period.Id && d.DeadlineType == DeadlineType.Charity);
        var history = await db.FilingHistories.SingleAsync(h =>
            h.CompanyId == period.CompanyId && h.PeriodId == period.Id && h.DeadlineType == DeadlineType.Charity);
        var entry = await db.AuditLogs.SingleAsync(a => a.Action == AuditEventCodes.DeadlineMarkedFiled);

        Assert.Equal(filedDate, deadline.FiledDate);
        Assert.True(deadline.IsLate);
        Assert.Equal("CRA-AR-2025-001", deadline.FilingReference);
        Assert.Equal(filedDate, history.FiledDate);
        Assert.Equal(3, history.DaysLate);
        Assert.Equal("CRA-AR-2025-001", history.FilingReference);
        Assert.Contains("\"FilingReference\":\"CRA-AR-2025-001\"", entry.NewValueJson);
    }

    [Fact]
    public async Task DeadlineMarkFiled_RejectsCharityBeforeAcceptedAnnualReturnPackageEvenWhenReportingDataExists()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = new DateOnly(2023, 1, 1);
        period.PeriodEnd = new DateOnly(2023, 12, 31);
        var company = await db.Companies.SingleAsync(c => c.Id == period.CompanyId);
        company.IsCharitableOrganisation = true;
        db.CharityInfos.Add(new CharityInfo
        {
            CompanyId = period.CompanyId,
            CharityNumber = "CHY-12345",
            CharityType = "CLG",
            GrossIncome = 100_000m,
            CharitableObjectives = "Community education",
            PrincipalActivities = "Training and support"
        });
        db.FundBalances.Add(new FundBalance
        {
            PeriodId = period.Id,
            FundName = "General fund",
            FundType = "Unrestricted",
            OpeningBalance = 100m,
            IncomingResources = 1_000m,
            ResourcesExpended = 900m,
            ClosingBalance = 200m
        });
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var service = new DeadlineService(db, audit);
        var deadlines = await service.CalculateDeadlinesAsync(period.CompanyId, period.Id, "reviewer@example.ie");
        var charityDeadline = deadlines.Single(d => d.DeadlineType == DeadlineType.Charity);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.MarkFiledAsync(
                period.CompanyId,
                period.Id,
                DeadlineType.Charity,
                charityDeadline.DueDate.AddDays(3),
                "reviewer@example.ie",
                "CRA-AR-2025-001"));

        var deadline = await db.FilingDeadlines.SingleAsync(d =>
            d.CompanyId == period.CompanyId && d.PeriodId == period.Id && d.DeadlineType == DeadlineType.Charity);
        Assert.Contains("Charity annual return", error.Message);
        Assert.Contains("accepted", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(deadline.FiledDate);
        Assert.Null(deadline.FilingReference);
        Assert.Empty(await db.FilingHistories.Where(h =>
            h.CompanyId == period.CompanyId && h.PeriodId == period.Id && h.DeadlineType == DeadlineType.Charity).ToListAsync());
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), a => a.Action == AuditEventCodes.DeadlineMarkedFiled);
    }

    [Fact]
    public async Task DeadlineMarkFiled_RejectsFutureFiledDateBeforeMutatingDeadlineOrHistory()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var service = new DeadlineService(
            db,
            audit,
            timeProvider: new FixedUtcTimeProvider(new DateTimeOffset(2026, 6, 7, 10, 0, 0, TimeSpan.Zero)));
        await service.CalculateDeadlinesAsync(period.CompanyId, period.Id, "reviewer@example.ie");
        var futureFiledDate = new DateOnly(2026, 6, 8);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.MarkFiledAsync(period.CompanyId, period.Id, DeadlineType.CRO, futureFiledDate, "reviewer@example.ie"));

        var croDeadline = await db.FilingDeadlines.SingleAsync(d =>
            d.CompanyId == period.CompanyId && d.PeriodId == period.Id && d.DeadlineType == DeadlineType.CRO);
        Assert.Contains("future", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(croDeadline.FiledDate);
        Assert.Empty(await db.FilingHistories.ToListAsync());
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), a => a.Action == AuditEventCodes.DeadlineMarkedFiled);
    }

    [Fact]
    public async Task DeadlineMarkFiled_AllowsIrishTodayWhenUtcDateIsStillYesterday()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var irishToday = new DateOnly(2026, 6, 7);
        db.FilingDeadlines.Add(new FilingDeadline
        {
            CompanyId = period.CompanyId,
            PeriodId = period.Id,
            DeadlineType = DeadlineType.CRO,
            DueDate = irishToday.AddDays(-10)
        });
        await db.SaveChangesAsync();
        await SeedAcceptedCroFilingPackageAsync(db, period.Id);
        var service = new DeadlineService(
            db,
            audit: null,
            timeProvider: new FixedUtcTimeProvider(new DateTimeOffset(2026, 6, 6, 23, 30, 0, TimeSpan.Zero)));

        await service.MarkFiledAsync(period.CompanyId, period.Id, DeadlineType.CRO, irishToday);

        var deadline = await db.FilingDeadlines.SingleAsync();
        Assert.Equal(irishToday, deadline.FiledDate);
        Assert.Single(await db.FilingHistories.ToListAsync());
    }

    [Fact]
    public async Task DeadlineMarkFiled_RejectsMismatchedCompanyPeriodEvenWhenDeadlineRowIsInconsistent()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10);
        db.FilingDeadlines.Add(new FilingDeadline
        {
            CompanyId = period.CompanyId,
            PeriodId = otherPeriod.Id,
            DeadlineType = DeadlineType.CRO,
            DueDate = dueDate
        });
        await db.SaveChangesAsync();
        var service = new DeadlineService(db);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.MarkFiledAsync(period.CompanyId, otherPeriod.Id, DeadlineType.CRO, dueDate.AddDays(1)));

        var inconsistentDeadline = await db.FilingDeadlines.SingleAsync();
        Assert.Null(inconsistentDeadline.FiledDate);
        Assert.Empty(await db.FilingHistories.ToListAsync());
    }

    [Fact]
    public async Task DeadlineJeopardy_CountsDistinctLateCroFilingObligations()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-1);
        db.FilingHistories.AddRange(
            new FilingHistory
            {
                CompanyId = period.CompanyId,
                PeriodId = period.Id,
                DeadlineType = DeadlineType.CRO,
                DueDate = dueDate,
                FiledDate = dueDate.AddDays(1),
                DaysLate = 1
            },
            new FilingHistory
            {
                CompanyId = period.CompanyId,
                PeriodId = period.Id,
                DeadlineType = DeadlineType.CRO,
                DueDate = dueDate.AddDays(1),
                FiledDate = dueDate.AddDays(3),
                DaysLate = 2
            },
            new FilingHistory
            {
                CompanyId = period.CompanyId,
                PeriodId = period.Id,
                DeadlineType = DeadlineType.Revenue,
                DueDate = dueDate,
                FiledDate = dueDate.AddDays(10),
                DaysLate = 10
            });
        await db.SaveChangesAsync();
        var service = new DeadlineService(db);

        var jeopardy = await service.CheckAuditExemptionJeopardyAsync(period.CompanyId);

        Assert.Equal(1, jeopardy.LateFilingCount);
        Assert.True(jeopardy.IsAtRisk);
        Assert.False(jeopardy.HasLostExemption);
    }

    [Fact]
    public void FilingHistoryUniqueObligationMigration_DeDuplicatesExistingRowsBeforeCreatingIndex()
    {
        var migration = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "backend",
            "Accounts.Api",
            "Data",
            "Migrations",
            "20260607073455_AddUniqueFilingHistoryObligation.cs"));

        Assert.Contains("DELETE FROM", migration);
        Assert.Contains("IX_filing_histories_CompanyId_PeriodId_DeadlineType", migration);
        Assert.True(
            migration.IndexOf("DELETE FROM", StringComparison.Ordinal)
            < migration.IndexOf("CreateIndex", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FilingWorkflow_LogsCroAndIxbrlDomainAudits()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.TaxBalances.Add(new TaxBalance
        {
            PeriodId = period.Id,
            TaxType = TaxType.CorporationTax,
            Paid = 0m
        });
        await db.SaveChangesAsync();

        var audit = new AuditService(db);
        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl, audit);

        await workflow.RecordCroDocumentGeneratedAsync(period.CompanyId, period.Id, "accounts", "reviewer@example.ie");
        await workflow.ValidateIxbrlAsync(period.CompanyId, period.Id, "reviewer@example.ie");

        var generated = await db.AuditLogs.SingleAsync(a => a.Action == AuditEventCodes.CroDocumentGenerated);
        Assert.Equal("CroFilingPackage", generated.EntityType);
        Assert.Equal(period.CompanyId, generated.CompanyId);
        Assert.Equal(period.Id, generated.PeriodId);
        Assert.Contains("\"DocumentType\":\"accounts\"", generated.NewValueJson);

        var ixbrlAudit = await db.AuditLogs.SingleAsync(a => a.Action == AuditEventCodes.IxbrlInternalCheckCompleted);
        Assert.Equal("RevenueFilingPackage", ixbrlAudit.EntityType);
        Assert.Equal(period.CompanyId, ixbrlAudit.CompanyId);
        Assert.Equal(period.Id, ixbrlAudit.PeriodId);
        Assert.Contains("\"IxbrlGenerated\":true", ixbrlAudit.NewValueJson);
        Assert.Contains("\"IxbrlValidated\"", ixbrlAudit.NewValueJson);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    public async Task NotesEndpoints_RejectOversizedCustomContentBeforePersistence(string action)
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var oversizedContent = new string('x', 20_001);
        var context = AuthenticatedRequest(
            "Accountant",
            action == "create" ? HttpMethods.Post : HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/notes");

        IResult result;
        if (action == "create")
        {
            result = await YearEndEndpoints.CreateNoteEndpointAsync(
                period.CompanyId,
                period.Id,
                new NotesDisclosure
                {
                    Title = "Oversized custom note",
                    Content = oversizedContent,
                    IsIncluded = true
                },
                db,
                audit,
                context);
        }
        else
        {
            var note = new NotesDisclosure
            {
                PeriodId = period.Id,
                NoteNumber = 1,
                Title = "Existing custom note",
                Content = "Original content",
                IsIncluded = true
            };
            db.NotesDisclosures.Add(note);
            await db.SaveChangesAsync();

            result = await YearEndEndpoints.UpdateNoteEndpointAsync(
                period.CompanyId,
                period.Id,
                note.Id,
                new NotesDisclosure
                {
                    Title = note.Title,
                    Content = oversizedContent,
                    IsIncluded = true
                },
                db,
                audit,
                context);
        }

        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(result));
        if (action == "create")
        {
            Assert.Empty(await db.NotesDisclosures.Where(n => n.PeriodId == period.Id).ToListAsync());
        }
        else
        {
            var saved = await db.NotesDisclosures.SingleAsync(n => n.PeriodId == period.Id);
            Assert.Equal("Original content", saved.Content);
        }
        Assert.Empty(await db.AuditLogs.Where(a => a.EntityType == "NotesDisclosure").ToListAsync());
    }

    [Fact]
    public async Task YearEndEvidence_LogsTaxReviewNotesAndShareCapitalAudits()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var accountantContext = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/tax-balances/CorporationTax");
        var reviewerContext = AuthenticatedRequest(
            "Reviewer",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/year-end-reviews/tax");

        await YearEndEndpoints.UpsertTaxBalanceEndpointAsync(
            period.CompanyId,
            period.Id,
            TaxType.CorporationTax,
            new TaxBalance { Liability = 1_250m, Paid = 250m, Balance = 1_000m },
            db,
            audit,
            accountantContext);
        await YearEndEndpoints.UpdateYearEndReviewEndpointAsync(
            period.CompanyId,
            period.Id,
            "tax",
            new YearEndReviewInput(true, null, "Tax balance agreed to CT computation."),
            db,
            audit,
            reviewerContext);

        var notes = new NotesDisclosureService(db);
        await YearEndEndpoints.GenerateNotesEndpointAsync(period.CompanyId, period.Id, notes, db, audit, accountantContext);
        var note = await db.NotesDisclosures.FirstAsync(n => n.PeriodId == period.Id);
        await YearEndEndpoints.UpdateNoteEndpointAsync(
            period.CompanyId,
            period.Id,
            note.Id,
            new NotesDisclosure
            {
                Title = note.Title,
                Content = note.Content + "\nReviewed by the board.",
                IsIncluded = true
            },
            db,
            audit,
            accountantContext);

        var writeGuard = new AccountingWriteGuard(db);
        await YearEndEndpoints.CreateShareCapitalEndpointAsync(
            period.CompanyId,
            new ShareCapital
            {
                ShareClass = "Ordinary",
                NominalValue = 1m,
                NumberIssued = 100,
                IsFullyPaid = true,
                IssueDate = period.PeriodStart
            },
            db,
            writeGuard,
            audit,
            accountantContext);

        Assert.Contains(await db.AuditLogs.ToListAsync(), a =>
            a.Action == AuditEventCodes.TaxBalanceUpserted
            && a.EntityType == "TaxBalance"
            && a.PeriodId == period.Id
            && a.NewValueJson!.Contains("\"Balance\":1000")
            && a.NewValueJson.Contains("\"WasCreated\":true"));
        Assert.Contains(await db.AuditLogs.ToListAsync(), a =>
            a.Action == AuditEventCodes.YearEndReviewConfirmationUpdated
            && a.NewValueJson!.Contains("\"SectionKey\":\"tax\"")
            && a.NewValueJson.Contains("\"Confirmed\":true"));
        Assert.Contains(await db.AuditLogs.ToListAsync(), a =>
            a.Action == AuditEventCodes.NotesGenerated
            && a.EntityType == "NotesDisclosureBatch"
            && a.NewValueJson!.Contains("\"GeneratedCount\""));
        Assert.Contains(await db.AuditLogs.ToListAsync(), a =>
            a.Action == AuditEventCodes.NoteDisclosureUpdated
            && a.NewValueJson!.Contains("\"ContentLength\""));
        Assert.Contains(await db.AuditLogs.ToListAsync(), a =>
            a.Action == AuditEventCodes.ShareCapitalCreated
            && a.NewValueJson!.Contains("\"TotalValue\":100"));
    }

    [Fact]
    public async Task AdjustmentEvidence_LogsManualAdjustmentLifecycleAndClearsApprovalOnUpdate()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var debit = AddCategory(db, period.CompanyId, "6810", "Accountancy Fees", AccountCategoryType.Expense);
        var credit = AddCategory(db, period.CompanyId, "2100", "Accruals", AccountCategoryType.Liability);
        var audit = new AuditService(db);
        var apiAccess = DisabledApiAccess();

        var createInput = new AdjustmentInput(
            Description: " Audit fee accrual ",
            DebitCategoryId: debit.Id,
            CreditCategoryId: credit.Id,
            Amount: 1_500m,
            Reason: " Year-end invoice received after close ",
            LegalBasis: " FRS 102 accruals concept ",
            ImpactOnProfit: -1_500m,
            ImpactOnAssets: 0m);

        await AdjustmentEndpoints.CreateAdjustmentEndpointAsync(
            period.CompanyId,
            period.Id,
            createInput,
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Post, $"/api/companies/{period.CompanyId}/periods/{period.Id}/adjustments"),
            apiAccess);
        var adjustment = await db.Adjustments.SingleAsync(a => a.PeriodId == period.Id);

        await AdjustmentEndpoints.ApproveAdjustmentEndpointAsync(
            period.CompanyId,
            period.Id,
            adjustment.Id,
            db,
            audit,
            AuthenticatedRequest("Reviewer", HttpMethods.Post, $"/api/companies/{period.CompanyId}/periods/{period.Id}/adjustments/{adjustment.Id}/approve"),
            apiAccess);

        var updateInput = createInput with
        {
            Description = "Audit and tax fee accrual",
            Amount = 1_750m,
            Reason = "Invoice updated after partner review",
            ImpactOnProfit = -1_750m
        };

        await AdjustmentEndpoints.UpdateAdjustmentEndpointAsync(
            period.CompanyId,
            period.Id,
            adjustment.Id,
            updateInput,
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Put, $"/api/companies/{period.CompanyId}/periods/{period.Id}/adjustments/{adjustment.Id}"),
            apiAccess);
        await AdjustmentEndpoints.DeleteAdjustmentEndpointAsync(
            period.CompanyId,
            period.Id,
            adjustment.Id,
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Delete, $"/api/companies/{period.CompanyId}/periods/{period.Id}/adjustments/{adjustment.Id}"),
            apiAccess);

        var audits = await db.AuditLogs
            .Where(a => a.EntityType == "Adjustment")
            .OrderBy(a => a.Id)
            .ToListAsync();

        Assert.Equal(
            [
                AuditEventCodes.AdjustmentCreated,
                AuditEventCodes.AdjustmentApproved,
                AuditEventCodes.AdjustmentUpdated,
                AuditEventCodes.AdjustmentDeleted
            ],
            audits.Select(a => a.Action).ToArray());
        Assert.All(audits, a =>
        {
            Assert.Equal(period.CompanyId, a.CompanyId);
            Assert.Equal(period.Id, a.PeriodId);
            Assert.Equal("user@example.ie", a.UserId);
        });
        Assert.Null(audits[0].OldValueJson);
        Assert.Contains("\"Description\":\"Audit fee accrual\"", audits[0].NewValueJson);
        Assert.Contains("\"Amount\":1500", audits[0].NewValueJson);
        Assert.Contains("\"ApprovedBy\":\"Example User\"", audits[1].NewValueJson);
        Assert.Contains("\"ApprovedBy\":\"Example User\"", audits[2].OldValueJson);
        Assert.Contains("\"Amount\":1750", audits[2].NewValueJson);
        Assert.Contains("\"ApprovedBy\":null", audits[2].NewValueJson);
        Assert.Contains("\"Amount\":1750", audits[3].OldValueJson);
        Assert.Contains("\"Deleted\":true", audits[3].NewValueJson);

        Assert.Empty(db.Adjustments);
    }

    [Fact]
    public async Task AdjustmentEvidence_RejectsInvalidEndpointMutationsWithoutAudit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var debit = AddCategory(db, period.CompanyId, "6811", "Audit fees", AccountCategoryType.Expense);
        var credit = AddCategory(db, period.CompanyId, "2101", "Accruals", AccountCategoryType.Liability);
        var otherCategory = AddCategory(db, otherPeriod.CompanyId, "6812", "Other audit fees", AccountCategoryType.Expense);
        var audit = new AuditService(db);
        var apiAccess = DisabledApiAccess();
        var input = new AdjustmentInput(
            Description: "Accrual",
            DebitCategoryId: debit.Id,
            CreditCategoryId: credit.Id,
            Amount: 300m,
            Reason: "Year-end",
            LegalBasis: "FRS 102",
            ImpactOnProfit: -300m,
            ImpactOnAssets: 0m);
        var adjustment = new Adjustment
        {
            PeriodId = period.Id,
            Description = "Original accrual",
            DebitCategoryId = debit.Id,
            CreditCategoryId = credit.Id,
            Amount = 250m,
            Reason = "Original",
            LegalBasis = "FRS 102",
            ImpactOnProfit = -250m,
            ImpactOnAssets = 0m,
            CreatedBy = "Original reviewer"
        };
        var otherAdjustment = new Adjustment
        {
            PeriodId = otherPeriod.Id,
            Description = "Other company accrual",
            DebitCategoryId = otherCategory.Id,
            Amount = 900m,
            Reason = "Other company",
            LegalBasis = "FRS 102",
            ImpactOnProfit = -900m,
            ImpactOnAssets = 0m,
            CreatedBy = "Other reviewer"
        };
        db.Adjustments.AddRange(adjustment, otherAdjustment);
        await db.SaveChangesAsync();

        var wrongCategoryResult = await AdjustmentEndpoints.CreateAdjustmentEndpointAsync(
            period.CompanyId,
            period.Id,
            input with { DebitCategoryId = otherCategory.Id },
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Post, $"/api/companies/{period.CompanyId}/periods/{period.Id}/adjustments"),
            apiAccess);
        var invalidCategoryUpdate = await AdjustmentEndpoints.UpdateAdjustmentEndpointAsync(
            period.CompanyId,
            period.Id,
            adjustment.Id,
            input with { DebitCategoryId = otherCategory.Id },
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Put, $"/api/companies/{period.CompanyId}/periods/{period.Id}/adjustments/{adjustment.Id}"),
            apiAccess);
        var wrongPeriodUpdate = await AdjustmentEndpoints.UpdateAdjustmentEndpointAsync(
            otherPeriod.CompanyId,
            otherPeriod.Id,
            adjustment.Id,
            input,
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Put, $"/api/companies/{otherPeriod.CompanyId}/periods/{otherPeriod.Id}/adjustments/{adjustment.Id}"),
            apiAccess);
        var wrongPeriodApprove = await AdjustmentEndpoints.ApproveAdjustmentEndpointAsync(
            otherPeriod.CompanyId,
            otherPeriod.Id,
            adjustment.Id,
            db,
            audit,
            AuthenticatedRequest("Reviewer", HttpMethods.Post, $"/api/companies/{otherPeriod.CompanyId}/periods/{otherPeriod.Id}/adjustments/{adjustment.Id}/approve"),
            apiAccess);
        var wrongPeriodDelete = await AdjustmentEndpoints.DeleteAdjustmentEndpointAsync(
            otherPeriod.CompanyId,
            otherPeriod.Id,
            adjustment.Id,
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Delete, $"/api/companies/{otherPeriod.CompanyId}/periods/{otherPeriod.Id}/adjustments/{adjustment.Id}"),
            apiAccess);
        var mismatchedCompanyPeriodCreate = await AdjustmentEndpoints.CreateAdjustmentEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            input,
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Post, $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/adjustments"),
            apiAccess);
        var mismatchedCompanyPeriodUpdate = await AdjustmentEndpoints.UpdateAdjustmentEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            otherAdjustment.Id,
            input,
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Put, $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/adjustments/{otherAdjustment.Id}"),
            apiAccess);
        var mismatchedCompanyPeriodApprove = await AdjustmentEndpoints.ApproveAdjustmentEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            otherAdjustment.Id,
            db,
            audit,
            AuthenticatedRequest("Reviewer", HttpMethods.Post, $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/adjustments/{otherAdjustment.Id}/approve"),
            apiAccess);
        var mismatchedCompanyPeriodDelete = await AdjustmentEndpoints.DeleteAdjustmentEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            otherAdjustment.Id,
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Delete, $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/adjustments/{otherAdjustment.Id}"),
            apiAccess);

        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(wrongCategoryResult));
        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(invalidCategoryUpdate));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(wrongPeriodUpdate));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(wrongPeriodApprove));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(wrongPeriodDelete));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(mismatchedCompanyPeriodCreate));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(mismatchedCompanyPeriodUpdate));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(mismatchedCompanyPeriodApprove));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(mismatchedCompanyPeriodDelete));
        Assert.Equal("Original accrual", (await db.Adjustments.SingleAsync(a => a.Id == adjustment.Id)).Description);
        Assert.Equal("Other company accrual", (await db.Adjustments.SingleAsync(a => a.Id == otherAdjustment.Id)).Description);
        Assert.Equal(2, await db.Adjustments.CountAsync());
        Assert.Empty(await db.AuditLogs.Where(a => a.EntityType == "Adjustment").ToListAsync());
    }

    [Fact]
    public async Task AdjustmentEvidence_GuardsListGenerateAndSummaryAgainstMismatchedCompanyPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.Adjustments.Add(new Adjustment
        {
            PeriodId = otherPeriod.Id,
            Description = "Other period adjustment",
            Amount = 900m,
            Reason = "Other company",
            LegalBasis = "FRS 102",
            ImpactOnProfit = -900m,
            ImpactOnAssets = 0m,
            CreatedBy = "Other reviewer"
        });
        await db.SaveChangesAsync();

        var context = AuthenticatedRequest("Accountant", HttpMethods.Get, $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/adjustments");
        var apiAccess = DisabledApiAccess();

        var list = await AdjustmentEndpoints.ListAdjustmentsEndpointAsync(period.CompanyId, otherPeriod.Id, db, context, null, null);
        var generated = await AdjustmentEndpoints.GenerateAdjustmentsEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new AdjustmentService(db),
            db,
            AuthenticatedRequest("Accountant", HttpMethods.Post, $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/adjustments/generate"),
            apiAccess);
        var summary = await AdjustmentEndpoints.GetAdjustmentSummaryEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            db,
            AuthenticatedRequest("Accountant", HttpMethods.Get, $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/adjustments/summary"));

        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(list));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(generated));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(summary));
        Assert.Single(await db.Adjustments.ToListAsync());
        Assert.Empty(await db.AuditLogs.Where(a => a.EntityType == "Adjustment").ToListAsync());
    }

    [Fact]
    public async Task BankingEvidence_LogsSingleAndBulkTransactionCategorisationAudits()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var sales = AddCategory(db, period.CompanyId, "4001", "Sales", AccountCategoryType.Income);
        var fees = AddCategory(db, period.CompanyId, "6811", "Fees", AccountCategoryType.Expense);
        var bank = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        db.ImportedTransactions.AddRange(
            new ImportedTransaction
            {
                BankAccountId = bank.Id,
                PeriodId = period.Id,
                Date = period.PeriodStart.AddDays(1),
                Description = "Invoice receipt",
                Amount = 500m,
                CategoryId = sales.Id,
                ConfidenceScore = 0.6m
            },
            new ImportedTransaction
            {
                BankAccountId = bank.Id,
                PeriodId = period.Id,
                Date = period.PeriodStart.AddDays(2),
                Description = "Bank fee",
                Amount = -10m
            },
            new ImportedTransaction
            {
                BankAccountId = bank.Id,
                PeriodId = period.Id,
                Date = period.PeriodStart.AddDays(3),
                Description = "Legal fee",
                Amount = -50m
            });
        await db.SaveChangesAsync();
        var transactions = await db.ImportedTransactions.OrderBy(t => t.Date).ToListAsync();
        var audit = new AuditService(db);
        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = AuthenticatedRole("Accountant");

        await BankingEndpoints.CategoriseTransactionEndpointAsync(
            period.CompanyId,
            period.Id,
            transactions[0].Id,
            new CategoriseInput(fees.Id),
            db,
            audit,
            context,
            DisabledApiAccess());
        await BankingEndpoints.BulkCategoriseTransactionsEndpointAsync(
            period.CompanyId,
            period.Id,
            new BulkCategoriseInput([transactions[1].Id, transactions[2].Id], fees.Id),
            db,
            audit,
            context,
            DisabledApiAccess());

        var refreshed = await db.ImportedTransactions.OrderBy(t => t.Date).ToListAsync();
        Assert.All(refreshed, transaction =>
        {
            Assert.Equal(fees.Id, transaction.CategoryId);
            Assert.True(transaction.ManualOverride);
            Assert.Equal(1.0m, transaction.ConfidenceScore);
        });

        var singleAudit = await db.AuditLogs.SingleAsync(a => a.Action == AuditEventCodes.TransactionCategorised);
        Assert.Equal(period.CompanyId, singleAudit.CompanyId);
        Assert.Equal(period.Id, singleAudit.PeriodId);
        Assert.Equal("ImportedTransaction", singleAudit.EntityType);
        Assert.Equal("user@example.ie", singleAudit.UserId);
        Assert.Contains("\"CategoryId\":" + sales.Id, singleAudit.OldValueJson);
        Assert.Contains("\"CategoryId\":" + fees.Id, singleAudit.NewValueJson);
        Assert.Contains("\"ManualOverride\":true", singleAudit.NewValueJson);

        var bulkAudit = await db.AuditLogs.SingleAsync(a => a.Action == AuditEventCodes.TransactionsBulkCategorised);
        Assert.Equal("ImportedTransactionBatch", bulkAudit.EntityType);
        Assert.Contains("\"RequestedCount\":2", bulkAudit.NewValueJson);
        Assert.Contains("\"UpdatedCount\":2", bulkAudit.NewValueJson);
        Assert.Contains("\"CategoryId\":" + fees.Id, bulkAudit.NewValueJson);
        Assert.Contains("\"Description\":\"Bank fee\"", bulkAudit.NewValueJson);
        Assert.Contains("\"ManualOverride\":true", bulkAudit.NewValueJson);
    }

    [Fact]
    public async Task BankingEvidence_RejectsInvalidCategorisationWithoutMutationOrAudit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var category = AddCategory(db, period.CompanyId, "4002", "Sales", AccountCategoryType.Income);
        var otherCategory = AddCategory(db, otherPeriod.CompanyId, "4003", "Other sales", AccountCategoryType.Income);
        var bank = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current account",
            OpeningBalance = 0m
        };
        var otherBank = new BankAccount
        {
            CompanyId = otherPeriod.CompanyId,
            Name = "Other current account",
            OpeningBalance = 0m
        };
        db.BankAccounts.AddRange(bank, otherBank);
        await db.SaveChangesAsync();
        var transaction = new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = period.Id,
            Date = period.PeriodStart.AddDays(1),
            Description = "Receipt",
            Amount = 250m,
            CategoryId = category.Id
        };
        var otherTransaction = new ImportedTransaction
        {
            BankAccountId = otherBank.Id,
            PeriodId = otherPeriod.Id,
            Date = otherPeriod.PeriodStart.AddDays(1),
            Description = "Other receipt",
            Amount = 999m,
            CategoryId = otherCategory.Id
        };
        db.ImportedTransactions.AddRange(transaction, otherTransaction);
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = AuthenticatedRole("Accountant");

        var invalidCategory = await BankingEndpoints.CategoriseTransactionEndpointAsync(
            period.CompanyId,
            period.Id,
            transaction.Id,
            new CategoriseInput(otherCategory.Id),
            db,
            audit,
            context,
            DisabledApiAccess());
        var wrongPeriod = await BankingEndpoints.CategoriseTransactionEndpointAsync(
            otherPeriod.CompanyId,
            otherPeriod.Id,
            transaction.Id,
            new CategoriseInput(otherCategory.Id),
            db,
            audit,
            context,
            DisabledApiAccess());
        var partialBulk = await BankingEndpoints.BulkCategoriseTransactionsEndpointAsync(
            period.CompanyId,
            period.Id,
            new BulkCategoriseInput([transaction.Id, otherTransaction.Id], category.Id),
            db,
            audit,
            context,
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(invalidCategory));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(wrongPeriod));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(partialBulk));
        Assert.Equal(category.Id, (await db.ImportedTransactions.SingleAsync(t => t.Id == transaction.Id)).CategoryId);
        Assert.Equal(otherCategory.Id, (await db.ImportedTransactions.SingleAsync(t => t.Id == otherTransaction.Id)).CategoryId);
        Assert.Empty(await db.AuditLogs.Where(a => a.EntityType.StartsWith("ImportedTransaction")).ToListAsync());
    }

    [Fact]
    public async Task BankingEvidence_CategoriseDoesNotPersistWhenAuditWriteFails()
    {
        var databaseName = Guid.NewGuid().ToString();
        var root = new InMemoryDatabaseRoot();
        int companyId;
        int periodId;
        int transactionId;
        int categoryId;

        await using (var setup = CreateDbContext(databaseName, root))
        {
            var period = await SeedCompanyPeriodAsync(setup, isFirstYear: true);
            var category = AddCategory(setup, period.CompanyId, "4005", "Sales", AccountCategoryType.Income);
            var bank = new BankAccount
            {
                CompanyId = period.CompanyId,
                Name = "Current account",
                OpeningBalance = 0m
            };
            setup.BankAccounts.Add(bank);
            await setup.SaveChangesAsync();
            var transaction = new ImportedTransaction
            {
                BankAccountId = bank.Id,
                PeriodId = period.Id,
                Date = period.PeriodStart.AddDays(1),
                Description = "Receipt",
                Amount = 250m
            };
            setup.ImportedTransactions.Add(transaction);
            await setup.SaveChangesAsync();
            companyId = period.CompanyId;
            periodId = period.Id;
            transactionId = transaction.Id;
            categoryId = category.Id;
        }

        await using (var failingAuditDb = CreateAuditFailingDbContext(databaseName, root))
        {
            var context = new DefaultHttpContext();
            context.Items[AuthContext.ItemKey] = AuthenticatedRole("Accountant");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                BankingEndpoints.CategoriseTransactionEndpointAsync(
                    companyId,
                    periodId,
                    transactionId,
                    new CategoriseInput(categoryId),
                    failingAuditDb,
                    new AuditService(failingAuditDb),
                    context,
                    DisabledApiAccess()));
        }

        await using var verify = CreateDbContext(databaseName, root);
        var persisted = await verify.ImportedTransactions.SingleAsync(t => t.Id == transactionId);
        Assert.Null(persisted.CategoryId);
        Assert.False(persisted.ManualOverride);
        Assert.Empty(await verify.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task BankingEvidence_BlocksCategorisationWhenPeriodIsLocked()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var category = AddCategory(db, period.CompanyId, "4004", "Sales", AccountCategoryType.Income);
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
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = AuthenticatedRole("Accountant");

        var result = await BankingEndpoints.CategoriseTransactionEndpointAsync(
            period.CompanyId,
            period.Id,
            transaction.Id,
            new CategoriseInput(category.Id),
            db,
            audit,
            context,
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status409Conflict, ResultStatusCode(result));
        Assert.Null((await db.ImportedTransactions.SingleAsync(t => t.Id == transaction.Id)).CategoryId);
        Assert.Empty(await db.AuditLogs.Where(a => a.EntityType == "ImportedTransaction").ToListAsync());
    }

    [Fact]
    public async Task BankingEvidence_BulkCategoriseRejectsMissingTransactionIdsWithoutMutationOrAudit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var category = AddCategory(db, period.CompanyId, "4006", "Sales", AccountCategoryType.Income);
        var audit = new AuditService(db);
        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = AuthenticatedRole("Accountant");

        var result = await BankingEndpoints.BulkCategoriseTransactionsEndpointAsync(
            period.CompanyId,
            period.Id,
            new BulkCategoriseInput(null!, category.Id),
            db,
            audit,
            context,
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(result));
        Assert.Empty(await db.AuditLogs.Where(a => a.EntityType.StartsWith("ImportedTransaction")).ToListAsync());
    }

    [Fact]
    public async Task BankingEvidence_BulkCategoriseHidesMissingPeriodBeforeEmptySelectionAndRoleDenial()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var category = AddCategory(db, period.CompanyId, "4008", "Sales", AccountCategoryType.Income);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest("Client", HttpMethods.Post, $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/transactions/bulk-categorise");
        context.Items[AuthContext.ItemKey] = AuthenticatedRole("Client") with
        {
            AllowedCompanyIds = new HashSet<int> { period.CompanyId }
        };

        var result = await BankingEndpoints.BulkCategoriseTransactionsEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new BulkCategoriseInput([], category.Id),
            db,
            audit,
            context,
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(result));
        Assert.Empty(await db.AuditLogs.Where(a => a.EntityType.StartsWith("ImportedTransaction")).ToListAsync());
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
    public void BankingWriteEndpoints_UseDirectCompanyAndRequestAuthorizationGuards()
    {
        var source = File
            .ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "BankingEndpoints.cs"))
            .Replace("\r\n", "\n");

        foreach (var marker in new[]
        {
            "banks.MapPost(\"/\",",
            "banks.MapPut(\"/{id:int}\",",
            "banks.MapDelete(\"/{id:int}\",",
            "categories.MapPost(\"/seed\",",
            "categories.MapPost(\"/\",",
            "rules.MapPost(\"/\",",
            "rules.MapDelete(\"/{id:int}\","
        })
        {
            var snippet = BankingEndpointSnippet(source, marker);
            Assert.Contains("HttpContext context", snippet);
            Assert.Contains("ApiAccessService apiAccess", snippet);
            Assert.Contains("CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId)", snippet);
            Assert.Contains("EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", snippet);
            AssertOccursBefore(snippet, "CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId)", "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)");
        }

        foreach (var marker in new[]
        {
            "public static async Task<IResult> CategoriseTransactionEndpointAsync",
            "public static async Task<IResult> BulkCategoriseTransactionsEndpointAsync",
            "public static async Task<IResult> ImportCsvEndpointAsync"
        })
        {
            var snippet = BankingMemberSnippet(source, marker);
            Assert.Contains("CompanyEndpointAccess.CanAccessCompanyAsync", snippet);
            Assert.Contains("EndpointRequestAuthorization.AuthorizeCurrentRequest", snippet);
            AssertOccursBefore(snippet, "CompanyEndpointAccess.CanAccessCompanyAsync", "EndpointRequestAuthorization.AuthorizeCurrentRequest");
        }

        foreach (var marker in new[]
        {
            "banks.MapPut(\"/{id:int}\",",
            "banks.MapDelete(\"/{id:int}\",",
            "rules.MapDelete(\"/{id:int}\","
        })
        {
            var snippet = BankingEndpointSnippet(source, marker);
            AssertOccursBefore(snippet, "FirstOrDefaultAsync", "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)");
        }

        static string BankingEndpointSnippet(string source, string marker)
        {
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Expected to find endpoint marker {marker}.");
            var nextMap = Regex.Match(source[start..], @"\n\s*(?:banks|categories|rules)\.Map(?:Get|Post|Put|Delete)", RegexOptions.None, TimeSpan.FromSeconds(1));
            return nextMap.Success && nextMap.Index > 0
                ? source[start..(start + nextMap.Index)]
                : source[start..];
        }

        static string BankingMemberSnippet(string source, string marker)
        {
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Expected to find member marker {marker}.");
            var nextMember = Regex.Match(source[(start + marker.Length)..], @"\n    (?:public|private) static", RegexOptions.None, TimeSpan.FromSeconds(1));
            return nextMember.Success
                ? source[start..(start + marker.Length + nextMember.Index)]
                : source[start..];
        }

        static void AssertOccursBefore(string snippet, string first, string second)
        {
            var firstIndex = snippet.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = snippet.IndexOf(second, StringComparison.Ordinal);
            Assert.True(firstIndex >= 0, $"Expected snippet to contain '{first}'.");
            Assert.True(secondIndex >= 0, $"Expected snippet to contain '{second}'.");
            Assert.True(firstIndex < secondIndex, $"Expected '{first}' before '{second}'.");
        }
    }

    [Fact]
    public void BankingReadEndpoints_UseDirectCompanyAccessGuards()
    {
        var source = File
            .ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "BankingEndpoints.cs"))
            .Replace("\r\n", "\n");

        foreach (var marker in new[]
        {
            "banks.MapGet(\"/\",",
            "categories.MapGet(\"/\",",
            "rules.MapGet(\"/\","
        })
        {
            var snippet = BankingEndpointSnippet(source, marker);
            Assert.Contains("HttpContext context", snippet);
            Assert.Contains("CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId)", snippet);
        }

        var listSnippet = BankingMemberSnippet(source, "public static async Task<IResult> ListTransactionsEndpointAsync");
        Assert.Contains("HttpContext context", listSnippet);
        Assert.Contains("CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId)", listSnippet);

        static string BankingEndpointSnippet(string source, string marker)
        {
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Expected to find endpoint marker {marker}.");
            var nextMap = Regex.Match(source[start..], @"\n\s*(?:banks|categories|rules)\.Map(?:Get|Post|Put|Delete)", RegexOptions.None, TimeSpan.FromSeconds(1));
            return nextMap.Success && nextMap.Index > 0
                ? source[start..(start + nextMap.Index)]
                : source[start..];
        }

        static string BankingMemberSnippet(string source, string marker)
        {
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Expected to find member marker {marker}.");
            var nextMember = Regex.Match(source[(start + marker.Length)..], @"\n    (?:public|private) static", RegexOptions.None, TimeSpan.FromSeconds(1));
            return nextMember.Success
                ? source[start..(start + marker.Length + nextMember.Index)]
                : source[start..];
        }
    }

    [Fact]
    public async Task BankingEvidence_ListTransactionsRequiresCompanyOwnedPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bank = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = otherPeriod.Id,
            Date = otherPeriod.PeriodStart.AddDays(1),
            Description = "Inconsistent transaction",
            Amount = 10m
        });
        await db.SaveChangesAsync();

        var result = await BankingEndpoints.ListTransactionsEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            db,
            AuthenticatedRequest("Owner", HttpMethods.Get, $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/transactions"),
            null,
            null,
            null,
            null,
            null,
            null);

        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(result));
    }

    [Fact]
    public async Task BankingEvidence_ListTransactionsHidesCompanyWhenClientIsNotAssigned()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bank = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bank);
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = period.Id,
            Date = period.PeriodStart.AddDays(1),
            Description = "Receipt",
            Amount = 10m
        });
        await db.SaveChangesAsync();
        var context = AuthenticatedRequest("Client", HttpMethods.Get, $"/api/companies/{period.CompanyId}/periods/{period.Id}/transactions");
        context.Items[AuthContext.ItemKey] = AuthenticatedRole("Client") with
        {
            AllowedCompanyIds = new HashSet<int> { otherPeriod.CompanyId }
        };

        var result = await BankingEndpoints.ListTransactionsEndpointAsync(
            period.CompanyId,
            period.Id,
            db,
            context,
            null,
            null,
            null,
            null,
            null,
            null);

        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(result));
    }

    [Fact]
    public async Task BankingEvidence_ImportSkipsRulesWithUnavailableCategories()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherCategory = AddCategory(db, otherPeriod.CompanyId, "4999", "Other company sales", AccountCategoryType.Income);
        var bank = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bank);
        db.TransactionRules.Add(new TransactionRule
        {
            CompanyId = period.CompanyId,
            Pattern = "Stripe",
            CategoryId = otherCategory.Id,
            Priority = 1
        });
        await db.SaveChangesAsync();
        using var csv = new MemoryStream(Encoding.UTF8.GetBytes("Date,Description,Amount\n01/01/2025,Stripe payout,100\n"));
        var service = new ImportService(db, Options.Create(new ImportLimitConfig()));

        var result = await service.ImportCsvAsync(period.CompanyId, bank.Id, period.Id, csv, "bank.csv");

        Assert.Equal(1, result.ImportedRows);
        Assert.Equal(0, result.AutoCategorised);
        Assert.Null((await db.ImportedTransactions.SingleAsync()).CategoryId);
    }

    [Fact]
    public async Task BankingEvidence_AutoCategoriseSkipsRulesWithUnavailableCategories()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherCategory = AddCategory(db, otherPeriod.CompanyId, "6901", "Other company charges", AccountCategoryType.Expense);
        db.TransactionRules.Add(new TransactionRule
        {
            CompanyId = period.CompanyId,
            Pattern = "Stripe",
            CategoryId = otherCategory.Id,
            Priority = 1
        });
        await db.SaveChangesAsync();
        var service = new CategoryService(db);

        var result = await service.AutoCategoriseAsync(period.CompanyId, "Stripe payout");

        Assert.Null(result.categoryId);
        Assert.Equal(0m, result.confidence);
    }

    [Fact]
    public async Task BankingEvidence_ListTransactionsDoesNotExposeUnavailableCategoryMetadata()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherCategory = AddCategory(db, otherPeriod.CompanyId, "7777", "Other Company Secret Category", AccountCategoryType.Expense);
        var bank = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = period.Id,
            Date = period.PeriodStart.AddDays(1),
            Description = "Contaminated category",
            Amount = -10m,
            CategoryId = otherCategory.Id
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await BankingEndpoints.ListTransactionsEndpointAsync(
            period.CompanyId,
            period.Id,
            db,
            AuthenticatedRequest("Owner", HttpMethods.Get, $"/api/companies/{period.CompanyId}/periods/{period.Id}/transactions"),
            null,
            null,
            null,
            null,
            null,
            null);

        var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(result);
        var payload = valueResult.Value;
        Assert.NotNull(payload);
        var itemsProperty = payload.GetType().GetProperty("items");
        Assert.NotNull(itemsProperty);
        var items = Assert.IsAssignableFrom<List<ImportedTransaction>>(
            itemsProperty.GetValue(payload));
        var transaction = Assert.Single(items);
        Assert.Equal(otherCategory.Id, transaction.CategoryId);
        Assert.Null(transaction.Category);
    }

    [Fact]
    public async Task YearEndEvidence_LogsReceivablePayableAndInventoryAudits()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/debtors");

        await YearEndEndpoints.CreateDebtorEndpointAsync(
            period.CompanyId,
            period.Id,
            new Debtor { Name = "Trade debtor", Amount = 500m, Type = DebtorType.Trade, Notes = "Invoice A" },
            db,
            audit,
            context);
        var debtor = await db.Debtors.SingleAsync(d => d.PeriodId == period.Id);
        await YearEndEndpoints.UpdateDebtorEndpointAsync(
            period.CompanyId,
            period.Id,
            debtor.Id,
            new Debtor { Name = "Trade debtor revised", Amount = 450m, Type = DebtorType.Other, Notes = "Board agreed" },
            db,
            audit,
            context);
        await YearEndEndpoints.DeleteDebtorEndpointAsync(period.CompanyId, period.Id, debtor.Id, db, audit, context);

        await YearEndEndpoints.CreateCreditorEndpointAsync(
            period.CompanyId,
            period.Id,
            new Creditor { Name = "Trade creditor", Amount = 700m, Type = CreditorType.Trade, DueWithinYear = true, Notes = "Supplier" },
            db,
            audit,
            context);
        var creditor = await db.Creditors.SingleAsync(c => c.PeriodId == period.Id);
        await YearEndEndpoints.UpdateCreditorEndpointAsync(
            period.CompanyId,
            period.Id,
            creditor.Id,
            new Creditor { Name = "Accrual", Amount = 725m, Type = CreditorType.Accrual, DueWithinYear = false, Notes = "Legal fees" },
            db,
            audit,
            context);
        await YearEndEndpoints.DeleteCreditorEndpointAsync(period.CompanyId, period.Id, creditor.Id, db, audit, context);

        await YearEndEndpoints.CreateInventoryEndpointAsync(
            period.CompanyId,
            period.Id,
            new Inventory { Description = "Finished goods", Value = 1_200m, ValuationMethod = ValuationMethod.Cost },
            db,
            audit,
            context);
        var inventory = await db.Inventories.SingleAsync(i => i.PeriodId == period.Id);
        await YearEndEndpoints.UpdateInventoryEndpointAsync(
            period.CompanyId,
            period.Id,
            inventory.Id,
            new Inventory { Description = "Finished goods write-down", Value = 1_050m, ValuationMethod = ValuationMethod.LowerOfCostAndNrv },
            db,
            audit,
            context);
        await YearEndEndpoints.DeleteInventoryEndpointAsync(period.CompanyId, period.Id, inventory.Id, db, audit, context);

        var debtorAudits = await db.AuditLogs.Where(a => a.EntityType == "Debtor").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.DebtorCreated, AuditEventCodes.DebtorUpdated, AuditEventCodes.DebtorDeleted],
            debtorAudits.Select(a => a.Action).ToArray());
        Assert.All(debtorAudits, a =>
        {
            Assert.Equal(period.CompanyId, a.CompanyId);
            Assert.Equal(period.Id, a.PeriodId);
            Assert.Equal("user@example.ie", a.UserId);
        });
        Assert.Null(debtorAudits[0].OldValueJson);
        Assert.Contains("\"Amount\":500", debtorAudits[0].NewValueJson);
        Assert.Contains("\"Amount\":500", debtorAudits[1].OldValueJson);
        Assert.Contains("\"Amount\":450", debtorAudits[1].NewValueJson);
        Assert.Contains("\"Deleted\":true", debtorAudits[2].NewValueJson);

        var creditorAudits = await db.AuditLogs.Where(a => a.EntityType == "Creditor").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.CreditorCreated, AuditEventCodes.CreditorUpdated, AuditEventCodes.CreditorDeleted],
            creditorAudits.Select(a => a.Action).ToArray());
        Assert.Contains("\"DueWithinYear\":true", creditorAudits[0].NewValueJson);
        Assert.Contains("\"DueWithinYear\":true", creditorAudits[1].OldValueJson);
        Assert.Contains("\"DueWithinYear\":false", creditorAudits[1].NewValueJson);
        Assert.Contains("\"Deleted\":true", creditorAudits[2].NewValueJson);

        var inventoryAudits = await db.AuditLogs.Where(a => a.EntityType == "Inventory").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.InventoryCreated, AuditEventCodes.InventoryUpdated, AuditEventCodes.InventoryDeleted],
            inventoryAudits.Select(a => a.Action).ToArray());
        Assert.Contains("\"Value\":1200", inventoryAudits[0].NewValueJson);
        Assert.Contains("\"ValuationMethod\":\"Cost\"", inventoryAudits[1].OldValueJson);
        Assert.Contains("\"Value\":1050", inventoryAudits[1].NewValueJson);
        Assert.Contains("\"Deleted\":true", inventoryAudits[2].NewValueJson);
    }

    [Fact]
    public async Task UpdateDividend_PersistsChangesAndLogsAudit()
    {
        // BL-25: dividends gained an update (PUT) endpoint so a recorded dividend can be corrected
        // in place rather than deleted and re-created.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/dividends/1");

        db.Dividends.Add(new Dividend { PeriodId = period.Id, Amount = 1_000m, DateDeclared = new DateOnly(2025, 6, 1) });
        await db.SaveChangesAsync();
        var dividend = await db.Dividends.SingleAsync(d => d.PeriodId == period.Id);

        await YearEndEndpoints.UpdateDividendEndpointAsync(
            period.CompanyId,
            period.Id,
            dividend.Id,
            new Dividend { Amount = 1_500m, DateDeclared = new DateOnly(2025, 6, 1), DatePaid = new DateOnly(2025, 7, 1) },
            db,
            audit,
            context);

        var updated = await db.Dividends.SingleAsync(d => d.Id == dividend.Id);
        Assert.Equal(1_500m, updated.Amount);
        Assert.Equal(new DateOnly(2025, 7, 1), updated.DatePaid);
        Assert.Contains(
            await db.AuditLogs.ToListAsync(),
            a => a.EntityType == "Dividend" && a.Action == AuditEventCodes.DividendUpdated);
    }

    [Fact]
    public async Task YearEndEvidence_LogsPayrollDividendAndGoingConcernAudits()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/payroll");

        await YearEndEndpoints.UpsertPayrollSummaryEndpointAsync(
            period.CompanyId,
            period.Id,
            new PayrollSummary
            {
                GrossWages = 10_000m,
                EmployerPrsi = 1_100m,
                PensionContributions = 500m,
                StaffCount = 4
            },
            db,
            audit,
            context);
        await YearEndEndpoints.UpsertPayrollSummaryEndpointAsync(
            period.CompanyId,
            period.Id,
            new PayrollSummary
            {
                GrossWages = 12_000m,
                EmployerPrsi = 1_320m,
                PensionContributions = 600m,
                StaffCount = 5
            },
            db,
            audit,
            context);

        await YearEndEndpoints.CreateDividendEndpointAsync(
            period.CompanyId,
            period.Id,
            new Dividend
            {
                Amount = 1_500m,
                DateDeclared = period.PeriodEnd.AddDays(-14),
                DatePaid = period.PeriodEnd.AddDays(-7)
            },
            db,
            audit,
            context);
        var dividend = await db.Dividends.SingleAsync(d => d.PeriodId == period.Id);
        await YearEndEndpoints.DeleteDividendEndpointAsync(period.CompanyId, period.Id, dividend.Id, db, audit, context);

        await YearEndEndpoints.UpdateGoingConcernEndpointAsync(
            period.CompanyId,
            period.Id,
            new GoingConcernInput(false, "Directors will provide support for at least twelve months."),
            db,
            audit,
            context);
        await YearEndEndpoints.UpdateGoingConcernEndpointAsync(
            period.CompanyId,
            period.Id,
            new GoingConcernInput(true, "   "),
            db,
            audit,
            context);

        var payrollAudits = await db.AuditLogs.Where(a => a.EntityType == "PayrollSummary").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal([AuditEventCodes.PayrollSummaryUpserted, AuditEventCodes.PayrollSummaryUpserted], payrollAudits.Select(a => a.Action).ToArray());
        Assert.All(payrollAudits, a =>
        {
            Assert.Equal(period.CompanyId, a.CompanyId);
            Assert.Equal(period.Id, a.PeriodId);
            Assert.Equal("user@example.ie", a.UserId);
        });
        Assert.Null(payrollAudits[0].OldValueJson);
        Assert.Contains("\"GrossWages\":10000", payrollAudits[0].NewValueJson);
        Assert.Contains("\"EmployerPrsi\":1100", payrollAudits[0].NewValueJson);
        Assert.Contains("\"PensionContributions\":500", payrollAudits[0].NewValueJson);
        Assert.Contains("\"StaffCount\":4", payrollAudits[0].NewValueJson);
        Assert.Contains("\"WasCreated\":true", payrollAudits[0].NewValueJson);
        Assert.Contains("\"GrossWages\":10000", payrollAudits[1].OldValueJson);
        Assert.Contains("\"EmployerPrsi\":1100", payrollAudits[1].OldValueJson);
        Assert.Contains("\"PensionContributions\":500", payrollAudits[1].OldValueJson);
        Assert.Contains("\"StaffCount\":4", payrollAudits[1].OldValueJson);
        Assert.Contains("\"GrossWages\":12000", payrollAudits[1].NewValueJson);
        Assert.Contains("\"EmployerPrsi\":1320", payrollAudits[1].NewValueJson);
        Assert.Contains("\"PensionContributions\":600", payrollAudits[1].NewValueJson);
        Assert.Contains("\"StaffCount\":5", payrollAudits[1].NewValueJson);
        Assert.Contains("\"WasCreated\":false", payrollAudits[1].NewValueJson);

        var dividendAudits = await db.AuditLogs.Where(a => a.EntityType == "Dividend").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal([AuditEventCodes.DividendCreated, AuditEventCodes.DividendDeleted], dividendAudits.Select(a => a.Action).ToArray());
        Assert.Null(dividendAudits[0].OldValueJson);
        Assert.Contains("\"Amount\":1500", dividendAudits[0].NewValueJson);
        Assert.Contains("\"DateDeclared\"", dividendAudits[0].NewValueJson);
        Assert.Contains("\"Amount\":1500", dividendAudits[1].OldValueJson);
        Assert.Contains("\"Deleted\":true", dividendAudits[1].NewValueJson);

        var goingConcernAudits = await db.AuditLogs
            .Where(a => a.EntityType == "AccountingPeriod" && a.Action == AuditEventCodes.GoingConcernUpdated)
            .OrderBy(a => a.Id)
            .ToListAsync();
        Assert.Equal(2, goingConcernAudits.Count);
        Assert.All(goingConcernAudits, a =>
        {
            Assert.Equal(period.CompanyId, a.CompanyId);
            Assert.Equal(period.Id, a.PeriodId);
            Assert.Equal(period.Id, a.EntityId);
        });
        Assert.Contains("\"GoingConcernConfirmed\":true", goingConcernAudits[0].OldValueJson);
        Assert.Contains("\"GoingConcernConfirmed\":false", goingConcernAudits[0].NewValueJson);
        Assert.Contains("Directors will provide support", goingConcernAudits[0].NewValueJson);
        Assert.Contains("\"GoingConcernConfirmed\":false", goingConcernAudits[1].OldValueJson);
        Assert.Contains("Directors will provide support", goingConcernAudits[1].OldValueJson);
        Assert.Contains("\"GoingConcernConfirmed\":true", goingConcernAudits[1].NewValueJson);
        Assert.Contains("\"GoingConcernNote\":null", goingConcernAudits[1].NewValueJson);
    }

    [Fact]
    public async Task YearEndEvidence_LogsFixedAssetAndLoanAudits()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var writeGuard = new AccountingWriteGuard(db);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/fixed-assets");

        await YearEndEndpoints.CreateFixedAssetEndpointAsync(
            period.CompanyId,
            new FixedAsset
            {
                Name = "Laptop",
                Category = "Computer Equipment",
                Cost = 2_000m,
                AcquisitionDate = period.PeriodStart.AddDays(1),
                UsefulLifeYears = 3,
                DepreciationMethod = DepreciationMethod.StraightLine
            },
            db,
            writeGuard,
            audit,
            context);
        var asset = await db.FixedAssets.SingleAsync(a => a.CompanyId == period.CompanyId);
        await YearEndEndpoints.UpdateFixedAssetEndpointAsync(
            period.CompanyId,
            asset.Id,
            new FixedAsset
            {
                Name = "Laptop revised",
                Category = "Computer Equipment",
                Cost = 2_200m,
                AcquisitionDate = period.PeriodStart.AddDays(1),
                DisposalDate = period.PeriodEnd,
                DisposalProceeds = 150m,
                UsefulLifeYears = 4,
                DepreciationMethod = DepreciationMethod.ReducingBalance
            },
            db,
            writeGuard,
            audit,
            context);
        await YearEndEndpoints.DeleteFixedAssetEndpointAsync(period.CompanyId, asset.Id, db, writeGuard, audit, context);

        await YearEndEndpoints.CreateLoanEndpointAsync(
            period.CompanyId,
            new Loan
            {
                Lender = "AIB",
                OriginalAmount = 10_000m,
                Balance = 8_500m,
                DrawdownDate = period.PeriodStart,
                BalanceAsOfDate = period.PeriodEnd,
                InterestRate = 5.25m,
                IsDirectorLoan = false,
                DueWithinYear = 3_000m,
                DueAfterYear = 5_500m
            },
            db,
            writeGuard,
            audit,
            context);
        var loan = await db.Loans.SingleAsync(l => l.CompanyId == period.CompanyId);
        await YearEndEndpoints.UpdateLoanEndpointAsync(
            period.CompanyId,
            loan.Id,
            new Loan
            {
                Lender = "AIB revised",
                OriginalAmount = 10_000m,
                Balance = 8_000m,
                DrawdownDate = period.PeriodStart,
                BalanceAsOfDate = period.PeriodEnd,
                InterestRate = 5.5m,
                IsDirectorLoan = false,
                DueWithinYear = 2_500m,
                DueAfterYear = 5_500m
            },
            db,
            writeGuard,
            audit,
            context);
        await YearEndEndpoints.DeleteLoanEndpointAsync(period.CompanyId, loan.Id, db, writeGuard, audit, context);

        var assetAudits = await db.AuditLogs.Where(a => a.EntityType == "FixedAsset").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.FixedAssetCreated, AuditEventCodes.FixedAssetUpdated, AuditEventCodes.FixedAssetDeleted],
            assetAudits.Select(a => a.Action).ToArray());
        Assert.All(assetAudits, a =>
        {
            Assert.Equal(period.CompanyId, a.CompanyId);
            Assert.Null(a.PeriodId);
            Assert.Equal("user@example.ie", a.UserId);
        });
        Assert.Null(assetAudits[0].OldValueJson);
        Assert.Contains("\"Name\":\"Laptop\"", assetAudits[0].NewValueJson);
        Assert.Contains("\"Cost\":2000", assetAudits[0].NewValueJson);
        Assert.Contains("\"DepreciationMethod\":\"StraightLine\"", assetAudits[0].NewValueJson);
        Assert.Contains("\"Cost\":2000", assetAudits[1].OldValueJson);
        Assert.Contains("\"Cost\":2200", assetAudits[1].NewValueJson);
        Assert.Contains("\"DepreciationMethod\":\"ReducingBalance\"", assetAudits[1].NewValueJson);
        Assert.Contains("\"DisposalProceeds\":150", assetAudits[1].NewValueJson);
        Assert.Contains("\"Deleted\":true", assetAudits[2].NewValueJson);

        var loanAudits = await db.AuditLogs.Where(a => a.EntityType == "Loan").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.LoanCreated, AuditEventCodes.LoanUpdated, AuditEventCodes.LoanDeleted],
            loanAudits.Select(a => a.Action).ToArray());
        Assert.All(loanAudits, a =>
        {
            Assert.Equal(period.CompanyId, a.CompanyId);
            Assert.Null(a.PeriodId);
            Assert.Equal("user@example.ie", a.UserId);
        });
        Assert.Null(loanAudits[0].OldValueJson);
        Assert.Contains("\"Lender\":\"AIB\"", loanAudits[0].NewValueJson);
        Assert.Contains("\"Balance\":8500", loanAudits[0].NewValueJson);
        Assert.Contains("\"DueAfterYear\":5500", loanAudits[0].NewValueJson);
        Assert.Contains("\"Balance\":8500", loanAudits[1].OldValueJson);
        Assert.Contains("\"Lender\":\"AIB revised\"", loanAudits[1].NewValueJson);
        Assert.Contains("\"Balance\":8000", loanAudits[1].NewValueJson);
        Assert.Contains("\"InterestRate\":5.5", loanAudits[1].NewValueJson);
        Assert.Contains("\"Deleted\":true", loanAudits[2].NewValueJson);
    }

    [Fact]
    public async Task FixedAssetCreate_IgnoresNestedDepreciationEntries()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var writeGuard = new AccountingWriteGuard(db);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/fixed-assets");
        var input = new FixedAsset
        {
            Name = "Laptop",
            Category = "Computer Equipment",
            Cost = 2_000m,
            AcquisitionDate = period.PeriodStart.AddDays(1),
            UsefulLifeYears = 3,
            DepreciationMethod = DepreciationMethod.StraightLine,
            DepreciationEntries =
            [
                new DepreciationEntry
                {
                    PeriodId = otherPeriod.Id,
                    OpeningNbv = 2_000m,
                    Charge = 1_999m,
                    ClosingNbv = 1m
                }
            ]
        };

        var result = await YearEndEndpoints.CreateFixedAssetEndpointAsync(
            period.CompanyId,
            input,
            db,
            writeGuard,
            audit,
            context);

        Assert.Equal(StatusCodes.Status201Created, ResultStatusCode(result));
        var asset = await db.FixedAssets.SingleAsync(a => a.CompanyId == period.CompanyId);
        Assert.Equal("Laptop", asset.Name);
        Assert.Empty(await db.DepreciationEntries.ToListAsync());
    }

    [Fact]
    public async Task FixedAssetCreateAndUpdate_IgnoreNullNestedDepreciationEntries()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var writeGuard = new AccountingWriteGuard(db);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/fixed-assets");

        var createResult = await YearEndEndpoints.CreateFixedAssetEndpointAsync(
            period.CompanyId,
            new FixedAsset
            {
                Name = "Laptop",
                Category = "Computer Equipment",
                Cost = 2_000m,
                AcquisitionDate = period.PeriodStart.AddDays(1),
                UsefulLifeYears = 3,
                DepreciationMethod = DepreciationMethod.StraightLine,
                DepreciationEntries = null!
            },
            db,
            writeGuard,
            audit,
            context);
        var asset = await db.FixedAssets.SingleAsync(a => a.CompanyId == period.CompanyId);

        var updateResult = await YearEndEndpoints.UpdateFixedAssetEndpointAsync(
            period.CompanyId,
            asset.Id,
            new FixedAsset
            {
                Name = "Laptop revised",
                Category = "Computer Equipment",
                Cost = 2_200m,
                AcquisitionDate = period.PeriodStart.AddDays(1),
                UsefulLifeYears = 4,
                DepreciationMethod = DepreciationMethod.ReducingBalance,
                DepreciationEntries = null!
            },
            db,
            writeGuard,
            audit,
            context);

        Assert.Equal(StatusCodes.Status201Created, ResultStatusCode(createResult));
        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(updateResult));
        Assert.Empty(await db.DepreciationEntries.ToListAsync());
        Assert.Equal("Laptop revised", (await db.FixedAssets.SingleAsync(a => a.CompanyId == period.CompanyId)).Name);
    }

    [Fact]
    public async Task YearEndEvidence_LogsLoanSnapshotAndDirectorLoanAudits()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var loan = new Loan
        {
            CompanyId = period.CompanyId,
            Lender = "AIB",
            OriginalAmount = 10_000m,
            Balance = 9_000m,
            DrawdownDate = period.PeriodStart,
            BalanceAsOfDate = period.PeriodEnd,
            InterestRate = 5.25m,
            DueWithinYear = 3_000m,
            DueAfterYear = 6_000m
        };
        db.Loans.Add(loan);
        await db.SaveChangesAsync();
        var director = await db.CompanyOfficers
            .FirstAsync(o => o.CompanyId == period.CompanyId && o.Role == OfficerRole.Director);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/loan-balance-snapshots");
        var spoofedEnteredAt = new DateTime(2000, 1, 1, 9, 0, 0, DateTimeKind.Utc);

        await YearEndEndpoints.CreateLoanBalanceSnapshotEndpointAsync(
            period.CompanyId,
            period.Id,
            new LoanBalanceSnapshot
            {
                LoanId = loan.Id,
                OpeningBalance = 8_000m,
                Drawdowns = 2_000m,
                Repayments = 1_000m,
                ClosingBalance = 9_000m,
                DueWithinYear = 3_000m,
                DueAfterYear = 6_000m,
                Notes = "Bank statement support",
                EnteredBy = "Spoofed payload reviewer",
                EnteredAt = spoofedEnteredAt
            },
            db,
            audit,
            context);
        var snapshot = await db.LoanBalanceSnapshots.SingleAsync(s => s.PeriodId == period.Id);
        Assert.Equal("Example User", snapshot.EnteredBy);
        Assert.NotEqual(spoofedEnteredAt, snapshot.EnteredAt);
        await YearEndEndpoints.UpdateLoanBalanceSnapshotEndpointAsync(
            period.CompanyId,
            period.Id,
            snapshot.Id,
            new LoanBalanceSnapshot
            {
                LoanId = loan.Id,
                OpeningBalance = 9_000m,
                Drawdowns = 500m,
                Repayments = 1_500m,
                ClosingBalance = 8_000m,
                DueWithinYear = 2_000m,
                DueAfterYear = 6_000m,
                Notes = "Updated bank confirmation",
                EnteredBy = "Spoofed update reviewer"
            },
            db,
            audit,
            context);
        var updatedSnapshot = await db.LoanBalanceSnapshots.SingleAsync(s => s.PeriodId == period.Id);
        Assert.Equal("Example User", updatedSnapshot.EnteredBy);
        Assert.NotEqual(spoofedEnteredAt, updatedSnapshot.EnteredAt);
        await YearEndEndpoints.DeleteLoanBalanceSnapshotEndpointAsync(period.CompanyId, period.Id, snapshot.Id, db, audit, context);

        await YearEndEndpoints.CreateDirectorLoanEndpointAsync(
            period.CompanyId,
            period.Id,
            new DirectorLoanInput(
                director.Id,
                OpeningBalance: 0m,
                Advances: 1_000m,
                Repayments: 200m,
                ClosingBalance: 800m,
                InterestRate: 5m,
                InterestCharged: 40m,
                IsDocumented: true,
                LoanTerms: " Repayable on demand ",
                MaxBalanceDuringYear: 1_000m),
            db,
            audit,
            context);
        var directorLoan = await db.DirectorLoans.SingleAsync(d => d.PeriodId == period.Id);
        await YearEndEndpoints.UpdateDirectorLoanEndpointAsync(
            period.CompanyId,
            period.Id,
            directorLoan.Id,
            new DirectorLoanInput(
                director.Id,
                OpeningBalance: 800m,
                Advances: 200m,
                Repayments: 500m,
                ClosingBalance: 500m,
                InterestRate: 5.5m,
                InterestCharged: 25m,
                IsDocumented: false,
                LoanTerms: "Board minute required",
                MaxBalanceDuringYear: 1_000m),
            db,
            audit,
            context);
        await YearEndEndpoints.DeleteDirectorLoanEndpointAsync(period.CompanyId, period.Id, directorLoan.Id, db, audit, context);

        var snapshotAudits = await db.AuditLogs.Where(a => a.EntityType == "LoanBalanceSnapshot").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.LoanBalanceSnapshotCreated, AuditEventCodes.LoanBalanceSnapshotUpdated, AuditEventCodes.LoanBalanceSnapshotDeleted],
            snapshotAudits.Select(a => a.Action).ToArray());
        Assert.All(snapshotAudits, a =>
        {
            Assert.Equal(period.CompanyId, a.CompanyId);
            Assert.Equal(period.Id, a.PeriodId);
            Assert.Equal("user@example.ie", a.UserId);
        });
        Assert.Null(snapshotAudits[0].OldValueJson);
        Assert.Contains("\"LoanId\":" + loan.Id, snapshotAudits[0].NewValueJson);
        Assert.Contains("\"ClosingBalance\":9000", snapshotAudits[0].NewValueJson);
        Assert.Contains("\"Notes\":\"Bank statement support\"", snapshotAudits[0].NewValueJson);
        Assert.Contains("\"EnteredBy\":\"Example User\"", snapshotAudits[0].NewValueJson);
        Assert.DoesNotContain("Spoofed", snapshotAudits[0].NewValueJson);
        Assert.Contains("\"ClosingBalance\":9000", snapshotAudits[1].OldValueJson);
        Assert.Contains("\"ClosingBalance\":8000", snapshotAudits[1].NewValueJson);
        Assert.Contains("\"DueWithinYear\":2000", snapshotAudits[1].NewValueJson);
        Assert.Contains("\"EnteredBy\":\"Example User\"", snapshotAudits[1].NewValueJson);
        Assert.DoesNotContain("Spoofed", snapshotAudits[1].NewValueJson);
        Assert.Contains("\"Deleted\":true", snapshotAudits[2].NewValueJson);

        var directorLoanAudits = await db.AuditLogs.Where(a => a.EntityType == "DirectorLoan").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.DirectorLoanCreated, AuditEventCodes.DirectorLoanUpdated, AuditEventCodes.DirectorLoanDeleted],
            directorLoanAudits.Select(a => a.Action).ToArray());
        Assert.All(directorLoanAudits, a =>
        {
            Assert.Equal(period.CompanyId, a.CompanyId);
            Assert.Equal(period.Id, a.PeriodId);
            Assert.Equal("user@example.ie", a.UserId);
        });
        Assert.Null(directorLoanAudits[0].OldValueJson);
        Assert.Contains("\"DirectorId\":" + director.Id, directorLoanAudits[0].NewValueJson);
        Assert.Contains("\"ClosingBalance\":800", directorLoanAudits[0].NewValueJson);
        Assert.Contains("\"LoanTerms\":\"Repayable on demand\"", directorLoanAudits[0].NewValueJson);
        Assert.Contains("\"ClosingBalance\":800", directorLoanAudits[1].OldValueJson);
        Assert.Contains("\"ClosingBalance\":500", directorLoanAudits[1].NewValueJson);
        Assert.Contains("\"IsDocumented\":false", directorLoanAudits[1].NewValueJson);
        Assert.Contains("\"Deleted\":true", directorLoanAudits[2].NewValueJson);
    }

    [Fact]
    public async Task YearEndEvidence_LogsDisclosureFactAudits()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/post-balance-sheet-events");

        await YearEndEndpoints.CreatePostBalanceSheetEventEndpointAsync(
            period.CompanyId,
            period.Id,
            new PostBalanceSheetEvent
            {
                Description = "Major customer insolvency",
                EventDate = period.PeriodEnd.AddDays(14),
                IsAdjusting = false,
                FinancialImpact = 12_500m,
                ActionRequired = "Disclose as non-adjusting event"
            },
            db,
            audit,
            context);
        var postBalanceSheetEvent = await db.PostBalanceSheetEvents.SingleAsync(e => e.PeriodId == period.Id);
        await YearEndEndpoints.DeletePostBalanceSheetEventEndpointAsync(
            period.CompanyId,
            period.Id,
            postBalanceSheetEvent.Id,
            db,
            audit,
            context);

        await YearEndEndpoints.CreateRelatedPartyTransactionEndpointAsync(
            period.CompanyId,
            period.Id,
            new RelatedPartyTransaction
            {
                PartyName = "Director Services Limited",
                Relationship = "Director-controlled company",
                TransactionType = "Management fee",
                Amount = 4_200m,
                BalanceOwed = 800m,
                Terms = "30 days"
            },
            db,
            audit,
            context);
        var relatedPartyTransaction = await db.RelatedPartyTransactions.SingleAsync(r => r.PeriodId == period.Id);
        await YearEndEndpoints.DeleteRelatedPartyTransactionEndpointAsync(
            period.CompanyId,
            period.Id,
            relatedPartyTransaction.Id,
            db,
            audit,
            context);

        await YearEndEndpoints.CreateContingentLiabilityEndpointAsync(
            period.CompanyId,
            period.Id,
            new ContingentLiability
            {
                Description = "Bank guarantee",
                Nature = "Guarantee",
                EstimatedAmount = 25_000m,
                Likelihood = "Possible"
            },
            db,
            audit,
            context);
        var contingentLiability = await db.ContingentLiabilities.SingleAsync(c => c.PeriodId == period.Id);
        await YearEndEndpoints.DeleteContingentLiabilityEndpointAsync(
            period.CompanyId,
            period.Id,
            contingentLiability.Id,
            db,
            audit,
            context);

        var eventAudits = await db.AuditLogs.Where(a => a.EntityType == "PostBalanceSheetEvent").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.PostBalanceSheetEventCreated, AuditEventCodes.PostBalanceSheetEventDeleted],
            eventAudits.Select(a => a.Action).ToArray());
        Assert.All(eventAudits, a =>
        {
            Assert.Equal(period.CompanyId, a.CompanyId);
            Assert.Equal(period.Id, a.PeriodId);
            Assert.Equal("user@example.ie", a.UserId);
        });
        Assert.Null(eventAudits[0].OldValueJson);
        Assert.Contains("\"Description\":\"Major customer insolvency\"", eventAudits[0].NewValueJson);
        Assert.Contains("\"IsAdjusting\":false", eventAudits[0].NewValueJson);
        Assert.Contains("\"FinancialImpact\":12500", eventAudits[0].NewValueJson);
        Assert.Contains("\"Description\":\"Major customer insolvency\"", eventAudits[1].OldValueJson);
        Assert.Contains("\"FinancialImpact\":12500", eventAudits[1].OldValueJson);
        Assert.Contains("\"Deleted\":true", eventAudits[1].NewValueJson);

        var relatedPartyAudits = await db.AuditLogs.Where(a => a.EntityType == "RelatedPartyTransaction").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.RelatedPartyTransactionCreated, AuditEventCodes.RelatedPartyTransactionDeleted],
            relatedPartyAudits.Select(a => a.Action).ToArray());
        Assert.Null(relatedPartyAudits[0].OldValueJson);
        Assert.Contains("\"PartyName\":\"Director Services Limited\"", relatedPartyAudits[0].NewValueJson);
        Assert.Contains("\"Amount\":4200", relatedPartyAudits[0].NewValueJson);
        Assert.Contains("\"BalanceOwed\":800", relatedPartyAudits[0].NewValueJson);
        Assert.Contains("\"PartyName\":\"Director Services Limited\"", relatedPartyAudits[1].OldValueJson);
        Assert.Contains("\"Amount\":4200", relatedPartyAudits[1].OldValueJson);
        Assert.Contains("\"Deleted\":true", relatedPartyAudits[1].NewValueJson);

        var contingentLiabilityAudits = await db.AuditLogs.Where(a => a.EntityType == "ContingentLiability").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.ContingentLiabilityCreated, AuditEventCodes.ContingentLiabilityDeleted],
            contingentLiabilityAudits.Select(a => a.Action).ToArray());
        Assert.Null(contingentLiabilityAudits[0].OldValueJson);
        Assert.Contains("\"Description\":\"Bank guarantee\"", contingentLiabilityAudits[0].NewValueJson);
        Assert.Contains("\"EstimatedAmount\":25000", contingentLiabilityAudits[0].NewValueJson);
        Assert.Contains("\"Likelihood\":\"Possible\"", contingentLiabilityAudits[0].NewValueJson);
        Assert.Contains("\"Description\":\"Bank guarantee\"", contingentLiabilityAudits[1].OldValueJson);
        Assert.Contains("\"EstimatedAmount\":25000", contingentLiabilityAudits[1].OldValueJson);
        Assert.Contains("\"Deleted\":true", contingentLiabilityAudits[1].NewValueJson);
    }

    [Fact]
    public async Task YearEndEvidence_LogsOpeningBalanceAndCustomNoteAudits()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var category = AddCategory(db, period.CompanyId, "3001", "Retained earnings", AccountCategoryType.Equity);
        db.NotesDisclosures.Add(new NotesDisclosure
        {
            PeriodId = period.Id,
            NoteNumber = 5,
            Title = "Accounting policies",
            Content = "Generated policy note.",
            IsRequired = true,
            IsIncluded = true
        });
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/opening-balances/{category.Id}");

        await YearEndEndpoints.UpsertOpeningBalanceEndpointAsync(
            period.CompanyId,
            period.Id,
            category.Id,
            new OpeningBalanceInput(0m, 1_000m, " Opening reserves per prior year accounts ", "Spoofed reviewer", true),
            db,
            audit,
            context);
        await YearEndEndpoints.UpsertOpeningBalanceEndpointAsync(
            period.CompanyId,
            period.Id,
            category.Id,
            new OpeningBalanceInput(0m, 1_250m, "Revised prior year accounts", "Spoofed reviewer", false),
            db,
            audit,
            context);
        await YearEndEndpoints.DeleteOpeningBalanceEndpointAsync(period.CompanyId, period.Id, category.Id, db, audit, context);

        await YearEndEndpoints.CreateNoteEndpointAsync(
            period.CompanyId,
            period.Id,
            new NotesDisclosure
            {
                Title = "Custom guarantees note",
                Content = "The company provided a guarantee after year end.",
                IsRequired = true,
                IsIncluded = true
            },
            db,
            audit,
            context);
        var customNote = await db.NotesDisclosures.SingleAsync(n => n.PeriodId == period.Id && n.Title == "Custom guarantees note");
        Assert.False(customNote.IsRequired);
        await YearEndEndpoints.DeleteNoteEndpointAsync(period.CompanyId, period.Id, customNote.Id, db, audit, context);

        var openingBalanceAudits = await db.AuditLogs.Where(a => a.EntityType == "OpeningBalance").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.OpeningBalanceUpserted, AuditEventCodes.OpeningBalanceUpserted, AuditEventCodes.OpeningBalanceDeleted],
            openingBalanceAudits.Select(a => a.Action).ToArray());
        Assert.All(openingBalanceAudits, a =>
        {
            Assert.Equal(period.CompanyId, a.CompanyId);
            Assert.Equal(period.Id, a.PeriodId);
            Assert.Equal("user@example.ie", a.UserId);
        });
        Assert.Null(openingBalanceAudits[0].OldValueJson);
        Assert.Contains("\"AccountCategoryId\":" + category.Id, openingBalanceAudits[0].NewValueJson);
        Assert.Contains("\"Credit\":1000", openingBalanceAudits[0].NewValueJson);
        Assert.Contains("\"SourceNote\":\"Opening reserves per prior year accounts\"", openingBalanceAudits[0].NewValueJson);
        Assert.Contains("\"EnteredBy\":\"Example User\"", openingBalanceAudits[0].NewValueJson);
        Assert.Contains("\"Reviewed\":true", openingBalanceAudits[0].NewValueJson);
        Assert.Contains("\"WasCreated\":true", openingBalanceAudits[0].NewValueJson);
        Assert.Contains("\"Credit\":1000", openingBalanceAudits[1].OldValueJson);
        Assert.Contains("\"Credit\":1250", openingBalanceAudits[1].NewValueJson);
        Assert.Contains("\"Reviewed\":false", openingBalanceAudits[1].NewValueJson);
        Assert.Contains("\"WasCreated\":false", openingBalanceAudits[1].NewValueJson);
        Assert.Contains("\"Credit\":1250", openingBalanceAudits[2].OldValueJson);
        Assert.Contains("\"Deleted\":true", openingBalanceAudits[2].NewValueJson);

        var noteAudits = await db.AuditLogs.Where(a => a.EntityType == "NotesDisclosure").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.NoteDisclosureCreated, AuditEventCodes.NoteDisclosureDeleted],
            noteAudits.Select(a => a.Action).ToArray());
        Assert.Null(noteAudits[0].OldValueJson);
        Assert.Contains("\"NoteNumber\":6", noteAudits[0].NewValueJson);
        Assert.Contains("\"Title\":\"Custom guarantees note\"", noteAudits[0].NewValueJson);
        Assert.Contains("\"IsRequired\":false", noteAudits[0].NewValueJson);
        Assert.Contains("\"ContentLength\":48", noteAudits[0].NewValueJson);
        Assert.Contains("\"NoteNumber\":6", noteAudits[1].OldValueJson);
        Assert.Contains("\"Title\":\"Custom guarantees note\"", noteAudits[1].OldValueJson);
        Assert.Contains("\"Deleted\":true", noteAudits[1].NewValueJson);
    }

    [Fact]
    public async Task UpsertOpeningBalance_RejectsIncomeAndExpenseAccountsButAllowsBalanceSheetAccounts()
    {
        // accounting-opening-balance-pl-accounts: an opening balance on a 4xxx/5xxx/6xxx income or
        // expense code folds a brought-forward figure into current-year turnover/expenses. The upsert
        // must reject it with a clean 400 and store nothing; a balance-sheet account is still accepted.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var income = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var expense = AddCategory(db, period.CompanyId, "5000", "Cost of sales", AccountCategoryType.Expense);
        var retainedEarnings = AddCategory(db, period.CompanyId, "3100", "Retained earnings", AccountCategoryType.Equity);
        await db.SaveChangesAsync();
        var audit = new AuditService(db);

        IStatusCodeHttpResult StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);

        var incomeResult = await YearEndEndpoints.UpsertOpeningBalanceEndpointAsync(
            period.CompanyId, period.Id, income.Id,
            new OpeningBalanceInput(0m, 10_000m, "Opening sales (wrong account)", null, true),
            db, audit,
            AuthenticatedRequest("Accountant", HttpMethods.Put,
                $"/api/companies/{period.CompanyId}/periods/{period.Id}/opening-balances/{income.Id}"));
        var expenseResult = await YearEndEndpoints.UpsertOpeningBalanceEndpointAsync(
            period.CompanyId, period.Id, expense.Id,
            new OpeningBalanceInput(10_000m, 0m, "Opening expense (wrong account)", null, true),
            db, audit,
            AuthenticatedRequest("Accountant", HttpMethods.Put,
                $"/api/companies/{period.CompanyId}/periods/{period.Id}/opening-balances/{expense.Id}"));

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(incomeResult).StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(expenseResult).StatusCode);
        // Nothing persisted for the income/expense codes, so turnover/expenses are untouched.
        Assert.Empty(await db.OpeningBalances.ToListAsync());
        Assert.DoesNotContain("OpeningBalance", (await db.AuditLogs.ToListAsync()).Select(a => a.EntityType));

        // A balance-sheet account (retained earnings) is still accepted.
        var equityResult = await YearEndEndpoints.UpsertOpeningBalanceEndpointAsync(
            period.CompanyId, period.Id, retainedEarnings.Id,
            new OpeningBalanceInput(0m, 10_000m, "Opening retained earnings per prior accounts", null, true),
            db, audit,
            AuthenticatedRequest("Accountant", HttpMethods.Put,
                $"/api/companies/{period.CompanyId}/periods/{period.Id}/opening-balances/{retainedEarnings.Id}"));
        Assert.Equal(StatusCodes.Status200OK, StatusOf(equityResult).StatusCode);
        var stored = Assert.Single(await db.OpeningBalances.ToListAsync());
        Assert.Equal(retainedEarnings.Id, stored.AccountCategoryId);
        Assert.Equal(10_000m, stored.Credit);
    }

    [Fact]
    public async Task UpsertTaxBalance_RejectsInconsistentOrNegativeTriple()
    {
        // accounting-tax-balance-internal-consistency: the upsert previously stored the triple verbatim,
        // so Balance != Liability - Paid (or a negative liability/paid) mis-stated creditors and
        // profit-after-tax. The endpoint must reject an inconsistent/negative triple and accept a
        // consistent one (including a legitimate overpayment producing a negative Balance).
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        IStatusCodeHttpResult StatusOf(IResult r) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(r);
        HttpContext Ctx() => AuthenticatedRequest("Accountant", HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/tax-balances/CorporationTax");

        var inconsistent = await YearEndEndpoints.UpsertTaxBalanceEndpointAsync(
            period.CompanyId, period.Id, TaxType.CorporationTax,
            new TaxBalance { Liability = 1_000m, Paid = 200m, Balance = 900m }, db, audit, Ctx());
        var negative = await YearEndEndpoints.UpsertTaxBalanceEndpointAsync(
            period.CompanyId, period.Id, TaxType.CorporationTax,
            new TaxBalance { Liability = -50m, Paid = 0m, Balance = -50m }, db, audit, Ctx());

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(inconsistent).StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(negative).StatusCode);
        Assert.Empty(await db.TaxBalances.ToListAsync());

        // A consistent triple (Balance == Liability - Paid) persists.
        var consistent = await YearEndEndpoints.UpsertTaxBalanceEndpointAsync(
            period.CompanyId, period.Id, TaxType.CorporationTax,
            new TaxBalance { Liability = 1_000m, Paid = 200m, Balance = 800m }, db, audit, Ctx());
        Assert.Equal(StatusCodes.Status200OK, StatusOf(consistent).StatusCode);
        Assert.Equal(800m, (await db.TaxBalances.SingleAsync()).Balance);

        // An overpayment (refund due) is consistent and allowed: -20 == 100 - 120.
        var overpaid = await YearEndEndpoints.UpsertTaxBalanceEndpointAsync(
            period.CompanyId, period.Id, TaxType.CorporationTax,
            new TaxBalance { Liability = 100m, Paid = 120m, Balance = -20m }, db, audit, Ctx());
        Assert.Equal(StatusCodes.Status200OK, StatusOf(overpaid).StatusCode);
        Assert.Equal(-20m, (await db.TaxBalances.SingleAsync()).Balance);
    }

    [Fact]
    public async Task YearEndEvidence_DeniesReviewerPreparationWriteWithoutApiAccessService()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var reviewerContext = AuthenticatedRequest(
            "Reviewer",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/debtors");

        var denied = await YearEndEndpoints.CreateDebtorEndpointAsync(
            period.CompanyId,
            period.Id,
            new Debtor { Name = "Reviewer debtor", Amount = 100m, Type = DebtorType.Other },
            db,
            audit,
            reviewerContext);

        Assert.Equal(StatusCodes.Status403Forbidden, ResultStatusCode(denied));
        Assert.Empty(await db.Debtors.Where(d => d.PeriodId == period.Id).ToListAsync());

        var accountantContext = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/debtors");

        var created = await YearEndEndpoints.CreateDebtorEndpointAsync(
            period.CompanyId,
            period.Id,
            new Debtor { Name = "Accountant debtor", Amount = 100m, Type = DebtorType.Other },
            db,
            audit,
            accountantContext);

        Assert.Equal(StatusCodes.Status201Created, ResultStatusCode(created));
        var debtor = await db.Debtors.SingleAsync(d => d.PeriodId == period.Id);
        Assert.Equal("Accountant debtor", debtor.Name);
    }

    [Fact]
    public async Task YearEndFigureInputs_RejectBadFiguresWithCleanBadRequestAndNoCorruption()
    {
        // G3 (customer inputs are safe): a fat-fingered negative amount, blank name or zero useful life
        // must fail with a clear 400 — never a 500, never a silently corrupted year-end figure.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var writeGuard = new AccountingWriteGuard(db);
        DefaultHttpContext Ctx() => AuthenticatedRequest("Accountant", HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/year-end");

        var badDebtor = await YearEndEndpoints.CreateDebtorEndpointAsync(
            period.CompanyId, period.Id,
            new Debtor { Name = "Customer", Amount = -100m, Type = DebtorType.Trade },
            db, audit, Ctx());
        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(badDebtor));
        Assert.Empty(await db.Debtors.Where(d => d.PeriodId == period.Id).ToListAsync());

        var badCreditor = await YearEndEndpoints.CreateCreditorEndpointAsync(
            period.CompanyId, period.Id,
            new Creditor { Name = "   ", Amount = 50m, Type = CreditorType.Trade },
            db, audit, Ctx());
        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(badCreditor));
        Assert.Empty(await db.Creditors.Where(c => c.PeriodId == period.Id).ToListAsync());

        var badInventory = await YearEndEndpoints.CreateInventoryEndpointAsync(
            period.CompanyId, period.Id,
            new Inventory { Description = "Stock", Value = -5m, ValuationMethod = ValuationMethod.Cost },
            db, audit, Ctx());
        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(badInventory));
        Assert.Empty(await db.Inventories.Where(i => i.PeriodId == period.Id).ToListAsync());

        var badDividend = await YearEndEndpoints.CreateDividendEndpointAsync(
            period.CompanyId, period.Id,
            new Dividend { Amount = -1m },
            db, audit, Ctx());
        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(badDividend));
        Assert.Empty(await db.Dividends.Where(d => d.PeriodId == period.Id).ToListAsync());

        // Zero useful life would otherwise be silently skipped by the depreciation engine.
        var badLifeAsset = await YearEndEndpoints.CreateFixedAssetEndpointAsync(
            period.CompanyId,
            new FixedAsset { Name = "Van", Category = "Motor Vehicles", Cost = 10_000m, AcquisitionDate = period.PeriodStart, UsefulLifeYears = 0 },
            db, writeGuard, audit, Ctx());
        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(badLifeAsset));

        var badCostAsset = await YearEndEndpoints.CreateFixedAssetEndpointAsync(
            period.CompanyId,
            new FixedAsset { Name = "Van", Category = "Motor Vehicles", Cost = -10_000m, AcquisitionDate = period.PeriodStart, UsefulLifeYears = 4 },
            db, writeGuard, audit, Ctx());
        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(badCostAsset));

        var badDisposalAsset = await YearEndEndpoints.CreateFixedAssetEndpointAsync(
            period.CompanyId,
            new FixedAsset { Name = "Van", Category = "Motor Vehicles", Cost = 10_000m, AcquisitionDate = period.PeriodEnd, DisposalDate = period.PeriodStart, UsefulLifeYears = 4 },
            db, writeGuard, audit, Ctx());
        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(badDisposalAsset));

        Assert.Empty(await db.FixedAssets.Where(a => a.CompanyId == period.CompanyId).ToListAsync());

        // Nothing was persisted, so no audit rows were written for any rejected input.
        Assert.Empty(await db.AuditLogs.Where(a =>
            a.EntityType == "Debtor" || a.EntityType == "Creditor" || a.EntityType == "Inventory"
            || a.EntityType == "Dividend" || a.EntityType == "FixedAsset").ToListAsync());
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
    public async Task YearEndEvidenceSemanticAudits_RejectPeriodFromDifferentCompanyWithoutAudit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/debtors");

        var result = await YearEndEndpoints.CreateDebtorEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new Debtor { Name = "Wrong company debtor", Amount = 100m, Type = DebtorType.Other },
            db,
            audit,
            context);

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, statusResult.StatusCode);
        Assert.Empty(await db.Debtors.Where(d => d.PeriodId == otherPeriod.Id).ToListAsync());
        Assert.Empty(await db.AuditLogs.Where(a => a.EntityType == "Debtor").ToListAsync());
    }

    [Fact]
    public async Task YearEndEvidenceMoreSemanticAudits_RejectPeriodFromDifferentCompanyWithoutAudit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/payroll");
        var existingOtherDividend = new Dividend
        {
            PeriodId = otherPeriod.Id,
            Amount = 2_000m,
            DateDeclared = otherPeriod.PeriodEnd
        };
        db.Dividends.Add(existingOtherDividend);
        await db.SaveChangesAsync();

        var payrollResult = await YearEndEndpoints.UpsertPayrollSummaryEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new PayrollSummary { GrossWages = 10_000m, StaffCount = 4 },
            db,
            audit,
            context);
        var dividendResult = await YearEndEndpoints.CreateDividendEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new Dividend { Amount = 1_500m, DateDeclared = otherPeriod.PeriodEnd },
            db,
            audit,
            context);
        var deleteDividendResult = await YearEndEndpoints.DeleteDividendEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            existingOtherDividend.Id,
            db,
            audit,
            context);
        var goingConcernResult = await YearEndEndpoints.UpdateGoingConcernEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new GoingConcernInput(false, "Wrong company period"),
            db,
            audit,
            context);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(payrollResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(dividendResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteDividendResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(goingConcernResult).StatusCode);
        Assert.Empty(await db.PayrollSummaries.Where(p => p.PeriodId == otherPeriod.Id).ToListAsync());
        var remainingOtherDividend = await db.Dividends.SingleAsync(d => d.PeriodId == otherPeriod.Id);
        Assert.Equal(existingOtherDividend.Id, remainingOtherDividend.Id);
        Assert.Equal(2_000m, remainingOtherDividend.Amount);
        Assert.True((await db.AccountingPeriods.FindAsync(otherPeriod.Id))!.GoingConcernConfirmed);

        var entityTypes = (await db.AuditLogs.ToListAsync()).Select(a => a.EntityType).ToArray();
        Assert.DoesNotContain("PayrollSummary", entityTypes);
        Assert.DoesNotContain("Dividend", entityTypes);
        Assert.DoesNotContain("AccountingPeriod", entityTypes);
    }

    [Fact]
    public async Task YearEndEvidenceFixedAssetAndLoanAudits_RejectWrongCompanyUpdatesAndDeletesWithoutAudit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var writeGuard = new AccountingWriteGuard(db);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/fixed-assets/1");
        var otherAsset = new FixedAsset
        {
            CompanyId = otherPeriod.CompanyId,
            Name = "Other company van",
            Category = "Motor Vehicles",
            Cost = 7_500m,
            AcquisitionDate = otherPeriod.PeriodStart,
            UsefulLifeYears = 5
        };
        var otherLoan = new Loan
        {
            CompanyId = otherPeriod.CompanyId,
            Lender = "Other bank",
            OriginalAmount = 20_000m,
            Balance = 18_000m,
            DrawdownDate = otherPeriod.PeriodStart,
            BalanceAsOfDate = otherPeriod.PeriodEnd,
            InterestRate = 4.5m,
            DueWithinYear = 6_000m,
            DueAfterYear = 12_000m
        };
        db.FixedAssets.Add(otherAsset);
        db.Loans.Add(otherLoan);
        await db.SaveChangesAsync();

        var updateAssetResult = await YearEndEndpoints.UpdateFixedAssetEndpointAsync(
            period.CompanyId,
            otherAsset.Id,
            new FixedAsset
            {
                Name = "Cross-company asset",
                Category = "Computer Equipment",
                Cost = 100m,
                AcquisitionDate = period.PeriodStart,
                UsefulLifeYears = 3
            },
            db,
            writeGuard,
            audit,
            context);
        var deleteAssetResult = await YearEndEndpoints.DeleteFixedAssetEndpointAsync(
            period.CompanyId,
            otherAsset.Id,
            db,
            writeGuard,
            audit,
            context);
        var updateLoanResult = await YearEndEndpoints.UpdateLoanEndpointAsync(
            period.CompanyId,
            otherLoan.Id,
            new Loan
            {
                Lender = "Cross-company loan",
                OriginalAmount = 1_000m,
                Balance = 900m,
                DrawdownDate = period.PeriodStart,
                BalanceAsOfDate = period.PeriodEnd,
                InterestRate = 3m,
                DueWithinYear = 900m
            },
            db,
            writeGuard,
            audit,
            context);
        var deleteLoanResult = await YearEndEndpoints.DeleteLoanEndpointAsync(
            period.CompanyId,
            otherLoan.Id,
            db,
            writeGuard,
            audit,
            context);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(updateAssetResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteAssetResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(updateLoanResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteLoanResult).StatusCode);

        var remainingAsset = await db.FixedAssets.SingleAsync(a => a.Id == otherAsset.Id);
        var remainingLoan = await db.Loans.SingleAsync(l => l.Id == otherLoan.Id);
        Assert.Equal("Other company van", remainingAsset.Name);
        Assert.Equal(7_500m, remainingAsset.Cost);
        Assert.Equal("Other bank", remainingLoan.Lender);
        Assert.Equal(18_000m, remainingLoan.Balance);

        var entityTypes = (await db.AuditLogs.ToListAsync()).Select(a => a.EntityType).ToArray();
        Assert.DoesNotContain("FixedAsset", entityTypes);
        Assert.DoesNotContain("Loan", entityTypes);
    }

    [Fact]
    public async Task YearEndEvidenceLoanEvidenceAudits_RejectPeriodFromDifferentCompanyWithoutAudit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var loan = new Loan
        {
            CompanyId = period.CompanyId,
            Lender = "AIB",
            OriginalAmount = 10_000m,
            Balance = 9_000m,
            DrawdownDate = period.PeriodStart,
            BalanceAsOfDate = period.PeriodEnd,
            InterestRate = 5.25m,
            DueWithinYear = 3_000m,
            DueAfterYear = 6_000m
        };
        var otherLoan = new Loan
        {
            CompanyId = otherPeriod.CompanyId,
            Lender = "Other bank",
            OriginalAmount = 5_000m,
            Balance = 4_000m,
            DrawdownDate = otherPeriod.PeriodStart,
            BalanceAsOfDate = otherPeriod.PeriodEnd,
            InterestRate = 4.5m,
            DueWithinYear = 1_000m,
            DueAfterYear = 3_000m
        };
        var otherDirector = await db.CompanyOfficers
            .FirstAsync(o => o.CompanyId == otherPeriod.CompanyId && o.Role == OfficerRole.Director);
        var otherDirectorLoan = new DirectorLoan
        {
            PeriodId = otherPeriod.Id,
            DirectorId = otherDirector.Id,
            OpeningBalance = 0m,
            Advances = 2_000m,
            Repayments = 500m,
            ClosingBalance = 1_500m,
            LoanTerms = "Other company support"
        };
        db.Loans.Add(loan);
        db.Loans.Add(otherLoan);
        db.DirectorLoans.Add(otherDirectorLoan);
        await db.SaveChangesAsync();
        var existingSnapshot = new LoanBalanceSnapshot
        {
            LoanId = loan.Id,
            PeriodId = period.Id,
            OpeningBalance = 9_000m,
            Drawdowns = 0m,
            Repayments = 1_000m,
            ClosingBalance = 8_000m,
            DueWithinYear = 2_000m,
            DueAfterYear = 6_000m,
            Notes = "Existing snapshot"
        };
        db.LoanBalanceSnapshots.Add(existingSnapshot);
        await db.SaveChangesAsync();
        var wrongCompanyLoanSnapshot = new LoanBalanceSnapshot
        {
            LoanId = otherLoan.Id,
            PeriodId = period.Id,
            OpeningBalance = 4_000m,
            Drawdowns = 0m,
            Repayments = 500m,
            ClosingBalance = 3_500m,
            DueWithinYear = 500m,
            DueAfterYear = 3_000m,
            Notes = "Inconsistent cross-company fixture"
        };
        db.LoanBalanceSnapshots.Add(wrongCompanyLoanSnapshot);
        await db.SaveChangesAsync();
        var director = await db.CompanyOfficers
            .FirstAsync(o => o.CompanyId == period.CompanyId && o.Role == OfficerRole.Director);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/loan-balance-snapshots");

        var createSnapshotWrongPeriodResult = await YearEndEndpoints.CreateLoanBalanceSnapshotEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new LoanBalanceSnapshot
            {
                LoanId = loan.Id,
                OpeningBalance = 9_000m,
                Drawdowns = 0m,
                Repayments = 1_000m,
                ClosingBalance = 8_000m,
                DueWithinYear = 2_000m,
                DueAfterYear = 6_000m
            },
            db,
            audit,
            context);
        var createSnapshotWrongCompanyLoanResult = await YearEndEndpoints.CreateLoanBalanceSnapshotEndpointAsync(
            period.CompanyId,
            period.Id,
            new LoanBalanceSnapshot
            {
                LoanId = otherLoan.Id,
                OpeningBalance = 0m,
                Drawdowns = 0m,
                Repayments = 0m,
                ClosingBalance = 0m
            },
            db,
            audit,
            context);
        var updateSnapshotWrongPeriodResult = await YearEndEndpoints.UpdateLoanBalanceSnapshotEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            existingSnapshot.Id,
            new LoanBalanceSnapshot
            {
                LoanId = loan.Id,
                OpeningBalance = 8_000m,
                Drawdowns = 0m,
                Repayments = 1_000m,
                ClosingBalance = 7_000m,
                DueWithinYear = 1_000m,
                DueAfterYear = 6_000m
            },
            db,
            audit,
            context);
        var updateSnapshotWrongCompanyLoanResult = await YearEndEndpoints.UpdateLoanBalanceSnapshotEndpointAsync(
            period.CompanyId,
            period.Id,
            existingSnapshot.Id,
            new LoanBalanceSnapshot
            {
                LoanId = otherLoan.Id,
                OpeningBalance = 8_000m,
                Drawdowns = 0m,
                Repayments = 1_000m,
                ClosingBalance = 7_000m,
                DueWithinYear = 1_000m,
                DueAfterYear = 6_000m
            },
            db,
            audit,
            context);
        var deleteSnapshotWrongPeriodResult = await YearEndEndpoints.DeleteLoanBalanceSnapshotEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            existingSnapshot.Id,
            db,
            audit,
            context);
        var deleteSnapshotWrongCompanyLoanResult = await YearEndEndpoints.DeleteLoanBalanceSnapshotEndpointAsync(
            period.CompanyId,
            period.Id,
            wrongCompanyLoanSnapshot.Id,
            db,
            audit,
            context);
        var createDirectorLoanWrongPeriodResult = await YearEndEndpoints.CreateDirectorLoanEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new DirectorLoanInput(
                director.Id,
                OpeningBalance: 0m,
                Advances: 1_000m,
                Repayments: 0m,
                ClosingBalance: 1_000m,
                InterestRate: 5m,
                InterestCharged: 0m,
                IsDocumented: true,
                LoanTerms: "Wrong period",
                MaxBalanceDuringYear: 1_000m),
            db,
            audit,
            context);
        var updateDirectorLoanWrongPeriodResult = await YearEndEndpoints.UpdateDirectorLoanEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            otherDirectorLoan.Id,
            new DirectorLoanInput(
                director.Id,
                OpeningBalance: 0m,
                Advances: 1_000m,
                Repayments: 0m,
                ClosingBalance: 1_000m,
                InterestRate: 5m,
                InterestCharged: 0m,
                IsDocumented: true,
                LoanTerms: "Wrong period",
                MaxBalanceDuringYear: 1_000m),
            db,
            audit,
            context);
        var deleteDirectorLoanResult = await YearEndEndpoints.DeleteDirectorLoanEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            otherDirectorLoan.Id,
            db,
            audit,
            context);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(createSnapshotWrongPeriodResult).StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(createSnapshotWrongCompanyLoanResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(updateSnapshotWrongPeriodResult).StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(updateSnapshotWrongCompanyLoanResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteSnapshotWrongPeriodResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteSnapshotWrongCompanyLoanResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(createDirectorLoanWrongPeriodResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(updateDirectorLoanWrongPeriodResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteDirectorLoanResult).StatusCode);
        Assert.Empty(await db.LoanBalanceSnapshots.Where(s => s.PeriodId == otherPeriod.Id).ToListAsync());
        var periodSnapshots = await db.LoanBalanceSnapshots.Where(s => s.PeriodId == period.Id).OrderBy(s => s.Id).ToListAsync();
        Assert.Equal([existingSnapshot.Id, wrongCompanyLoanSnapshot.Id], periodSnapshots.Select(s => s.Id).ToArray());
        var remainingSnapshot = periodSnapshots.Single(s => s.Id == existingSnapshot.Id);
        Assert.Equal(8_000m, remainingSnapshot.ClosingBalance);
        Assert.Equal("Existing snapshot", remainingSnapshot.Notes);
        Assert.NotNull(await db.LoanBalanceSnapshots.FindAsync(wrongCompanyLoanSnapshot.Id));
        var otherDirectorLoans = await db.DirectorLoans.Where(d => d.PeriodId == otherPeriod.Id).ToListAsync();
        Assert.Single(otherDirectorLoans);
        Assert.Equal(otherDirectorLoan.Id, otherDirectorLoans[0].Id);
        Assert.Empty(await db.DirectorLoans.Where(d => d.PeriodId == period.Id).ToListAsync());

        var entityTypes = (await db.AuditLogs.ToListAsync()).Select(a => a.EntityType).ToArray();
        Assert.DoesNotContain("LoanBalanceSnapshot", entityTypes);
        Assert.DoesNotContain("DirectorLoan", entityTypes);
    }

    [Fact]
    public async Task YearEndEvidenceDisclosureFactAudits_RejectPeriodFromDifferentCompanyWithoutAudit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherEvent = new PostBalanceSheetEvent
        {
            PeriodId = otherPeriod.Id,
            Description = "Other company event",
            EventDate = otherPeriod.PeriodEnd.AddDays(1)
        };
        var otherRelatedParty = new RelatedPartyTransaction
        {
            PeriodId = otherPeriod.Id,
            PartyName = "Other party",
            Relationship = "Director",
            TransactionType = "Loan",
            Amount = 1_000m
        };
        var otherContingentLiability = new ContingentLiability
        {
            PeriodId = otherPeriod.Id,
            Description = "Other guarantee",
            Nature = "Guarantee",
            EstimatedAmount = 5_000m
        };
        db.PostBalanceSheetEvents.Add(otherEvent);
        db.RelatedPartyTransactions.Add(otherRelatedParty);
        db.ContingentLiabilities.Add(otherContingentLiability);
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/post-balance-sheet-events");

        var createEventResult = await YearEndEndpoints.CreatePostBalanceSheetEventEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new PostBalanceSheetEvent { Description = "Wrong period event", EventDate = otherPeriod.PeriodEnd },
            db,
            audit,
            context);
        var deleteEventResult = await YearEndEndpoints.DeletePostBalanceSheetEventEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            otherEvent.Id,
            db,
            audit,
            context);
        var createRelatedPartyResult = await YearEndEndpoints.CreateRelatedPartyTransactionEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new RelatedPartyTransaction { PartyName = "Wrong party", Relationship = "Director", TransactionType = "Loan", Amount = 1_000m },
            db,
            audit,
            context);
        var deleteRelatedPartyResult = await YearEndEndpoints.DeleteRelatedPartyTransactionEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            otherRelatedParty.Id,
            db,
            audit,
            context);
        var createContingentLiabilityResult = await YearEndEndpoints.CreateContingentLiabilityEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new ContingentLiability { Description = "Wrong liability", Nature = "Guarantee", EstimatedAmount = 5_000m },
            db,
            audit,
            context);
        var deleteContingentLiabilityResult = await YearEndEndpoints.DeleteContingentLiabilityEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            otherContingentLiability.Id,
            db,
            audit,
            context);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(createEventResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteEventResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(createRelatedPartyResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteRelatedPartyResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(createContingentLiabilityResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteContingentLiabilityResult).StatusCode);
        var remainingEvent = await db.PostBalanceSheetEvents.SingleAsync(e => e.PeriodId == otherPeriod.Id);
        var remainingRelatedParty = await db.RelatedPartyTransactions.SingleAsync(r => r.PeriodId == otherPeriod.Id);
        var remainingContingentLiability = await db.ContingentLiabilities.SingleAsync(c => c.PeriodId == otherPeriod.Id);
        Assert.Equal(otherEvent.Id, remainingEvent.Id);
        Assert.Equal("Other company event", remainingEvent.Description);
        Assert.Equal(otherPeriod.PeriodEnd.AddDays(1), remainingEvent.EventDate);
        Assert.Equal(otherRelatedParty.Id, remainingRelatedParty.Id);
        Assert.Equal("Other party", remainingRelatedParty.PartyName);
        Assert.Equal(1_000m, remainingRelatedParty.Amount);
        Assert.Equal(otherContingentLiability.Id, remainingContingentLiability.Id);
        Assert.Equal("Other guarantee", remainingContingentLiability.Description);
        Assert.Equal(5_000m, remainingContingentLiability.EstimatedAmount);

        var entityTypes = (await db.AuditLogs.ToListAsync()).Select(a => a.EntityType).ToArray();
        Assert.DoesNotContain("PostBalanceSheetEvent", entityTypes);
        Assert.DoesNotContain("RelatedPartyTransaction", entityTypes);
        Assert.DoesNotContain("ContingentLiability", entityTypes);
    }

    [Fact]
    public async Task YearEndEvidenceOpeningBalanceAndCustomNoteAudits_RejectInvalidOwnershipWithoutAudit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var category = AddCategory(db, period.CompanyId, "3002", "Opening equity", AccountCategoryType.Equity);
        var otherCategory = AddCategory(db, otherPeriod.CompanyId, "3003", "Other company equity", AccountCategoryType.Equity);
        var otherOpeningBalance = new OpeningBalance
        {
            PeriodId = otherPeriod.Id,
            AccountCategoryId = otherCategory.Id,
            Credit = 2_000m,
            SourceNote = "Other company opening balance",
            EnteredBy = "Other reviewer",
            Reviewed = true
        };
        var crossCompanyCategoryOpeningBalance = new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = otherCategory.Id,
            Credit = 3_000m,
            SourceNote = "Cross-company category fixture",
            EnteredBy = "Prior import",
            Reviewed = true
        };
        var otherNote = new NotesDisclosure
        {
            PeriodId = otherPeriod.Id,
            NoteNumber = 1,
            Title = "Other company note",
            Content = "Other disclosure",
            IsIncluded = true
        };
        db.OpeningBalances.Add(otherOpeningBalance);
        db.OpeningBalances.Add(crossCompanyCategoryOpeningBalance);
        db.NotesDisclosures.Add(otherNote);
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/opening-balances/{category.Id}");

        var upsertWrongPeriodResult = await YearEndEndpoints.UpsertOpeningBalanceEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            category.Id,
            new OpeningBalanceInput(0m, 1_000m, "Wrong period", null, true),
            db,
            audit,
            context);
        var upsertWrongCategoryResult = await YearEndEndpoints.UpsertOpeningBalanceEndpointAsync(
            period.CompanyId,
            period.Id,
            otherCategory.Id,
            new OpeningBalanceInput(0m, 1_000m, "Wrong category", null, true),
            db,
            audit,
            context);
        var deleteWrongPeriodResult = await YearEndEndpoints.DeleteOpeningBalanceEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            otherCategory.Id,
            db,
            audit,
            context);
        var deleteWrongCategoryResult = await YearEndEndpoints.DeleteOpeningBalanceEndpointAsync(
            period.CompanyId,
            period.Id,
            otherCategory.Id,
            db,
            audit,
            context);
        var createNoteWrongPeriodResult = await YearEndEndpoints.CreateNoteEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new NotesDisclosure { Title = "Wrong company note", Content = "Should not persist", IsIncluded = true },
            db,
            audit,
            context);
        var deleteNoteWrongPeriodResult = await YearEndEndpoints.DeleteNoteEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            otherNote.Id,
            db,
            audit,
            context);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(upsertWrongPeriodResult).StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(upsertWrongCategoryResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteWrongPeriodResult).StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteWrongCategoryResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(createNoteWrongPeriodResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteNoteWrongPeriodResult).StatusCode);
        var remainingPeriodBalance = await db.OpeningBalances.SingleAsync(o => o.PeriodId == period.Id);
        Assert.Equal(crossCompanyCategoryOpeningBalance.Id, remainingPeriodBalance.Id);
        Assert.Equal(3_000m, remainingPeriodBalance.Credit);
        Assert.Equal("Cross-company category fixture", remainingPeriodBalance.SourceNote);
        var remainingOpeningBalance = await db.OpeningBalances.SingleAsync(o => o.PeriodId == otherPeriod.Id);
        Assert.Equal(otherOpeningBalance.Id, remainingOpeningBalance.Id);
        Assert.Equal(2_000m, remainingOpeningBalance.Credit);
        Assert.Equal("Other company opening balance", remainingOpeningBalance.SourceNote);
        Assert.Empty(await db.NotesDisclosures.Where(n => n.PeriodId == period.Id).ToListAsync());
        var remainingNote = await db.NotesDisclosures.SingleAsync(n => n.PeriodId == otherPeriod.Id);
        Assert.Equal(otherNote.Id, remainingNote.Id);
        Assert.Equal("Other company note", remainingNote.Title);

        var entityTypes = (await db.AuditLogs.ToListAsync()).Select(a => a.EntityType).ToArray();
        Assert.DoesNotContain("OpeningBalance", entityTypes);
        Assert.DoesNotContain("NotesDisclosure", entityTypes);
    }

    [Fact]
    public async Task YearEndEvidenceNotes_RejectWrongPeriodAndProtectCustomNotes()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var requiredNote = new NotesDisclosure
        {
            PeriodId = period.Id,
            NoteNumber = 1,
            Title = "Accounting policies",
            Content = "Required generated note.",
            IsRequired = true,
            IsIncluded = true
        };
        var customNote = new NotesDisclosure
        {
            PeriodId = period.Id,
            NoteNumber = 9,
            Title = "Custom covenant note",
            Content = "Custom disclosure should survive regeneration.",
            IsRequired = false,
            IsIncluded = true
        };
        var otherNote = new NotesDisclosure
        {
            PeriodId = otherPeriod.Id,
            NoteNumber = 1,
            Title = "Other company note",
            Content = "Do not mutate",
            IsIncluded = true
        };
        db.NotesDisclosures.AddRange(requiredNote, customNote, otherNote);
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var context = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/notes/generate");
        var notes = new NotesDisclosureService(db);

        var deleteRequiredResult = await YearEndEndpoints.DeleteNoteEndpointAsync(
            period.CompanyId,
            period.Id,
            requiredNote.Id,
            db,
            audit,
            context);
        var generateWrongPeriodResult = await YearEndEndpoints.GenerateNotesEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            notes,
            db,
            audit,
            context);
        var updateWrongPeriodResult = await YearEndEndpoints.UpdateNoteEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            otherNote.Id,
            new NotesDisclosure { Title = "Wrong company update", Content = "Mutated", IsIncluded = false },
            db,
            audit,
            context);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(deleteRequiredResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(generateWrongPeriodResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(updateWrongPeriodResult).StatusCode);
        Assert.NotNull(await db.NotesDisclosures.FindAsync(requiredNote.Id));
        var unchangedOtherNote = await db.NotesDisclosures.SingleAsync(n => n.Id == otherNote.Id);
        Assert.Equal("Other company note", unchangedOtherNote.Title);
        Assert.True(unchangedOtherNote.IsIncluded);

        await YearEndEndpoints.GenerateNotesEndpointAsync(period.CompanyId, period.Id, notes, db, audit, context);

        var remainingCustomNote = await db.NotesDisclosures.SingleAsync(n => n.Id == customNote.Id);
        Assert.Equal("Custom covenant note", remainingCustomNote.Title);
        Assert.False(remainingCustomNote.IsRequired);
        Assert.True(remainingCustomNote.NoteNumber > 1);
        Assert.Contains(await db.NotesDisclosures.Where(n => n.PeriodId == period.Id).ToListAsync(), n =>
            n.IsRequired && n.Title == "Accounting Policies");
    }

    [Fact]
    public async Task NotesDisclosureService_RejectsMismatchedCompanyPeriodBeforeMutating()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherRequired = new NotesDisclosure
        {
            PeriodId = otherPeriod.Id,
            NoteNumber = 1,
            Title = "Other generated note",
            Content = "Do not remove",
            IsRequired = true,
            IsIncluded = true
        };
        var otherCustom = new NotesDisclosure
        {
            PeriodId = otherPeriod.Id,
            NoteNumber = 2,
            Title = "Other custom note",
            Content = "Do not renumber",
            IsRequired = false,
            IsIncluded = true
        };
        db.NotesDisclosures.AddRange(otherRequired, otherCustom);
        await db.SaveChangesAsync();
        var service = new NotesDisclosureService(db);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.GenerateNotesAsync(period.CompanyId, otherPeriod.Id));

        var unchanged = await db.NotesDisclosures
            .Where(n => n.PeriodId == otherPeriod.Id)
            .OrderBy(n => n.NoteNumber)
            .ToListAsync();
        Assert.Equal(["Other generated note", "Other custom note"], unchanged.Select(n => n.Title).ToArray());
        Assert.Equal([1, 2], unchanged.Select(n => n.NoteNumber).ToArray());
    }

    [Fact]
    public void NotesDisclosureService_RequiresCompanyIdForGeneration()
    {
        var methods = typeof(NotesDisclosureService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name == nameof(NotesDisclosureService.GenerateNotesAsync))
            .Select(m => m.GetParameters().Select(p => p.ParameterType).ToArray())
            .ToList();

        Assert.Contains(methods, parameters =>
            parameters.Length == 2
            && parameters[0] == typeof(int)
            && parameters[1] == typeof(int));
        Assert.DoesNotContain(methods, parameters =>
            parameters.Length == 1
            && parameters[0] == typeof(int));
    }

    [Fact]
    public void NotesEndpoints_UseCompanyAwareGenerationAndReads()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "YearEndEndpoints.cs"));

        Assert.Contains("GenerateNotesAsync(companyId, periodId)", source);
        Assert.DoesNotContain("GenerateNotesAsync(periodId)", source);
        Assert.Contains("p.Id == periodId && p.CompanyId == companyId", source);
    }

    [Fact]
    public void YearEndPeriodReadEndpoints_UseExplicitPeriodOwnershipGuards()
    {
        var source = File
            .ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "YearEndEndpoints.cs"))
            .Replace("\r\n", "\n");

        var guardedListReads = new (string Marker, string[] Fragments)[]
        {
            ("debtors.MapGet", ["db.Debtors.Where(d => d.PeriodId == periodId)"]),
            ("creditors.MapGet", ["db.Creditors.Where(c => c.PeriodId == periodId)"]),
            ("inventory.MapGet", ["db.Inventories.Where(i => i.PeriodId == periodId)"]),
            ("loanSnapshots.MapGet", ["db.LoanBalanceSnapshots", ".Where(s => s.PeriodId == periodId && s.Loan.CompanyId == companyId)"]),
            ("dirLoans.MapGet", ["db.DirectorLoans", ".Include(d => d.Director)", ".Where(d => d.PeriodId == periodId && d.Director.CompanyId == companyId)"]),
            ("taxes.MapGet", ["db.TaxBalances.Where(t => t.PeriodId == periodId)"]),
            ("dividends.MapGet", ["db.Dividends.Where(d => d.PeriodId == periodId)"]),
            ("reviews.MapGet", ["db.YearEndReviewConfirmations", ".Where(r => r.PeriodId == periodId)"]),
            ("openingBalances.MapGet", ["db.OpeningBalances", ".Where(o => o.PeriodId == periodId)"]),
            ("pbse.MapGet", ["db.PostBalanceSheetEvents.Where(x => x.PeriodId == periodId)"]),
            ("rpt.MapGet", ["db.RelatedPartyTransactions.Where(x => x.PeriodId == periodId)"]),
            ("cl.MapGet", ["db.ContingentLiabilities.Where(x => x.PeriodId == periodId)"])
        };

        foreach (var (marker, fragments) in guardedListReads)
        {
            var snippet = EndpointSnippet(source, marker);
            Assert.Contains("ListPeriodOwnedRowsAsync", snippet);
            Assert.Contains("HttpContext context", snippet);
            Assert.Contains("context", snippet);
            Assert.DoesNotContain(".ToListAsync()", snippet);
            foreach (var fragment in fragments)
                Assert.Contains(fragment, snippet);
        }

        var payrollSnippet = EndpointSnippet(source, "payroll.MapGet");
        Assert.Contains("GetPeriodOwnedValueAsync", payrollSnippet);
        Assert.Contains("HttpContext context", payrollSnippet);
        Assert.Contains("db.PayrollSummaries.Where(p => p.PeriodId == periodId)", payrollSnippet);
        Assert.DoesNotContain("FirstOrDefaultAsync", payrollSnippet);

        var summarySnippet = BlockSnippet(source, "app.MapGet($\"{basePath}/year-end-summary\"", "}).WithTags(\"Year-End Summary\")");
        Assert.Contains("HttpContext context", summarySnippet);
        Assert.Contains("CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", summarySnippet);
        Assert.Contains("d.Director.CompanyId == companyId", summarySnippet);
        AssertOccursBefore(summarySnippet, "if (period == null) return Results.NotFound();", "db.Debtors.Where(d => d.PeriodId == periodId)");
        AssertOccursBefore(summarySnippet, "if (period == null) return Results.NotFound();", "db.Creditors.Where(c => c.PeriodId == periodId)");

        var listHelper = MethodSnippet(source, "private static async Task<IResult> ListPeriodOwnedRowsAsync");
        AssertHelperChecksDirectPeriodAccessBeforeMaterializing(listHelper, "query.ToListAsync()");

        var valueHelper = MethodSnippet(source, "private static async Task<IResult> GetPeriodOwnedValueAsync");
        AssertHelperChecksDirectPeriodAccessBeforeMaterializing(valueHelper, "query.FirstOrDefaultAsync()");

        var periodWriteHelper = MethodSnippet(source, "private static async Task<IResult?> RequirePeriodWriteAccessAsync");
        Assert.Contains("CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", periodWriteHelper);
        Assert.Contains("AuthorizeCurrentWriteRequest(context)", periodWriteHelper);
        Assert.Contains("PeriodStatus.Finalised or PeriodStatus.Filed", periodWriteHelper);
        AssertOccursBefore(periodWriteHelper, "CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", "AuthorizeCurrentWriteRequest(context)");
        AssertOccursBefore(periodWriteHelper, "AuthorizeCurrentWriteRequest(context)", "PeriodStatus.Finalised or PeriodStatus.Filed");

        var companyWriteHelper = MethodSnippet(source, "private static async Task<IResult?> RequireCompanyWriteAccessAsync");
        Assert.Contains("CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId)", companyWriteHelper);
        Assert.Contains("AuthorizeCurrentWriteRequest(context)", companyWriteHelper);

        var authorizationHelper = MethodSnippet(source, "private static IResult? AuthorizeCurrentWriteRequest");
        Assert.Contains("EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", authorizationHelper);
        Assert.Contains("RoleAuthorizationService.Authorize(user, context.Request.Path, context.Request.Method)", authorizationHelper);

        static string EndpointSnippet(string source, string marker)
        {
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Expected to find endpoint marker {marker}.");
            var end = source.IndexOf("\n\n", start, StringComparison.Ordinal);
            return end > start ? source[start..end] : source[start..];
        }

        static string BlockSnippet(string source, string startMarker, string endMarker)
        {
            var start = source.IndexOf(startMarker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Expected to find block start marker {startMarker}.");
            var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
            Assert.True(end > start, $"Expected to find block end marker {endMarker}.");
            return source[start..end];
        }

        static string MethodSnippet(string source, string marker)
        {
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Expected to find method marker {marker}.");
            var end = source.IndexOf("\n    private static ", start + marker.Length, StringComparison.Ordinal);
            return end > start ? source[start..end] : source[start..];
        }

        static void AssertHelperChecksDirectPeriodAccessBeforeMaterializing(string snippet, string materializer)
        {
            var ownershipCheck = snippet.IndexOf("CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", StringComparison.Ordinal);
            var materialization = snippet.IndexOf(materializer, StringComparison.Ordinal);
            Assert.True(ownershipCheck >= 0, "Expected helper to check direct period access.");
            Assert.True(materialization >= 0, $"Expected helper to materialize via {materializer}.");
            Assert.True(ownershipCheck < materialization, "Expected direct period access before query materialization.");
        }

        static void AssertOccursBefore(string snippet, string first, string second)
        {
            var firstIndex = snippet.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = snippet.IndexOf(second, StringComparison.Ordinal);
            Assert.True(firstIndex >= 0, $"Expected to find {first}.");
            Assert.True(secondIndex >= 0, $"Expected to find {second}.");
            Assert.True(firstIndex < secondIndex, $"Expected {first} before {second}.");
        }
    }

    [Fact]
    public async Task YearEndRoutes_EnforceRuntimeCompanyPeriodRoleApiAndLockGuards()
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
                },
                new ApiAccessKeyConfig
                {
                    Name = "Admin",
                    Role = "Admin",
                    DevelopmentKey = "admin",
                    AllowedCompanyIds = []
                }
            ];
        });
        builder.Services.AddScoped<ApiAccessService>();
        builder.Services.AddScoped<AccountingWriteGuard>();
        builder.Services.AddScoped<AuditService>();
        builder.Services.AddScoped<FinancialStatementsService>();
        builder.Services.AddScoped<DirectorLoanComplianceService>();
        builder.Services.AddScoped<NotesDisclosureService>();

        await using var app = builder.Build();
        int tenantId;
        int allowedCompanyId;
        int restrictedCompanyId;
        int lockedCompanyId;
        int allowedPeriodId;
        int restrictedPeriodId;
        int lockedPeriodId;
        int deleteDebtorId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var tenant = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
            var allowedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Allowed Client Limited");
            var lockedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Locked Client Limited");
            var restrictedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Restricted Client Limited");
            var allowedPeriod = new AccountingPeriod
            {
                CompanyId = allowedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true
            };
            var lockedPeriod = new AccountingPeriod
            {
                CompanyId = lockedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true,
                Status = PeriodStatus.Finalised,
                LockedAt = DateTime.UtcNow,
                LockedBy = "Reviewer"
            };
            var restrictedPeriod = new AccountingPeriod
            {
                CompanyId = restrictedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true
            };
            db.AccountingPeriods.AddRange(allowedPeriod, lockedPeriod, restrictedPeriod);
            await db.SaveChangesAsync();
            var deleteDebtor = new Debtor
            {
                PeriodId = allowedPeriod.Id,
                Name = "Delete debtor",
                Amount = 75m,
                Type = DebtorType.Trade
            };
            db.Debtors.AddRange(
                deleteDebtor,
                new Debtor
                {
                    PeriodId = restrictedPeriod.Id,
                    Name = "Hidden debtor",
                    Amount = 900m,
                    Type = DebtorType.Other
                });
            db.FixedAssets.Add(new FixedAsset
            {
                CompanyId = restrictedCompany.Id,
                Name = "Hidden laptop",
                Category = "Computer Equipment",
                Cost = 1_500m,
                AcquisitionDate = restrictedPeriod.PeriodStart,
                UsefulLifeYears = 3,
                DepreciationMethod = DepreciationMethod.StraightLine
            });
            await db.SaveChangesAsync();

            tenantId = tenant.Id;
            allowedCompanyId = allowedCompany.Id;
            restrictedCompanyId = restrictedCompany.Id;
            lockedCompanyId = lockedCompany.Id;
            allowedPeriodId = allowedPeriod.Id;
            restrictedPeriodId = restrictedPeriod.Id;
            lockedPeriodId = lockedPeriod.Id;
            deleteDebtorId = deleteDebtor.Id;
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
                AllowedCompanyIds: new HashSet<int> { allowedCompanyId, lockedCompanyId });
            await next();
        });
        app.MapYearEndEndpoints();
        await app.StartAsync();

        try
        {
            var addresses = app.Services
                .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features
                .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(addresses!.Addresses)) };
            var debtorBody = new
            {
                name = "Runtime debtor",
                amount = 100m,
                type = DebtorType.Trade,
                notes = "Invoice support"
            };
            var reviewBody = new
            {
                confirmed = true,
                note = "Reviewed by file reviewer."
            };

            using var hiddenDebtorsRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/periods/{restrictedPeriodId}/debtors/",
                role: "Client");
            var hiddenDebtors = await client.SendAsync(hiddenDebtorsRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenDebtors.StatusCode);

            using var hiddenAssetsRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/fixed-assets/",
                role: "Client");
            var hiddenAssets = await client.SendAsync(hiddenAssetsRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenAssets.StatusCode);

            using var hiddenLoansRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/loans/",
                role: "Client");
            var hiddenLoans = await client.SendAsync(hiddenLoansRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenLoans.StatusCode);

            using var hiddenSharesRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/share-capital/",
                role: "Client");
            var hiddenShares = await client.SendAsync(hiddenSharesRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenShares.StatusCode);

            using var hiddenSummaryRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/periods/{restrictedPeriodId}/year-end-summary",
                role: "Client");
            var hiddenSummary = await client.SendAsync(hiddenSummaryRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenSummary.StatusCode);

            using var hiddenNotesRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/periods/{restrictedPeriodId}/notes/",
                role: "Client");
            var hiddenNotes = await client.SendAsync(hiddenNotesRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenNotes.StatusCode);

            using var mismatchedVisibleCompanyPeriodRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{allowedCompanyId}/periods/{restrictedPeriodId}/going-concern/",
                role: "Client");
            var mismatchedVisibleCompanyPeriod = await client.SendAsync(mismatchedVisibleCompanyPeriodRequest);
            Assert.Equal(HttpStatusCode.NotFound, mismatchedVisibleCompanyPeriod.StatusCode);

            using var hiddenComplianceRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/periods/{restrictedPeriodId}/director-loans/compliance",
                role: "Client");
            var hiddenCompliance = await client.SendAsync(hiddenComplianceRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenCompliance.StatusCode);

            using var hiddenSection307Request = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/periods/{restrictedPeriodId}/director-loans/section-307-note",
                role: "Client");
            var hiddenSection307 = await client.SendAsync(hiddenSection307Request);
            Assert.Equal(HttpStatusCode.NotFound, hiddenSection307.StatusCode);

            using var hiddenWriterCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{restrictedCompanyId}/periods/{restrictedPeriodId}/debtors/",
                role: "Client",
                apiKey: "writer",
                body: debtorBody);
            var hiddenWriterCreateDenied = await client.SendAsync(hiddenWriterCreate);
            Assert.Equal(HttpStatusCode.NotFound, hiddenWriterCreateDenied.StatusCode);

            using var reviewerCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/debtors/",
                role: "Reviewer",
                apiKey: "writer",
                body: debtorBody);
            var reviewerCreateDenied = await client.SendAsync(reviewerCreate);
            Assert.Equal(HttpStatusCode.Forbidden, reviewerCreateDenied.StatusCode);

            using var readerCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/debtors/",
                role: "Accountant",
                apiKey: "reader",
                body: debtorBody);
            var readerCreateDenied = await client.SendAsync(readerCreate);
            Assert.Equal(HttpStatusCode.Unauthorized, readerCreateDenied.StatusCode);

            using var writerCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/debtors/",
                role: "Accountant",
                apiKey: "writer",
                body: debtorBody);
            var writerCreateAllowed = await client.SendAsync(writerCreate);
            Assert.Equal(HttpStatusCode.Created, writerCreateAllowed.StatusCode);

            using var accountantReview = Request(
                HttpMethod.Put,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/year-end-reviews/tax",
                role: "Accountant",
                apiKey: "writer",
                body: reviewBody);
            var accountantReviewDenied = await client.SendAsync(accountantReview);
            Assert.Equal(HttpStatusCode.Forbidden, accountantReviewDenied.StatusCode);

            using var readerReviewerReview = Request(
                HttpMethod.Put,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/year-end-reviews/tax",
                role: "Reviewer",
                apiKey: "reader",
                body: reviewBody);
            var readerReviewerReviewDenied = await client.SendAsync(readerReviewerReview);
            Assert.Equal(HttpStatusCode.Unauthorized, readerReviewerReviewDenied.StatusCode);

            using var reviewerReview = Request(
                HttpMethod.Put,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/year-end-reviews/tax",
                role: "Reviewer",
                apiKey: "writer",
                body: reviewBody);
            var reviewerReviewAllowed = await client.SendAsync(reviewerReview);
            Assert.Equal(HttpStatusCode.OK, reviewerReviewAllowed.StatusCode);

            using var lockedCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{lockedCompanyId}/periods/{lockedPeriodId}/debtors/",
                role: "Accountant",
                apiKey: "writer",
                body: debtorBody);
            var lockedCreateDenied = await client.SendAsync(lockedCreate);
            Assert.Equal(HttpStatusCode.Conflict, lockedCreateDenied.StatusCode);

            using var lockedReview = Request(
                HttpMethod.Put,
                $"/api/companies/{lockedCompanyId}/periods/{lockedPeriodId}/year-end-reviews/tax",
                role: "Reviewer",
                apiKey: "writer",
                body: reviewBody);
            var lockedReviewDenied = await client.SendAsync(lockedReview);
            Assert.Equal(HttpStatusCode.Conflict, lockedReviewDenied.StatusCode);

            using var writerDelete = Request(
                HttpMethod.Delete,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/debtors/{deleteDebtorId}",
                role: "Accountant",
                apiKey: "writer");
            var writerDeleteDenied = await client.SendAsync(writerDelete);
            Assert.Equal(HttpStatusCode.Unauthorized, writerDeleteDenied.StatusCode);

            using var adminDelete = Request(
                HttpMethod.Delete,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/debtors/{deleteDebtorId}",
                role: "Accountant",
                apiKey: "admin");
            var adminDeleteAllowed = await client.SendAsync(adminDelete);
            Assert.Equal(HttpStatusCode.NoContent, adminDeleteAllowed.StatusCode);

            await using var verifyScope = app.Services.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            Assert.Single(await verifyDb.Debtors.Where(d => d.PeriodId == allowedPeriodId).ToListAsync());
            Assert.Single(await verifyDb.Debtors.Where(d => d.PeriodId == restrictedPeriodId).ToListAsync());
            Assert.False(await verifyDb.Debtors.AnyAsync(d => d.PeriodId == lockedPeriodId));
            var review = await verifyDb.YearEndReviewConfirmations.SingleAsync(r => r.PeriodId == allowedPeriodId && r.SectionKey == "tax");
            Assert.True(review.Confirmed);
            Assert.Equal(2, await verifyDb.AuditLogs.CountAsync(a => a.EntityType == "Debtor"));
            Assert.Single(await verifyDb.AuditLogs.Where(a => a.EntityType == "YearEndReviewConfirmation").ToListAsync());
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
    public void AuthenticatedIdentity_UsesPrincipalEmailForAuditUserId()
    {
        var reviewer = new AuthenticatedUser(7, 1, "Firm A", "Reviewer@Example.IE", "Maeve Reviewer", "Reviewer");

        Assert.Equal("reviewer@example.ie", AuthenticatedIdentity.AuditUserId(reviewer));
    }

    [Fact]
    public void AuthenticatedIdentity_UsesEmailForBlankReviewerDisplayName()
    {
        var reviewer = new AuthenticatedUser(7, 1, "Firm A", " reviewer@example.ie ", "   ", "Reviewer");

        Assert.Equal("reviewer@example.ie", AuthenticatedIdentity.ReviewerDisplayName(reviewer));
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
        builder.Services.AddScoped<AuditService>();
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
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Tests", "AccountsWorkflowTests.cs"));

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
    public void KestrelIntegrationTests_DoNotReserveLoopbackPortsBeforeBinding()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Tests", "AccountsWorkflowTests.cs"));

        Assert.DoesNotContain("Get" + "FreeLoopbackPort", source);
        Assert.DoesNotContain("Tcp" + "Listener", source);
        Assert.DoesNotContain("UseUrls($\"http://127.0.0.1:{" + "port}\")", source);
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
        });
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IPasswordVerifier, Pbkdf2PasswordVerifier>();
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<AuditService>();

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
                Assert.Equal("owner@example.ie", entry.UserId);
                Assert.Equal("Owner User", entry.ActorDisplayName);
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
        builder.Services.Configure<AuthSessionConfig>(config => config.SigningKey = StrongSessionSigningKey());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IPasswordVerifier, Pbkdf2PasswordVerifier>();
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<AuditService>();

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
            Assert.Equal("owner@example.ie", lockoutAudit.UserId);
            Assert.Equal("Owner User", lockoutAudit.ActorDisplayName);
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
        builder.Services.Configure<AuthSessionConfig>(config => config.SigningKey = StrongSessionSigningKey());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IPasswordVerifier, Pbkdf2PasswordVerifier>();
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<AuditService>();

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
    public void AuthEndpoints_WriteAuthAuditEventsDurablyAfterCommittedAuthStateChanges()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "AuthEndpoints.cs"));

        Assert.DoesNotContain("cancellationToken: context.RequestAborted", source);
        Assert.True(
            Regex.Matches(source, "durableAudit:\\s*true", RegexOptions.None, TimeSpan.FromSeconds(1)).Count >= 4,
            "Auth audit writes should be durable so a client disconnect cannot cancel evidence after auth state has changed.");
    }

    [Fact]
    public void DevelopmentConfig_ProvidesStrongSessionSigningKeyWithoutBaseDefault()
    {
        var apiPath = Path.Combine(RepositoryRoot(), "backend", "Accounts.Api");
        var baseConfig = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(apiPath, "appsettings.json"), optional: false)
            .Build();
        var developmentConfig = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(apiPath, "appsettings.json"), optional: false)
            .AddJsonFile(Path.Combine(apiPath, "appsettings.Development.json"), optional: false)
            .Build();

        Assert.True(string.IsNullOrWhiteSpace(baseConfig["AuthSession:SigningKey"]));
        Assert.True(AuthSessionKey.HasStrongKey(developmentConfig["AuthSession:SigningKey"]));
    }

    [Fact]
    public void FrontendStartScript_UsesStandaloneServerWhenNextOutputStandalone()
    {
        var packageJsonPath = Path.Combine(RepositoryRoot(), "frontend", "package.json");
        using var packageJson = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
        var startScript = packageJson.RootElement.GetProperty("scripts").GetProperty("start").GetString();
        var nextConfig = File.ReadAllText(Path.Combine(RepositoryRoot(), "frontend", "next.config.ts"));

        Assert.Contains("output: \"standalone\"", nextConfig);
        Assert.Equal("node .next/standalone/server.js", startScript);
    }

    [Fact]
    public void FrontendBuild_UsesStandardDistDirWithOptInWorkerThreadFallback()
    {
        var nextConfig = File.ReadAllText(Path.Combine(RepositoryRoot(), "frontend", "next.config.ts"));
        var frontendGitignore = File.ReadAllText(Path.Combine(RepositoryRoot(), "frontend", ".gitignore"));
        var eslintConfig = File.ReadAllText(Path.Combine(RepositoryRoot(), "frontend", "eslint.config.mjs"));
        var tsconfig = File.ReadAllText(Path.Combine(RepositoryRoot(), "frontend", "tsconfig.json"));
        var runbook = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "operations", "production-runbook.md"));

        Assert.Contains("NEXT_BUILD_WORKER_THREADS", nextConfig);
        Assert.Contains("workerThreads: true", nextConfig);
        Assert.DoesNotContain("distDir:", nextConfig);
        Assert.DoesNotContain("NEXT_DIST_DIR", nextConfig);
        Assert.Contains("/.next-probe/", frontendGitignore);
        Assert.Contains("/.tmp-*/", frontendGitignore);
        Assert.Contains("\".next-probe/**\"", eslintConfig);
        Assert.Contains("\".tmp-*/**\"", eslintConfig);
        Assert.Contains("\".next-probe/**\"", tsconfig);
        Assert.Contains("\".tmp-*/**\"", tsconfig);
        Assert.Contains("NEXT_TURBOPACK_USE_WORKER", runbook);
        Assert.Contains("NEXT_BUILD_WORKER_THREADS", runbook);
        Assert.Contains("Do not commit a custom `distDir`", runbook);
    }

    [Fact]
    public void FrontendPackageJson_PinsDirectDependenciesToLockedVersions()
    {
        var packageJsonPath = Path.Combine(RepositoryRoot(), "frontend", "package.json");
        var packageLockPath = Path.Combine(RepositoryRoot(), "frontend", "package-lock.json");
        using var packageJson = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
        using var packageLock = JsonDocument.Parse(File.ReadAllText(packageLockPath));
        var lockRoot = packageLock.RootElement.GetProperty("packages").GetProperty("");

        foreach (var section in new[] { "dependencies", "devDependencies" })
        {
            var packageSection = packageJson.RootElement.GetProperty(section);
            var lockSection = lockRoot.GetProperty(section);

            foreach (var dependency in packageSection.EnumerateObject())
            {
                var requestedVersion = dependency.Value.GetString();

                Assert.Matches(@"^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$", requestedVersion);
                Assert.Equal(requestedVersion, lockSection.GetProperty(dependency.Name).GetString());
            }
        }
    }

    [Fact]
    public void FrontendProxy_HasTypedUpstreamFailureEnvelopeAndTimeout()
    {
        var proxyRoute = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "frontend",
            "src",
            "app",
            "api",
            "[...path]",
            "route.ts"));

        Assert.Contains("API_PROXY_TIMEOUT_MS", proxyRoute);
        Assert.Contains("UPSTREAM_TIMEOUT_MS", proxyRoute);
        Assert.Contains("process.env.API_PROXY_TIMEOUT_MS ?? \"15000\"", proxyRoute);
        Assert.Contains("? UPSTREAM_TIMEOUT_MS : 15000", proxyRoute);
        Assert.Contains("AbortSignal.timeout", proxyRoute);
        Assert.Contains("upstream_unavailable", proxyRoute);
        Assert.Contains("NextResponse.json", proxyRoute);
        Assert.Contains("status: 502", proxyRoute);
    }

    [Fact]
    public void FrontendProxy_FailsFastWhenProductionApiKeyIsMissing()
    {
        var proxyRoute = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "frontend",
            "src",
            "app",
            "api",
            "[...path]",
            "route.ts"));

        Assert.Contains("ACCOUNTS_API_KEY is required for the frontend API proxy outside development.", proxyRoute);
        Assert.Contains("requireApiKey()", proxyRoute);
        Assert.Contains("const isDevelopmentRuntime = process.env.NODE_ENV === \"development\"", proxyRoute);
        Assert.Contains("if (!isDevelopmentRuntime && !configuredApiKey)", proxyRoute);
    }

    [Fact]
    public void FrontendProxy_UsesServerOnlyApiUrlAndRejectsInvalidSchemes()
    {
        var proxyRoute = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "frontend",
            "src",
            "app",
            "api",
            "[...path]",
            "route.ts"));

        Assert.Contains("const apiUrl = process.env.API_URL;", proxyRoute);
        Assert.DoesNotContain("NEXT_PUBLIC_API_URL", proxyRoute);
        Assert.Contains("parsedApiUrl.protocol !== \"http:\" && parsedApiUrl.protocol !== \"https:\"", proxyRoute);
        Assert.Contains("API_URL must use http or https for the frontend API proxy.", proxyRoute);
    }

    [Fact]
    public void FrontendProxy_UsesLongerTimeoutForDocumentAndIxbrlGeneration()
    {
        var proxyRoute = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "frontend",
            "src",
            "app",
            "api",
            "[...path]",
            "route.ts"));

        Assert.Contains("API_PROXY_DOCUMENT_TIMEOUT_MS", proxyRoute);
        Assert.Contains("DOCUMENT_TIMEOUT_MS", proxyRoute);
        Assert.Contains("process.env.API_PROXY_DOCUMENT_TIMEOUT_MS ?? \"120000\"", proxyRoute);
        Assert.Contains("? DOCUMENT_TIMEOUT_MS : 120000", proxyRoute);
        Assert.Contains("documentGenerationTimeoutMs()", proxyRoute);
        Assert.Contains("isDocumentGenerationPath(path)", proxyRoute);
        Assert.Contains("pathMatchesCompanyPeriodPrefix(segments)", proxyRoute);
        Assert.Contains("segments[4] === \"documents\"", proxyRoute);
        Assert.Contains("segments[4] === \"revenue\" && segments[5] === \"ixbrl\"", proxyRoute);
        Assert.DoesNotContain("includes(\"/documents/\")", proxyRoute);
        Assert.Contains("proxyTimeoutMs(path)", proxyRoute);
        Assert.Contains("AbortSignal.timeout(proxyTimeoutMs(path))", proxyRoute);
    }

    [Fact]
    public void FrontendProxy_StreamsUnsafeBodiesAndEnforcesSizeLimit()
    {
        var root = RepositoryRoot();
        var proxyRoute = File.ReadAllText(Path.Combine(
            root,
            "frontend",
            "src",
            "app",
            "api",
            "[...path]",
            "route.ts"));
        var requestHelper = File.ReadAllText(Path.Combine(
            root,
            "frontend",
            "src",
            "lib",
            "apiProxyRequest.ts"));

        Assert.Contains("API_PROXY_MAX_BODY_BYTES", proxyRoute);
        Assert.Contains("MAX_PROXY_BODY_BYTES", proxyRoute);
        Assert.Contains("content-length", proxyRoute);
        Assert.Contains("payload_too_large", proxyRoute);
        Assert.Contains("status: 413", proxyRoute);
        Assert.Contains("error.cause instanceof PayloadTooLargeError", proxyRoute);
        Assert.Contains("TRUST_PROXY_HEADERS", proxyRoute);
        Assert.Contains("buildApiProxyRequestHeaders(request.headers", proxyRoute);
        Assert.Contains("requestProtocol: request.nextUrl.protocol", proxyRoute);
        Assert.Contains("export function buildApiProxyRequestHeaders", requestHelper);
        Assert.Contains("configuredApiKeyHeaderName", requestHelper);
        Assert.Contains("ApiProxyRequestConfigurationError", requestHelper);
        Assert.Contains("ACCOUNTS_API_KEY_HEADER must be a valid HTTP header name.", requestHelper);
        Assert.Contains("ACCOUNTS_API_KEY_HEADER must be an end-to-end HTTP header name.", requestHelper);
        Assert.Contains("headers.delete(defaultApiKeyHeader)", requestHelper);
        Assert.Contains("headers.delete(apiKeyHeader)", requestHelper);
        Assert.Contains("export function deleteHopByHopHeaders", requestHelper);
        Assert.Contains("headers.get(\"Connection\")", requestHelper);
        Assert.Contains("headers.delete(header)", requestHelper);
        Assert.Contains("function deleteForwardedHeaders", requestHelper);
        Assert.Contains("header.toLowerCase().startsWith(\"x-forwarded-\")", requestHelper);
        Assert.Contains("headers.delete(\"Forwarded\")", requestHelper);
        Assert.Contains("headers.delete(\"X-Real-IP\")", requestHelper);
        Assert.Contains("sourceHeaders.get(\"x-forwarded-proto\")", requestHelper);
        Assert.Contains("options.requestProtocol.replace(\":\", \"\")", requestHelper);
        Assert.Contains("headers.set(\"X-Forwarded-Proto\", forwardedProto)", requestHelper);
        Assert.DoesNotContain("headers.set(\"X-Forwarded-Proto\", request.nextUrl.protocol", requestHelper);
        Assert.Contains("request.body", proxyRoute);
        Assert.Contains("duplex: \"half\"", proxyRoute);
        Assert.DoesNotContain("await request.arrayBuffer()", proxyRoute);
    }

    [Fact]
    public void FrontendProxy_HidesProductionConfigurationDetailsFromClients()
    {
        var proxyRoute = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "frontend",
            "src",
            "app",
            "api",
            "[...path]",
            "route.ts"));

        Assert.Contains("proxyConfigurationMessage(error)", proxyRoute);
        Assert.Contains("isDevelopmentRuntime", proxyRoute);
        Assert.Contains("The frontend API proxy is unavailable.", proxyRoute);
        Assert.Contains("console.error(\"Frontend API proxy configuration error\", error)", proxyRoute);
        Assert.DoesNotContain("message: error.message", proxyRoute);
    }

    [Fact]
    public void FrontendDocumentDownloads_UseAnchorAndDoNotWindowOpenBlob()
    {
        var periodPage = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "frontend",
            "src",
            "app",
            "companies",
            "[companyId]",
            "periods",
            "[periodId]",
            "page.tsx"));
        var apiClient = File.ReadAllText(Path.Combine(RepositoryRoot(), "frontend", "src", "lib", "api.ts"));

        Assert.Contains("document.createElement(\"a\")", periodPage);
        Assert.Contains("anchor.click()", periodPage);
        Assert.Contains("download =", periodPage);
        Assert.Contains("downloadDelivered", periodPage);
        Assert.Contains("extension = \"pdf\"", periodPage);
        Assert.Contains("fetchDocumentBlob(url, documentType ? \"POST\" : \"GET\")", periodPage);
        Assert.Contains("downloadDocument(croPackUrl, \"CRO filing pack\", \"accounts\", true)", periodPage);
        Assert.Contains("downloadDocument(sigPageUrl, \"signature page\", \"signature\", true)", periodPage);
        Assert.Contains("downloadDocument(ixbrlUrl, \"iXBRL filing\", undefined, false, \"xhtml\")", periodPage);
        Assert.DoesNotContain("window.open(objectUrl", periodPage);
        Assert.Contains("export async function fetchDocumentBlob", apiClient);
        Assert.Contains("headers: withCsrfHeader(method)", apiClient);
        Assert.Contains("return response.blob()", apiClient);
    }

    [Fact]
    public void FrontendHealth_IsLocalAndDoesNotBakeLocalhostApiFallback()
    {
        var nextConfig = File.ReadAllText(Path.Combine(RepositoryRoot(), "frontend", "next.config.ts"));
        var frontendDockerfile = File.ReadAllText(Path.Combine(RepositoryRoot(), "Dockerfile.frontend"));
        var healthRoutePath = Path.Combine(RepositoryRoot(), "frontend", "src", "app", "health", "route.ts");

        Assert.DoesNotContain("rewrites()", nextConfig);
        Assert.DoesNotContain("http://localhost:5090", nextConfig);
        Assert.DoesNotContain("ARG API_URL=http://localhost:5090", frontendDockerfile);
        Assert.True(File.Exists(healthRoutePath), "Frontend /health should be a local liveness route, not a build-time rewrite to the API.");

        var healthRoute = File.ReadAllText(healthRoutePath);
        Assert.Contains("NextResponse.json", healthRoute);
        Assert.Contains("alive", healthRoute);
    }

    [Fact]
    public void FrontendReadiness_ChecksBackendReadinessThroughRuntimeApiUrl()
    {
        var readyRoutePath = Path.Combine(RepositoryRoot(), "frontend", "src", "app", "health", "ready", "route.ts");
        var readyHelperPath = Path.Combine(RepositoryRoot(), "frontend", "src", "app", "health", "ready", "readiness.ts");

        Assert.True(File.Exists(readyRoutePath), "Frontend /health/ready should verify upstream API readiness.");
        Assert.True(File.Exists(readyHelperPath), "Frontend /health/ready should keep readiness decisions in a testable helper.");

        var readyRoute = File.ReadAllText(readyRoutePath) + File.ReadAllText(readyHelperPath);
        Assert.Contains("process.env.API_URL", readyRoute);
        Assert.Contains("readServerSecret(env, \"ACCOUNTS_API_KEY\")", readyRoute);
        Assert.Contains("process.env.ACCOUNTS_API_KEY_HEADER", readyRoute);
        Assert.Contains("function isDevelopmentRuntime", readyRoute);
        Assert.Contains("if (!isDevelopmentRuntime(env) && !configuredApiKey(env))", readyRoute);
        Assert.Contains("/health/ready", readyRoute);
        Assert.Contains("/api/companies", readyRoute);
        Assert.Contains("apiProxyAuthHeaders()", readyRoute);
        Assert.Contains("acceptsProxyAuthentication(proxyAuthResponse, proxyAuth)", readyRoute);
        Assert.Contains("Authentication required.", readyRoute);
        Assert.Contains("proxyAuth: \"rejected\"", readyRoute);
        Assert.Contains("proxyAuth: \"accepted\"", readyRoute);
        Assert.Contains("cache: \"no-store\"", readyRoute);
        Assert.Contains("AbortSignal.timeout", readyRoute);
        Assert.Contains("protocol !== \"http:\"", readyRoute);
        Assert.Contains("protocol !== \"https:\"", readyRoute);
        Assert.Contains("API_URL must use http or https for frontend readiness checks.", readyRoute);
        Assert.Contains("Status503", readyRoute);
        Assert.Contains("api_proxy_misconfigured", readyRoute);
        Assert.Contains("api_unavailable", readyRoute);
        Assert.DoesNotContain("localhost:5090", readyRoute);
    }

    [Fact]
    public void FrontendApiProxy_PreservesAuthCookiesWithoutForwardingUnsafeHeaders()
    {
        var proxyRoutePath = Path.Combine(
            RepositoryRoot(),
            "frontend",
            "src",
            "app",
            "api",
            "[...path]",
            "route.ts");
        var proxyHelperPath = Path.Combine(RepositoryRoot(), "frontend", "src", "lib", "apiProxyResponse.ts");

        var proxyRoute = File.ReadAllText(proxyRoutePath);
        var proxyHelper = File.ReadAllText(proxyHelperPath);

        Assert.Contains("allowSetCookieForProxyResponse", proxyRoute);
        Assert.Contains("allowSetCookie: allowSetCookieForProxyResponse(request.method, path, response.status)", proxyRoute);
        Assert.DoesNotContain("isAuthProxyPath", proxyRoute);
        Assert.Contains("export function allowSetCookieForProxyResponse", proxyHelper);
        Assert.Contains("options.allowSetCookie", proxyHelper);
        Assert.Contains("appendSetCookieHeaders", proxyHelper);
        Assert.Contains("getSetCookie", proxyHelper);
        Assert.Contains("const passThroughHeaders", proxyHelper);
        Assert.DoesNotContain("\"set-cookie\"", proxyHelper);
    }

    [Fact]
    public void DefaultCompose_ProvidesDevelopmentFrontendProxyKey()
    {
        var compose = File.ReadAllText(Path.Combine(RepositoryRoot(), "compose.yml"));

        Assert.Contains("ACCOUNTS_API_KEY: \"${ACCOUNTS_API_KEY:-accounts_dev_frontend_proxy}\"", compose);
        Assert.DoesNotContain("ACCOUNTS_API_KEY: \"${ACCOUNTS_API_KEY:-}\"", compose);
    }

    [Fact]
    public void FrontendNextConfig_DefinesProductionSecurityHeaders()
    {
        var nextConfig = File.ReadAllText(Path.Combine(RepositoryRoot(), "frontend", "next.config.ts"));

        Assert.Contains("async headers()", nextConfig);
        Assert.DoesNotContain("Content-Security-Policy", nextConfig);
        Assert.DoesNotContain("Strict-Transport-Security", nextConfig);
        Assert.DoesNotContain("NEXT_DIST_DIR", nextConfig);
        Assert.DoesNotContain("distDir", nextConfig);
        Assert.Contains("X-Frame-Options", nextConfig);
        Assert.Contains("DENY", nextConfig);
        Assert.Contains("X-Content-Type-Options", nextConfig);
        Assert.Contains("nosniff", nextConfig);
        Assert.Contains("Referrer-Policy", nextConfig);
        Assert.Contains("Permissions-Policy", nextConfig);
    }

    [Fact]
    public void FrontendProxy_DefinesNonceBasedCspAndExplicitHstsGate()
    {
        var proxyPath = Path.Combine(RepositoryRoot(), "frontend", "src", "proxy.ts");
        var securityHeadersPath = Path.Combine(RepositoryRoot(), "frontend", "src", "lib", "securityHeaders.ts");

        Assert.True(File.Exists(proxyPath), "Next proxy should generate per-request CSP nonces for App Router scripts.");
        var proxy = File.ReadAllText(proxyPath);
        var securityHeaders = File.ReadAllText(securityHeadersPath);

        Assert.Contains("Content-Security-Policy", proxy);
        Assert.Contains("crypto.randomUUID()", proxy);
        Assert.Contains("requestHeaders.set(\"x-nonce\", nonce)", proxy);
        Assert.Contains("const isDevelopmentRuntime = process.env.NODE_ENV === \"development\"", proxy);
        Assert.Contains("script-src 'self' 'nonce-${nonce}' 'strict-dynamic'", proxy);
        Assert.Contains("'unsafe-inline' 'unsafe-eval'", proxy);
        Assert.Contains("script-src-attr 'none'", proxy);
        Assert.Contains("frame-ancestors 'none'", proxy);
        Assert.Contains("upgrade-insecure-requests", proxy);
        Assert.Contains("process.env.ENABLE_HSTS === \"true\"", securityHeaders);
        Assert.Contains("enableStrictTransportSecurity", securityHeaders);
        Assert.Contains("Strict-Transport-Security", securityHeaders);
        Assert.Contains("max-age=31536000; includeSubDomains; preload", securityHeaders);
        Assert.Contains("withStrictTransportSecurity", proxy);
        Assert.Contains("matcher", proxy);
        Assert.DoesNotContain("process.env.NODE_ENV === \"production\"", proxy);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", proxy);
    }

    [Fact]
    public void SecurityHeaders_EmitCspOnApiAndHstsOverHttps()
    {
        // BL-31: the backend (not just the frontend proxy) now emits a Content-Security-Policy on
        // /api responses and HSTS over HTTPS, as defence in depth for direct API access.
        var apiHttps = new DefaultHttpContext();
        apiHttps.Request.Scheme = "https";
        apiHttps.Request.Path = "/api/companies";
        SecurityHeadersMiddleware.ApplyTo(apiHttps);
        Assert.Equal("default-src 'none'; frame-ancestors 'none'; base-uri 'none'", apiHttps.Response.Headers["Content-Security-Policy"].ToString());
        Assert.Contains("max-age=", apiHttps.Response.Headers["Strict-Transport-Security"].ToString());
        Assert.Equal("nosniff", apiHttps.Response.Headers["X-Content-Type-Options"].ToString());

        // Plain HTTP, non-/api: no HSTS (don't advertise over http) and no API CSP.
        var plain = new DefaultHttpContext();
        plain.Request.Scheme = "http";
        plain.Request.Path = "/health";
        SecurityHeadersMiddleware.ApplyTo(plain);
        Assert.False(plain.Response.Headers.ContainsKey("Strict-Transport-Security"));
        Assert.False(plain.Response.Headers.ContainsKey("Content-Security-Policy"));
    }

    [Fact]
    public void RateLimitClientKey_IgnoresForwardedForUnlessExplicitlyTrusted()
    {
        // BL-31: a spoofable X-Forwarded-For must not partition the rate limiter unless the deployment
        // has explicitly opted in (behind a trusted proxy); otherwise an attacker rotates XFF to evade it.
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");
        context.Request.Headers["X-Forwarded-For"] = "10.0.0.1, 198.51.100.9";

        Assert.Equal("203.0.113.7", RateLimitClientKey.FromHttpContext(context, trustForwardedFor: false));
        Assert.Equal("10.0.0.1", RateLimitClientKey.FromHttpContext(context, trustForwardedFor: true));
    }

    [Fact]
    public void ApiKeyRotationRunbook_DocumentsHashAndZeroDowntimeRotation()
    {
        // BL-30: ops need a documented rotation procedure for the single service API key.
        var runbookPath = Path.Combine(RepositoryRoot(), "Docs", "operations", "api-key-rotation.md");
        Assert.True(File.Exists(runbookPath), "API key rotation runbook should exist for operations (BL-30).");
        var runbook = File.ReadAllText(runbookPath);
        Assert.Contains("ACCOUNTS_API_KEY_HASH", runbook);
        Assert.Contains("ACCOUNTS_API_KEY_FILE", runbook);
        Assert.Contains("sha256", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ApiAccess__Keys", runbook);
        Assert.Contains("Rollback", runbook);
    }

    [Fact]
    public void FrontendRouteHandlers_ShareRuntimeHstsHeaderGate()
    {
        var root = RepositoryRoot();
        var securityHeadersPath = Path.Combine(root, "frontend", "src", "lib", "securityHeaders.ts");
        var proxyPath = Path.Combine(root, "frontend", "src", "proxy.ts");
        var proxyHelperPath = Path.Combine(root, "frontend", "src", "lib", "apiProxyResponse.ts");
        var apiRoutePath = Path.Combine(root, "frontend", "src", "app", "api", "[...path]", "route.ts");
        var readyRoutePath = Path.Combine(root, "frontend", "src", "app", "health", "ready", "route.ts");

        Assert.True(File.Exists(securityHeadersPath), "Runtime HSTS handling should live in one shared frontend helper.");

        var securityHeaders = File.ReadAllText(securityHeadersPath);
        var proxy = File.ReadAllText(proxyPath);
        var proxyHelper = File.ReadAllText(proxyHelperPath);
        var apiRoute = File.ReadAllText(apiRoutePath);
        var readyRoute = File.ReadAllText(readyRoutePath);

        Assert.Contains("process.env.ENABLE_HSTS === \"true\"", securityHeaders);
        Assert.Contains("max-age=31536000; includeSubDomains; preload", securityHeaders);
        Assert.Contains("withStrictTransportSecurity", securityHeaders);

        Assert.Contains("withStrictTransportSecurity", proxy);
        Assert.DoesNotContain("const enableStrictTransportSecurity", proxy);
        Assert.DoesNotContain("const strictTransportSecurity", proxy);

        Assert.Contains("withStrictTransportSecurity", proxyHelper);
        Assert.Contains("return withStrictTransportSecurity(responseForUpstream", proxyHelper);
        Assert.Contains("return withStrictTransportSecurity(Response.json", proxyHelper);

        Assert.Contains("withStrictTransportSecurity", apiRoute);
        Assert.Contains("return withStrictTransportSecurity(NextResponse.json", apiRoute);

        Assert.Contains("withStrictTransportSecurity", readyRoute);
        Assert.Contains("return withStrictTransportSecurity(NextResponse.json", readyRoute);
    }

    [Fact]
    public void FrontendThemeBootstrap_UsesExternalSameOriginScriptForCsp()
    {
        var layout = File.ReadAllText(Path.Combine(RepositoryRoot(), "frontend", "src", "app", "layout.tsx"));
        var themeScriptPath = Path.Combine(RepositoryRoot(), "frontend", "public", "theme-init.js");

        Assert.DoesNotContain("dangerouslySetInnerHTML", layout);
        Assert.DoesNotContain("localStorage.getItem('theme')", layout);
        Assert.Contains("src=\"/theme-init.js\"", layout);
        Assert.Contains("headers()", layout);
        Assert.Contains("nonce={nonce}", layout);
        Assert.True(File.Exists(themeScriptPath), "Theme bootstrap should be a same-origin static script so script-src can omit unsafe-inline.");

        var themeScript = File.ReadAllText(themeScriptPath);
        Assert.Contains("localStorage.getItem(\"theme\")", themeScript);
        Assert.Contains("document.documentElement.classList.add(\"dark\")", themeScript);
        Assert.DoesNotContain("<script", themeScript);
    }

    [Fact]
    public void FrontendUnsafeRequestsAttachCsrfTokenHeader()
    {
        var apiClient = File.ReadAllText(Path.Combine(RepositoryRoot(), "frontend", "src", "lib", "api.ts"));
        var authClient = File.ReadAllText(Path.Combine(RepositoryRoot(), "frontend", "src", "lib", "auth.ts"));

        Assert.Contains("ACCOUNTS_CSRF_COOKIE", apiClient);
        Assert.Contains("X-CSRF-Token", apiClient);
        Assert.Contains("withCsrfHeader", apiClient);
        Assert.Contains("method !== \"GET\"", apiClient);
        Assert.Contains("headers: withCsrfHeader", apiClient);
        Assert.Contains("X-CSRF-Token", authClient);
        Assert.Contains("readCsrfToken", authClient);
    }

    [Fact]
    public void FrontendApiClient_DoesNotRetryUnsafeWritesByDefault()
    {
        var apiClient = File.ReadAllText(Path.Combine(RepositoryRoot(), "frontend", "src", "lib", "api.ts"));

        Assert.Contains("const effectiveRetries = isUnsafeMethod(fetchOptions.method) ? 0 : retries;", apiClient);
        Assert.Contains("attempt <= effectiveRetries", apiClient);
        Assert.Contains("attempt < effectiveRetries", apiClient);
    }

    [Fact]
    public void FrontendBuild_HasCleanCopyVerifierWithoutChangingProductionDistDir()
    {
        var root = RepositoryRoot();
        var packageJson = File.ReadAllText(Path.Combine(root, "frontend", "package.json"));
        var nextConfig = File.ReadAllText(Path.Combine(root, "frontend", "next.config.ts"));
        var dockerfile = File.ReadAllText(Path.Combine(root, "Dockerfile.frontend"));
        var runbook = File.ReadAllText(Path.Combine(root, "Docs", "operations", "production-runbook.md"));
        var verifierPath = Path.Combine(root, "frontend", "scripts", "verify-clean-build.mjs");

        Assert.True(File.Exists(verifierPath), "Local Windows/Codex build verification should have a clean-copy build script.");
        var verifier = File.ReadAllText(verifierPath);

        Assert.Contains("\"build:clean\": \"node scripts/verify-clean-build.mjs\"", packageJson);
        Assert.DoesNotContain("distDir", nextConfig);
        Assert.Contains("RUN npm run build", dockerfile);
        Assert.Contains("COPY --from=builder --chown=nextjs:nodejs /app/.next/standalone ./", dockerfile);
        Assert.Contains("npm run build:clean", runbook);
        Assert.Contains("createHash", verifier);
        Assert.Contains("accounts-frontend-clean-build-", verifier);
        Assert.Contains("fs.cp", verifier);
        Assert.Contains("node_modules", verifier);
        Assert.Contains("cleanBuildRoot", verifier);
        Assert.Contains("copyProjectSource", verifier);
        Assert.Contains("copyDependencies", verifier);
        Assert.Contains("sourceNodeModules, targetNodeModules", verifier);
        Assert.Contains("NEXT_TURBOPACK_USE_WORKER", verifier);
        Assert.Contains("NEXT_BUILD_WORKER_THREADS", verifier);
        Assert.Contains(".next/standalone/server.js", verifier.Replace("\\", "/"));
    }

    [Fact]
    public void ProductionCompose_DisablesDevelopmentDefaultsAndDefinesHealthChecks()
    {
        var composePath = Path.Combine(RepositoryRoot(), "compose.production.yml");

        Assert.True(File.Exists(composePath), "compose.production.yml should provide a production-safe runtime entrypoint.");

        var compose = File.ReadAllText(composePath);
        Assert.Contains("ASPNETCORE_ENVIRONMENT: Production", compose);
        Assert.Contains("DatabaseStartup__AutoMigrateOnStartup: \"false\"", compose);
        Assert.Contains("DatabaseStartup__SeedDemoData: \"false\"", compose);
        Assert.Contains("ApiAccess__Enabled: \"true\"", compose);
        Assert.Contains("RateLimits__TrustForwardedFor: \"true\"", compose);
        Assert.Contains("ApiAccess__Keys__0__KeyHash: \"${ACCOUNTS_API_KEY_HASH:?set ACCOUNTS_API_KEY_HASH}\"", compose);
        Assert.Contains("ACCOUNTS_API_KEY_FILE: /run/secrets/accounts_api_key", compose);
        Assert.Contains("API_PROXY_MAX_BODY_BYTES: \"${API_PROXY_MAX_BODY_BYTES:-6291456}\"", compose);
        Assert.Contains("API_PROXY_DOCUMENT_TIMEOUT_MS: \"${API_PROXY_DOCUMENT_TIMEOUT_MS:-120000}\"", compose);
        Assert.Contains("TRUST_PROXY_HEADERS: \"${TRUST_PROXY_HEADERS:?set TRUST_PROXY_HEADERS", compose);
        Assert.Equal(3, Regex.Matches(compose, "^\\s+TRUST_PROXY_HEADERS:", RegexOptions.Multiline).Count);
        Assert.Contains("\"127.0.0.1:${FRONTEND_PORT:-3000}:3000\"", compose);
        Assert.Contains("AllowedHosts: \"${ACCOUNTS_ALLOWED_HOSTS:?set ACCOUNTS_ALLOWED_HOSTS};api\"", compose);
        Assert.Contains("curl --fail --header 'Host: api' http://localhost:8080/health/ready", compose);
        Assert.Contains("wget -qO- http://127.0.0.1:3000/health/ready", compose);
        Assert.DoesNotContain("wget -qO- http://localhost:3000/login", compose);
        Assert.Contains("condition: service_healthy", compose);
        Assert.DoesNotContain("accounts_dev", compose);
        Assert.DoesNotContain(DevelopmentSessionSigningKey(), compose);
        Assert.DoesNotContain("DatabaseStartup__AutoMigrateOnStartup: \"true\"", compose);
        Assert.DoesNotContain("DatabaseStartup__SeedDemoData: \"true\"", compose);
        Assert.DoesNotContain("AllowedOrigins__0: \"http://localhost", compose);
        Assert.DoesNotContain("DevelopmentKey", compose);
    }

    [Fact]
    public void ProductionCompose_UsesDockerSecretsForSensitiveRuntimeValues()
    {
        var root = RepositoryRoot();
        var compose = File.ReadAllText(Path.Combine(root, "compose.production.yml"));
        var program = File.ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Program.cs"));
        var proxyRoute = File.ReadAllText(Path.Combine(root, "frontend", "src", "app", "api", "[...path]", "route.ts"));
        var readyHelper = File.ReadAllText(Path.Combine(root, "frontend", "src", "app", "health", "ready", "readiness.ts"));

        Assert.Contains("secrets:", compose);
        Assert.Contains("POSTGRES_PASSWORD_FILE: /run/secrets/postgres_password", compose);
        Assert.Contains("ConnectionStrings__DefaultConnection_FILE: /run/secrets/accounts_connection_string", compose);
        Assert.Contains("AuthSession__SigningKey_FILE: /run/secrets/auth_session_signing_key", compose);
        Assert.Contains("AuditIntegrity__SigningKeys__0__SigningKey_FILE: /run/secrets/audit_integrity_signing_key", compose);
        Assert.Contains("BootstrapOwner__OwnerInitialPassword_FILE: /run/secrets/bootstrap_owner_password", compose);
        Assert.Contains("ACCOUNTS_API_KEY_FILE: /run/secrets/accounts_api_key", compose);
        Assert.Contains("file: \"${POSTGRES_PASSWORD_FILE:?set POSTGRES_PASSWORD_FILE}\"", compose);
        Assert.Contains("file: \"${ACCOUNTS_CONNECTION_STRING_FILE:?set ACCOUNTS_CONNECTION_STRING_FILE}\"", compose);
        Assert.Contains("file: \"${AUTH_SESSION_SIGNING_KEY_FILE:?set AUTH_SESSION_SIGNING_KEY_FILE}\"", compose);
        Assert.Contains("file: \"${AUDIT_INTEGRITY_SIGNING_KEY_FILE:?set AUDIT_INTEGRITY_SIGNING_KEY_FILE}\"", compose);
        Assert.Contains("file: \"${ACCOUNTS_API_KEY_FILE:?set ACCOUNTS_API_KEY_FILE}\"", compose);
        Assert.Contains("file: \"${BOOTSTRAP_OWNER_PASSWORD_FILE:?set BOOTSTRAP_OWNER_PASSWORD_FILE}\"", compose);

        foreach (var forbidden in new[]
        {
            "POSTGRES_PASSWORD: \"${POSTGRES_PASSWORD",
            "ConnectionStrings__DefaultConnection: \"${ACCOUNTS_CONNECTION_STRING",
            "AuthSession__SigningKey: \"${AUTH_SESSION_SIGNING_KEY",
            "AuditIntegrity__SigningKeys__0__SigningKey: \"${AUDIT_INTEGRITY_SIGNING_KEY",
            "BootstrapOwner__OwnerInitialPassword: \"${BOOTSTRAP_OWNER_PASSWORD",
            "ACCOUNTS_API_KEY: \"${ACCOUNTS_API_KEY"
        })
        {
            Assert.DoesNotContain(forbidden, compose);
        }

        Assert.Contains("FileBackedConfiguration.AddFileBackedEnvironmentVariables(builder.Configuration)", program);
        Assert.Contains("_FILE", program);
        Assert.Contains("readServerSecret", proxyRoute);
        Assert.Contains("readServerSecret", readyHelper);
    }

    [Fact]
    public void ProductionReadiness_VerifiesConfiguredBootstrapOwnerWithoutExposingPasswordToApiService()
    {
        var root = RepositoryRoot();
        var compose = File.ReadAllText(Path.Combine(root, "compose.production.yml"));
        var program = File.ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Program.cs"));
        var apiService = ServiceBlock(compose, "api", "frontend");

        Assert.Contains("BootstrapOwner__TenantSlug: \"${BOOTSTRAP_TENANT_SLUG:?set BOOTSTRAP_TENANT_SLUG}\"", apiService);
        Assert.Contains("BootstrapOwner__OwnerEmail: \"${BOOTSTRAP_OWNER_EMAIL:?set BOOTSTRAP_OWNER_EMAIL}\"", apiService);
        Assert.DoesNotContain("BootstrapOwner__OwnerInitialPassword", apiService);
        Assert.Contains("hasConfiguredBootstrapOwner", program);
        Assert.DoesNotContain("bootstrap.Enabled && !string.IsNullOrWhiteSpace(bootstrapOwnerEmail)", program);

        static string ServiceBlock(string compose, string serviceName, string nextServiceName)
        {
            var start = compose.IndexOf($"  {serviceName}:", StringComparison.Ordinal);
            var end = compose.IndexOf($"  {nextServiceName}:", StringComparison.Ordinal);
            Assert.True(start >= 0, $"Expected {serviceName} service in compose.production.yml.");
            Assert.True(end > start, $"Expected {nextServiceName} service after {serviceName}.");
            return compose[start..end];
        }
    }

    [Fact]
    public void ProductionRunbook_DocumentsFrontendApiKeyHashPair()
    {
        var runbook = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "operations", "production-runbook.md"));

        Assert.Contains("Frontend API Key", runbook);
        Assert.Contains("Required Production Environment", runbook);
        Assert.Contains("ACCOUNTS_API_KEY", runbook);
        Assert.Contains("ACCOUNTS_API_KEY_FILE", runbook);
        Assert.Contains("ACCOUNTS_API_KEY_HASH", runbook);
        Assert.Contains("AUTH_SESSION_SIGNING_KEY", runbook);
        Assert.Contains("AUTH_SESSION_SIGNING_KEY_FILE", runbook);
        Assert.Contains("AUDIT_INTEGRITY_SIGNING_KEY", runbook);
        Assert.Contains("AUDIT_INTEGRITY_SIGNING_KEY_FILE", runbook);
        Assert.Contains("BOOTSTRAP_OWNER_PASSWORD", runbook);
        Assert.Contains("BOOTSTRAP_OWNER_PASSWORD_FILE", runbook);
        Assert.Contains("Docker secrets", runbook);
        Assert.Contains("/run/secrets", runbook);
        Assert.Contains("at least 20 characters and include upper case, lower case, number, and symbol", runbook);
        Assert.Contains("CI sample values are not production secrets", runbook);
        Assert.Contains("config --quiet", runbook);
        Assert.Contains("Do not run plain `docker compose -f compose.production.yml config` with production secrets", runbook);
        Assert.Contains("SHA256", runbook);
        Assert.Contains("ToLowerInvariant()", runbook);
        Assert.Contains("/health/ready", runbook);
        Assert.Contains("mismatched key/hash pair", runbook);
        Assert.Contains("Backend auth endpoints are proxy-only in production", runbook);
        Assert.Contains("Do not expose `/api/auth/login` directly without the frontend service key", runbook);
    }

    [Fact]
    public void ProductionIngressContract_DocumentsTlsTerminationAndTrustedForwardedHeaders()
    {
        var root = RepositoryRoot();
        var caddyPath = Path.Combine(root, "deploy", "caddy", "Caddyfile.example");
        var runbookPath = Path.Combine(root, "Docs", "operations", "production-runbook.md");
        var compose = File.ReadAllText(Path.Combine(root, "compose.production.yml"));

        Assert.True(File.Exists(caddyPath), "A checked-in ingress example should define the HTTPS reverse-proxy contract.");
        var caddy = File.ReadAllText(caddyPath);
        var runbook = File.ReadAllText(runbookPath);

        Assert.Contains("TLS termination", runbook);
        Assert.Contains("reverse proxy", runbook);
        Assert.Contains("TRUST_PROXY_HEADERS=true", runbook);
        Assert.Contains("internal `api` host", runbook);
        Assert.Contains("overwrites X-Forwarded-For", runbook);
        Assert.Contains("overwrites X-Forwarded-Host", runbook);
        Assert.Contains("overwrites X-Forwarded-Proto", runbook);
        Assert.Contains("127.0.0.1:${FRONTEND_PORT:-3000}:3000", compose);
        Assert.Contains("{$ACCOUNTS_CADDY_GLOBAL_OPTIONS}", caddy);
        Assert.Contains("reverse_proxy 127.0.0.1:{$FRONTEND_PORT:3000}", caddy);
        Assert.Contains("header_up X-Forwarded-For {remote_host}", caddy);
        Assert.Contains("header_up X-Forwarded-Host {host}", caddy);
        Assert.Contains("header_up X-Forwarded-Proto {scheme}", caddy);
        Assert.Contains("Strict-Transport-Security", caddy);
        Assert.Contains("local_certs", runbook);
        Assert.DoesNotContain("{header.X-Forwarded-For}", caddy);
        Assert.DoesNotContain("{header.X-Forwarded-Host}", caddy);
        Assert.DoesNotContain("{header.X-Forwarded-Proto}", caddy);
    }

    [Fact]
    public void HealthEndpoints_ExposeLivenessAndDatabaseReadiness()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Program.cs"));

        Assert.Contains("MapGet(\"/health/live\"", program);
        Assert.Contains("MapGet(\"/health/ready\"", program);
        Assert.Contains("CanConnectAsync", program);
        Assert.Contains("StatusCodes.Status503ServiceUnavailable", program);
    }

    [Fact]
    public void Dockerfiles_AvoidLocalhostRuntimeDefaultsAndRunApiAsNonRoot()
    {
        var backendDockerfile = File.ReadAllText(Path.Combine(RepositoryRoot(), "Dockerfile.backend"));
        var frontendDockerfile = File.ReadAllText(Path.Combine(RepositoryRoot(), "Dockerfile.frontend"));

        Assert.Contains("USER $APP_UID", backendDockerfile);
        Assert.DoesNotContain("ENV API_URL=http://localhost:5090", frontendDockerfile);
    }

    [Fact]
    public void DockerBuildContext_ExcludesGeneratedAndLocalDependencyArtifacts()
    {
        var dockerignorePath = Path.Combine(RepositoryRoot(), ".dockerignore");

        Assert.True(File.Exists(dockerignorePath), ".dockerignore should keep generated artifacts out of container builds.");

        var dockerignore = File.ReadAllText(dockerignorePath);
        Assert.Contains(".git", dockerignore);
        Assert.Contains("frontend/node_modules", dockerignore);
        Assert.Contains("frontend/.next", dockerignore);
        Assert.Contains("frontend/.next-probe", dockerignore);
        Assert.Contains(".tmp-*", dockerignore);
        Assert.Contains("frontend/.tmp-*", dockerignore);
        Assert.Contains("frontend/.codex-acl-probe", dockerignore);
        Assert.Contains("backend/**/bin", dockerignore);
        Assert.Contains("backend/**/obj", dockerignore);
    }

    [Fact]
    public void ContinuousIntegrationWorkflow_RunsBackendFrontendAndProductionConfigGates()
    {
        var workflowPath = Path.Combine(RepositoryRoot(), ".github", "workflows", "ci.yml");

        Assert.True(File.Exists(workflowPath), "CI should run production-readiness gates on every push and pull request.");

        var workflow = File.ReadAllText(workflowPath);
        AssertPushRunsOnEveryBranch(workflow);
        Assert.Contains("pull_request:", workflow);

        var backendJob = WorkflowJob(workflow, "backend");
        Assert.Contains("actions/checkout", backendJob);
        Assert.Contains("actions/setup-dotnet", backendJob);
        Assert.Contains("dotnet-version: 10.0.x", backendJob);
        Assert.Contains("dotnet restore backend/Accounts.slnx", backendJob);
        Assert.DoesNotContain("dotnet restore backend/Accounts.sln\r", backendJob);
        Assert.DoesNotContain("dotnet restore backend/Accounts.sln\n", backendJob);
        Assert.Contains("dotnet test backend/Accounts.Tests/Accounts.Tests.csproj", backendJob);
        Assert.Contains("dotnet build backend/Accounts.Api/Accounts.Api.csproj", backendJob);

        var frontendJob = WorkflowJob(workflow, "frontend");
        Assert.Contains("actions/checkout", frontendJob);
        Assert.Contains("actions/setup-node", frontendJob);
        Assert.Contains("node-version: 22", frontendJob);
        Assert.Contains("cache: npm", frontendJob);
        Assert.Contains("cache-dependency-path: frontend/package-lock.json", frontendJob);
        Assert.Contains("npm ci", frontendJob);
        Assert.Contains("npm audit --audit-level=moderate", frontendJob);
        Assert.Contains("npm run lint", frontendJob);
        Assert.Contains("npx tsc --noEmit --incremental false", frontendJob);
        Assert.Contains("npm test", frontendJob);
        Assert.Contains("npm run build", frontendJob);
        Assert.Contains("API_URL", frontendJob);
        Assert.Contains("ACCOUNTS_API_KEY", frontendJob);
        Assert.Contains("Generate ephemeral frontend build API key", frontendJob);
        Assert.Contains("::add-mask::", frontendJob);
        Assert.Contains("GITHUB_ENV", frontendJob);
        Assert.DoesNotContain("ACCOUNTS_API_KEY: ", frontendJob);

        // CI runs the whole frontend suite through `npm test`; that aggregate must keep chaining every
        // gate (unit harness + readiness + proxy + auth + api-client) so none can silently drop from CI.
        var frontendPackageJson = File.ReadAllText(Path.Combine(RepositoryRoot(), "frontend", "package.json"));
        foreach (var suite in new[] { "test:unit", "test:readiness", "test:proxy", "test:auth", "test:api-client" })
            Assert.Contains(suite, frontendPackageJson);

        var productionConfigJob = WorkflowJob(workflow, "production-config");
        Assert.Contains("actions/checkout", productionConfigJob);
        Assert.Contains("docker compose -f compose.production.yml config --quiet", productionConfigJob);
        Assert.Contains("Validate production image contract", productionConfigJob);
        Assert.Contains("shell: pwsh", productionConfigJob);
        Assert.Contains("./scripts/verify-production-compose-images.ps1", productionConfigJob);
        Assert.DoesNotContain("docker compose -f compose.production.yml config\r", productionConfigJob);
        Assert.DoesNotContain("docker compose -f compose.production.yml config\n", productionConfigJob);
        Assert.Contains("caddy validate --config /etc/caddy/Caddyfile", productionConfigJob);
        Assert.Contains("deploy/caddy/Caddyfile.example", productionConfigJob);
        Assert.Contains("docker build -f Dockerfile.backend", productionConfigJob);
        Assert.Contains("--tag \"$ACCOUNTS_API_IMAGE\"", productionConfigJob);
        Assert.Contains("docker build -f Dockerfile.frontend", productionConfigJob);
        Assert.Contains("--tag \"$ACCOUNTS_FRONTEND_IMAGE\"", productionConfigJob);
        Assert.Contains("--build-arg API_URL=http://api:8080", productionConfigJob);
        Assert.Contains("ACCOUNTS_API_IMAGE: accounts-api-ci:${{ github.sha }}", productionConfigJob);
        Assert.Contains("ACCOUNTS_FRONTEND_IMAGE: accounts-frontend-ci:${{ github.sha }}", productionConfigJob);
        Assert.Contains("AUDIT_INTEGRITY_SIGNING_KEY", productionConfigJob);
        Assert.Contains("TRUST_PROXY_HEADERS", productionConfigJob);
        Assert.Contains("ACCOUNTS_API_KEY_HASH", productionConfigJob);
        Assert.Contains("BOOTSTRAP_OWNER_PASSWORD", productionConfigJob);
        Assert.Contains("write_secret_file", productionConfigJob);
        Assert.Contains("POSTGRES_PASSWORD_FILE", productionConfigJob);
        Assert.Contains("ACCOUNTS_CONNECTION_STRING_FILE", productionConfigJob);
        Assert.Contains("AUTH_SESSION_SIGNING_KEY_FILE", productionConfigJob);
        Assert.Contains("AUDIT_INTEGRITY_SIGNING_KEY_FILE", productionConfigJob);
        Assert.Contains("ACCOUNTS_API_KEY_FILE", productionConfigJob);
        Assert.Contains("BOOTSTRAP_OWNER_PASSWORD_FILE", productionConfigJob);
        Assert.Contains("Generate ephemeral production config secrets", productionConfigJob);
        Assert.Contains("::add-mask::", productionConfigJob);
        Assert.Contains("GITHUB_ENV", productionConfigJob);
        Assert.Contains("sha256sum", productionConfigJob);
        Assert.Contains("generate_bootstrap_owner_password()", productionConfigJob);
        Assert.Contains("printf 'CiOwner1!%s' \"$(generate_secret)\"", productionConfigJob);
        Assert.DoesNotContain("set_masked_env BOOTSTRAP_OWNER_PASSWORD \"$(generate_secret)\"", productionConfigJob);
        Assert.DoesNotContain("set_masked_env POSTGRES_PASSWORD", productionConfigJob);
        Assert.DoesNotContain("set_masked_env ACCOUNTS_CONNECTION_STRING", productionConfigJob);
        Assert.DoesNotContain("set_masked_env AUTH_SESSION_SIGNING_KEY", productionConfigJob);
        Assert.DoesNotContain("set_masked_env AUDIT_INTEGRITY_SIGNING_KEY", productionConfigJob);
        Assert.DoesNotContain("set_masked_env ACCOUNTS_API_KEY \"$accounts_api_key\"", productionConfigJob);
        Assert.DoesNotContain("set_masked_env BOOTSTRAP_OWNER_PASSWORD", productionConfigJob);
        Assert.DoesNotContain("ACCOUNTS_API_KEY: ", productionConfigJob);
        Assert.DoesNotContain("POSTGRES_PASSWORD: ", productionConfigJob);

        var productionSmokeJob = WorkflowJob(workflow, "production-smoke");
        Assert.Contains("needs:", productionSmokeJob);
        Assert.Contains("production-config", productionSmokeJob);
        Assert.Contains("ACCOUNTS_ALLOWED_HOSTS: accounts-smoke.local", productionSmokeJob);
        Assert.Contains("ACCOUNTS_ALLOWED_ORIGIN: https://accounts-smoke.local", productionSmokeJob);
        Assert.Contains("ACCOUNTS_API_IMAGE: accounts-api-ci:${{ github.sha }}", productionSmokeJob);
        Assert.Contains("ACCOUNTS_FRONTEND_IMAGE: accounts-frontend-ci:${{ github.sha }}", productionSmokeJob);
        Assert.Contains("FRONTEND_PORT: \"3000\"", productionSmokeJob);
        Assert.Contains("NO_PROXY: accounts-smoke.local,127.0.0.1,localhost", productionSmokeJob);
        Assert.Contains("Generate ephemeral production smoke secrets", productionSmokeJob);
        Assert.Contains("::add-mask::", productionSmokeJob);
        Assert.Contains("GITHUB_ENV", productionSmokeJob);
        Assert.Contains("accounts_api_key_hash=\"$(printf '%s' \"$accounts_api_key\" | sha256sum", productionSmokeJob);
        Assert.Contains("write_secret_file", productionSmokeJob);
        Assert.Contains("POSTGRES_PASSWORD_FILE", productionSmokeJob);
        Assert.Contains("ACCOUNTS_CONNECTION_STRING_FILE", productionSmokeJob);
        Assert.Contains("AUTH_SESSION_SIGNING_KEY_FILE", productionSmokeJob);
        Assert.Contains("AUDIT_INTEGRITY_SIGNING_KEY_FILE", productionSmokeJob);
        Assert.Contains("ACCOUNTS_API_KEY_FILE", productionSmokeJob);
        Assert.Contains("BOOTSTRAP_OWNER_PASSWORD_FILE", productionSmokeJob);
        Assert.Contains("set_masked_env ACCOUNTS_API_KEY_HASH \"$accounts_api_key_hash\"", productionSmokeJob);
        Assert.Contains("generate_bootstrap_owner_password()", productionSmokeJob);
        Assert.Contains("printf 'CiOwner1!%s' \"$(generate_secret)\"", productionSmokeJob);
        Assert.Contains("bootstrap_owner_password=\"$(generate_bootstrap_owner_password)\"", productionSmokeJob);
        Assert.DoesNotContain("set_masked_env BOOTSTRAP_OWNER_PASSWORD \"$(generate_secret)\"", productionSmokeJob);
        Assert.DoesNotContain("set_masked_env POSTGRES_PASSWORD", productionSmokeJob);
        Assert.DoesNotContain("set_masked_env ACCOUNTS_CONNECTION_STRING", productionSmokeJob);
        Assert.DoesNotContain("set_masked_env AUTH_SESSION_SIGNING_KEY", productionSmokeJob);
        Assert.DoesNotContain("set_masked_env AUDIT_INTEGRITY_SIGNING_KEY", productionSmokeJob);
        Assert.DoesNotContain("set_masked_env ACCOUNTS_API_KEY \"$accounts_api_key\"", productionSmokeJob);
        Assert.DoesNotContain("set_masked_env BOOTSTRAP_OWNER_PASSWORD", productionSmokeJob);
        Assert.Contains("Build production smoke images", productionSmokeJob);
        Assert.Contains("docker build -f Dockerfile.backend --tag \"$ACCOUNTS_API_IMAGE\" .", productionSmokeJob);
        Assert.Contains("docker build -f Dockerfile.frontend --build-arg API_URL=http://api:8080 --tag \"$ACCOUNTS_FRONTEND_IMAGE\" .", productionSmokeJob);
        Assert.Contains("docker compose -f compose.production.yml up -d --wait --wait-timeout 300", productionSmokeJob);
        Assert.DoesNotContain("docker compose -f compose.production.yml up -d --build", productionSmokeJob);
        Assert.Contains("127.0.0.1 accounts-smoke.local", productionSmokeJob);
        Assert.Contains("accounts-production-smoke-ingress", productionSmokeJob);
        Assert.Contains("--network host", productionSmokeJob);
        Assert.Contains("ACCOUNTS_CADDY_GLOBAL_OPTIONS=local_certs", productionSmokeJob);
        Assert.Contains("update-ca-certificates", productionSmokeJob);
        Assert.Contains("Run production smoke script", productionSmokeJob);
        Assert.Contains("./scripts/smoke-production.ps1", productionSmokeJob);
        Assert.Contains("-BaseUrl https://accounts-smoke.local", productionSmokeJob);
        Assert.Contains("-Email $env:BOOTSTRAP_OWNER_EMAIL", productionSmokeJob);
        Assert.Contains("-Password $bootstrapOwnerPassword", productionSmokeJob);
        Assert.Contains("Run production backup restore drill", productionSmokeJob);
        Assert.Contains("$env:RUNNER_TEMP", productionSmokeJob);
        Assert.Contains("accounts-backups", productionSmokeJob);
        Assert.Contains("./scripts/backup-postgres.ps1", productionSmokeJob);
        Assert.Contains("-OutputDirectory $backupDir", productionSmokeJob);
        Assert.Contains("$backups = @(Get-ChildItem -LiteralPath $backupDir -Filter \"*.dump\"", productionSmokeJob);
        Assert.Contains("$backups.Count -ne 1", productionSmokeJob);
        Assert.Contains("Expected exactly one backup dump", productionSmokeJob);
        Assert.Contains("$backup = $backups[0]", productionSmokeJob);
        Assert.Contains("Test-Path -LiteralPath \"$($backup.FullName).sha256\"", productionSmokeJob);
        Assert.Contains("Missing sha256 for", productionSmokeJob);
        Assert.Contains("./scripts/verify-postgres-backup.ps1", productionSmokeJob);
        Assert.Contains("-BackupPath $backup.FullName", productionSmokeJob);
        Assert.DoesNotContain("-AllowRepositoryOutputForLocalDryRun", productionSmokeJob);
        Assert.DoesNotContain("-BaseUrl http://127.0.0.1:3000", productionSmokeJob);
        Assert.DoesNotContain("-AllowInsecureHttp", productionSmokeJob);
        Assert.Contains("docker compose -f compose.production.yml logs", productionSmokeJob);
        Assert.Contains("docker compose -f compose.production.yml down -v --remove-orphans", productionSmokeJob);
        Assert.DoesNotContain("ACCOUNTS_API_KEY: ", productionSmokeJob);
        Assert.DoesNotContain("ACCOUNTS_API_KEY_HASH: ", productionSmokeJob);
        Assert.DoesNotContain("POSTGRES_PASSWORD: ", productionSmokeJob);
    }

    [Fact]
    public void BackendBuild_FailsCiOnVulnerableNuGetPackages()
    {
        // ops-backend-vuln-scan: the backend parses untrusted CSV and handles auth/crypto, so a
        // known-vulnerable NuGet dependency (direct or transitive) must fail the build instead of
        // shipping green. NuGetAudit emits NU1901-NU1904 during `dotnet restore`; Directory.Build.props
        // promotes them to errors so CI's restore step is the gate. CI already audits npm but not NuGet.
        var propsPath = Path.Combine(RepositoryRoot(), "backend", "Directory.Build.props");
        Assert.True(File.Exists(propsPath), "backend/Directory.Build.props must exist to host the NuGet audit gate.");
        var props = File.ReadAllText(propsPath);

        Assert.Contains("<NuGetAudit>true</NuGetAudit>", props);
        // Audit transitive packages too, at every advisory severity.
        Assert.Contains("<NuGetAuditMode>all</NuGetAuditMode>", props);
        Assert.Contains("<NuGetAuditLevel>low</NuGetAuditLevel>", props);
        // Every NuGet vulnerability warning code must be promoted to an error.
        foreach (var code in new[] { "NU1901", "NU1902", "NU1903", "NU1904" })
            Assert.Contains(code, props);
        Assert.Matches(@"<WarningsAsErrors>.*NU1901.*NU1902.*NU1903.*NU1904.*</WarningsAsErrors>", props);

        // CI restores the solution before testing, so the audit gate runs on every push/PR.
        var workflow = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "ci.yml"));
        Assert.Contains("dotnet restore backend/Accounts.slnx", WorkflowJob(workflow, "backend"));
    }

    [Fact]
    public void ProductionCompose_UsesImmutableImageReferencesInsteadOfBuildContexts()
    {
        var compose = File.ReadAllText(Path.Combine(RepositoryRoot(), "compose.production.yml"));
        var runbook = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "operations", "production-runbook.md"));

        Assert.Contains("image: \"${ACCOUNTS_API_IMAGE:?set ACCOUNTS_API_IMAGE}\"", compose);
        Assert.Contains("image: \"${ACCOUNTS_FRONTEND_IMAGE:?set ACCOUNTS_FRONTEND_IMAGE}\"", compose);
        Assert.DoesNotContain("build:", compose);
        Assert.DoesNotContain("dockerfile: Dockerfile.backend", compose);
        Assert.DoesNotContain("dockerfile: Dockerfile.frontend", compose);
        Assert.Equal(2, Regex.Matches(compose, "image: \"\\$\\{ACCOUNTS_API_IMAGE").Count);
        Assert.Single(Regex.Matches(compose, "image: \"\\$\\{ACCOUNTS_FRONTEND_IMAGE"));
        Assert.Contains("ACCOUNTS_API_IMAGE", runbook);
        Assert.Contains("ACCOUNTS_FRONTEND_IMAGE", runbook);
        Assert.Contains("docker compose -f compose.production.yml pull", runbook);
        Assert.Contains("docker image inspect", runbook);
        Assert.Contains("CI-promoted immutable image", runbook);
        Assert.Contains("Do not deploy production by rebuilding from the checkout", runbook);
        var imageVerifierPath = Path.Combine(RepositoryRoot(), "scripts", "verify-production-compose-images.ps1");
        Assert.True(File.Exists(imageVerifierPath));
        var imageVerifier = File.ReadAllText(imageVerifierPath);
        Assert.Contains("config --format json", imageVerifier);
        Assert.Contains("Invoke-WithTemporaryEnvironment", imageVerifier);
        Assert.Contains("Remove-Item -Path \"Env:$name\"", imageVerifier);
        Assert.Contains("Set-Item -Path \"Env:$name\" -Value $previousValues[$name]", imageVerifier);
        Assert.Contains("$RepositoryRoot", imageVerifier);
        Assert.Contains("Assert-NoBuildContext", imageVerifier);
        Assert.Contains("WorkflowRunBlocks", imageVerifier);
        Assert.Contains("composeUpBlocks", imageVerifier);
        Assert.Contains("accounts-api-ci:verify", imageVerifier);
        Assert.Contains("accounts-frontend-ci:verify", imageVerifier);
        Assert.Contains("--build", imageVerifier);
    }

    [Fact]
    public void BackupArtifacts_AreExcludedFromGitAndDockerBuildContexts()
    {
        var gitignore = File.ReadAllText(Path.Combine(RepositoryRoot(), ".gitignore"));
        var dockerignore = File.ReadAllText(Path.Combine(RepositoryRoot(), ".dockerignore"));

        foreach (var ignoreFile in new[] { gitignore, dockerignore })
        {
            Assert.Contains("backups/", ignoreFile);
            Assert.Contains("*.dump", ignoreFile);
            Assert.Contains("*.dump.sha256", ignoreFile);
            Assert.Contains(".env.*", ignoreFile);
        }
    }

    [Fact]
    public void Frontend_ProvidesFirstLoginPasswordChangeFlow()
    {
        var root = RepositoryRoot();
        var authClient = File.ReadAllText(Path.Combine(root, "frontend", "src", "lib", "auth.ts"));
        var authProvider = File.ReadAllText(Path.Combine(root, "frontend", "src", "components", "AuthProvider.tsx"));
        var loginPage = File.ReadAllText(Path.Combine(root, "frontend", "src", "app", "login", "page.tsx"));
        var passwordPagePath = Path.Combine(root, "frontend", "src", "app", "change-password", "page.tsx");

        Assert.True(File.Exists(passwordPagePath), "Bootstrap owners need a usable first-login password-change screen.");
        var passwordPage = File.ReadAllText(passwordPagePath);
        Assert.Contains("mustChangePassword", authClient);
        Assert.Contains("changePassword", authClient);
        Assert.Contains("/api/auth/password", authClient);
        Assert.Contains("mustChangePassword", authProvider);
        Assert.Contains("/change-password", authProvider);
        Assert.Contains("mustChangePassword", loginPage);
        Assert.Contains("changePasswordRouteForReturnTo", loginPage);
        Assert.Contains("changePassword", passwordPage);
        Assert.Contains("current-password", passwordPage);
        Assert.Contains("new-password", passwordPage);
    }

    [Fact]
    public void FrontendAuth_HandlesExpiredSessionsWithReturnToLoginFlow()
    {
        var root = RepositoryRoot();
        var apiClient = File.ReadAllText(Path.Combine(root, "frontend", "src", "lib", "api.ts"));
        var authProvider = File.ReadAllText(Path.Combine(root, "frontend", "src", "components", "AuthProvider.tsx"));
        var loginPage = File.ReadAllText(Path.Combine(root, "frontend", "src", "app", "login", "page.tsx"));
        var changePasswordPage = File.ReadAllText(Path.Combine(root, "frontend", "src", "app", "change-password", "page.tsx"));
        var navigationPath = Path.Combine(root, "frontend", "src", "lib", "navigation.ts");

        Assert.True(File.Exists(navigationPath), "Return-to routing should be centralised so auth pages reject unsafe absolute URLs.");
        var navigation = File.ReadAllText(navigationPath);

        Assert.Contains("SESSION_EXPIRED_EVENT", apiClient);
        Assert.Contains("status === 401", apiClient);
        Assert.Contains("window.dispatchEvent(new CustomEvent", apiClient);
        Assert.Contains("SESSION_EXPIRED_EVENT", authProvider);
        Assert.Contains("window.addEventListener", authProvider);
        Assert.Contains("dispatchSessionExpired(returnTo)", authProvider);
        Assert.Contains("loginRouteForReturnTo(currentPathWithSearch())", authProvider);
        Assert.Contains("loginRouteForReturnTo(returnTo)", authProvider);
        Assert.Contains("returnToFromLocation", loginPage);
        Assert.Contains("changePasswordRouteForReturnTo(returnTo)", loginPage);
        Assert.Contains("returnToFromLocation", changePasswordPage);
        Assert.Contains("normaliseReturnTo", navigation);
        Assert.Contains("candidate.startsWith(\"//\")", navigation);
        Assert.Contains("pathname === \"/login\"", navigation);
        Assert.Contains("pathname === \"/change-password\"", navigation);
    }

    [Fact]
    public void FrontendAuth_LogoutFailureKeepsActiveSessionAndSurfacesRetryError()
    {
        var root = RepositoryRoot();
        var logoutSessionPath = Path.Combine(root, "frontend", "src", "lib", "logoutSession.ts");
        var authProvider = File.ReadAllText(Path.Combine(root, "frontend", "src", "components", "AuthProvider.tsx"));
        var normalisedAuthProvider = authProvider.Replace("\r\n", "\n");
        var appNavbar = File.ReadAllText(Path.Combine(root, "frontend", "src", "components", "AppNavbar.tsx"));
        var normalisedNavbar = appNavbar.Replace("\r\n", "\n");
        var packageJson = File.ReadAllText(Path.Combine(root, "frontend", "package.json"));

        Assert.True(File.Exists(logoutSessionPath), "Logout state transitions should be captured in a tested pure helper.");
        var logoutSession = File.ReadAllText(logoutSessionPath);

        Assert.Contains("shouldClearLocalSessionAfterLogout", logoutSession);
        Assert.Contains("error === undefined || hasHttpStatus(error, 401)", logoutSession);
        Assert.Contains("Sign out did not complete. Your session is still active", logoutSession);
        Assert.Contains("shouldClearLocalSessionAfterLogout(err)", authProvider);
        Assert.Contains("logoutFailureMessage(err)", authProvider);
        Assert.Contains("setLogoutError(message)", authProvider);
        Assert.Contains("toast.error(message)", authProvider);
        Assert.DoesNotContain("\n      router.replace(\"/login\");\n    } catch", normalisedAuthProvider);
        Assert.Contains("logoutError", appNavbar);
        Assert.Contains("aria-live=\"polite\"", appNavbar);
        Assert.Contains("md:block", appNavbar);
        Assert.DoesNotContain("lg:block", appNavbar);
        var mobileMenuIndex = normalisedNavbar.IndexOf("{/* Mobile menu */}", StringComparison.Ordinal);
        Assert.True(mobileMenuIndex > 0, "Navbar should keep the mobile menu section marker for source guards.");
        var mobileHeader = normalisedNavbar[..mobileMenuIndex];
        Assert.Contains("logoutError && user", mobileHeader);
        Assert.Contains("md:hidden", mobileHeader);
        Assert.Contains("role=\"status\"", mobileHeader);
        Assert.Contains("test:auth", packageJson);
        Assert.DoesNotContain("catch {\n      // The local shell should still clear stale session state", normalisedAuthProvider);
    }

    [Fact]
    public void FrontendApiErrors_DoNotExposeServerOrProxyBodiesToUsers()
    {
        var root = RepositoryRoot();
        var apiClient = File.ReadAllText(Path.Combine(root, "frontend", "src", "lib", "api.ts"));
        var periodPage = File.ReadAllText(Path.Combine(
            root,
            "frontend",
            "src",
            "app",
            "companies",
            "[companyId]",
            "periods",
            "[periodId]",
            "page.tsx"));
        var errorBoundary = File.ReadAllText(Path.Combine(root, "frontend", "src", "components", "ErrorBoundary.tsx"));

        Assert.Contains("SAFE_MESSAGE_STATUSES", apiClient);
        Assert.Contains("status >= 500", apiClient);
        Assert.Contains("api_proxy_misconfigured", apiClient);
        Assert.Contains("upstream_unavailable", apiClient);
        Assert.DoesNotContain("if (parsed.message) return parsed.message;", apiClient);
        Assert.DoesNotContain("if (parsed.error) return parsed.error;", apiClient);
        Assert.DoesNotContain("if (body && body.length < 200) return body;", apiClient);
        Assert.Contains("export async function fetchDocumentBlob", apiClient);
        Assert.Contains("new ApiError(response.status, response.statusText, body)", apiClient);
        Assert.Contains("fetchDocumentBlob(url, documentType ? \"POST\" : \"GET\")", periodPage);
        Assert.DoesNotContain("parsed.error ?? parsed.message", periodPage);
        Assert.DoesNotContain("this.state.error?.message", errorBoundary);
    }

    private static void AssertPushRunsOnEveryBranch(string workflow)
    {
        Assert.Contains("push:", workflow);

        var pushMatch = Regex.Match(
            workflow,
            @"(?ms)^  push:\s*\r?\n(?<body>.*?)(?=^  [A-Za-z_][A-Za-z0-9_-]*:|^permissions:|^jobs:|\z)");
        Assert.True(pushMatch.Success, "CI workflow should declare a push trigger.");
        Assert.DoesNotContain("branches:", pushMatch.Groups["body"].Value);
    }

    private static string WorkflowJob(string workflow, string jobName)
    {
        var jobMatch = Regex.Match(
            workflow,
            $@"(?ms)^  {Regex.Escape(jobName)}:\s*\r?\n(?<body>.*?)(?=^  [A-Za-z0-9_-]+:|\z)");
        Assert.True(jobMatch.Success, $"CI workflow should define a {jobName} job.");
        return jobMatch.Groups["body"].Value;
    }

    [Fact]
    public void ApiAccess_AllowsKeyForConfiguredCompanyOnly()
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
                        KeyHash = ApiAccessService.HashKey("secret-a"),
                        AllowedCompanyIds = [10]
                    }
                ]
            }),
            new TestEnvironment("Production"));

        var allowed = service.Authorize("secret-a", new PathString("/api/companies/10/periods"));
        var denied = service.Authorize("secret-a", new PathString("/api/companies/11/periods"));
        var invalid = service.Authorize("wrong", new PathString("/api/companies/10/periods"));

        Assert.True(allowed.IsAllowed);
        Assert.False(denied.IsAllowed);
        Assert.False(invalid.IsAllowed);
        Assert.Contains("not authorised", denied.DenialReason);
    }

    [Fact]
    public void ApiAccess_EnforcesRolesForWritesAndAdminActions()
    {
        var service = new ApiAccessService(
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
                    },
                    new ApiAccessKeyConfig
                    {
                        Name = "Admin",
                        Role = "Admin",
                        DevelopmentKey = "admin"
                    }
                ]
            }),
            new TestEnvironment("Development"));

        var readerWrite = service.Authorize("reader", new PathString("/api/companies/10/periods"), HttpMethods.Post);
        var writerWrite = service.Authorize("writer", new PathString("/api/companies/10/periods"), HttpMethods.Post);
        var writerDelete = service.Authorize("writer", new PathString("/api/companies/10"), HttpMethods.Delete);
        var scopedCompanyCreate = service.Authorize("writer", new PathString("/api/companies"), HttpMethods.Post);
        var adminCompanyCreate = service.Authorize("admin", new PathString("/api/companies"), HttpMethods.Post);

        Assert.False(readerWrite.IsAllowed);
        Assert.Contains("read-only", readerWrite.DenialReason);
        Assert.True(writerWrite.IsAllowed);
        Assert.False(writerDelete.IsAllowed);
        Assert.Contains("administrative", writerDelete.DenialReason);
        Assert.False(scopedCompanyCreate.IsAllowed);
        Assert.Contains("administrative", scopedCompanyCreate.DenialReason);
        Assert.True(adminCompanyCreate.IsAllowed);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void ApiAccess_RejectsDevelopmentKeysOutsideDevelopment(string environmentName)
    {
        var service = new ApiAccessService(
            Options.Create(new ApiAccessConfig
            {
                Enabled = true,
                Keys = [new ApiAccessKeyConfig { Name = "Dev key", DevelopmentKey = "plain-text" }]
            }),
            new TestEnvironment(environmentName));

        var failures = service.ValidateConfiguration();

        Assert.Contains(failures, f => f.Contains("DevelopmentKey"));
    }

    [Fact]
    public void ApiAccess_MatchesDevelopmentKeysOnlyInDevelopment()
    {
        var config = new ApiAccessConfig
        {
            Enabled = true,
            Keys = [new ApiAccessKeyConfig { Name = "Dev key", DevelopmentKey = "plain-text" }]
        };
        var development = new ApiAccessService(Options.Create(config), new TestEnvironment("Development"));
        var staging = new ApiAccessService(Options.Create(config), new TestEnvironment("Staging"));

        Assert.True(development.Authorize("plain-text", new PathString("/api/companies")).IsAllowed);
        var stagingDecision = staging.Authorize("plain-text", new PathString("/api/companies"));
        Assert.False(stagingDecision.IsAllowed);
        Assert.Contains("Invalid API access key", stagingDecision.DenialReason);
    }

    [Fact]
    public void ApiAccess_DisabledBypassIsDevelopmentOnly()
    {
        var config = new ApiAccessConfig { Enabled = false };
        var development = new ApiAccessService(Options.Create(config), new TestEnvironment("Development"));
        var staging = new ApiAccessService(Options.Create(config), new TestEnvironment("Staging"));

        Assert.True(development.Authorize(null, new PathString("/api/companies")).IsAllowed);
        var stagingDecision = staging.Authorize(null, new PathString("/api/companies"));
        Assert.False(stagingDecision.IsAllowed);
        Assert.Contains("not configured", stagingDecision.DenialReason);
    }

    [Fact]
    public void ApiAccess_RejectsInvalidRoleConfiguration()
    {
        var service = new ApiAccessService(
            Options.Create(new ApiAccessConfig
            {
                Enabled = true,
                Keys = [new ApiAccessKeyConfig { Name = "Odd key", Role = "SuperUser", DevelopmentKey = "plain-text" }]
            }),
            new TestEnvironment("Development"));

        var failures = service.ValidateConfiguration();

        Assert.Contains(failures, f => f.Contains("invalid Role"));
    }

    [Fact]
    public async Task PeriodOwnershipMiddleware_BlocksPeriodFromDifferentCompany()
    {
        await using var db = CreateDbContext();
        var allowedPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Path = $"/api/companies/{allowedPeriod.CompanyId}/periods/{otherPeriod.Id}/debtors";
        var middleware = new PeriodOwnershipMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task PeriodOwnershipMiddleware_AllowsPeriodFromRouteCompany()
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
        context.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/debtors";
        var middleware = new PeriodOwnershipMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task TenantAccessMiddleware_HidesCrossTenantCompany()
    {
        await using var db = CreateDbContext();
        var tenantA = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
        var tenantB = await SeedTenantAsync(db, name: "Tenant B", slug: "tenant-b");
        var companyB = await SeedTenantCompanyAsync(db, tenantB.Id, "Tenant B Limited");
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Path = $"/api/companies/{companyB.Id}";
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 1,
            TenantId: tenantA.Id,
            TenantName: tenantA.Name,
            Email: "owner@tenant-a.test",
            DisplayName: "Tenant A Owner",
            Role: "Owner");
        var middleware = new TenantAccessMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task TenantAccessMiddleware_AllowsOwnTenantCompany()
    {
        await using var db = CreateDbContext();
        var tenantA = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
        var companyA = await SeedTenantCompanyAsync(db, tenantA.Id, "Tenant A Limited");
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Path = $"/api/companies/{companyA.Id}";
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 1,
            TenantId: tenantA.Id,
            TenantName: tenantA.Name,
            Email: "owner@tenant-a.test",
            DisplayName: "Tenant A Owner",
            Role: "Owner");
        var middleware = new TenantAccessMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task TenantAccessMiddleware_HidesSameTenantCompanyOutsideClientAssignment()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
        var assignedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Assigned Client Limited");
        var otherCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Other Client Limited");
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Path = $"/api/companies/{otherCompany.Id}";
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 1,
            TenantId: tenant.Id,
            TenantName: tenant.Name,
            Email: "client@tenant-a.test",
            DisplayName: "Tenant A Client",
            Role: "Client",
            AllowedCompanyIds: new HashSet<int> { assignedCompany.Id });
        var middleware = new TenantAccessMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task CompanyList_IntersectsTenantAndApiKeyAllowedCompanyIds()
    {
        await using var db = CreateDbContext();
        var tenantA = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
        var companyA = await SeedTenantCompanyAsync(db, tenantA.Id, "Tenant A Limited");
        var companyB = await SeedTenantCompanyAsync(db, tenantA.Id, "Tenant B Limited");
        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 1,
            TenantId: tenantA.Id,
            TenantName: tenantA.Name,
            Email: "owner@tenant-a.test",
            DisplayName: "Tenant A Owner",
            Role: "Owner");
        context.Items["ApiAllowedCompanyIds"] = new[] { companyA.Id };

        var visibleCompanyIds = await CompanyListQuery
            .ForContext(context, db.Companies)
            .Select(c => c.Id)
            .ToListAsync();

        Assert.Equal([companyA.Id], visibleCompanyIds);
        Assert.DoesNotContain(companyB.Id, visibleCompanyIds);
    }

    [Fact]
    public async Task CompanyList_FiltersHumanClientCompanyAssignments()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
        var assignedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Assigned Client Limited");
        var otherCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Other Client Limited");
        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 1,
            TenantId: tenant.Id,
            TenantName: tenant.Name,
            Email: "client@tenant-a.test",
            DisplayName: "Tenant A Client",
            Role: "Client",
            AllowedCompanyIds: new HashSet<int> { assignedCompany.Id });

        var visibleCompanyIds = await CompanyListQuery
            .ForContext(context, db.Companies)
            .Select(c => c.Id)
            .ToListAsync();

        Assert.Equal([assignedCompany.Id], visibleCompanyIds);
        Assert.DoesNotContain(otherCompany.Id, visibleCompanyIds);
    }

    [Fact]
    public async Task CompanyList_TrimsClientRoleBeforeApplyingCompanyAssignments()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
        var assignedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Assigned Client Limited");
        var otherCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Other Client Limited");
        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 1,
            TenantId: tenant.Id,
            TenantName: tenant.Name,
            Email: "client@tenant-a.test",
            DisplayName: "Tenant A Client",
            Role: " Client ",
            AllowedCompanyIds: new HashSet<int> { assignedCompany.Id });

        var visibleCompanyIds = await CompanyListQuery
            .ForContext(context, db.Companies)
            .Select(c => c.Id)
            .ToListAsync();

        Assert.Equal([assignedCompany.Id], visibleCompanyIds);
        Assert.DoesNotContain(otherCompany.Id, visibleCompanyIds);
    }

    [Fact]
    public async Task CompanyEndpointAccess_UsesCompanyListPolicyForDirectHandlerGuards()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
        var assignedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Assigned Client Limited");
        var otherCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Other Client Limited");
        var clientContext = new DefaultHttpContext();
        clientContext.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 1,
            TenantId: tenant.Id,
            TenantName: tenant.Name,
            Email: "client@tenant-a.test",
            DisplayName: "Tenant A Client",
            Role: "Client",
            AllowedCompanyIds: new HashSet<int> { assignedCompany.Id });
        var ownerContext = new DefaultHttpContext();
        ownerContext.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 2,
            TenantId: tenant.Id,
            TenantName: tenant.Name,
            Email: "owner@tenant-a.test",
            DisplayName: "Tenant A Owner",
            Role: "Owner");
        ownerContext.Items["ApiAllowedCompanyIds"] = new[] { assignedCompany.Id };

        Assert.True(await CompanyEndpointAccess.CanAccessCompanyAsync(clientContext, db, assignedCompany.Id));
        Assert.False(await CompanyEndpointAccess.CanAccessCompanyAsync(clientContext, db, otherCompany.Id));
        Assert.True(await CompanyEndpointAccess.CanAccessCompanyAsync(ownerContext, db, assignedCompany.Id));
        Assert.False(await CompanyEndpointAccess.CanAccessCompanyAsync(ownerContext, db, otherCompany.Id));
    }

    [Fact]
    public async Task CompanyEndpointAccess_UsesCompanyListAndPeriodOwnershipForDirectPeriodGuards()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
        var assignedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Assigned Client Limited");
        var otherCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Other Client Limited");
        var assignedPeriod = new AccountingPeriod
        {
            CompanyId = assignedCompany.Id,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = true
        };
        var otherPeriod = new AccountingPeriod
        {
            CompanyId = otherCompany.Id,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = true
        };
        db.AccountingPeriods.AddRange(assignedPeriod, otherPeriod);
        await db.SaveChangesAsync();
        var clientContext = new DefaultHttpContext();
        clientContext.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 1,
            TenantId: tenant.Id,
            TenantName: tenant.Name,
            Email: "client@tenant-a.test",
            DisplayName: "Tenant A Client",
            Role: "Client",
            AllowedCompanyIds: new HashSet<int> { assignedCompany.Id });

        Assert.True(await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(clientContext, db, assignedCompany.Id, assignedPeriod.Id));
        Assert.False(await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(clientContext, db, assignedCompany.Id, otherPeriod.Id));
        Assert.False(await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(clientContext, db, otherCompany.Id, otherPeriod.Id));
    }

    [Fact]
    public void CoreCompanyEndpoints_UseDirectCompanyAccessGuards()
    {
        var root = RepositoryRoot();
        var source = File
            .ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Program.cs"))
            .Replace("\r\n", "\n");
        var periodStatusEndpoint = File
            .ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Endpoints", "PeriodStatusEndpoint.cs"))
            .Replace("\r\n", "\n");
        var guardedCoreEndpoints = source + "\n" + periodStatusEndpoint;

        Assert.Contains("CompanyEndpointAccess.CanAccessCompanyAsync", guardedCoreEndpoints);
        Assert.Contains("CompanyListQuery.ForContext(context, db.Companies)", source);
        Assert.True(
            Regex.Matches(source, "CompanyEndpointAccess\\.CanAccessCompanyAsync\\(context, db, id\\)").Count >= 3,
            "Company get, update, and delete endpoints should guard direct company access.");
        Assert.True(
            Regex.Matches(guardedCoreEndpoints, "CompanyEndpointAccess\\.CanAccessCompanyAsync\\(context, db, companyId\\)").Count >= 8,
            "Officer and period endpoints should guard direct company access.");
        Assert.Contains("periods.MapPut(\"/{id:int}/status\", PeriodStatusEndpoint.UpdateAsync)", source);

        Assert.DoesNotContain("officers.MapGet(\"/\", async (int companyId, AccountsDbContext db)", source);
        Assert.DoesNotContain("officers.MapPost(\"/\", async (int companyId, CompanyOfficerInput input, AccountsDbContext db", source);
        Assert.DoesNotContain("officers.MapPut(\"/{id:int}\", async (int companyId, int id, CompanyOfficerInput input, AccountsDbContext db", source);
        Assert.DoesNotContain("officers.MapDelete(\"/{id:int}\", async (int companyId, int id, AccountsDbContext db", source);
        Assert.DoesNotContain("periods.MapGet(\"/\", async (int companyId, AccountsDbContext db)", source);
        Assert.DoesNotContain("periods.MapGet(\"/{id:int}\", async (int companyId, int id, AccountsDbContext db)", source);
        Assert.DoesNotContain("periods.MapPost(\"/\", async (int companyId, AccountingPeriodInput input, AccountsDbContext db)", source);
    }

    [Fact]
    public void ReportingOutputEndpoints_UseDirectCompanyPeriodAccessGuards()
    {
        foreach (var endpointFile in new[]
        {
            ("StatementsEndpoints.cs", 7),
            ("DocumentEndpoints.cs", 5),
            ("RevenueEndpoints.cs", 3)
        })
        {
            var source = File
                .ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", endpointFile.Item1))
                .Replace("\r\n", "\n");
            var mapGetCount = Regex.Matches(source, "\\.MapGet\\(").Count;

            Assert.Equal(endpointFile.Item2, mapGetCount);
            Assert.Contains("AccountsDbContext db", source);
            Assert.Contains("HttpContext context", source);

            foreach (Match match in Regex.Matches(source, "\\.MapGet\\("))
            {
                var remaining = source[(match.Index + match.Length)..];
                var nextEndpoint = Regex.Match(
                    remaining,
                    @"\n\s*\w*\.Map(?:Get|Post|Put|Patch|Delete)\(",
                    RegexOptions.None,
                    TimeSpan.FromSeconds(1));
                var snippet = nextEndpoint.Success
                    ? source[match.Index..(match.Index + match.Length + nextEndpoint.Index)]
                    : source[match.Index..];

                Assert.Contains("CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", snippet);
            }
        }
    }

    [Fact]
    public async Task ReportingOutputEndpoint_HidesInaccessibleCompanyPeriodAtRuntime()
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

        await using var app = builder.Build();
        int tenantId;
        int restrictedPeriodId;
        int allowedCompanyId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var tenant = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
            var allowedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Allowed Client Limited");
            var restrictedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Restricted Client Limited");
            var restrictedPeriod = new AccountingPeriod
            {
                CompanyId = restrictedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true
            };
            db.AccountingPeriods.Add(restrictedPeriod);
            await db.SaveChangesAsync();
            tenantId = tenant.Id;
            allowedCompanyId = allowedCompany.Id;
            restrictedPeriodId = restrictedPeriod.Id;
        }

        app.Use(async (context, next) =>
        {
            context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
                UserId: 1,
                TenantId: tenantId,
                TenantName: "Tenant A",
                Email: "client@tenant-a.test",
                DisplayName: "Tenant A Client",
                Role: "Client",
                AllowedCompanyIds: new HashSet<int> { allowedCompanyId });
            await next();
        });
        app.MapStatementsEndpoints();
        await app.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(LoopbackBaseAddress(app)) };
            var response = await client.GetAsync($"/api/companies/{allowedCompanyId}/periods/{restrictedPeriodId}/statements/readiness");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }

    }

    [Fact]
    public void FilingWorkflowWriteEndpoints_UseDirectPeriodAccessAndRequestAuthorization()
    {
        var source = File
            .ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "FilingWorkflowEndpoints.cs"))
            .Replace("\r\n", "\n");

        var statusSnippet = FilingWorkflowSnippet(source, "group.MapGet(\"/status\"");
        Assert.Contains("AccountsDbContext db", statusSnippet);
        Assert.Contains("HttpContext context", statusSnippet);
        Assert.Contains("CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", statusSnippet);

        foreach (var marker in new[]
        {
            "group.MapPut(\"/cro-status\"",
            "group.MapPost(\"/cro-payment\"",
            "group.MapPost(\"/charity-report-generated\"",
            "group.MapPut(\"/charity-status\"",
            "public static async Task<IResult> ValidateIxbrlEndpointAsync"
        })
        {
            var snippet = FilingWorkflowSnippet(source, marker);
            Assert.Contains("AccountsDbContext db", snippet);
            Assert.Contains("ApiAccessService apiAccess", snippet);
            Assert.Contains("HttpContext context", snippet);
            Assert.Contains("CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", snippet);
            Assert.Contains("EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", snippet);
            AssertOccursBefore(snippet, "CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)");
            AssertOccursBefore(snippet, "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", "service.");
        }

        var retiredMarkGeneratedSnippet = FilingWorkflowSnippet(source, "group.MapPost(\"/mark-generated\"");
        Assert.Contains("AccountsDbContext db", retiredMarkGeneratedSnippet);
        Assert.Contains("ApiAccessService apiAccess", retiredMarkGeneratedSnippet);
        Assert.Contains("HttpContext context", retiredMarkGeneratedSnippet);
        Assert.Contains("CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", retiredMarkGeneratedSnippet);
        Assert.Contains("EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", retiredMarkGeneratedSnippet);
        Assert.Contains("StatusCodes.Status410Gone", retiredMarkGeneratedSnippet);
        Assert.DoesNotContain("service.", retiredMarkGeneratedSnippet);
        AssertOccursBefore(retiredMarkGeneratedSnippet, "CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)");
        AssertOccursBefore(retiredMarkGeneratedSnippet, "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", "StatusCodes.Status410Gone");

        static string FilingWorkflowSnippet(string source, string marker)
        {
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Expected to find filing workflow marker {marker}.");
            var next = Regex.Match(source[(start + marker.Length)..], @"\n\s*(?:group\.Map(?:Get|Post|Put|Delete)|public static|private static)", RegexOptions.None, TimeSpan.FromSeconds(1));
            return next.Success
                ? source[start..(start + marker.Length + next.Index)]
                : source[start..];
        }

        static void AssertOccursBefore(string snippet, string first, string second)
        {
            var firstIndex = snippet.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = snippet.IndexOf(second, StringComparison.Ordinal);
            Assert.True(firstIndex >= 0, $"Expected snippet to contain '{first}'.");
            Assert.True(secondIndex >= 0, $"Expected snippet to contain '{second}'.");
            Assert.True(firstIndex < secondIndex, $"Expected '{first}' before '{second}'.");
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

    [Fact]
    public void CoreCompanyWriteEndpoints_UseDirectRequestAuthorization()
    {
        var root = RepositoryRoot();
        var source = File
            .ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Program.cs"))
            .Replace("\r\n", "\n");
        var periodStatusEndpoint = File
            .ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Endpoints", "PeriodStatusEndpoint.cs"))
            .Replace("\r\n", "\n");
        var guardedCoreEndpoints = source + "\n" + periodStatusEndpoint;

        Assert.Contains("EndpointRequestAuthorization.AuthorizeCurrentRequest", guardedCoreEndpoints);
        Assert.True(
            Regex.Matches(guardedCoreEndpoints, "EndpointRequestAuthorization\\.AuthorizeCurrentRequest\\(context, apiAccess\\)").Count >= 8,
            "Core company write endpoints should guard direct API and role authorization.");

        foreach (var marker in new[]
        {
            "companies.MapPost",
            "companies.MapPut",
            "companies.MapDelete",
            "officers.MapPost",
            "officers.MapPut",
            "officers.MapDelete",
            "periods.MapPost"
        })
        {
            var snippet = EndpointSnippet(source, marker);
            Assert.Contains("EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", snippet);
            Assert.Contains("ApiAccessService apiAccess", snippet);
        }
        Assert.Contains("periods.MapPut(\"/{id:int}/status\", PeriodStatusEndpoint.UpdateAsync)", source);
        Assert.Contains("ApiAccessService apiAccess", periodStatusEndpoint);
        Assert.Contains("EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", periodStatusEndpoint);

        foreach (var marker in new[]
        {
            "companies.MapPut",
            "companies.MapDelete"
        })
        {
            var snippet = EndpointSnippet(source, marker);
            AssertOccursBefore(snippet, "CompanyEndpointAccess.CanAccessCompanyAsync(context, db, id)", "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)");
        }

        foreach (var marker in new[]
        {
            "officers.MapPost",
            "officers.MapPut",
            "officers.MapDelete",
            "periods.MapPost"
        })
        {
            var snippet = EndpointSnippet(source, marker);
            AssertOccursBefore(snippet, "CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId)", "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)");
        }
        AssertOccursBefore(periodStatusEndpoint, "CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId)", "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)");

        AssertOccursBefore(EndpointSnippet(source, "companies.MapPut"), "CompanyEndpointAccess.CanAccessCompanyAsync(context, db, id)", "EndpointInputs.ValidateCompany(input)");

        static string EndpointSnippet(string source, string marker)
        {
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Expected to find endpoint marker {marker}.");
            var nextMap = Regex.Match(source[start..], @"\n(?:companies|officers|periods)\.Map(?:Get|Post|Put|Delete)", RegexOptions.None, TimeSpan.FromSeconds(1));
            var end = nextMap.Success && nextMap.Index > 0
                ? start + nextMap.Index
                : source.IndexOf("\n\n//", start, StringComparison.Ordinal);
            return end > start ? source[start..end] : source[start..];
        }

        static void AssertOccursBefore(string snippet, string first, string second)
        {
            var firstIndex = snippet.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = snippet.IndexOf(second, StringComparison.Ordinal);
            Assert.True(firstIndex >= 0, $"Expected to find {first}.");
            Assert.True(secondIndex >= 0, $"Expected to find {second}.");
            Assert.True(firstIndex < secondIndex, $"Expected {first} before {second}.");
        }
    }

    [Theory]
    [InlineData("Accountant", "POST", "/api/companies", false)]
    [InlineData("Client", "POST", "/api/companies/1/periods", false)]
    [InlineData("Client", "POST", "/api/companies/1/periods/2/documents/cro-filing-pack", false)]
    [InlineData("Accountant", "POST", "/api/companies/1/periods", true)]
    [InlineData("Accountant", "POST", "/api/companies/1/periods/2/documents/cro-filing-pack", true)]
    [InlineData("Reviewer", "POST", "/api/companies/1/periods/2/adjustments/3/approve", true)]
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
    public async Task PeriodLockMiddleware_BlocksAccountingWritesToFinalisedPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/debtors";
        var middleware = new PeriodLockMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }

    [Fact]
    public async Task PeriodLockMiddleware_AllowsReadsToFinalisedPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/debtors";
        var middleware = new PeriodLockMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("PUT", "filing/cro-status")]
    [InlineData("POST", "filing/cro-payment")]
    [InlineData("POST", "documents/cro-filing-pack")]
    [InlineData("POST", "documents/signature-page")]
    [InlineData("POST", "mark-filed")]
    [InlineData("POST", "deadlines/calculate")]
    public async Task PeriodLockMiddleware_AllowsExactPostLockWorkflowWritesToFinalisedPeriod(string method, string suffix)
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = method;
        context.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/{suffix}";
        var middleware = new PeriodLockMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("POST", "filing/mark-generated")]
    [InlineData("POST", "filing/validate-ixbrl")]
    [InlineData("POST", "filing/cro-payment/extra")]
    [InlineData("POST", "documents/cro-filing-pack/extra")]
    [InlineData("POST", "deadlines/calculate/extra")]
    [InlineData("PUT", "filing/mark-generated")]
    public async Task PeriodLockMiddleware_BlocksPreparationAndFalsePositiveWorkflowWritesToFinalisedPeriod(string method, string suffix)
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = method;
        context.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/{suffix}";
        var middleware = new PeriodLockMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }

    [Fact]
    public async Task AccountingWriteGuard_BlocksCompanyAccountingWritesWhenAnyPeriodLocked()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var guard = new AccountingWriteGuard(db);

        var decision = await guard.CheckCompanyAccountingWriteAsync(period.CompanyId);

        Assert.False(decision.CanWrite);
        Assert.Contains("locked", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AccountingWriteGuard_AllowsFutureDatedCompanyAccountingWritesAfterLockedPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var guard = new AccountingWriteGuard(db);

        var decision = await guard.CheckCompanyAccountingWriteAsync(period.CompanyId, period.PeriodEnd.AddDays(1));

        Assert.True(decision.CanWrite);
    }

    [Fact]
    public async Task AccountingWriteGuard_BlocksMasterDataWritesWhenAnyPeriodLocked()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var guard = new AccountingWriteGuard(db);

        var decision = await guard.CheckCompanyMasterDataWriteAsync(period.CompanyId);

        Assert.False(decision.CanWrite);
        Assert.Contains("locked", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CharityEndpoints_UseDirectAccessAndRequestAuthorizationGuards()
    {
        var source = File
            .ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "CharityEndpoints.cs"))
            .Replace("\r\n", "\n");

        var companyInfoRead = CharityMapSnippet(source, "companyGroup.MapGet(\"/info\"");
        Assert.Contains("AccountsDbContext db", companyInfoRead);
        Assert.Contains("HttpContext context", companyInfoRead);
        Assert.Contains("CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId)", companyInfoRead);
        Assert.DoesNotContain("EndpointRequestAuthorization.AuthorizeCurrentRequest", companyInfoRead);

        var companyInfoWrite = CharityMemberSnippet(source, "public static async Task<IResult> SaveCharityInfoEndpointAsync");
        Assert.Contains("AccountsDbContext db", companyInfoWrite);
        Assert.Contains("HttpContext context", companyInfoWrite);
        Assert.Contains("ApiAccessService apiAccess", companyInfoWrite);
        Assert.Contains("AuditService audit", companyInfoWrite);
        Assert.Contains("CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId)", companyInfoWrite);
        Assert.Contains("EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", companyInfoWrite);
        Assert.Contains("audit.LogAsync(", companyInfoWrite);
        AssertOccursBefore(companyInfoWrite, "CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId)", "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)");
        AssertOccursBefore(companyInfoWrite, "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", "writeGuard.BlockIfCompanyMasterDataLockedAsync(companyId)");
        AssertOccursBefore(companyInfoWrite, "writeGuard.BlockIfCompanyMasterDataLockedAsync(companyId)", "service.SaveCharityInfoAsync(companyId, input)");
        AssertOccursBefore(companyInfoWrite, "service.SaveCharityInfoAsync(companyId, input)", "audit.LogAsync(");

        foreach (var marker in new[]
        {
            "periodGroup.MapGet(\"/sofa\"",
            "periodGroup.MapGet(\"/trustees-report\""
        })
        {
            var snippet = CharityMapSnippet(source, marker);
            Assert.Contains("AccountsDbContext db", snippet);
            Assert.Contains("HttpContext context", snippet);
            Assert.Contains("CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", snippet);
            Assert.DoesNotContain("EndpointRequestAuthorization.AuthorizeCurrentRequest", snippet);
        }

        var listFunds = CharityMemberSnippet(source, "public static async Task<IResult> ListFundBalancesEndpointAsync");
        Assert.Contains("HttpContext context", listFunds);
        Assert.Contains("CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", listFunds);
        Assert.DoesNotContain("ApiAccessService apiAccess", listFunds);
        Assert.DoesNotContain("EndpointRequestAuthorization.AuthorizeCurrentRequest", listFunds);

        foreach (var marker in new[]
        {
            "public static async Task<IResult> CreateFundBalanceEndpointAsync",
            "public static async Task<IResult> UpdateFundBalanceEndpointAsync",
            "public static async Task<IResult> DeleteFundBalanceEndpointAsync"
        })
        {
            var snippet = CharityMemberSnippet(source, marker);
            Assert.Contains("HttpContext context", snippet);
            Assert.Contains("ApiAccessService apiAccess", snippet);
            Assert.Contains("CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", snippet);
            Assert.Contains("EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", snippet);
            AssertOccursBefore(snippet, "CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)");
            AssertOccursBefore(snippet, "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", "ValidateFundWritePeriodAsync(db, companyId, periodId)");
        }

        static string CharityMemberSnippet(string source, string marker)
        {
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Expected to find charity endpoint marker {marker}.");
            var nextMember = Regex.Match(source[(start + marker.Length)..], @"\n    (?:public|private) static", RegexOptions.None, TimeSpan.FromSeconds(1));
            return nextMember.Success
                ? source[start..(start + marker.Length + nextMember.Index)]
                : source[start..];
        }

        static string CharityMapSnippet(string source, string marker)
        {
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Expected to find charity map marker {marker}.");
            var nextMap = Regex.Match(source[(start + marker.Length)..], @"\n\s*(?:companyGroup\.Map(?:Get|Put)|periodGroup\.Map(?:Get|Post|Put|Delete)|public static)", RegexOptions.None, TimeSpan.FromSeconds(1));
            return nextMap.Success
                ? source[start..(start + marker.Length + nextMap.Index)]
                : source[start..];
        }

        static void AssertOccursBefore(string snippet, string first, string second)
        {
            var firstIndex = snippet.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = snippet.IndexOf(second, StringComparison.Ordinal);
            Assert.True(firstIndex >= 0, $"Expected snippet to contain '{first}'.");
            Assert.True(secondIndex >= 0, $"Expected snippet to contain '{second}'.");
            Assert.True(firstIndex < secondIndex, $"Expected '{first}' before '{second}'.");
        }
    }

    [Fact]
    public async Task CharityRoutes_EnforceRuntimeCompanyPeriodRoleApiAndLockGuards()
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
                },
                new ApiAccessKeyConfig
                {
                    Name = "Admin",
                    Role = "Admin",
                    DevelopmentKey = "admin",
                    AllowedCompanyIds = []
                }
            ];
        });
        builder.Services.AddScoped<ApiAccessService>();
        builder.Services.AddScoped<AccountingWriteGuard>();
        builder.Services.AddScoped<AuditService>();
        builder.Services.AddScoped<CharityReportingService>();

        await using var app = builder.Build();
        int tenantId;
        int allowedCompanyId;
        int restrictedCompanyId;
        int lockedCompanyId;
        int allowedPeriodId;
        int restrictedPeriodId;
        int lockedPeriodId;
        int deleteFundId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var tenant = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
            var allowedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Allowed Charity Limited");
            var lockedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Locked Charity Limited");
            var restrictedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Restricted Charity Limited");
            var allowedPeriod = new AccountingPeriod
            {
                CompanyId = allowedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true
            };
            var lockedPeriod = new AccountingPeriod
            {
                CompanyId = lockedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true,
                Status = PeriodStatus.Finalised,
                LockedAt = DateTime.UtcNow,
                LockedBy = "Reviewer"
            };
            var restrictedPeriod = new AccountingPeriod
            {
                CompanyId = restrictedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true
            };
            db.AccountingPeriods.AddRange(allowedPeriod, lockedPeriod, restrictedPeriod);
            db.CharityInfos.AddRange(
                new CharityInfo
                {
                    CompanyId = allowedCompany.Id,
                    CharityNumber = "CHY-ALLOWED",
                    CharityType = "CLG",
                    GrossIncome = 100_000m
                },
                new CharityInfo
                {
                    CompanyId = restrictedCompany.Id,
                    CharityNumber = "CHY-HIDDEN",
                    CharityType = "CLG",
                    GrossIncome = 100_000m
                });
            await db.SaveChangesAsync();

            var generalFund = new FundBalance
            {
                PeriodId = allowedPeriod.Id,
                FundName = "General fund",
                FundType = "Unrestricted",
                OpeningBalance = 100m,
                ClosingBalance = 100m
            };
            var deleteFund = new FundBalance
            {
                PeriodId = allowedPeriod.Id,
                FundName = "Delete fund",
                FundType = "Restricted",
                OpeningBalance = 50m,
                ClosingBalance = 50m
            };
            var hiddenFund = new FundBalance
            {
                PeriodId = restrictedPeriod.Id,
                FundName = "Hidden fund",
                FundType = "Restricted",
                OpeningBalance = 900m,
                ClosingBalance = 900m
            };
            db.FundBalances.AddRange(generalFund, deleteFund, hiddenFund);
            await db.SaveChangesAsync();

            tenantId = tenant.Id;
            allowedCompanyId = allowedCompany.Id;
            restrictedCompanyId = restrictedCompany.Id;
            lockedCompanyId = lockedCompany.Id;
            allowedPeriodId = allowedPeriod.Id;
            restrictedPeriodId = restrictedPeriod.Id;
            lockedPeriodId = lockedPeriod.Id;
            deleteFundId = deleteFund.Id;
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
                AllowedCompanyIds: new HashSet<int> { allowedCompanyId, lockedCompanyId });
            await next();
        });
        app.MapCharityEndpoints();
        await app.StartAsync();

        try
        {
            var addresses = app.Services
                .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features
                .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(addresses!.Addresses)) };
            var charityInfoBody = new
            {
                charityNumber = "CHY-WRITER",
                charityType = "CLG",
                grossIncome = 200_000m,
                charitableObjectives = "Community services",
                principalActivities = "Local programmes"
            };
            var fundBody = new
            {
                fundName = "New restricted grant",
                fundType = "Restricted",
                openingBalance = 10m,
                incomingResources = 25m,
                resourcesExpended = 5m,
                transfers = 0m,
                gainsLosses = 0m,
                notes = "Grant letter"
            };

            using var hiddenInfoRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/charity/info",
                role: "Client");
            var hiddenInfo = await client.SendAsync(hiddenInfoRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenInfo.StatusCode);

            using var hiddenFundsRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/periods/{restrictedPeriodId}/charity/funds",
                role: "Client");
            var hiddenFunds = await client.SendAsync(hiddenFundsRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenFunds.StatusCode);

            using var hiddenSofaRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/periods/{restrictedPeriodId}/charity/sofa",
                role: "Client");
            var hiddenSofa = await client.SendAsync(hiddenSofaRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenSofa.StatusCode);

            using var reviewerInfo = Request(
                HttpMethod.Put,
                $"/api/companies/{allowedCompanyId}/charity/info",
                role: "Reviewer",
                apiKey: "writer",
                body: charityInfoBody);
            var reviewerInfoDenied = await client.SendAsync(reviewerInfo);
            Assert.Equal(HttpStatusCode.Forbidden, reviewerInfoDenied.StatusCode);

            using var readerInfo = Request(
                HttpMethod.Put,
                $"/api/companies/{allowedCompanyId}/charity/info",
                role: "Accountant",
                apiKey: "reader",
                body: charityInfoBody);
            var readerInfoDenied = await client.SendAsync(readerInfo);
            Assert.Equal(HttpStatusCode.Unauthorized, readerInfoDenied.StatusCode);

            using var writerInfo = Request(
                HttpMethod.Put,
                $"/api/companies/{allowedCompanyId}/charity/info",
                role: "Accountant",
                apiKey: "writer",
                body: charityInfoBody);
            var writerInfoAllowed = await client.SendAsync(writerInfo);
            Assert.Equal(HttpStatusCode.OK, writerInfoAllowed.StatusCode);

            using var reviewerCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/charity/funds",
                role: "Reviewer",
                apiKey: "writer",
                body: fundBody);
            var reviewerCreateDenied = await client.SendAsync(reviewerCreate);
            Assert.Equal(HttpStatusCode.Forbidden, reviewerCreateDenied.StatusCode);

            using var readerCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/charity/funds",
                role: "Accountant",
                apiKey: "reader",
                body: fundBody);
            var readerCreateDenied = await client.SendAsync(readerCreate);
            Assert.Equal(HttpStatusCode.Unauthorized, readerCreateDenied.StatusCode);

            using var writerCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/charity/funds",
                role: "Accountant",
                apiKey: "writer",
                body: fundBody);
            var writerCreateAllowed = await client.SendAsync(writerCreate);
            Assert.Equal(HttpStatusCode.Created, writerCreateAllowed.StatusCode);

            using var lockedCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{lockedCompanyId}/periods/{lockedPeriodId}/charity/funds",
                role: "Accountant",
                apiKey: "writer",
                body: fundBody);
            var lockedCreateDenied = await client.SendAsync(lockedCreate);
            Assert.Equal(HttpStatusCode.Conflict, lockedCreateDenied.StatusCode);

            using var writerDelete = Request(
                HttpMethod.Delete,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/charity/funds/{deleteFundId}",
                role: "Accountant",
                apiKey: "writer");
            var writerDeleteDenied = await client.SendAsync(writerDelete);
            Assert.Equal(HttpStatusCode.Unauthorized, writerDeleteDenied.StatusCode);

            using var adminDelete = Request(
                HttpMethod.Delete,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/charity/funds/{deleteFundId}",
                role: "Accountant",
                apiKey: "admin");
            var adminDeleteAllowed = await client.SendAsync(adminDelete);
            Assert.Equal(HttpStatusCode.NoContent, adminDeleteAllowed.StatusCode);

            await using var verifyScope = app.Services.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var info = await verifyDb.CharityInfos.SingleAsync(c => c.CompanyId == allowedCompanyId);
            Assert.Equal("CHY-WRITER", info.CharityNumber);
            Assert.Equal(2, await verifyDb.FundBalances.CountAsync(f => f.PeriodId == allowedPeriodId));
            Assert.False(await verifyDb.FundBalances.AnyAsync(f => f.Id == deleteFundId));
            Assert.Equal(2, await verifyDb.AuditLogs.CountAsync(a => a.EntityType == "FundBalance"));
            var charityAudit = await verifyDb.AuditLogs.SingleAsync(a => a.EntityType == "CharityInfo");
            Assert.Equal(AuditEventCodes.CharityInfoUpdated, charityAudit.Action);
            Assert.Contains("\"CharityNumber\":\"CHY-ALLOWED\"", charityAudit.OldValueJson);
            Assert.Contains("\"CharityNumber\":\"CHY-WRITER\"", charityAudit.NewValueJson);
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
    public async Task CharityInfoEndpoint_BlocksCompanyAccountingWriteWhenAnyPeriodLocked()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var service = new CharityReportingService(db);
        var guard = new AccountingWriteGuard(db);
        var context = AuthenticatedRequest("Accountant", HttpMethods.Put, $"/api/companies/{period.CompanyId}/charity/info");
        var input = new CharityInfo
        {
            CompanyId = 999,
            CharityNumber = "CHY-1",
            CharityType = "CLG",
            GrossIncome = 100_000m
        };

        var result = await CharityEndpoints.SaveCharityInfoEndpointAsync(
            period.CompanyId,
            input,
            service,
            guard,
            new AuditService(db),
            db,
            context,
            DisabledApiAccess());

        Assert.IsAssignableFrom<IResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, ResultStatusCode(result));
        Assert.Null(await db.CharityInfos.FirstOrDefaultAsync(c => c.CompanyId == period.CompanyId));
    }

    [Fact]
    public async Task CharityInfoEndpoint_AuditsCreateAndUpdateWithRouteOwnedFields()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var service = new CharityReportingService(db);
        var guard = new AccountingWriteGuard(db);
        var audit = new AuditService(db);
        var apiAccess = DisabledApiAccess();

        var create = await CharityEndpoints.SaveCharityInfoEndpointAsync(
            period.CompanyId,
            new CharityInfo
            {
                Id = 12_345,
                CompanyId = 99_999,
                CharityNumber = "CHY-CREATE",
                CharityType = "CLG",
                GrossIncome = 100_000m,
                CharitableObjectives = "Community development"
            },
            service,
            guard,
            audit,
            db,
            AuthenticatedRequest("Accountant", HttpMethods.Put, $"/api/companies/{period.CompanyId}/charity/info"),
            apiAccess);

        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(create));
        var created = await db.CharityInfos.SingleAsync();
        Assert.NotEqual(12_345, created.Id);
        Assert.Equal(period.CompanyId, created.CompanyId);

        var update = await CharityEndpoints.SaveCharityInfoEndpointAsync(
            period.CompanyId,
            new CharityInfo
            {
                CompanyId = 88_888,
                CharityNumber = "CHY-UPDATE",
                CharityType = "CLG",
                GrossIncome = 750_000m,
                CharitableObjectives = "Community development and education",
                TrusteeRemunerationPaid = true,
                TrusteeRemunerationAmount = 500m
            },
            service,
            guard,
            audit,
            db,
            AuthenticatedRequest("Accountant", HttpMethods.Put, $"/api/companies/{period.CompanyId}/charity/info"),
            apiAccess);

        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(update));
        var audits = await db.AuditLogs.Where(a => a.EntityType == "CharityInfo").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal([AuditEventCodes.CharityInfoCreated, AuditEventCodes.CharityInfoUpdated], audits.Select(a => a.Action).ToArray());
        Assert.Null(audits[0].OldValueJson);
        Assert.Contains("\"CharityNumber\":\"CHY-CREATE\"", audits[0].NewValueJson);
        Assert.Contains("\"CharityNumber\":\"CHY-CREATE\"", audits[1].OldValueJson);
        Assert.Contains("\"CharityNumber\":\"CHY-UPDATE\"", audits[1].NewValueJson);
        Assert.Contains("\"SorpTier\":2", audits[1].NewValueJson);
        Assert.All(audits, auditLog =>
        {
            Assert.Equal(period.CompanyId, auditLog.CompanyId);
            Assert.Null(auditLog.PeriodId);
            Assert.Equal("user@example.ie", auditLog.UserId);
        });
    }

    [Fact]
    public async Task CharityFundEvidence_LogsCreateUpdateDeleteAuditsAndRecalculatesClosingBalance()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var apiAccess = DisabledApiAccess();
        var createContext = AuthenticatedRequest("Accountant", HttpMethods.Post, $"/api/companies/{period.CompanyId}/periods/{period.Id}/charity/funds");

        var create = await CharityEndpoints.CreateFundBalanceEndpointAsync(
            period.CompanyId,
            period.Id,
            new FundBalance
            {
                Id = 12_345,
                PeriodId = 99_999,
                FundName = "Restricted grant",
                FundType = "Restricted",
                OpeningBalance = 100m,
                IncomingResources = 250m,
                ResourcesExpended = 40m,
                Transfers = -10m,
                GainsLosses = 5m,
                Notes = "Grant award letter"
            },
            db,
            audit,
            createContext,
            apiAccess);

        Assert.Equal(StatusCodes.Status201Created, ResultStatusCode(create));
        var fund = await db.FundBalances.SingleAsync();
        Assert.NotEqual(12_345, fund.Id);
        Assert.Equal(period.Id, fund.PeriodId);
        Assert.Equal(305m, fund.ClosingBalance);

        var updateContext = AuthenticatedRequest("Accountant", HttpMethods.Put, $"/api/companies/{period.CompanyId}/periods/{period.Id}/charity/funds/{fund.Id}");
        var update = await CharityEndpoints.UpdateFundBalanceEndpointAsync(
            period.CompanyId,
            period.Id,
            fund.Id,
            new FundBalance
            {
                FundName = "Restricted grant revised",
                FundType = "Restricted",
                OpeningBalance = 100m,
                IncomingResources = 300m,
                ResourcesExpended = 75m,
                Transfers = -15m,
                GainsLosses = 0m,
                Notes = "Trustee-approved revision"
            },
            db,
            audit,
            updateContext,
            apiAccess);
        var deleteContext = AuthenticatedRequest("Accountant", HttpMethods.Delete, $"/api/companies/{period.CompanyId}/periods/{period.Id}/charity/funds/{fund.Id}");
        var delete = await CharityEndpoints.DeleteFundBalanceEndpointAsync(
            period.CompanyId,
            period.Id,
            fund.Id,
            db,
            audit,
            deleteContext,
            apiAccess);

        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(update));
        Assert.Equal(StatusCodes.Status204NoContent, ResultStatusCode(delete));
        Assert.Empty(await db.FundBalances.ToListAsync());

        var audits = await db.AuditLogs.Where(a => a.EntityType == "FundBalance").OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(
            [AuditEventCodes.FundBalanceCreated, AuditEventCodes.FundBalanceUpdated, AuditEventCodes.FundBalanceDeleted],
            audits.Select(a => a.Action).ToArray());
        Assert.Null(audits[0].OldValueJson);
        Assert.Contains("\"FundName\":\"Restricted grant\"", audits[0].NewValueJson);
        Assert.Contains("\"ClosingBalance\":305", audits[0].NewValueJson);
        Assert.Contains("\"ClosingBalance\":305", audits[1].OldValueJson);
        Assert.Contains("\"ClosingBalance\":310", audits[1].NewValueJson);
        Assert.Contains("\"Deleted\":true", audits[2].NewValueJson);
        Assert.All(audits, auditLog =>
        {
            Assert.Equal(period.CompanyId, auditLog.CompanyId);
            Assert.Equal(period.Id, auditLog.PeriodId);
            Assert.Equal("user@example.ie", auditLog.UserId);
        });
    }

    [Fact]
    public async Task CharityFundEvidence_GuardsMismatchedAndLockedPeriodWritesWithoutMutationOrAudit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FundBalances.Add(new FundBalance
        {
            PeriodId = period.Id,
            FundName = "General fund",
            FundType = "Unrestricted",
            OpeningBalance = 100m,
            ClosingBalance = 100m
        });
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var apiAccess = DisabledApiAccess();

        var mismatchedCreate = await CharityEndpoints.CreateFundBalanceEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new FundBalance { FundName = "Wrong period", FundType = "Restricted", OpeningBalance = 1m },
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Post, $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/charity/funds"),
            apiAccess);
        var mismatchedUpdate = await CharityEndpoints.UpdateFundBalanceEndpointAsync(
            otherPeriod.CompanyId,
            period.Id,
            (await db.FundBalances.SingleAsync()).Id,
            new FundBalance { FundName = "Wrong company", FundType = "Restricted", OpeningBalance = 2m },
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Put, $"/api/companies/{otherPeriod.CompanyId}/periods/{period.Id}/charity/funds/{(await db.FundBalances.SingleAsync()).Id}"),
            apiAccess);
        var invalidCreate = await CharityEndpoints.CreateFundBalanceEndpointAsync(
            period.CompanyId,
            period.Id,
            new FundBalance { FundName = " ", FundType = "Restricted", OpeningBalance = 1m },
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Post, $"/api/companies/{period.CompanyId}/periods/{period.Id}/charity/funds"),
            apiAccess);
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var lockedDelete = await CharityEndpoints.DeleteFundBalanceEndpointAsync(
            period.CompanyId,
            period.Id,
            (await db.FundBalances.SingleAsync()).Id,
            db,
            audit,
            AuthenticatedRequest("Accountant", HttpMethods.Delete, $"/api/companies/{period.CompanyId}/periods/{period.Id}/charity/funds/{(await db.FundBalances.SingleAsync()).Id}"),
            apiAccess);

        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(mismatchedCreate));
        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(mismatchedUpdate));
        Assert.Equal(StatusCodes.Status400BadRequest, ResultStatusCode(invalidCreate));
        Assert.Equal(StatusCodes.Status409Conflict, ResultStatusCode(lockedDelete));
        var fund = await db.FundBalances.SingleAsync();
        Assert.Equal("General fund", fund.FundName);
        Assert.Empty(await db.AuditLogs.Where(a => a.EntityType == "FundBalance").ToListAsync());
    }

    [Fact]
    public async Task CharityReporting_SofaRejectsPeriodFromAnotherCompany()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FundBalances.Add(new FundBalance
        {
            PeriodId = otherPeriod.Id,
            FundName = "Other company restricted fund",
            FundType = "Restricted",
            OpeningBalance = 0m,
            IncomingResources = 1_000m,
            ClosingBalance = 1_000m
        });
        await db.SaveChangesAsync();
        var service = new CharityReportingService(db);

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            service.GenerateSofaAsync(period.CompanyId, otherPeriod.Id));
    }

    [Fact]
    public async Task DirectorLoanInputs_RejectsDirectorFromAnotherCompany()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherDirector = await db.CompanyOfficers
            .FirstAsync(o => o.CompanyId == otherPeriod.CompanyId && o.Role == OfficerRole.Director);
        var input = new DirectorLoanInput(
            otherDirector.Id,
            OpeningBalance: 0,
            Advances: 1_000m,
            Repayments: 0,
            ClosingBalance: 1_000m,
            InterestRate: 5m,
            InterestCharged: 0,
            IsDocumented: true,
            LoanTerms: "Repayable on demand",
            MaxBalanceDuringYear: 1_000m);

        var validation = await DirectorLoanInputs.ValidateAsync(db, period.CompanyId, period.Id, input);

        Assert.NotNull(validation);
    }

    [Fact]
    public async Task DirectorLoanReporting_IgnoresDirtyRowsForDirectorsFromAnotherCompany()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var director = await db.CompanyOfficers
            .FirstAsync(o => o.CompanyId == period.CompanyId && o.Role == OfficerRole.Director);
        var otherDirector = await db.CompanyOfficers
            .FirstAsync(o => o.CompanyId == otherPeriod.CompanyId && o.Role == OfficerRole.Director);
        otherDirector.Name = "Other Company Director";
        var cleanLoan = new DirectorLoan
        {
            PeriodId = period.Id,
            DirectorId = director.Id,
            OpeningBalance = 100m,
            Advances = 600m,
            Repayments = 100m,
            ClosingBalance = 600m,
            IsDocumented = true,
            LoanTerms = "Documented board-approved advance",
            MaxBalanceDuringYear = 700m
        };
        var dirtyLoan = new DirectorLoan
        {
            PeriodId = period.Id,
            DirectorId = otherDirector.Id,
            OpeningBalance = 0m,
            Advances = 9_999m,
            Repayments = 0m,
            ClosingBalance = 9_999m,
            IsDocumented = false,
            LoanTerms = "Dirty legacy cross-company row",
            MaxBalanceDuringYear = 9_999m
        };
        db.DirectorLoans.AddRange(cleanLoan, dirtyLoan);
        await db.SaveChangesAsync();
        var compliance = new DirectorLoanComplianceService(db, new FinancialStatementsService(db));

        var status = await compliance.GetComplianceStatusAsync(period.CompanyId, period.Id);
        var note = await compliance.GenerateSection307NoteAsync(period.CompanyId, period.Id);
        var generatedNotes = await new NotesDisclosureService(db).GenerateNotesAsync(period.CompanyId, period.Id);

        var reportedLoan = Assert.Single(status.Loans);
        Assert.Equal(cleanLoan.Id, reportedLoan.Id);
        Assert.Equal(600m, status.TotalDirectorLoans);
        Assert.Contains(director.Name, note);
        Assert.DoesNotContain("Other Company Director", note);
        Assert.Contains(generatedNotes, n => n.Content?.Contains(director.Name, StringComparison.Ordinal) == true);
        Assert.DoesNotContain(generatedNotes, n => n.Content?.Contains("Other Company Director", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task DirectorLoanInputs_AllowsDirectorWhoServedDuringPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var director = await db.CompanyOfficers
            .FirstAsync(o => o.CompanyId == period.CompanyId && o.Role == OfficerRole.Director);
        director.AppointedDate = period.PeriodStart.AddDays(10);
        director.ResignedDate = period.PeriodEnd.AddDays(-10);
        await db.SaveChangesAsync();
        var input = new DirectorLoanInput(
            director.Id,
            OpeningBalance: 0,
            Advances: 1_000m,
            Repayments: 0,
            ClosingBalance: 1_000m,
            InterestRate: 5m,
            InterestCharged: 0,
            IsDocumented: true,
            LoanTerms: "Repaid before resignation",
            MaxBalanceDuringYear: 1_000m);

        var validation = await DirectorLoanInputs.ValidateAsync(db, period.CompanyId, period.Id, input);

        Assert.Null(validation);
    }

    [Fact]
    public async Task LoanBalanceSnapshotInputs_RejectsNegativeDueSplits()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var loan = new Loan
        {
            CompanyId = period.CompanyId,
            Lender = "AIB",
            OriginalAmount = 10_000m,
            Balance = 0m,
            DrawdownDate = period.PeriodStart,
            BalanceAsOfDate = period.PeriodEnd
        };
        db.Loans.Add(loan);
        await db.SaveChangesAsync();

        var negativeCurrent = await LoanBalanceSnapshotInputs.ValidateAsync(
            db,
            period.CompanyId,
            new LoanBalanceSnapshot
            {
                LoanId = loan.Id,
                OpeningBalance = 0m,
                Drawdowns = 0m,
                Repayments = 0m,
                ClosingBalance = 0m,
                DueWithinYear = -1m,
                DueAfterYear = 1m
            });
        var negativeLongTerm = await LoanBalanceSnapshotInputs.ValidateAsync(
            db,
            period.CompanyId,
            new LoanBalanceSnapshot
            {
                LoanId = loan.Id,
                OpeningBalance = 0m,
                Drawdowns = 0m,
                Repayments = 0m,
                ClosingBalance = 0m,
                DueWithinYear = 1m,
                DueAfterYear = -1m
            });

        Assert.NotNull(negativeCurrent);
        Assert.NotNull(negativeLongTerm);
    }

    [Fact]
    public async Task TransactionRuleInputs_RejectsCategoryFromAnotherCompany()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherCategory = AddCategory(db, otherPeriod.CompanyId, "7001", "Other income", AccountCategoryType.Income);
        var input = new TransactionRuleInput("Stripe", otherCategory.Id, 1);

        var validation = await TransactionRuleInputs.ValidateAsync(db, period.CompanyId, input);

        Assert.NotNull(validation);
    }

    [Fact]
    public void AddTenantUsersMigration_DoesNotCreateHardCodedBootstrapTenant()
    {
        var migrationPath = Directory
            .GetFiles(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Data", "Migrations"), "*_AddTenantUsersDemoSeed.cs")
            .Single();

        var migration = File.ReadAllText(migrationPath);

        Assert.DoesNotContain("default-firm", migration);
        Assert.DoesNotContain("UPDATE companies", migration);
    }

    [Fact]
    public void AddUserCompanyAccessMigration_DoesNotGrantClientsBlanketCompanyAccess()
    {
        var migrationPath = Directory
            .GetFiles(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Data", "Migrations"), "*_AddUserCompanyAccess.cs")
            .Single();

        var migration = File.ReadAllText(migrationPath);

        Assert.DoesNotContain("INSERT INTO user_company_accesses", migration);
        Assert.DoesNotContain("LOWER(TRIM(u.\"Role\")) = 'client'", migration);
        Assert.DoesNotContain("INNER JOIN companies", migration);
    }

    [Fact]
    public void AuthSessionRevocationMigration_BackfillsReadableSessionVersionAndLockoutCounters()
    {
        var migrationPath = Directory
            .GetFiles(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Data", "Migrations"), "*_AddAuthSessionRevocationAndLockout.cs")
            .Single();

        var migration = File.ReadAllText(migrationPath);

        Assert.Contains("table: \"user_accounts\"", migration);
        Assert.Contains("name: \"SessionVersion\"", migration);
        Assert.Contains("name: \"FailedLoginCount\"", migration);
        Assert.Contains("name: \"LastFailedLoginAt\"", migration);
        Assert.Contains("name: \"LockedUntilUtc\"", migration);
        Assert.Contains("defaultValue: 1", migration);
        Assert.Contains("defaultValue: 0", migration);
    }

    [Fact]
    public void PeriodReopenMigration_HasEfMigrationMetadata()
    {
        var migrationPath = Directory
            .GetFiles(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Data", "Migrations"), "*_AddPeriodReopenMetadata.cs")
            .Single();

        var migration = File.ReadAllText(migrationPath);

        Assert.Contains("[Migration(\"20260606230500_AddPeriodReopenMetadata\")]", migration);
        Assert.Contains("[DbContext(typeof(AccountsDbContext))]", migration);
    }

    [Fact]
    public void PeriodEffectiveDatesMigration_BackfillsBeforeAddingConstraints()
    {
        var migrationPath = Directory
            .GetFiles(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Data", "Migrations"), "*_AddPeriodEffectiveAccountingDates.cs")
            .Single();

        var migration = File.ReadAllText(migrationPath);
        var backfillIndex = migration.IndexOf("UPDATE bank_accounts", StringComparison.Ordinal);
        var constraintIndex = migration.IndexOf("CK_bank_accounts_opening_balance_date_required", StringComparison.Ordinal);

        Assert.Contains("UPDATE bank_accounts", migration);
        Assert.Contains("UPDATE loans", migration);
        Assert.Contains("UPDATE share_capitals", migration);
        Assert.True(backfillIndex >= 0 && constraintIndex > backfillIndex);
    }

    [Fact]
    public void LateHandWrittenMigrations_AreDiscoverableForAccountsDbContext()
    {
        var migrationsPath = Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Data", "Migrations");
        var filingReferenceMigration = File.ReadAllText(Path.Combine(
            migrationsPath,
            "20260608093000_AddFilingReferencesToDeadlineEvidence.cs"));
        var charityFilingMigration = File.ReadAllText(Path.Combine(
            migrationsPath,
            "20260608101500_AddCharityFilingPackageWorkflow.cs"));

        Assert.Contains("using Accounts.Api.Data;", filingReferenceMigration);
        Assert.Contains("using Microsoft.EntityFrameworkCore.Infrastructure;", filingReferenceMigration);
        Assert.Contains("[DbContext(typeof(AccountsDbContext))]", filingReferenceMigration);
        Assert.Contains("[Migration(\"20260608093000_AddFilingReferencesToDeadlineEvidence\")]", filingReferenceMigration);

        Assert.Contains("using Accounts.Api.Data;", charityFilingMigration);
        Assert.Contains("using Microsoft.EntityFrameworkCore.Infrastructure;", charityFilingMigration);
        Assert.Contains("[DbContext(typeof(AccountsDbContext))]", charityFilingMigration);
        Assert.Contains("[Migration(\"20260608101500_AddCharityFilingPackageWorkflow\")]", charityFilingMigration);
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
            ArdMonth = 9,
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
            ArdMonth = 9,
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
            ArdMonth = 9,
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
    public void ProductionCompose_DefinesControlledMigrationAndBootstrapJob()
    {
        var compose = File.ReadAllText(Path.Combine(RepositoryRoot(), "compose.production.yml"));

        Assert.Contains("command: [\"--migrate-only\"]", compose);
        Assert.DoesNotContain("command: [\"dotnet\", \"Accounts.Api.dll\", \"--migrate-only\"]", compose);
        Assert.Contains("--migrate-only", compose);
        Assert.Contains("BOOTSTRAP_OWNER_EMAIL", compose);
        Assert.Contains("BOOTSTRAP_OWNER_PASSWORD", compose);
        Assert.Contains("AuditIntegrity__ActiveKeyId", compose);
        Assert.Contains("AuditIntegrity__SigningKeys__0__KeyId", compose);
        Assert.Contains("AuditIntegrity__SigningKeys__0__SigningKey", compose);
        Assert.Contains("condition: service_completed_successfully", compose);
    }

    [Fact]
    public void ProductionBackupRunbook_ProvidesDumpRestoreAndVerificationWorkflow()
    {
        var root = RepositoryRoot();
        var runbookPath = Path.Combine(root, "Docs", "operations", "production-runbook.md");
        var backupScriptPath = Path.Combine(root, "scripts", "backup-postgres.ps1");
        var restoreScriptPath = Path.Combine(root, "scripts", "restore-postgres.ps1");
        var verifyScriptPath = Path.Combine(root, "scripts", "verify-postgres-backup.ps1");

        Assert.True(File.Exists(runbookPath), "Production operations runbook should document backup and restore procedures.");
        Assert.True(File.Exists(backupScriptPath), "Production backup script should create PostgreSQL backups.");
        Assert.True(File.Exists(restoreScriptPath), "Production restore script should support restore drills.");
        Assert.True(File.Exists(verifyScriptPath), "Production backup verification script should exercise restores.");

        var runbook = File.ReadAllText(runbookPath);
        var backupScript = File.ReadAllText(backupScriptPath);
        var restoreScript = File.ReadAllText(restoreScriptPath);
        var verifyScript = File.ReadAllText(verifyScriptPath);

        Assert.Contains("RPO", runbook);
        Assert.Contains("RTO", runbook);
        Assert.Contains("restore drill", runbook);
        Assert.Contains("sha256", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pg_dump", backupScript);
        Assert.Contains("--format=custom", backupScript);
        Assert.Contains("Get-FileHash", backupScript);
        Assert.DoesNotContain("[string]$OutputDirectory = \"backups\"", backupScript);
        Assert.Contains("[Parameter(Mandatory = $true)]", backupScript);
        Assert.Contains("[string]$OutputDirectory", backupScript);
        Assert.Contains("AllowRepositoryOutputForLocalDryRun", backupScript);
        Assert.Contains("OutputDirectory must be outside the repository", backupScript);
        Assert.Contains("PSScriptRoot", backupScript);
        Assert.Contains("GetFullPath", backupScript);
        Assert.Contains("ReparsePoint", backupScript);
        Assert.Contains(".Target", backupScript);
        Assert.Contains("Too many nested filesystem links", backupScript);
        Assert.Contains("finally", backupScript);
        Assert.Contains("rm -f", backupScript);
        Assert.Contains("function Invoke-NativeCommand", backupScript);
        Assert.Contains("Native command failed", backupScript);
        AssertWrappedNativeCommand(backupScript, "Create PostgreSQL backup inside container", "pg_dump");
        AssertWrappedNativeCommand(backupScript, "Copy PostgreSQL backup out of container", " cp ");
        AssertWrappedNativeCommand(backupScript, "Remove temporary PostgreSQL backup from container", "rm -f");
        Assert.Contains("outside the repository", runbook);
        Assert.Contains("AllowRepositoryOutputForLocalDryRun", runbook);
        Assert.Contains("pg_restore", restoreScript);
        Assert.Contains("--single-transaction", restoreScript);
        Assert.Contains("--exit-on-error", restoreScript);
        Assert.Contains("$LASTEXITCODE", restoreScript);
        Assert.Contains("Native command failed", restoreScript);
        Assert.Contains("RESTORE_CONFIRM", restoreScript);
        Assert.Contains(".sha256", restoreScript);
        Assert.Contains("Get-FileHash", restoreScript);
        Assert.Contains("AllowUnverifiedBackupRestore", restoreScript);
        Assert.Contains("finally", restoreScript);
        Assert.Contains("rm -f", restoreScript);
        Assert.Contains("function Invoke-NativeCommand", restoreScript);
        AssertWrappedNativeCommand(restoreScript, "Copy PostgreSQL backup into container", " cp ");
        AssertWrappedNativeCommand(restoreScript, "Restore PostgreSQL backup", "& docker @restoreArgs");
        AssertWrappedNativeCommand(restoreScript, "Remove temporary PostgreSQL backup from container", "rm -f");
        var restoreWrapperIndex = restoreScript.IndexOf("Invoke-NativeCommand \"Restore PostgreSQL backup\"", StringComparison.Ordinal);
        var successMessageIndex = restoreScript.IndexOf("Restore completed into database", StringComparison.Ordinal);
        Assert.True(successMessageIndex > restoreWrapperIndex, "Restore script should not print success before the wrapped restore command completes.");
        Assert.Contains("accounts_restore_verify", verifyScript);
        Assert.Contains("restore-postgres.ps1", verifyScript);
        Assert.Contains("function Invoke-NativeCommand", verifyScript);
        Assert.Contains("Native command failed", verifyScript);
        Assert.Contains("Invoke-ScalarQuery", verifyScript);
        Assert.Contains("Assert-RestoredCountMatchesSource", verifyScript);
        Assert.Contains("SourceDatabase", verifyScript);
        Assert.Contains("VerifyDatabase must be different from SourceDatabase", verifyScript);
        Assert.Contains(".Equals($SourceDatabase, [StringComparison]::OrdinalIgnoreCase)", verifyScript);
        AssertOccursBefore(verifyScript, "VerifyDatabase must be different from SourceDatabase", "Drop previous restore verification database");
        Assert.Contains("[switch]$RequireNonEmpty", verifyScript);
        Assert.Contains("if ($RequireNonEmpty -and $sourceCount -le 0)", verifyScript);
        Assert.Contains("restoredCount -ne $sourceCount", verifyScript);
        AssertWrappedNativeCommand(verifyScript, "Drop previous restore verification database", "dropdb");
        AssertWrappedNativeCommand(verifyScript, "Create restore verification database", "createdb");
        Assert.Contains("Invoke-ScalarQuery \"Count source $TableName\" $SourceDatabase $Sql", verifyScript);
        Assert.Contains("Invoke-ScalarQuery \"Count restored $TableName\" $VerifyDatabase $Sql", verifyScript);
        Assert.Contains("Assert-RestoredCountMatchesSource \"tenants\" \"select count(*) from tenants;\" -RequireNonEmpty", verifyScript);
        Assert.Contains("Assert-RestoredCountMatchesSource \"user accounts\" \"select count(*) from user_accounts;\" -RequireNonEmpty", verifyScript);
        Assert.Contains("Assert-RestoredCountMatchesSource \"companies\" \"select count(*) from companies;\"", verifyScript);
        Assert.Contains("Assert-RestoredCountMatchesSource \"accounting periods\" \"select count(*) from accounting_periods;\"", verifyScript);
        Assert.Contains("[switch]$KeepVerifyDatabase", verifyScript);
        Assert.Contains("finally", verifyScript);
        Assert.Contains("if (-not $KeepVerifyDatabase)", verifyScript);
        AssertWrappedNativeCommand(verifyScript, "Drop restore verification database", "dropdb");
        Assert.Contains("single transaction", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exit on the first restore error", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" sh -lc ", restoreScript);
        Assert.DoesNotContain(" sh -lc ", verifyScript);
        Assert.DoesNotContain("accounts_dev", backupScript);
        Assert.DoesNotContain("accounts_dev", restoreScript);

        static void AssertWrappedNativeCommand(string script, string description, string commandMarker)
        {
            var wrapperMarker = $"Invoke-NativeCommand \"{description}\"";
            var wrapperIndex = script.IndexOf(wrapperMarker, StringComparison.Ordinal);
            Assert.True(wrapperIndex >= 0, $"Expected script to wrap native command '{description}'.");

            var commandIndex = script.IndexOf(commandMarker, wrapperIndex, StringComparison.Ordinal);
            Assert.True(commandIndex > wrapperIndex, $"Expected wrapped command '{description}' to contain '{commandMarker}'.");

            var nextWrapperIndex = script.IndexOf("Invoke-NativeCommand \"", wrapperIndex + wrapperMarker.Length, StringComparison.Ordinal);
            Assert.True(nextWrapperIndex < 0 || commandIndex < nextWrapperIndex, $"Expected '{commandMarker}' inside wrapper '{description}'.");
        }
    }

    [Fact]
    public void ProductionSmokeRunbook_ExercisesFrontendProxySessionAndOptionalDownloads()
    {
        var root = RepositoryRoot();
        var runbookPath = Path.Combine(root, "Docs", "operations", "production-runbook.md");
        var smokeScriptPath = Path.Combine(root, "scripts", "smoke-production.ps1");

        Assert.True(File.Exists(smokeScriptPath), "Production smoke script should validate the deployed frontend/proxy/session path.");

        var runbook = File.ReadAllText(runbookPath);
        var smokeScript = File.ReadAllText(smokeScriptPath);

        Assert.Contains("smoke-production.ps1", runbook);
        Assert.Contains("ACCOUNTS_FRONTEND_URL", runbook);
        Assert.Contains("SMOKE_LOGIN_EMAIL", runbook);
        Assert.Contains("SMOKE_LOGIN_PASSWORD", runbook);
        Assert.Contains("/health/ready", smokeScript);
        Assert.Contains("/api/auth/login", smokeScript);
        Assert.Contains("/api/auth/me", smokeScript);
        Assert.Contains("accounts_csrf", smokeScript);
        Assert.Contains("Assert-SetCookieAttribute", smokeScript);
        Assert.Contains("$Headers -is [System.Net.WebHeaderCollection]", smokeScript);
        Assert.Contains("$Headers.GetValues($Name)", smokeScript);
        Assert.Contains("accounts_session", smokeScript);
        Assert.Contains("Cookie '$CookieName' from HTTPS login did not include '$Attribute'", smokeScript);
        Assert.Contains("-CookieName \"accounts_session\" -Attribute \"Secure\"", smokeScript);
        Assert.Contains("-CookieName \"accounts_csrf\" -Attribute \"Secure\"", smokeScript);
        Assert.Contains("X-CSRF-Token", smokeScript);
        Assert.Contains("/api/auth/logout", smokeScript);
        Assert.Contains("Checking session is cleared after logout", smokeScript);
        Assert.Contains("Expected /api/auth/me to be unauthorized after logout", smokeScript);
        Assert.Contains("System.Net.HttpStatusCode]::Unauthorized", smokeScript);
        Assert.Contains("Invoke-RestMethod", smokeScript);
        Assert.Contains("Invoke-WebRequest", smokeScript);
        Assert.Contains("-UseBasicParsing", smokeScript);
        Assert.Contains("-PassThru", smokeScript);
        Assert.Contains("$readyResponse = Invoke-WebRequest", smokeScript);
        Assert.Contains("$currentUserResponse = Invoke-WebRequest", smokeScript);
        Assert.Contains("$currentUserResponse.Content | ConvertFrom-Json", smokeScript);
        Assert.Contains("Assert-SecurityHeader -Response $readyResponse -Name \"Strict-Transport-Security\"", smokeScript);
        Assert.Contains("Assert-SecurityHeader -Response $loginResponse -Name \"Strict-Transport-Security\"", smokeScript);
        Assert.Contains("Assert-SecurityHeader -Response $currentUserResponse -Name \"Strict-Transport-Security\"", smokeScript);
        Assert.Contains("AllowInsecureHttp", smokeScript);
        Assert.Contains("$baseUri.Scheme -eq \"http\" -and -not $AllowInsecureHttp", smokeScript);
        Assert.Contains("must use https", smokeScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AllowInsecureHttp is only for local dry runs", smokeScript);
        Assert.Contains("Assert-SecurityHeader", smokeScript);
        Assert.Contains("Assert-ContentSecurityPolicy", smokeScript);
        Assert.Contains("Get-CspDirectives", smokeScript);
        Assert.Contains("Get-CspDirective", smokeScript);
        Assert.Contains("Test-ContainsIgnoreCase", smokeScript);
        Assert.Contains(".IndexOf($Needle, [StringComparison]::OrdinalIgnoreCase)", smokeScript);
        Assert.DoesNotContain(".Contains($ExpectedText, [StringComparison]", smokeScript);
        Assert.DoesNotContain(".Contains(\"script-src 'self' 'unsafe-inline'\", [StringComparison]", smokeScript);
        Assert.DoesNotContain(".Contains(\"theme-init.js\", [StringComparison]", smokeScript);
        Assert.DoesNotContain(".Contains(\"nonce=`\"$nonce`\"\", [StringComparison]", smokeScript);
        Assert.Contains("script-src 'self'", smokeScript);
        Assert.Contains("'nonce-", smokeScript);
        Assert.Contains("'strict-dynamic'", smokeScript);
        Assert.Contains("script-src-attr", smokeScript);
        Assert.Contains("must be exactly 'none'", smokeScript);
        Assert.Contains("unsafe-inline scripts", smokeScript);
        Assert.Contains("unsafe-eval scripts", smokeScript);
        Assert.Contains("theme-init.js", smokeScript);
        Assert.Contains("nonce=", smokeScript);
        Assert.Contains("secondHomeResponse", smokeScript);
        Assert.Contains("CSP nonce was reused", smokeScript);
        Assert.Contains("-AllowUnsafeInlineScripts:$AllowInsecureHttp", smokeScript);
        Assert.Contains("Strict-Transport-Security", smokeScript);
        Assert.Contains("Content-Security-Policy", smokeScript);
        Assert.Contains("X-Frame-Options", smokeScript);
        Assert.Contains("X-Content-Type-Options", smokeScript);
        Assert.Contains("Referrer-Policy", smokeScript);
        Assert.Contains("Permissions-Policy", smokeScript);
        Assert.Contains("-OutFile $OutputPath", smokeScript);
        Assert.Contains("CheckDownloads", smokeScript);
        Assert.Contains("/documents/accounts-package", smokeScript);
        Assert.Contains("/revenue/ixbrl", smokeScript);
        Assert.Contains("application/pdf", smokeScript);
        Assert.Contains("application/xhtml+xml", smokeScript);
        Assert.Contains("%PDF", smokeScript);
        Assert.Contains("xmlns", smokeScript);
        Assert.Contains("confirms logout clears the authenticated session", runbook);
        Assert.Contains("GET `/api/auth/me` must return `401 Unauthorized` after logout", runbook);
        Assert.DoesNotContain("Write-Host $Password", smokeScript);
    }

    [Fact]
    public void HealthReadiness_FailsWhenMigrationsOrOwnerBootstrapAreMissing()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Program.cs"));

        Assert.Contains("GetPendingMigrationsAsync", program);
        Assert.Contains("UserAccounts", program);
        Assert.DoesNotContain("u.Role == \"Owner\"", program);
        Assert.Contains("u.Role.Trim().ToLower() == \"owner\"", program);
        Assert.Contains("IOptions<BootstrapOwnerConfig>", program);
        Assert.Contains("hasConfiguredBootstrapOwner", program);
        Assert.Contains("u.Tenant.Slug", program);
        Assert.Contains("u.Email.ToLower() == bootstrapOwnerEmail", program);
    }

    [Fact]
    public void NormalDatabaseStartup_EnsuresConfiguredBootstrapOwnerAfterMigrationsAndSeeding()
    {
        var program = File
            .ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Program.cs"))
            .Replace("\r\n", "\n");
        var startupStart = program.IndexOf("// Database startup tasks. In production these must be explicitly opted into.", StringComparison.Ordinal);
        var middlewareStart = program.IndexOf("// Middleware", StringComparison.Ordinal);
        Assert.True(startupStart >= 0, "Expected normal database startup block marker.");
        Assert.True(middlewareStart > startupStart, "Expected middleware block after normal database startup block.");
        var startupBlock = program[startupStart..middlewareStart];

        var migrateIndex = startupBlock.IndexOf("await db.Database.MigrateAsync();", StringComparison.Ordinal);
        var seedIndex = startupBlock.IndexOf("await SeedData.SeedAsync(db, dbStartup.SeedDemoUsers, dbStartup.SeedSampleCompanies);", StringComparison.Ordinal);
        var bootstrapIndex = startupBlock.IndexOf("await scope.ServiceProvider.GetRequiredService<BootstrapOwnerService>().EnsureAsync();", StringComparison.Ordinal);

        Assert.True(migrateIndex >= 0, "Normal startup should run migrations when configured.");
        Assert.True(seedIndex >= 0, "Normal startup should seed local demo data when configured.");
        Assert.True(bootstrapIndex >= 0, "Normal startup should ensure the configured local owner account.");
        Assert.True(migrateIndex < bootstrapIndex, "The bootstrap owner needs the migrated schema first.");
        Assert.True(seedIndex < bootstrapIndex, "The local owner should attach to the seeded main-demo tenant.");
    }

    [Fact]
    public void LocalSetupGuide_PublishesRunStepsAndAdminCredentials()
    {
        var guide = File.ReadAllText(Path.Combine(RepositoryRoot(), "LOCAL_SETUP.md"));

        Assert.Contains("docker compose up -d", guide);
        Assert.Contains("http://localhost:3000", guide);
        Assert.Contains("http://localhost:5090/health/ready", guide);
        Assert.Contains("admin@accounts.local", guide);
        Assert.Contains("LocalAdmin!Accounts-2026-9Qx", guide);
        Assert.Contains("Green Valley Community Development CLG", guide);
        Assert.Contains("No Stripe", guide);
        Assert.Contains("no external storage", guide, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompanyAccountingEndpoints_RequireEffectiveDatesForPeriodScopedSharedRecords()
    {
        var bankingEndpoints = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "BankingEndpoints.cs"));
        var yearEndEndpoints = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "YearEndEndpoints.cs"));

        Assert.Contains("BankingEndpointInputs.ValidateBankAccount", bankingEndpoints);
        Assert.Contains("OpeningBalanceDate is null", bankingEndpoints);
        Assert.Contains("LoanInputs.Validate", yearEndEndpoints);
        Assert.Contains("input.DrawdownDate", yearEndEndpoints);
        Assert.Contains("input.BalanceAsOfDate", yearEndEndpoints);
        Assert.Contains("ShareCapitalInputs.Validate", yearEndEndpoints);
        Assert.Contains("input.IssueDate", yearEndEndpoints);
        Assert.Contains("/loan-balance-snapshots", yearEndEndpoints);
        Assert.Contains("LoanBalanceSnapshotInputs.ValidateAsync", yearEndEndpoints);
        Assert.Contains("snapshotClosingBalance", yearEndEndpoints);
        Assert.Contains("BlockIfCompanyAccountingLockedAsync(companyId, effectiveDate)", yearEndEndpoints);
    }

    [Fact]
    public void RateLimitClientKey_UsesForwardedClientIpFromFrontendProxy()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("172.18.0.12");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.24, 172.18.0.10";

        var key = RateLimitClientKey.FromHttpContext(context, trustForwardedFor: true);

        Assert.Equal("203.0.113.24", key);
    }

    [Fact]
    public void RateLimitClientKey_IgnoresForwardedClientIpUnlessTrusted()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("172.18.0.12");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.24, 172.18.0.10";

        var key = RateLimitClientKey.FromHttpContext(context);

        Assert.Equal("172.18.0.12", key);
    }

    [Fact]
    public void RateLimitClientKey_FallsBackToRemoteIpWhenForwardedForIsInvalid()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("172.18.0.12");
        context.Request.Headers["X-Forwarded-For"] = "not an ip";

        var key = RateLimitClientKey.FromHttpContext(context, trustForwardedFor: true);

        Assert.Equal("172.18.0.12", key);
    }

    [Fact]
    public void RateLimiter_RunsAfterApiAccessAndUsesProxyAwareClientKey()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Program.cs"));

        Assert.Contains("RateLimits:TrustForwardedFor", program);
        Assert.Contains("RateLimitClientKey.FromHttpContext(context, trustForwardedFor)", program);
        Assert.DoesNotContain("context.Connection.RemoteIpAddress?.ToString() ?? \"unknown\"", program);
        Assert.True(
            program.IndexOf("UseMiddleware<Accounts.Api.Middleware.ApiAccessMiddleware>", StringComparison.Ordinal)
            < program.IndexOf("UseRateLimiter()", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AdjustmentInputs_RejectsCategoryFromAnotherCompany()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherCategory = AddCategory(db, otherPeriod.CompanyId, "7000", "Other expenses", AccountCategoryType.Expense);
        var input = new AdjustmentInput(
            Description: "Cross-company category attempt",
            DebitCategoryId: otherCategory.Id,
            CreditCategoryId: null,
            Amount: 100m,
            Reason: "Testing ownership guard",
            LegalBasis: "Internal control",
            ImpactOnProfit: -100m,
            ImpactOnAssets: 0m);

        var validation = await AdjustmentInputs.ValidateAsync(db, period.CompanyId, input);

        Assert.NotNull(validation);
    }

    [Fact]
    public void EndpointInputs_RejectsInvalidCompanyAndPeriodInputs()
    {
        var badCompany = new CompanyInput
        {
            LegalName = "",
            IncorporationDate = default,
            FinancialYearStartMonth = 13,
            ArdMonth = 0
        };
        var badPeriod = new AccountingPeriodInput
        {
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2026, 8, 1),
            MemberAuditNoticeReceived = true
        };

        Assert.NotNull(EndpointInputs.ValidateCompany(badCompany));
        Assert.NotNull(EndpointInputs.ValidatePeriod(badPeriod));
    }

    [Fact]
    public void EndpointInputs_RequiresReasonWhenReopeningLockedPeriod()
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
        var invalid = new PeriodStatusUpdate(PeriodStatus.Review, null, "too short");
        var valid = new PeriodStatusUpdate(PeriodStatus.Review, null, "Material correction required");
        var owner = AuthenticatedRole("Owner");

        Assert.NotNull(EndpointInputs.ValidatePeriodStatusUpdate(period, invalid, owner));
        Assert.Null(EndpointInputs.ValidatePeriodStatusUpdate(period, valid, owner));
    }

    [Fact]
    public void EndpointInputs_RequiresOwnerWhenReopeningLockedPeriod()
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
        var update = new PeriodStatusUpdate(PeriodStatus.Review, null, "Material correction required");

        Assert.NotNull(EndpointInputs.ValidatePeriodStatusUpdate(period, update, AuthenticatedRole("Reviewer")));
        Assert.Null(EndpointInputs.ValidatePeriodStatusUpdate(period, update, AuthenticatedRole("Owner")));
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
    public void EndpointInputs_PeriodStatusFinaliseDoesNotRequireCallerSuppliedLockedBy()
    {
        var period = new AccountingPeriod
        {
            CompanyId = 1,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = true,
            Status = PeriodStatus.Review
        };

        var result = EndpointInputs.ValidatePeriodStatusUpdate(
            period,
            new PeriodStatusUpdate(PeriodStatus.Finalised, null, null),
            AuthenticatedRole("Reviewer"));

        Assert.Null(result);
    }

    [Theory]
    [InlineData(PeriodStatus.Finalised, "accounts finalisation")]
    [InlineData(PeriodStatus.Filed, "accounts filing")]
    public async Task PeriodStatusEndpoint_RejectsFinaliseOrFileWhenReadinessBlockersRemain(
        PeriodStatus targetStatus,
        string outputName)
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Review;
        await db.SaveChangesAsync();
        var context = AuthenticatedRequest(
            "Reviewer",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/status");
        var statements = new FinancialStatementsService(db);
        var audit = new AuditService(db);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PeriodStatusEndpoint.UpdateAsync(
                period.CompanyId,
                period.Id,
                new PeriodStatusUpdate(targetStatus, null, null),
                db,
                audit,
                statements,
                context,
                DisabledApiAccess()));

        Assert.Contains($"Cannot generate final {outputName}", error.Message);
        Assert.Contains("readiness blockers", error.Message);
        var reloaded = await db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == period.Id);
        Assert.Equal(PeriodStatus.Review, reloaded.Status);
        Assert.Null(reloaded.LockedAt);
        Assert.Null(reloaded.LockedBy);
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == "StatusUpdated").ToListAsync());
    }

    [Fact]
    public async Task PeriodStatusEndpoint_RejectsFiledWhenFilingDeadlinesRemainUnfiled()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Review;
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
        await new DeadlineService(db).CalculateDeadlinesAsync(period.CompanyId, period.Id);
        var context = AuthenticatedRequest(
            "Reviewer",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/status");
        var statements = new FinancialStatementsService(db);
        var audit = new AuditService(db);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PeriodStatusEndpoint.UpdateAsync(
                period.CompanyId,
                period.Id,
                new PeriodStatusUpdate(PeriodStatus.Filed, null, null),
                db,
                audit,
                statements,
                context,
                DisabledApiAccess()));

        Assert.Contains("Cannot mark period as filed", error.Message);
        Assert.Contains("CRO filing has not been recorded as filed", error.Message);
        Assert.Contains("Revenue filing has not been recorded as filed", error.Message);
        var reloaded = await db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == period.Id);
        Assert.Equal(PeriodStatus.Review, reloaded.Status);
        Assert.Null(reloaded.LockedAt);
        Assert.Null(reloaded.LockedBy);
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == "StatusUpdated").ToListAsync());
    }

    [Fact]
    public async Task PeriodStatusEndpoint_RejectsFiledWhenApplicableCharityDeadlineIsUnfiled()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Review;
        var company = await db.Companies.SingleAsync(c => c.Id == period.CompanyId);
        company.IsCharitableOrganisation = true;
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
        var deadlines = await new DeadlineService(db).CalculateDeadlinesAsync(period.CompanyId, period.Id);
        foreach (var deadline in deadlines.Where(d => d.DeadlineType is DeadlineType.CRO or DeadlineType.Revenue))
        {
            deadline.FiledDate = deadline.DueDate;
            deadline.FilingReference = deadline.DeadlineType == DeadlineType.Revenue
                ? "ROS-CT1-2025-0001"
                : "CORE-2025-0001";
        }
        await db.SaveChangesAsync();
        var context = AuthenticatedRequest(
            "Reviewer",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/status");
        var statements = new FinancialStatementsService(db);
        var audit = new AuditService(db);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PeriodStatusEndpoint.UpdateAsync(
                period.CompanyId,
                period.Id,
                new PeriodStatusUpdate(PeriodStatus.Filed, null, null),
                db,
                audit,
                statements,
                context,
                DisabledApiAccess()));

        Assert.Contains("Cannot mark period as filed", error.Message);
        Assert.Contains("Charity filing has not been recorded as filed", error.Message);
        var reloaded = await db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == period.Id);
        Assert.Equal(PeriodStatus.Review, reloaded.Status);
        Assert.Null(reloaded.LockedAt);
        Assert.Null(reloaded.LockedBy);
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == "StatusUpdated").ToListAsync());
    }

    [Fact]
    public async Task PeriodStatusEndpoint_RejectsFiledWhenCroDeadlineReferenceMissing()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Review;
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
        var deadlines = await new DeadlineService(db).CalculateDeadlinesAsync(period.CompanyId, period.Id);
        foreach (var deadline in deadlines.Where(d => d.DeadlineType is DeadlineType.CRO or DeadlineType.Revenue))
        {
            deadline.FiledDate = deadline.DueDate;
            deadline.FilingReference = deadline.DeadlineType == DeadlineType.Revenue
                ? "ROS-CT1-2025-0001"
                : null;
        }
        await db.SaveChangesAsync();
        var context = AuthenticatedRequest(
            "Reviewer",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/status");
        var statements = new FinancialStatementsService(db);
        var audit = new AuditService(db);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PeriodStatusEndpoint.UpdateAsync(
                period.CompanyId,
                period.Id,
                new PeriodStatusUpdate(PeriodStatus.Filed, null, null),
                db,
                audit,
                statements,
                context,
                DisabledApiAccess()));

        Assert.Contains("Cannot mark period as filed", error.Message);
        Assert.Contains("CRO filing reference has not been recorded", error.Message);
        var reloaded = await db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == period.Id);
        Assert.Equal(PeriodStatus.Review, reloaded.Status);
        Assert.Null(reloaded.LockedAt);
        Assert.Null(reloaded.LockedBy);
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == "StatusUpdated").ToListAsync());
    }

    [Fact]
    public async Task PeriodStatusEndpoint_AllowsFiledWhenApplicableDeadlinesAreRecordedAsFiled()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Review;
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
        var deadlines = await new DeadlineService(db).CalculateDeadlinesAsync(period.CompanyId, period.Id);
        foreach (var deadline in deadlines)
        {
            deadline.FiledDate = deadline.DueDate;
            deadline.FilingReference = deadline.DeadlineType switch
            {
                DeadlineType.Revenue => "ROS-CT1-2025-0001",
                DeadlineType.Charity => "CRA-AR-2025-0001",
                _ => "CORE-2025-0001"
            };
        }
        await db.SaveChangesAsync();
        var context = AuthenticatedRequest(
            "Reviewer",
            HttpMethods.Put,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/status");
        var statements = new FinancialStatementsService(db);
        var audit = new AuditService(db);

        var result = await PeriodStatusEndpoint.UpdateAsync(
            period.CompanyId,
            period.Id,
            new PeriodStatusUpdate(PeriodStatus.Filed, null, null),
            db,
            audit,
            statements,
            context,
            DisabledApiAccess());

        var reloaded = await db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == period.Id);
        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(result));
        Assert.Equal(PeriodStatus.Filed, reloaded.Status);
        Assert.NotNull(reloaded.LockedAt);
        Assert.Equal("Example User", reloaded.LockedBy);
        Assert.Single(await db.AuditLogs.Where(a => a.Action == "StatusUpdated").ToListAsync());
    }

    [Fact]
    public void PeriodStatusEndpoint_IsMappedAndChecksReadinessBeforeMutatingStatus()
    {
        var root = RepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Program.cs"));
        var endpoint = File.ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Endpoints", "PeriodStatusEndpoint.cs"));

        Assert.Contains("periods.MapPut(\"/{id:int}/status\", PeriodStatusEndpoint.UpdateAsync)", program);
        Assert.Contains("FinancialStatementsService statements", endpoint);
        Assert.Contains("AssertFinalOutputReadinessAsync(companyId, id, outputName)", endpoint);
        Assert.Contains("AssertFilingObligationsRecordedAsync(db, companyId, id)", endpoint);
        AssertOccursBefore(endpoint, "AssertFinalOutputReadinessAsync(companyId, id, outputName)", "EndpointInputs.ApplyPeriodStatusUpdate");
        AssertOccursBefore(endpoint, "AssertFilingObligationsRecordedAsync(db, companyId, id)", "EndpointInputs.ApplyPeriodStatusUpdate");
        AssertOccursBefore(endpoint, "AssertFinalOutputReadinessAsync(companyId, id, outputName)", "audit.LogAsync(");
        AssertOccursBefore(endpoint, "AssertFilingObligationsRecordedAsync(db, companyId, id)", "audit.LogAsync(");
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

    private static AccountsDbContext CreateDbContext(string? databaseName = null, InMemoryDatabaseRoot? root = null)
    {
        var builder = new DbContextOptionsBuilder<AccountsDbContext>();
        if (root is null)
            builder.UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString());
        else
            builder.UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString(), root);

        return new AccountsDbContext(builder.Options);
    }

    private static AccountsDbContext CreateAuditFailingDbContext(string databaseName, InMemoryDatabaseRoot root)
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(databaseName, root)
            .Options;
        return new AuditFailingDbContext(options);
    }

    private static string RepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        foreach (var startPath in new[] { Directory.GetCurrentDirectory(), Path.GetDirectoryName(sourceFilePath) })
        {
            if (string.IsNullOrWhiteSpace(startPath))
                continue;

            var directory = new DirectoryInfo(startPath);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "compose.yml")))
            {
                directory = directory.Parent;
            }

            if (directory is not null)
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string TestMethodSnippet(string source, string methodName)
    {
        var declaration = Regex.Match(
            source,
            $@"\n\s+public\s+(?:async\s+)?Task\s+{Regex.Escape(methodName)}\s*\(",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));
        Assert.True(declaration.Success, $"Expected to find test method {methodName}.");
        var start = declaration.Index;
        var next = Regex.Match(
            source[(start + declaration.Length)..],
            @"\n\s+\[(?:Fact|Theory)\]",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));
        return next.Success
            ? source[start..(start + declaration.Length + next.Index)]
            : source[start..];
    }

    private static void AssertOccursBefore(string snippet, string first, string second)
    {
        var firstIndex = snippet.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = snippet.IndexOf(second, StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, $"Expected snippet to contain '{first}'.");
        Assert.True(secondIndex >= 0, $"Expected snippet to contain '{second}'.");
        Assert.True(firstIndex < secondIndex, $"Expected '{first}' before '{second}'.");
    }

    private static async Task<AccountingPeriod> SeedCompanyPeriodAsync(AccountsDbContext db, bool isFirstYear)
    {
        var company = new Company
        {
            TenantId = 1,
            LegalName = "Example Micro Limited",
            CroNumber = "123456",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2025, 1, 1),
            ArdMonth = 9,
            IsTrading = true,
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        db.CompanyOfficers.AddRange(
            new CompanyOfficer { CompanyId = company.Id, Name = "A Director", Role = OfficerRole.Director },
            new CompanyOfficer { CompanyId = company.Id, Name = "B Secretary", Role = OfficerRole.Secretary }
        );

        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = isFirstYear
        };
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();

        return period;
    }

    private static async Task SeedAcceptedCroFilingPackageAsync(AccountsDbContext db, int periodId)
    {
        db.CroFilingPackages.Add(new CroFilingPackage
        {
            PeriodId = periodId,
            FilingStatus = FilingStatus.Accepted,
            AccountsPdfGenerated = true,
            SignaturePageGenerated = true,
            PaymentCompleted = true,
            SubmittedBy = "reviewer@example.ie",
            SubmittedAt = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
            CroSubmissionReference = "CORE-ACCEPTED-TEST"
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedRevenueInternalIxbrlChecksPassedAsync(
        AccountsDbContext db,
        int periodId,
        string? ct1Reference = "ROS-INTERNAL-CHECKS-TEST")
    {
        db.RevenueFilingPackages.Add(new RevenueFilingPackage
        {
            PeriodId = periodId,
            FilingStatus = FilingStatus.InProgress,
            IxbrlGenerated = true,
            IxbrlValidated = false,
            IxbrlValidationErrors = "Internal checks passed. External ROS/iXBRL validation is still required before Revenue filing.",
            Ct1Reference = ct1Reference
        });
        await db.SaveChangesAsync();
    }

    private static AccountCategory AddCategory(
        AccountsDbContext db,
        int companyId,
        string code,
        string name,
        AccountCategoryType type)
    {
        var category = new AccountCategory
        {
            CompanyId = companyId,
            Code = code,
            Name = name,
            Type = type,
            IsSystem = true
        };
        db.AccountCategories.Add(category);
        db.SaveChanges();
        return category;
    }

    private static void SetRequiredDate(object target, string propertyName, DateOnly value)
    {
        var property = target.GetType().GetProperty(propertyName)
            ?? throw new InvalidOperationException($"{target.GetType().Name}.{propertyName} is required for period-effective accounting data.");
        property.SetValue(target, value);
    }

    private static void SetRequiredValue(object target, string propertyName, object value)
    {
        var property = target.GetType().GetProperty(propertyName)
            ?? throw new InvalidOperationException($"{target.GetType().Name}.{propertyName} is required.");
        property.SetValue(target, value);
    }

    private static DefaultHttpContext WriteAuditContext(string method, string path, string correlationId)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Headers["X-Correlation-ID"] = correlationId;
        context.TraceIdentifier = "trace-" + correlationId;
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 7,
            TenantId: 17,
            TenantName: "Firm A",
            Email: "reviewer@example.ie",
            DisplayName: "Maeve Reviewer",
            Role: "Reviewer");
        return context;
    }

    private static DefaultHttpContext AuthenticatedRequest(string role, string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Items[AuthContext.ItemKey] = AuthenticatedRole(role);
        return context;
    }

    private static ApiAccessService DisabledApiAccess() =>
        new(
            Options.Create(new ApiAccessConfig { Enabled = false }),
            new TestEnvironment("Development"));

    private static async Task LogAuditWithEvidenceMetadataAsync(
        AuditService audit,
        int? companyId,
        int? periodId,
        string entityType,
        int entityId,
        string action,
        object? oldValue,
        object? newValue,
        string? userId,
        int tenantId,
        string requestId,
        string actorDisplayName)
    {
        var method = typeof(AuditService)
            .GetMethods()
            .SingleOrDefault(m =>
            {
                if (m.Name != nameof(AuditService.LogAsync))
                    return false;

                var names = m.GetParameters().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                return names.Contains("tenantId")
                    && names.Contains("requestId")
                    && names.Contains("actorDisplayName");
            });

        Assert.True(
            method is not null,
            "AuditService.LogAsync should accept tenantId, requestId, and actorDisplayName metadata.");

        var arguments = method.GetParameters()
            .Select(parameter => parameter.Name?.ToLowerInvariant() switch
            {
                "companyid" => companyId,
                "periodid" => periodId,
                "entitytype" => entityType,
                "entityid" => entityId,
                "action" => action,
                "oldvalue" => oldValue,
                "newvalue" => newValue,
                "userid" => userId,
                "tenantid" => tenantId,
                "requestid" => requestId,
                "actordisplayname" => actorDisplayName,
                _ when parameter.HasDefaultValue => parameter.DefaultValue,
                _ => throw new InvalidOperationException($"Unexpected AuditService.LogAsync parameter '{parameter.Name}'.")
            })
            .ToArray();

        var result = method.Invoke(audit, arguments);
        await Assert.IsAssignableFrom<Task>(result);
    }

    private static void AssertAuditLogValue(AuditLog entry, string propertyName, object expected) =>
        Assert.Equal(expected, ReadAuditLogValue(entry, propertyName));

    private static int? ResultStatusCode(IResult result) =>
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static object? ReadAuditLogValue(AuditLog entry, string propertyName)
    {
        var property = typeof(AuditLog).GetProperty(propertyName);
        Assert.True(property is not null, $"AuditLog should persist evidence metadata property '{propertyName}'.");
        return property.GetValue(entry);
    }

    private static string RequiredAuditLogString(AuditLog entry, string propertyName)
    {
        var value = ReadAuditLogValue(entry, propertyName);
        return Assert.IsType<string>(value);
    }

    private static string RequiredCookieValue(CookieContainer cookies, Uri baseUri, string cookieName)
    {
        var value = cookies.GetCookies(baseUri)[cookieName]?.Value;
        Assert.False(string.IsNullOrWhiteSpace(value), $"Expected cookie '{cookieName}' to be set.");
        return value;
    }

    private static string LoopbackBaseAddress(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()
            ?.Addresses
            ?? [];
        var loopbackAddresses = addresses
            .Where(value => value.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(
            loopbackAddresses.Length == 1,
            $"Expected Kestrel to publish exactly one http://127.0.0.1 loopback test address; found {loopbackAddresses.Length}: {string.Join(", ", addresses)}.");
        var address = loopbackAddresses[0];
        Assert.False(string.IsNullOrWhiteSpace(address), "Expected Kestrel to publish an actual loopback test address.");
        Assert.False(
            address!.EndsWith(":0", StringComparison.Ordinal),
            "Expected Kestrel to replace port 0 with the actual bound port.");
        return address;
    }

    private static bool IsHex64(string value) =>
        value.Length == 64 && value.All(c =>
            c is >= '0' and <= '9'
            || c is >= 'a' and <= 'f'
            || c is >= 'A' and <= 'F');

    private static async Task MakePeriodReadyForCroDocumentsAsync(AccountsDbContext db, AccountingPeriod period)
    {
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            CalculatedClass = CompanySizeClass.Micro,
            Turnover = 100m,
            BalanceSheetTotal = 101m,
            AvgEmployees = 1
        });

        var bankCategory = AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var salesCategory = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var shareCapitalCategory = AddCategory(db, period.CompanyId, "3000", "Share Capital", AccountCategoryType.Equity);

        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 1m,
            OpeningBalanceDate = period.PeriodStart
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = shareCapitalCategory.Id,
            Credit = 1m,
            SourceNote = "Opening share capital per register",
            EnteredBy = "Accounts reviewer",
            Reviewed = true,
            ReviewedBy = "Accounts reviewer",
            ReviewedAt = DateTime.UtcNow
        });
        db.ShareCapitals.Add(new ShareCapital
        {
            CompanyId = period.CompanyId,
            ShareClass = "Ordinary",
            NumberIssued = 1,
            NominalValue = 1m,
            TotalValue = 1m,
            IssueDate = period.PeriodStart
        });
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bankAccount.Id,
            PeriodId = period.Id,
            Date = new DateOnly(2025, 3, 1),
            Description = "Customer receipt",
            Amount = 100m,
            CategoryId = salesCategory.Id
        });
        db.Adjustments.Add(new Adjustment
        {
            PeriodId = period.Id,
            Description = "No year-end adjustment required",
            Amount = 0m,
            ImpactOnProfit = 0m,
            Source = AdjustmentSource.Manual,
            CreatedBy = "Accounts reviewer",
            ApprovedBy = "Accounts reviewer",
            ApprovedAt = DateTime.UtcNow
        });
        db.YearEndReviewConfirmations.AddRange(
            NilReview(period.Id, "debtors"),
            NilReview(period.Id, "creditors"),
            NilReview(period.Id, "payroll"),
            NilReview(period.Id, "tax"),
            NilReview(period.Id, "dividends"),
            NilReview(period.Id, "post-balance-sheet-events"),
            NilReview(period.Id, "related-parties"),
            NilReview(period.Id, "contingent-liabilities"),
            NilReview(period.Id, "going-concern"));
        await db.SaveChangesAsync();

        _ = bankCategory;
    }

    private static YearEndReviewConfirmation NilReview(int periodId, string sectionKey) => new()
    {
        PeriodId = periodId,
        SectionKey = sectionKey,
        Confirmed = true,
        ConfirmedBy = "Accounts reviewer",
        Note = "Nil position reviewed."
    };

    private static AuthService CreateAuthService(
        AccountsDbContext db,
        string? signingKey = null,
        string environmentName = "Development",
        int expiryMinutes = 60,
        bool secureCookiesInProduction = true,
        IPasswordVerifier? passwordVerifier = null,
        TimeProvider? timeProvider = null) =>
        new(
            db,
            Options.Create(new AuthSessionConfig
            {
                SigningKey = signingKey ?? StrongSessionSigningKey(),
                ExpiryMinutes = expiryMinutes,
                SecureCookiesInProduction = secureCookiesInProduction
            }),
            new TestEnvironment(environmentName),
            passwordVerifier ?? new Pbkdf2PasswordVerifier(),
            timeProvider);

    private static async Task<Tenant> SeedTenantAsync(
        AccountsDbContext db,
        string name = "Example Firm",
        string slug = "example-firm")
    {
        var tenant = new Tenant
        {
            Name = name,
            Slug = slug
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static async Task<Company> SeedTenantCompanyAsync(
        AccountsDbContext db,
        int tenantId,
        string legalName)
    {
        var company = new Company
        {
            TenantId = tenantId,
            LegalName = legalName,
            CroNumber = $"T{tenantId}{Guid.NewGuid():N}"[..20],
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2025, 1, 1),
            ArdMonth = 9,
            IsTrading = true,
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return company;
    }

    private static async Task<UserAccount> SeedUserAsync(
        AccountsDbContext db,
        Tenant tenant,
        string email,
        string password,
        bool isActive = true,
        string role = "Owner",
        string passwordAlgorithm = AuthService.PasswordAlgorithm)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            210_000,
            HashAlgorithmName.SHA256,
            32);
        var user = new UserAccount
        {
            TenantId = tenant.Id,
            Tenant = tenant,
            Email = email.Trim().ToLowerInvariant(),
            DisplayName = "Owner User",
            Role = role,
            PasswordHash = Convert.ToBase64String(hash),
            PasswordSalt = Convert.ToBase64String(salt),
            PasswordAlgorithm = passwordAlgorithm,
            PasswordStrengthScore = 5,
            IsActive = isActive
        };
        db.UserAccounts.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static IOptions<AuthSessionConfig> AuthSessionOptions(IConfiguration config) =>
        Options.Create(config.GetSection("AuthSession").Get<AuthSessionConfig>() ?? new AuthSessionConfig());

    private static AuditIntegrityConfig AuditIntegrityCheckpointOptions() => new()
    {
        ActiveKeyId = "audit-key-2026",
        SigningKeys =
        [
            new AuditIntegritySigningKeyConfig
            {
                KeyId = "audit-key-2026",
                SigningKey = StrongAuditCheckpointSigningKey()
            }
        ]
    };

    private static string StrongSessionSigningKey() =>
        Convert.ToBase64String(Enumerable.Range(0, 64).Select(i => (byte)i).ToArray());

    private static string StrongSessionSigningKeyBase64Url() =>
        StrongSessionSigningKey().TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string DevelopmentSessionSigningKey() =>
        "IyRih0V4m+9WHp+buroLFov10a+LGRhgg8g4J7vm3uTnRtUU6t1JenYYfZTaqT9Gl9H7FmYhGwNORssVZ/BPkg==";

    private static string StrongAuditCheckpointSigningKey() =>
        Convert.ToBase64String(Enumerable.Range(64, 64).Select(i => (byte)i).ToArray());

    private static AuthenticatedUser AuthenticatedRole(string role) => new(
        UserId: 1,
        TenantId: 1,
        TenantName: "Example Firm",
        Email: "user@example.ie",
        DisplayName: "Example User",
        Role: role);

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Accounts.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed record CapturedLog(LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<CapturedLog> Entries { get; } = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullLogger.Instance.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new CapturedLog(logLevel, formatter(state, exception), exception));
    }

    private sealed class FailingIxbrlService(AccountsDbContext db, FinancialStatementsService statementsService) : IxbrlService(db, statementsService)
    {
        public override Task<byte[]> GenerateIxbrlAsync(int companyId, int periodId) =>
            throw new InvalidOperationException("Npgsql failure for password=secret while rendering iXBRL");
    }

    private sealed class FailingProfitAndLossStatementsService(AccountsDbContext db) : FinancialStatementsService(db)
    {
        public override Task<ProfitAndLoss> GetProfitAndLossAsync(int companyId, int periodId) =>
            throw new InvalidOperationException("Simulated profit and loss failure");
    }

    private sealed class FixedUtcTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class ThrowingReadStream(string message) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException(message);

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class AuditFailingDbContext(DbContextOptions<AccountsDbContext> options) : AccountsDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ChangeTracker.Entries<AuditLog>().Any(entry => entry.State == EntityState.Added))
                throw new InvalidOperationException("Simulated audit write failure");

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class CountingPasswordVerifier : IPasswordVerifier
    {
        public int CallCount { get; private set; }

        public bool Verify(string password, UserAccount? user)
        {
            CallCount++;
            return false;
        }
    }

}
