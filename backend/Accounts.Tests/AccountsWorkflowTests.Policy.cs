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
    public void DocumentEndpoints_UseGuardedFinalDocumentMethods()
    {
        var source = File
            .ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "DocumentEndpoints.cs"))
            .Replace("\r\n", "\n");

        Assert.Contains("GenerateAccountsReviewPackageAsync(companyId, periodId)", source);
        Assert.Contains("GenerateAgmReviewPackAsync(companyId, periodId)", source);
        Assert.Contains("GenerateCroFilingReviewPackAsync(companyId, periodId)", source);
        Assert.Contains("GenerateSignatureReviewPageAsync(companyId, periodId)", source);
        Assert.Contains("FilingReleaseArtifact.CroAccountsPdf", source);
        Assert.Contains("FilingReleaseArtifact.CroSignaturePage", source);
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

        Assert.Contains("auditGroup.MapGet(\"/\", GetAuditLogEndpointAsync)", source);
        var auditLogSnippet = AdjustmentMemberSnippet(source, "public static async Task<IResult> GetAuditLogEndpointAsync");
        Assert.Contains("AccountsDbContext db", auditLogSnippet);
        Assert.Contains("HttpContext context", auditLogSnippet);
        Assert.Contains("CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId)", auditLogSnippet);
        Assert.Contains("RequireAuditEvidenceAccess(context)", auditLogSnippet);
        Assert.DoesNotContain("EndpointRequestAuthorization.AuthorizeCurrentRequest", auditLogSnippet);

        foreach (var marker in new[]
        {
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
    public void BankingImportEndpoint_UsesCompanyAwareImportService()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "BankingEndpoints.cs"));

        Assert.Matches(@"ImportCsvAsync\(\s*companyId,\s*bankAccountId,\s*periodId,\s*stream,", source);
        Assert.DoesNotMatch(@"ImportCsvAsync\(\s*bankAccountId,\s*periodId", source);
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
    public void ProductionMonitoring_IsWiredIntoApiStartupAndProductionCompose()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Program.cs"));
        var compose = File.ReadAllText(Path.Combine(RepositoryRoot(), "compose.production.yml"));

        Assert.Contains("Configure<MonitoringConfig>", program);
        Assert.Contains("UseSentry", program);
        Assert.Contains("options.Environment = builder.Environment.EnvironmentName", program);
        Assert.Contains("builder.Configuration[\"FilingRelease:Candidate\"]", program);
        Assert.Contains("options.Release = releaseCandidate", program);
        Assert.Contains("options.SendDefaultPii = false", program);
        Assert.Contains("options.SetBeforeSend(MonitoringEventSanitizer.ScrubSentryEvent)", program);
        Assert.Contains("AddJsonConsole", program);
        Assert.Contains("IncludeScopes = true", program);
        Assert.Contains("AddSingleton<IErrorReporter, SentryErrorReporter>", program);
        Assert.Contains("Monitoring__ErrorTrackingDsn", compose);
        Assert.Contains("Monitoring__ErrorTrackingProvider", compose);
        Assert.Contains("Monitoring__StructuredJsonConsole: \"true\"", compose);
        Assert.Contains("Monitoring__IncludeCorrelationId: \"true\"", compose);
        Assert.Contains("Monitoring__TracesSampleRate", compose);
        Assert.Contains("Monitoring__ErrorSmokeEnabled", compose);
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
    public void AuditTrailMiddleware_RunsAfterSessionCsrfAndTenantGuards()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Program.cs"));
        var sessionIndex = program.IndexOf("UseMiddleware<Accounts.Api.Middleware.UserSessionMiddleware>", StringComparison.Ordinal);
        var auditIndex = program.IndexOf("UseMiddleware<Accounts.Api.Middleware.AuditTrailMiddleware>", StringComparison.Ordinal);
        var csrfIndex = program.IndexOf("UseMiddleware<Accounts.Api.Middleware.CsrfProtectionMiddleware>", StringComparison.Ordinal);
        var tenantIndex = program.IndexOf("UseMiddleware<Accounts.Api.Middleware.TenantAccessMiddleware>", StringComparison.Ordinal);

        Assert.NotEqual(-1, sessionIndex);
        Assert.NotEqual(-1, auditIndex);
        Assert.NotEqual(-1, csrfIndex);
        Assert.NotEqual(-1, tenantIndex);
        Assert.True(sessionIndex < csrfIndex);
        Assert.True(csrfIndex < tenantIndex);
        Assert.True(tenantIndex < auditIndex);
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
            "AcquireDistributedChainLockAsync(companyId, resolvedTenantId, auditCancellationToken)",
            "GetPreviousIntegrityHashAsync(companyId, resolvedTenantId, auditCancellationToken)");

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
    public void AuditTrailMiddleware_RunsAfterTenantAndBeforeRoleAndLockGuards()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Program.cs"));
        var auditIndex = program.IndexOf("UseMiddleware<Accounts.Api.Middleware.AuditTrailMiddleware>", StringComparison.Ordinal);
        var tenantIndex = program.IndexOf("UseMiddleware<Accounts.Api.Middleware.TenantAccessMiddleware>", StringComparison.Ordinal);

        Assert.True(tenantIndex < auditIndex);
        Assert.True(auditIndex < program.IndexOf("UseMiddleware<Accounts.Api.Middleware.RoleAuthorizationMiddleware>", StringComparison.Ordinal));
        Assert.True(auditIndex < program.IndexOf("UseMiddleware<Accounts.Api.Middleware.PeriodLockMiddleware>", StringComparison.Ordinal));
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
            "backend/Accounts.Api/Endpoints/CharityEndpoints.cs"
        };

        foreach (var relativePath in endpointFiles)
        {
            var source = File.ReadAllText(Path.Combine(root, relativePath));
            Assert.DoesNotContain("catch (Exception ex)", source);
            Assert.DoesNotContain("error = ex.Message", source);
        }

        // YearEndEndpoints is a partial class split across YearEnd*.cs — guard every part.
        var yearEndSource = YearEndEndpointsSource();
        Assert.DoesNotContain("catch (Exception ex)", yearEndSource);
        Assert.DoesNotContain("error = ex.Message", yearEndSource);
    }

    [Fact]
    public void RevenueIxbrlEndpoint_UsesFinalIxbrlReadinessGuard()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "RevenueEndpoints.cs"));

        Assert.Contains("GenerateReviewIxbrlAsync(companyId, periodId)", source);
        Assert.Contains("FilingReleaseArtifact.RevenueIxbrl", source);
        Assert.DoesNotContain("GenerateIxbrlAsync(periodId)", source);
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
    public void PeriodWorkspace_LabelsIxbrlActionAsInternalChecks()
    {
        var root = RepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "frontend/src/app/companies/[companyId]/periods/[periodId]/page.tsx"));
        var workspaceRoute = File.ReadAllText(Path.Combine(root, "frontend/src/components/period/PeriodWorkspaceRoute.tsx"));
        var reviewCentre = File.ReadAllText(Path.Combine(root, "frontend/src/components/period/FilingReviewCentre.tsx"));
        var source = page + workspaceRoute + reviewCentre;

        Assert.Contains("Check draft iXBRL structure", source);
        Assert.Contains("result.manualHandoffRequired && result.reviewPrototypeChecksPassed", source);
        Assert.Contains("filing-ready generation remains disabled and requires manual handoff", source);
        Assert.DoesNotContain("toast.success(\"iXBRL validation passed\")", source);
        Assert.DoesNotContain(">Validate iXBRL<", source);
    }

    [Fact]
    public void PeriodWorkspace_CapturesFilingReferencesBeforeMarkFiled()
    {
        var root = RepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "frontend/src/app/companies/[companyId]/periods/[periodId]/page.tsx"));
        var workspaceRoute = File.ReadAllText(Path.Combine(root, "frontend/src/components/period/PeriodWorkspaceRoute.tsx"));
        var filingWorkspace = File.ReadAllText(Path.Combine(root, "frontend/src/components/period/PeriodFilingWorkspace.tsx"));
        var deadlinesPanel = File.ReadAllText(Path.Combine(root, "frontend/src/components/period/FilingDeadlinesPanel.tsx"));
        var source = page + workspaceRoute + filingWorkspace + deadlinesPanel;
        var api = File.ReadAllText(Path.Combine(root, "frontend/src/lib/api.ts"));
        var endpoint = File.ReadAllText(Path.Combine(root, "backend/Accounts.Api/Endpoints/DeadlineEndpoints.cs"));

        Assert.Contains("filingReference?: string", api);
        Assert.Contains("string? FilingReference", endpoint);
        Assert.Contains("input.FilingReference", endpoint);
        Assert.Contains("<PeriodFilingWorkspace", workspaceRoute);
        Assert.Contains("<FilingDeadlinesPanel", filingWorkspace);
        Assert.Contains("handleMarkDeadlineFiled", workspaceRoute);
        Assert.Contains("Revenue ROS or CT1 filing reference", source);
        Assert.Contains("ROS/CT1 reference", source);
        Assert.Contains("Revenue filing reference is required", source);
        Assert.Contains("Charities Regulator annual return reference", source);
        Assert.Contains("Annual return reference", source);
        Assert.Contains("Charity annual return reference is required", source);
        Assert.Contains("onMarkFiled(deadline, normalisedReference || undefined)", deadlinesPanel);
        Assert.Contains("filingReference ? { filingReference }", workspaceRoute);
    }

    [Fact]
    public void PeriodWorkspace_CapturesCoreSubmissionReferenceBeforeCroSubmit()
    {
        var root = RepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "frontend/src/app/companies/[companyId]/periods/[periodId]/page.tsx"));
        var workspaceRoute = File.ReadAllText(Path.Combine(root, "frontend/src/components/period/PeriodWorkspaceRoute.tsx"));
        var reviewCentre = File.ReadAllText(Path.Combine(root, "frontend/src/components/period/FilingReviewCentre.tsx"));
        var source = page + workspaceRoute + reviewCentre;
        var api = File.ReadAllText(Path.Combine(root, "frontend/src/lib/api.ts"));

        Assert.Contains("submissionReference?: string", api);
        Assert.Contains("croSubmissionReference", source);
        Assert.Contains("CORE submission reference", source);
        Assert.Contains("CORE submission reference is required", source);
        Assert.Contains("const trimmedReference = croSubmissionReference.trim();", source);
        Assert.Contains("const missingSubmissionReference = trimmedReference.length === 0;", source);
        Assert.Contains("onMarkCroSubmitted(trimmedReference)", source);
        Assert.Contains("status: \"Submitted\", submissionReference", source);
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
    public void DirectorsReportEndpoint_UsesCompanyAwareServiceCall()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "DocumentEndpoints.cs"));

        Assert.Contains("GenerateAsync(companyId, periodId)", source);
        Assert.DoesNotContain("GenerateAsync(periodId)", source);
    }

    [Fact]
    public void PeriodWorkspace_AuditTrailDisplaysOldAndNewPayloads()
    {
        var root = RepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "frontend/src/app/companies/[companyId]/periods/[periodId]/page.tsx"));
        var workspaceRoute = File.ReadAllText(Path.Combine(root, "frontend/src/components/period/PeriodWorkspaceRoute.tsx"));
        var filingWorkspace = File.ReadAllText(Path.Combine(root, "frontend/src/components/period/PeriodFilingWorkspace.tsx"));
        var auditPanel = File.ReadAllText(Path.Combine(root, "frontend/src/components/period/PeriodAuditTrailPanel.tsx"));
        var source = page + workspaceRoute + filingWorkspace + auditPanel;

        Assert.Contains("getAuditLog(cId, pId, auditPage, auditPageSize)", workspaceRoute);
        Assert.Contains("<PeriodFilingWorkspace", workspaceRoute);
        Assert.Contains("<PeriodAuditTrailPanel", filingWorkspace);
        Assert.Contains("Audit details", source);
        Assert.Contains("entry.oldValueJson", auditPanel);
        Assert.Contains("entry.newValueJson", auditPanel);
        Assert.Contains("whitespace-pre-wrap", auditPanel);
    }

    [Fact]
    public void DeadlineCalculation_UsesAtomicPostgresUpsertForIdempotentRecalculation()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend/Accounts.Api/Services/DeadlineService.cs"));

        Assert.Contains("InsertDeadlineAtomicallyAsync", source);
        Assert.Contains("ON CONFLICT", source);
        Assert.Contains("\"CompanyId\", \"PeriodId\", \"DeadlineType\"", source);
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

    // YearEndEndpoints is a partial class split across Endpoints/YearEnd*.cs. Source-structure guards
    // must read the whole partial class (all parts + the input/validator classes), not a single file,
    // so they keep their teeth after the split.
    private static string YearEndEndpointsSource() =>
        string.Join(
            "\n",
            Directory
                .EnumerateFiles(
                    Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints"),
                    "YearEnd*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));

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
    public void FrontendStartScript_PreparesStandaloneAssetsWhenNextOutputStandalone()
    {
        var packageJsonPath = Path.Combine(RepositoryRoot(), "frontend", "package.json");
        using var packageJson = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
        var startScript = packageJson.RootElement.GetProperty("scripts").GetProperty("start").GetString();
        var nextConfig = File.ReadAllText(Path.Combine(RepositoryRoot(), "frontend", "next.config.ts"));
        var startHelperPath = Path.Combine(RepositoryRoot(), "frontend", "scripts", "start-standalone.mjs");

        Assert.Contains("output: \"standalone\"", nextConfig);
        Assert.Equal("node scripts/start-standalone.mjs", startScript);
        Assert.True(File.Exists(startHelperPath), "npm start should mirror the Docker standalone asset layout for local production smoke tests.");

        var startHelper = File.ReadAllText(startHelperPath);
        Assert.Contains("path.join(rootDir, \".next\", \"static\")", startHelper);
        Assert.Contains("path.join(standaloneDir, \".next\", \"static\")", startHelper);
        Assert.Contains("path.join(rootDir, \"public\")", startHelper);
        Assert.Contains("cwd: standaloneDir", startHelper);
        Assert.Contains("server.js", startHelper);
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
            "page.tsx")) + File.ReadAllText(Path.Combine(
                RepositoryRoot(), "frontend", "src", "components", "period", "PeriodWorkspaceRoute.tsx"));
        var apiClient = File.ReadAllText(Path.Combine(RepositoryRoot(), "frontend", "src", "lib", "api.ts"));

        Assert.Contains("document.createElement(\"a\")", periodPage);
        Assert.Contains("anchor.click()", periodPage);
        Assert.Contains("download =", periodPage);
        Assert.Contains("downloadDelivered", periodPage);
        Assert.Contains("extension = \"pdf\"", periodPage);
        Assert.Contains("fetchDocumentBlob(url, documentType ? \"POST\" : \"GET\")", periodPage);
        Assert.Contains("downloadDocument(croPackUrl, \"CRO filing pack\", \"accounts\", true)", periodPage);
        Assert.Contains("downloadDocument(sigPageUrl, \"signature page\", \"signature\", true)", periodPage);
        Assert.Contains("downloadDocument(ixbrlUrl, \"draft iXBRL review prototype\", undefined, false, \"xhtml\")", periodPage);
        Assert.DoesNotContain("window.open(objectUrl", periodPage);
        Assert.Contains("export async function fetchDocumentBlob", apiClient);
        Assert.Contains("const headers = new Headers(withCsrfHeader(method))", apiClient);
        Assert.Contains("headers,", apiClient);
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
        Assert.Contains("const requestHeaders = new Headers(withCsrfHeader(fetchOptions.method, {", apiClient);
        Assert.Contains("headers: requestHeaders", apiClient);
        Assert.Contains("new Headers(withCsrfHeader(\"POST\"))", apiClient);
        Assert.Contains("new Headers(withCsrfHeader(method))", apiClient);
        Assert.Contains("X-CSRF-Token", authClient);
        Assert.Contains("readCsrfToken", authClient);
    }

    [Fact]
    public void FrontendApiClient_DoesNotRetryUnsafeWritesByDefault()
    {
        var apiClient = File.ReadAllText(Path.Combine(RepositoryRoot(), "frontend", "src", "lib", "api.ts"));

        Assert.Contains("retryUnsafe = false", apiClient);
        Assert.Contains("const effectiveRetries = isUnsafeMethod(fetchOptions.method) && !retryUnsafe ? 0 : retries;", apiClient);
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
        Assert.Equal(2, Regex.Matches(compose, "DatabaseStartup__AllowInsecureDatabaseConnection: \"false\"").Count);
        Assert.DoesNotContain("DatabaseStartup__AllowInsecureDatabaseConnection: \"true\"", compose);
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
        Assert.Contains("sslmode=verify-full", compose);
        Assert.Contains("sslrootcert=/run/secrets/postgres_ca_certificate", compose);
        Assert.Contains("ssl_min_protocol_version=TLSv1.2", compose);
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
        Assert.Contains("postgres_server_certificate", compose);
        Assert.Contains("postgres_server_key", compose);
        Assert.Contains("postgres_ca_certificate", compose);
        Assert.Contains("ConnectionStrings__DefaultConnection_FILE: /run/secrets/accounts_migration_connection_string", compose);
        Assert.Contains("ConnectionStrings__DefaultConnection_FILE: /run/secrets/accounts_application_connection_string", compose);
        Assert.Contains("DatabaseTenantIsolation__ContextSigningKey_FILE: /run/secrets/database_tenant_context_key", compose);
        Assert.Contains("IdentitySecurity__IdentityHmacKey_FILE: /run/secrets/identity_hmac_key", compose);
        Assert.Contains("IdentitySecurity__MfaEncryptionKeys__0__EncryptionKey_FILE: /run/secrets/mfa_encryption_key", compose);
        Assert.Contains("DeadlineDelivery__ProviderToken_FILE: /run/secrets/deadline_provider_token", compose);
        Assert.Contains("AuthSession__SigningKey_FILE: /run/secrets/auth_session_signing_key", compose);
        Assert.Contains("AuditIntegrity__SigningKeys__0__SigningKey_FILE: /run/secrets/audit_integrity_signing_key", compose);
        Assert.Contains("BootstrapOwner__OwnerInitialPassword_FILE: /run/secrets/bootstrap_owner_password", compose);
        Assert.Contains("ACCOUNTS_API_KEY_FILE: /run/secrets/accounts_api_key", compose);
        Assert.Contains("file: \"${POSTGRES_PASSWORD_FILE:?set POSTGRES_PASSWORD_FILE}\"", compose);
        Assert.Contains("file: \"${ACCOUNTS_MIGRATION_CONNECTION_STRING_FILE:?set ACCOUNTS_MIGRATION_CONNECTION_STRING_FILE}\"", compose);
        Assert.Contains("file: \"${ACCOUNTS_APPLICATION_CONNECTION_STRING_FILE:?set ACCOUNTS_APPLICATION_CONNECTION_STRING_FILE}\"", compose);
        Assert.Contains("file: \"${POSTGRES_APPLICATION_PASSWORD_FILE:?set POSTGRES_APPLICATION_PASSWORD_FILE}\"", compose);
        Assert.Contains("file: \"${DATABASE_TENANT_CONTEXT_KEY_FILE:?set DATABASE_TENANT_CONTEXT_KEY_FILE}\"", compose);
        Assert.Contains("file: \"${IDENTITY_HMAC_KEY_FILE:?set IDENTITY_HMAC_KEY_FILE}\"", compose);
        Assert.Contains("file: \"${MFA_ENCRYPTION_KEY_FILE:?set MFA_ENCRYPTION_KEY_FILE}\"", compose);
        Assert.Contains("file: \"${DEADLINE_PROVIDER_TOKEN_FILE:?set DEADLINE_PROVIDER_TOKEN_FILE}\"", compose);
        Assert.Contains("file: \"${AUTH_SESSION_SIGNING_KEY_FILE:?set AUTH_SESSION_SIGNING_KEY_FILE}\"", compose);
        Assert.Contains("file: \"${AUDIT_INTEGRITY_SIGNING_KEY_FILE:?set AUDIT_INTEGRITY_SIGNING_KEY_FILE}\"", compose);
        Assert.Contains("file: \"${ACCOUNTS_API_KEY_FILE:?set ACCOUNTS_API_KEY_FILE}\"", compose);
        Assert.Contains("file: \"${BOOTSTRAP_OWNER_PASSWORD_FILE:?set BOOTSTRAP_OWNER_PASSWORD_FILE}\"", compose);

        foreach (var forbidden in new[]
        {
            "POSTGRES_PASSWORD: \"${POSTGRES_PASSWORD",
            "ConnectionStrings__DefaultConnection: \"${ACCOUNTS_",
            "AuthSession__SigningKey: \"${AUTH_SESSION_SIGNING_KEY",
            "AuditIntegrity__SigningKeys__0__SigningKey: \"${AUDIT_INTEGRITY_SIGNING_KEY",
            "BootstrapOwner__OwnerInitialPassword: \"${BOOTSTRAP_OWNER_PASSWORD",
            "ACCOUNTS_API_KEY: \"${ACCOUNTS_API_KEY"
        })
        {
            Assert.DoesNotContain(forbidden, compose);
        }

        Assert.Contains("FileBackedConfiguration.AddFileBackedEnvironmentVariables(builder.Configuration)", program);
        var hostingConfig = File.ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "HostingConfiguration.cs"));
        Assert.Contains("_FILE", hostingConfig);
        Assert.Contains("readServerSecret", proxyRoute);
        Assert.Contains("readServerSecret", readyHelper);
    }

    [Fact]
    public void ProductionReadiness_VerifiesConfiguredBootstrapOwnerWithoutExposingPasswordToApiService()
    {
        var root = RepositoryRoot();
        var compose = File.ReadAllText(Path.Combine(root, "compose.production.yml"));
        var endpoints = File.ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Endpoints", "SystemEndpoints.cs"));
        var probe = File.ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Services", "SystemReadinessProbeService.cs"));
        var apiService = ServiceBlock(compose, "api", "frontend");

        Assert.Contains("BootstrapOwner__TenantSlug: \"${BOOTSTRAP_TENANT_SLUG:?set BOOTSTRAP_TENANT_SLUG}\"", apiService);
        Assert.Contains("BootstrapOwner__OwnerEmail: \"${BOOTSTRAP_OWNER_EMAIL:?set BOOTSTRAP_OWNER_EMAIL}\"", apiService);
        Assert.DoesNotContain("BootstrapOwner__OwnerInitialPassword", apiService);
        Assert.Contains("SystemReadinessProbeService readiness", endpoints);
        Assert.Contains("await readiness.GetAsync", endpoints);
        Assert.Contains("DatabaseTenantBootstrapResolver", probe);
        Assert.Contains("ResolveLoginTenantAsync(email", probe);
        Assert.Contains("user.Email.ToLower() == email", probe);
        Assert.Contains("user.Tenant.Slug.ToLower() == tenantSlug", probe);

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
        Assert.Contains("reverse_proxy {$ACCOUNTS_FRONTEND_UPSTREAM:127.0.0.1:3000}", caddy);
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
        var endpoints = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "SystemEndpoints.cs"));
        var probe = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Services", "SystemReadinessProbeService.cs"));

        Assert.Contains("MapGet(\"/health/live\"", endpoints);
        Assert.Contains("MapGet(\"/health/ready\"", endpoints);
        Assert.Contains("SystemReadinessProbeService readiness", endpoints);
        Assert.Contains("await readiness.GetAsync", endpoints);
        Assert.Contains("!probe.Ready", endpoints);
        Assert.Contains("StatusCodes.Status503ServiceUnavailable", endpoints);
        Assert.Contains("CanConnectAsync", probe);
        Assert.Contains("GetPendingMigrationsAsync", probe);
        Assert.Contains("HasActiveOwnerAsync", probe);
    }

    [Fact]
    public void Dockerfiles_AvoidLocalhostRuntimeDefaultsAndRunApiAsNonRoot()
    {
        var backendDockerfile = File.ReadAllText(Path.Combine(RepositoryRoot(), "Dockerfile.backend"));
        var frontendDockerfile = File.ReadAllText(Path.Combine(RepositoryRoot(), "Dockerfile.frontend"));

        Assert.Contains("USER $APP_UID", backendDockerfile);
        Assert.DoesNotContain("ENV API_URL=http://localhost:5090", frontendDockerfile);
        Assert.Contains(
            "/usr/local/lib/node_modules/npm",
            frontendDockerfile);
        Assert.Contains(
            "/usr/local/lib/node_modules/corepack",
            frontendDockerfile);
        Assert.Contains(
            "/opt/yarn-v*",
            frontendDockerfile);
        Assert.Contains("CMD [\"node\", \"server.js\"]", frontendDockerfile);
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

        Assert.True(File.Exists(workflowPath), "CI should run production-readiness gates on main pushes and every pull request.");

        var workflow = File.ReadAllText(workflowPath);
        var governanceVerifier = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "scripts", "verify-github-governance.ps1"));
        AssertPushRunsOnMainOnly(workflow);
        Assert.Contains("pull_request:", workflow);
        Assert.Contains(
            "group: ci-${{ github.event_name }}-${{ github.event.pull_request.number || github.ref || github.run_id }}",
            workflow);
        Assert.Contains("cancel-in-progress: true", workflow);
        Assert.Contains("vulnerability-alerts", governanceVerifier);
        Assert.Contains("required_linear_history.enabled", governanceVerifier);
        Assert.Contains("required_conversation_resolution.enabled", governanceVerifier);
        Assert.Contains("bypass_pull_request_allowances", governanceVerifier);
        Assert.Contains("reviewBypassAllowanceCount", governanceVerifier);
        Assert.Contains("reviewBypassShapeValid", governanceVerifier);
        Assert.Contains("reviewBypassUnsupportedForUserRepository", governanceVerifier);
        Assert.Contains("repositoryState.owner.type -ceq \"User\"", governanceVerifier);
        Assert.Contains("organization-only shape for personal repositories", governanceVerifier);
        Assert.Contains("$requiredCheckAppId = 15368", governanceVerifier);
        Assert.Contains("exactly one app-bound checks row", governanceVerifier);
        Assert.Contains("$branchState.commit.sha -cne $CommitSha", governanceVerifier);
        Assert.Contains("$requiredCodeScanningLanguages", governanceVerifier);
        Assert.Contains("CodeQL default setup is missing required language", governanceVerifier);

        var backendJob = WorkflowJob(workflow, "backend");
        Assert.Contains("actions/checkout", backendJob);
        Assert.Contains("actions/setup-dotnet", backendJob);
        Assert.Contains("global-json-file: global.json", backendJob);
        Assert.Contains("dotnet restore backend/Accounts.slnx --locked-mode", backendJob);
        Assert.Contains("dotnet restore backend/Accounts.slnx", backendJob);
        Assert.DoesNotContain("dotnet restore backend/Accounts.sln\r", backendJob);
        Assert.DoesNotContain("dotnet restore backend/Accounts.sln\n", backendJob);
        Assert.Contains("dotnet test backend/Accounts.Tests/Accounts.Tests.csproj", backendJob);
        Assert.Contains("dotnet build backend/Accounts.Api/Accounts.Api.csproj", backendJob);
        Assert.Contains("dotnet tool restore", backendJob);
        Assert.Contains("dotnet ef migrations has-pending-model-changes", backendJob);
        Assert.Contains("FullyQualifiedName~MigrationUpgradePostgresTests", backendJob);
        Assert.Contains("verify-migration-upgrade-evidence.ps1", backendJob);
        Assert.Contains("migration-upgrade-report.json", backendJob);
        Assert.Contains("migration-upgrade-verification-report.json", backendJob);
        Assert.Contains("name: postgres-migration-upgrade-gate", backendJob);

        var frontendJob = WorkflowJob(workflow, "frontend");
        Assert.Contains("actions/checkout", frontendJob);
        Assert.Contains("actions/setup-node", frontendJob);
        Assert.Contains("node-version-file: .nvmrc", frontendJob);
        Assert.Contains("cache: npm", frontendJob);
        Assert.Contains("cache-dependency-path: frontend/package-lock.json", frontendJob);
        Assert.Contains("npm ci", frontendJob);
        Assert.Contains("Audit frontend dependencies and write evidence", frontendJob);
        Assert.Contains("npm audit --audit-level=moderate --json", frontendJob);
        Assert.Contains("../scripts/write-dependency-evidence.ps1", frontendJob);
        Assert.Contains("dependency-audit-report.json", frontendJob);
        Assert.Contains("Upload dependency audit evidence", frontendJob);
        Assert.Contains("name: dependency-audit-release", frontendJob);
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
        var nodeVersionFile = File.ReadAllText(Path.Combine(RepositoryRoot(), ".nvmrc")).Trim();
        Assert.Equal("24.15.0", nodeVersionFile);

        var frontendPackageJson = File.ReadAllText(Path.Combine(RepositoryRoot(), "frontend", "package.json"));
        foreach (var suite in new[] { "test:unit", "test:readiness", "test:proxy", "test:auth", "test:api-client" })
            Assert.Contains(suite, frontendPackageJson);

        var productionConfigJob = WorkflowJob(workflow, "production-config");
        Assert.Contains("actions/checkout", productionConfigJob);
        Assert.Contains("docker compose -f compose.production.yml config --quiet", productionConfigJob);
        Assert.Contains("Validate production image contract", productionConfigJob);
        Assert.Contains("shell: pwsh", productionConfigJob);
        Assert.Contains("./scripts/verify-production-compose-images.ps1", productionConfigJob);
        Assert.Contains("-EvidencePath (Join-Path $env:RUNNER_TEMP \"accounts-production-safety/production-safety-report.json\")", productionConfigJob);
        Assert.Contains("Upload production safety evidence", productionConfigJob);
        Assert.Contains("name: production-safety-config", productionConfigJob);
        Assert.Contains("production-safety-report.json", productionConfigJob);
        Assert.Contains("if-no-files-found: error", productionConfigJob);
        Assert.DoesNotContain("docker compose -f compose.production.yml config\r", productionConfigJob);
        Assert.DoesNotContain("docker compose -f compose.production.yml config\n", productionConfigJob);
        Assert.Contains("caddy validate --config /etc/caddy/Caddyfile", productionConfigJob);
        Assert.Contains("deploy/caddy/Caddyfile.example", productionConfigJob);
        Assert.Contains("caddy:2@sha256:af5fdcd76f2db5e4e974ee92f96ee8c0fc3edb55bd4ba5032547cbf3f65e486d", productionConfigJob);
        Assert.DoesNotContain("docker build", productionConfigJob);
        Assert.DoesNotContain("docker/build-push-action", productionConfigJob);
        Assert.Contains("ACCOUNTS_API_IMAGE: ghcr.io/example/accounts-api@sha256:", productionConfigJob);
        Assert.Contains("ACCOUNTS_FRONTEND_IMAGE: ghcr.io/example/accounts-frontend@sha256:", productionConfigJob);
        Assert.Contains("AUDIT_INTEGRITY_SIGNING_KEY", productionConfigJob);
        Assert.Contains("TRUST_PROXY_HEADERS", productionConfigJob);
        Assert.Contains("ACCOUNTS_API_KEY_HASH", productionConfigJob);
        Assert.Contains("BOOTSTRAP_OWNER_PASSWORD", productionConfigJob);
        Assert.Contains("write_secret_file", productionConfigJob);
        Assert.Contains("POSTGRES_PASSWORD_FILE", productionConfigJob);
        Assert.Contains("ACCOUNTS_MIGRATION_CONNECTION_STRING_FILE", productionConfigJob);
        Assert.Contains("ACCOUNTS_APPLICATION_CONNECTION_STRING_FILE", productionConfigJob);
        Assert.Contains("POSTGRES_APPLICATION_PASSWORD_FILE", productionConfigJob);
        Assert.Contains("DATABASE_TENANT_CONTEXT_KEY_FILE", productionConfigJob);
        Assert.Contains("IDENTITY_HMAC_KEY_FILE", productionConfigJob);
        Assert.Contains("MFA_ENCRYPTION_KEY_FILE", productionConfigJob);
        Assert.Contains("DEADLINE_PROVIDER_TOKEN_FILE", productionConfigJob);
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
        Assert.DoesNotContain("set_masked_env ACCOUNTS_MIGRATION_CONNECTION_STRING", productionConfigJob);
        Assert.DoesNotContain("set_masked_env ACCOUNTS_APPLICATION_CONNECTION_STRING", productionConfigJob);
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
        Assert.DoesNotContain("ACCOUNTS_API_IMAGE: accounts-api-ci:${{ github.sha }}", productionSmokeJob);
        Assert.DoesNotContain("ACCOUNTS_FRONTEND_IMAGE: accounts-frontend-ci:${{ github.sha }}", productionSmokeJob);
        Assert.Contains("FRONTEND_PORT: \"3000\"", productionSmokeJob);
        Assert.Contains("NO_PROXY: accounts-smoke.local,127.0.0.1,localhost", productionSmokeJob);
        Assert.Contains("Generate ephemeral production smoke secrets", productionSmokeJob);
        Assert.Contains("::add-mask::", productionSmokeJob);
        Assert.Contains("GITHUB_ENV", productionSmokeJob);
        Assert.Contains("accounts_api_key_hash=\"$(printf '%s' \"$accounts_api_key\" | sha256sum", productionSmokeJob);
        Assert.Contains("write_secret_file", productionSmokeJob);
        Assert.Contains("POSTGRES_PASSWORD_FILE", productionSmokeJob);
        Assert.Contains("ACCOUNTS_MIGRATION_CONNECTION_STRING_FILE", productionSmokeJob);
        Assert.Contains("ACCOUNTS_APPLICATION_CONNECTION_STRING_FILE", productionSmokeJob);
        Assert.Contains("POSTGRES_APPLICATION_PASSWORD_FILE", productionSmokeJob);
        Assert.Contains("DATABASE_TENANT_CONTEXT_KEY_FILE", productionSmokeJob);
        Assert.Contains("IDENTITY_HMAC_KEY_FILE", productionSmokeJob);
        Assert.Contains("MFA_ENCRYPTION_KEY_FILE", productionSmokeJob);
        Assert.Contains("DEADLINE_PROVIDER_TOKEN_FILE", productionSmokeJob);
        Assert.Contains("AUTH_SESSION_SIGNING_KEY_FILE", productionSmokeJob);
        Assert.Contains("AUDIT_INTEGRITY_SIGNING_KEY_FILE", productionSmokeJob);
        Assert.Contains("ACCOUNTS_API_KEY_FILE", productionSmokeJob);
        Assert.Contains("BOOTSTRAP_OWNER_PASSWORD_FILE", productionSmokeJob);
        Assert.Contains("MONITORING_ERROR_SMOKE_ENABLED: \"true\"", productionSmokeJob);
        Assert.Contains("set_masked_env ACCOUNTS_API_KEY_HASH \"$accounts_api_key_hash\"", productionSmokeJob);
        Assert.Contains("generate_bootstrap_owner_password()", productionSmokeJob);
        Assert.Contains("printf 'CiOwner1!%s' \"$(generate_secret)\"", productionSmokeJob);
        Assert.Contains("bootstrap_owner_password=\"$(generate_bootstrap_owner_password)\"", productionSmokeJob);
        Assert.DoesNotContain("set_masked_env BOOTSTRAP_OWNER_PASSWORD \"$(generate_secret)\"", productionSmokeJob);
        Assert.DoesNotContain("set_masked_env POSTGRES_PASSWORD", productionSmokeJob);
        Assert.DoesNotContain("set_masked_env ACCOUNTS_MIGRATION_CONNECTION_STRING", productionSmokeJob);
        Assert.DoesNotContain("set_masked_env ACCOUNTS_APPLICATION_CONNECTION_STRING", productionSmokeJob);
        Assert.DoesNotContain("set_masked_env AUTH_SESSION_SIGNING_KEY", productionSmokeJob);
        Assert.DoesNotContain("set_masked_env AUDIT_INTEGRITY_SIGNING_KEY", productionSmokeJob);
        Assert.DoesNotContain("set_masked_env ACCOUNTS_API_KEY \"$accounts_api_key\"", productionSmokeJob);
        Assert.DoesNotContain("set_masked_env BOOTSTRAP_OWNER_PASSWORD", productionSmokeJob);
        Assert.Contains("Determine container promotion mode", productionSmokeJob);
        Assert.Contains("$GITHUB_EVENT_NAME\" == \"push\" && \"$GITHUB_REF\" == \"refs/heads/main", productionSmokeJob);
        Assert.Contains("Log in to GHCR for trusted promotion", productionSmokeJob);
        Assert.Contains("if: steps.promotion-mode.outputs.enabled == 'true'", productionSmokeJob);
        Assert.Equal(2, Regex.Matches(productionSmokeJob, "uses: docker/build-push-action@").Count);
        Assert.Contains("Build backend image exactly once", productionSmokeJob);
        Assert.Contains("Build frontend image exactly once", productionSmokeJob);
        Assert.Contains("push: ${{ steps.promotion-mode.outputs.enabled == 'true' }}", productionSmokeJob);
        Assert.Contains("load: ${{ steps.promotion-mode.outputs.enabled != 'true' }}", productionSmokeJob);
        Assert.DoesNotContain("docker build ", productionSmokeJob);
        Assert.Contains("backend_ref='${{ steps.promotion-mode.outputs.backend-name }}'@\"$backend_digest\"", productionSmokeJob);
        Assert.Contains("frontend_ref='${{ steps.promotion-mode.outputs.frontend-name }}'@\"$frontend_digest\"", productionSmokeJob);
        Assert.Contains("docker pull \"$ACCOUNTS_API_IMAGE\"", productionSmokeJob);
        Assert.Contains("docker pull \"$ACCOUNTS_FRONTEND_IMAGE\"", productionSmokeJob);
        Assert.Equal(2, Regex.Matches(productionSmokeJob, "uses: aquasecurity/trivy-action@").Count);
        Assert.Equal(2, Regex.Matches(productionSmokeJob, "uses: anchore/sbom-action@").Count);
        Assert.Equal(2, Regex.Matches(productionSmokeJob, "uses: actions/attest-build-provenance@").Count);
        Assert.Contains("severity: HIGH,CRITICAL", productionSmokeJob);
        Assert.Contains("format: spdx-json", productionSmokeJob);
        Assert.Contains("write-container-supply-chain-report.ps1", productionSmokeJob);
        Assert.Contains("verify-container-supply-chain-report.ps1", productionSmokeJob);
        Assert.Contains("name: container-supply-chain", productionSmokeJob);
        Assert.Contains("docker compose -f compose.production.yml up -d --wait --wait-timeout 300", productionSmokeJob);
        Assert.DoesNotContain("docker compose -f compose.production.yml up -d --build", productionSmokeJob);
        Assert.Contains("127.0.0.1 accounts-smoke.local", productionSmokeJob);
        Assert.Contains("accounts-production-smoke-ingress", productionSmokeJob);
        Assert.Contains("frontend_network=\"$(docker inspect", productionSmokeJob);
        Assert.Contains("docker create", productionSmokeJob);
        Assert.Contains("--network bridge", productionSmokeJob);
        Assert.Contains("docker network connect \"$frontend_network\" accounts-production-smoke-ingress", productionSmokeJob);
        Assert.Contains("docker start accounts-production-smoke-ingress", productionSmokeJob);
        Assert.Contains("-p 127.0.0.1:443:443", productionSmokeJob);
        Assert.Contains("ACCOUNTS_FRONTEND_UPSTREAM=frontend:3000", productionSmokeJob);
        Assert.Contains("--noproxy '*' --resolve accounts-smoke.local:443:127.0.0.1", productionSmokeJob);
        Assert.DoesNotContain("--network host", productionSmokeJob);
        Assert.Contains("caddy:2@sha256:af5fdcd76f2db5e4e974ee92f96ee8c0fc3edb55bd4ba5032547cbf3f65e486d", productionSmokeJob);
        Assert.Contains("ACCOUNTS_CADDY_GLOBAL_OPTIONS=local_certs", productionSmokeJob);
        Assert.Contains("update-ca-certificates", productionSmokeJob);
        Assert.Contains("NODE_EXTRA_CA_CERTS: ${{ github.workspace }}/.tmp/production-smoke-caddy/caddy-local-root.crt", productionSmokeJob);
        Assert.Contains("Run production smoke script", productionSmokeJob);
        Assert.Contains("./scripts/smoke-production.ps1", productionSmokeJob);
        Assert.Contains("-BaseUrl https://accounts-smoke.local", productionSmokeJob);
        Assert.Contains("-Email $env:BOOTSTRAP_OWNER_EMAIL", productionSmokeJob);
        Assert.Contains("-Password $bootstrapOwnerPassword", productionSmokeJob);
        Assert.Contains("-OutputDirectory (Join-Path $env:RUNNER_TEMP \"accounts-smoke\")", productionSmokeJob);
        Assert.Contains("-AllowEphemeralMfaEnrollment", productionSmokeJob);
        Assert.Contains(
            "-EphemeralMfaHandoffPath (Join-Path $env:RUNNER_TEMP \"accounts-visual-auth/totp-handoff.json\")",
            productionSmokeJob);
        Assert.Contains("--mfa-handoff-file=\"$MFA_HANDOFF_FILE\"", productionSmokeJob);
        Assert.Contains("trap 'rm -f \"$MFA_HANDOFF_FILE\"' EXIT", productionSmokeJob);
        Assert.Contains("rm -f \"$RUNNER_TEMP/accounts-visual-auth/totp-handoff.json\"", productionSmokeJob);
        Assert.Contains("-CheckMonitoringErrorRouting", productionSmokeJob);
        Assert.Contains("Upload monitoring error routing evidence", productionSmokeJob);
        Assert.Contains("name: monitoring-error-routing-smoke", productionSmokeJob);
        Assert.Contains("monitoring-error-routing-report.json", productionSmokeJob);
        Assert.Contains("Capture structured API log sample", productionSmokeJob);
        Assert.Contains("docker compose -f compose.production.yml logs --no-color --no-log-prefix api", productionSmokeJob);
        Assert.Contains("./scripts/verify-structured-logs.ps1", productionSmokeJob);
        Assert.Contains("api-structured.log", productionSmokeJob);
        Assert.Contains("structured-log-report.json", productionSmokeJob);
        Assert.Contains("Upload structured API log evidence", productionSmokeJob);
        Assert.Contains("name: structured-json-log-sample", productionSmokeJob);
        Assert.Contains("Verify certificate-validated PostgreSQL transport", productionSmokeJob);
        Assert.Contains("./scripts/verify-postgres-tls.ps1", productionSmokeJob);
        Assert.Contains("postgres-tls-report.json", productionSmokeJob);
        Assert.Contains("name: postgres-tls-runtime", productionSmokeJob);
        Assert.Contains("Run production backup restore drill", productionSmokeJob);
        Assert.Contains("Test PostgreSQL backup evidence binding", workflow);
        Assert.Contains("./scripts/test-postgres-backup-evidence.ps1", workflow);
        Assert.Contains("Test disposable MFA handoff writer", workflow);
        Assert.Contains("./scripts/test-smoke-mfa-handoff.ps1", workflow);
        Assert.Contains("$env:RUNNER_TEMP", productionSmokeJob);
        Assert.Contains("accounts-backups", productionSmokeJob);
        Assert.Contains("./scripts/backup-postgres.ps1", productionSmokeJob);
        Assert.Contains("-OutputDirectory $backupDir", productionSmokeJob);
        Assert.Equal(
            2,
            Regex.Matches(
                productionSmokeJob,
                Regex.Escape("-ReleaseCandidate $env:GITHUB_SHA")).Count);
        Assert.Contains("$backups = @(Get-ChildItem -LiteralPath $backupDir -Filter \"*.dump.cms\"", productionSmokeJob);
        Assert.Contains("$backups.Count -ne 1", productionSmokeJob);
        Assert.Contains("Expected exactly one backup dump", productionSmokeJob);
        Assert.Contains("$backup = $backups[0]", productionSmokeJob);
        Assert.Contains("Test-Path -LiteralPath \"$($backup.FullName).sha256\"", productionSmokeJob);
        Assert.Contains("Missing sha256 for", productionSmokeJob);
        Assert.Contains("Test-Path -LiteralPath \"$($backup.FullName).manifest.json\"", productionSmokeJob);
        Assert.Contains("Plaintext PostgreSQL dumps must not remain", productionSmokeJob);
        Assert.Contains("BACKUP_DECRYPTION_PRIVATE_KEY_FILE", productionSmokeJob);
        Assert.Contains("$restoreEvidencePath = Join-Path $backupDir \"restore-drill-report.json\"", productionSmokeJob);
        Assert.Contains("./scripts/verify-postgres-backup.ps1", productionSmokeJob);
        Assert.Contains("-BackupPath $backup.FullName", productionSmokeJob);
        Assert.Contains("-EvidencePath $restoreEvidencePath", productionSmokeJob);
        Assert.Contains("-GitHubActionsRunUrl $runUrl", productionSmokeJob);
        Assert.Contains("Upload backup restore drill evidence", productionSmokeJob);
        Assert.Contains("name: postgres-backup-restore-drill", productionSmokeJob);
        Assert.Contains("path: ${{ runner.temp }}/accounts-backups", productionSmokeJob);
        Assert.Contains("if-no-files-found: error", productionSmokeJob);
        Assert.DoesNotContain("-AllowRepositoryOutputForLocalDryRun", productionSmokeJob);
        Assert.DoesNotContain("-BaseUrl http://127.0.0.1:3000", productionSmokeJob);
        Assert.DoesNotContain("-AllowInsecureHttp", productionSmokeJob);
        Assert.Contains("docker compose -f compose.production.yml logs", productionSmokeJob);
        Assert.Contains("docker compose -f compose.production.yml down -v --remove-orphans", productionSmokeJob);
        Assert.DoesNotContain("ACCOUNTS_API_KEY: ", productionSmokeJob);
        Assert.DoesNotContain("ACCOUNTS_API_KEY_HASH: ", productionSmokeJob);
        Assert.DoesNotContain("POSTGRES_PASSWORD: ", productionSmokeJob);

        var machineEvidenceJob = WorkflowJob(workflow, "ci-machine-evidence-pack");
        Assert.Contains("actions/checkout", machineEvidenceJob);
        Assert.Contains("actions/download-artifact@37930b1c2abaa49bbe596cd826c3c89aef350131", machineEvidenceJob);
        Assert.Contains("pattern: \"!*.dockerbuild\"", machineEvidenceJob);
        Assert.Contains(
            "if: github.event_name == 'pull_request' || (github.event_name == 'push' && github.ref == 'refs/heads/main')",
            machineEvidenceJob);
        Assert.Contains("frontend", machineEvidenceJob);
        Assert.Contains("production-config", machineEvidenceJob);
        Assert.Contains("production-smoke", machineEvidenceJob);
        Assert.Contains("accounts-ci-machine-evidence", machineEvidenceJob);
        Assert.Contains("verify-ci-machine-evidence-pack.ps1", machineEvidenceJob);
        Assert.Contains("-CommitSha $env:GITHUB_SHA", machineEvidenceJob);
        Assert.Contains("-GitHubActionsRunUrl $runUrl", machineEvidenceJob);
        Assert.Equal(
            2,
            Regex.Matches(
                machineEvidenceJob,
                Regex.Escape("$allowVerificationOnlySupplyChain = $env:GITHUB_EVENT_NAME -eq \"pull_request\"")).Count);
        Assert.Equal(
            2,
            Regex.Matches(
                machineEvidenceJob,
                Regex.Escape("-AllowVerificationOnlySupplyChain:$allowVerificationOnlySupplyChain")).Count);
        Assert.Contains("-ReviewerWorkspaceDirectory $workspaceRoot", machineEvidenceJob);
        Assert.Contains("ci-machine-evidence-pack-report.json", machineEvidenceJob);
        Assert.Contains("name: ci-machine-evidence-pack", machineEvidenceJob);
    }

    [Fact]
    public void ContainerSupplyChain_PromotesAndVerifiesExactDigestsWithoutTrustingForks()
    {
        var root = RepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        var actionPolicy = File.ReadAllText(Path.Combine(root, "scripts", "verify-ci-actions.mjs"));
        var writerPath = Path.Combine(root, "scripts", "write-container-supply-chain-report.ps1");
        var writerTestsPath = Path.Combine(root, "scripts", "test-container-supply-chain-report.ps1");
        var verifierPath = Path.Combine(root, "scripts", "verify-container-supply-chain-report.ps1");
        var machinePack = File.ReadAllText(Path.Combine(root, "scripts", "verify-ci-machine-evidence-pack.ps1"));
        var releasePack = File.ReadAllText(Path.Combine(root, "scripts", "verify-release-artifact-pack.ps1"));
        var runbook = File.ReadAllText(Path.Combine(root, "Docs", "operations", "production-runbook.md"));
        var imageExample = File.ReadAllText(Path.Combine(root, "deploy", "production-images.env.example"));

        Assert.True(File.Exists(writerPath));
        Assert.True(File.Exists(writerTestsPath));
        Assert.True(File.Exists(verifierPath));
        var writer = File.ReadAllText(writerPath);
        var writerTests = File.ReadAllText(writerTestsPath);
        var verifier = File.ReadAllText(verifierPath);

        var actionUsages = Regex.Matches(workflow, @"^\s*uses:\s*[^@\s]+@([^\s#]+)", RegexOptions.Multiline);
        Assert.NotEmpty(actionUsages);
        Assert.All(actionUsages.Cast<Match>(), match => Assert.Matches("^[0-9a-f]{40}$", match.Groups[1].Value));
        Assert.Equal(
            Regex.Matches(workflow, "uses: actions/checkout@").Count,
            Regex.Matches(workflow, "persist-credentials: false").Count);
        foreach (var reviewedReference in new[]
        {
            "actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0",
            "actions/setup-node@48b55a011bda9f5d6aeb4c2d9c7362e8dae4041e",
            "actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1",
            "actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a",
            "actions/download-artifact@37930b1c2abaa49bbe596cd826c3c89aef350131",
            "docker/login-action@c94ce9fb468520275223c153574b00df6fe4bcc9",
            "docker/setup-buildx-action@8d2750c68a42422c14e847fe6c8ac0403b4cbd6f",
            "docker/build-push-action@10e90e3645eae34f1e60eeb005ba3a3d33f178e8",
            "aquasecurity/trivy-action@ed142fd0673e97e23eac54620cfb913e5ce36c25",
            "anchore/sbom-action@43a17d6e7add2b5535efe4dcae9952337c479a93",
            "actions/attest-build-provenance@43d14bc2b83dec42d39ecae14e916627a18bb661"
        })
        {
            Assert.Contains(reviewedReference, workflow);
            Assert.Contains(reviewedReference.Split('@')[0], actionPolicy);
            Assert.Contains(reviewedReference.Split('@')[1], actionPolicy);
        }

        Assert.Equal(2, Regex.Matches(workflow, "uses: docker/build-push-action@").Count);
        Assert.Contains("GITHUB_EVENT_NAME\" == \"push\"", workflow);
        Assert.Contains("GITHUB_REF\" == \"refs/heads/main\"", workflow);
        Assert.Contains("if: steps.promotion-mode.outputs.enabled == 'true'", workflow);
        Assert.Contains(
            "if: github.event_name == 'pull_request' || (github.event_name == 'push' && github.ref == 'refs/heads/main')",
            workflow);
        Assert.Contains("push: ${{ steps.promotion-mode.outputs.enabled == 'true' }}", workflow);
        Assert.Contains("load: ${{ steps.promotion-mode.outputs.enabled != 'true' }}", workflow);
        Assert.Contains("docker pull \"$ACCOUNTS_API_IMAGE\"", workflow);
        Assert.Contains("docker pull \"$ACCOUNTS_FRONTEND_IMAGE\"", workflow);
        Assert.DoesNotContain("docker build -f Dockerfile.backend", workflow);
        Assert.DoesNotContain("docker build -f Dockerfile.frontend", workflow);

        foreach (var requiredEvidence in new[]
        {
            "container-supply-chain-report.json",
            "container-supply-chain-verification-report.json",
            "backend-trivy.json",
            "frontend-trivy.json",
            "backend-sbom.spdx.json",
            "frontend-sbom.spdx.json",
            "backend-provenance.jsonl",
            "frontend-provenance.jsonl"
        })
        {
            Assert.Contains(requiredEvidence, workflow + writer + verifier + machinePack + releasePack);
        }

        Assert.Contains("highCriticalVulnerabilityCount", writer);
        Assert.Contains("Trivy omits this property for a clean target", writer);
        Assert.Contains("Results array must not be empty", writer);
        Assert.Contains("non-empty Severity", writer);
        Assert.Contains("New-CleanScan", writerTests);
        Assert.Contains("CVE-SYNTHETIC-HIGH", writerTests);
        Assert.Contains("CVE-SYNTHETIC-CRITICAL", writerTests);
        Assert.Contains("Assert-VerifierRejectsRetainedBlockedFinding", writerTests);
        Assert.Contains("must contain a Results array", writerTests);
        Assert.Contains("test-container-supply-chain-report.ps1", workflow);
        Assert.Contains("test-container-supply-chain-report.ps1", actionPolicy);
        Assert.Contains("builtInvocationCount = 1", writer);
        Assert.Contains("productionSmokeUsedExactDigestReferences", writer);
        Assert.Contains("status must be passed for release evidence", verifier);
        Assert.Contains("Unpromoted evidence status must be explicitly blocked", verifier);
        Assert.Contains("retained Trivy report contains HIGH/CRITICAL vulnerabilities", verifier);
        Assert.Contains("ArtifactName must match the exact scanned image reference", verifier);
        Assert.Contains("Results must not be empty", verifier);
        Assert.Contains("Vulnerabilities must be an array when present", verifier);
        Assert.Contains("retained SBOM must be SPDX JSON", verifier);
        Assert.Contains("retained GitHub provenance bundle", verifier);
        Assert.Contains("evidenceReport", verifier);
        Assert.Contains("Assert-ContainerSupplyChainEvidence", machinePack);
        Assert.Contains("Assert-ContainerSupplyChainEvidence", releasePack);
        Assert.Contains("Tags are not accepted for production", runbook);
        Assert.Contains("Pull requests and forks use local verification images without registry credentials", runbook);
        Assert.Contains("gh attestation verify", runbook);
        Assert.Equal(2, Regex.Matches(imageExample, @"ghcr\.io/[a-z0-9._/-]+@sha256:[0-9a-f]{64}").Count);
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
    public void DependencyEvidenceWriter_RecordsAuditPolicyAndLockfileHashes()
    {
        var root = RepositoryRoot();
        var scriptPath = Path.Combine(root, "scripts", "write-dependency-evidence.ps1");
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        var readinessService = ProductionReadinessSource(root);

        Assert.True(File.Exists(scriptPath), "Dependency evidence writer should retain release audit evidence.");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("[string]$NpmAuditJsonPath", script);
        Assert.Contains("[string]$EvidencePath", script);
        Assert.Contains("package-lock.json", script);
        Assert.Contains("lockfileVersion", script);
        Assert.Contains("metadata.vulnerabilities", script);
        Assert.Contains("moderate/high/critical vulnerabilities", script);
        Assert.Contains("NuGetAudit", script);
        Assert.Contains("NU1901", script);
        Assert.Contains("NU1904", script);
        Assert.Contains("verify-ci-actions.mjs", script);
        Assert.Contains("actions/download-artifact", File.ReadAllText(Path.Combine(root, "scripts", "verify-ci-actions.mjs")));
        Assert.Contains("dependency-audit-report.json", workflow);
        Assert.Contains("npm-audit.json", workflow);
        Assert.Contains("dependency-audit-release", workflow);
        Assert.Contains("write-dependency-evidence.ps1", readinessService);
        Assert.Contains("dependency-audit-release", readinessService);
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
        Assert.Contains("exact CI-promoted GHCR digest", runbook);
        Assert.Contains("Do not deploy production by rebuilding from the checkout", runbook);
        Assert.Contains("production-safety-config", runbook);
        Assert.Contains("production-safety-report.json", runbook);
        Assert.Contains("DatabaseStartup__AutoMigrateOnStartup=false", runbook);
        Assert.Contains("demo seeding is disabled", runbook);
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
        Assert.Contains("ghcr.io/example/accounts-api@sha256:", imageVerifier);
        Assert.Contains("ghcr.io/example/accounts-frontend@sha256:", imageVerifier);
        Assert.Contains("digestPinned", imageVerifier);
        Assert.Contains("backendAndMigrateSameDigest", imageVerifier);
        Assert.Contains("productionSmokePullsExactDigests", imageVerifier);
        Assert.Contains("--build", imageVerifier);
    }

    [Fact]
    public void ProductionComposeVerifier_EmitsMigrationAndSeedSafetyEvidence()
    {
        var root = RepositoryRoot();
        var compose = File.ReadAllText(Path.Combine(root, "compose.production.yml"));
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        var imageVerifierPath = Path.Combine(root, "scripts", "verify-production-compose-images.ps1");
        var imageVerifier = File.ReadAllText(imageVerifierPath);

        Assert.Contains("command: [\"--migrate-only\"]", compose);
        Assert.Contains("condition: service_completed_successfully", compose);
        Assert.Contains("DatabaseStartup__AutoMigrateOnStartup: \"false\"", compose);
        Assert.Contains("DatabaseStartup__SeedDemoData: \"false\"", compose);
        Assert.DoesNotContain("DatabaseStartup__AllowStartupMigrationInProduction: \"true\"", compose);
        Assert.DoesNotContain("DatabaseStartup__AllowDemoSeedInProduction: \"true\"", compose);

        Assert.Contains("[string]$EvidencePath", imageVerifier);
        Assert.Contains("migrate service must run exactly '--migrate-only'", imageVerifier);
        Assert.Contains("DatabaseStartup__AutoMigrateOnStartup", imageVerifier);
        Assert.Contains("DatabaseStartup__SeedDemoData", imageVerifier);
        Assert.Contains("DatabaseStartup__AllowStartupMigrationInProduction", imageVerifier);
        Assert.Contains("DatabaseStartup__AllowDemoSeedInProduction", imageVerifier);
        Assert.Contains("BootstrapOwner__OwnerInitialPassword_FILE", imageVerifier);
        Assert.Contains("api service must not mount or receive the bootstrap owner initial password", imageVerifier);
        Assert.Contains("production-safety-report.json", workflow);
        Assert.Contains("production-safety-config", workflow);
        Assert.Contains("migrationSafety", imageVerifier);
        Assert.Contains("seedSafety", imageVerifier);
        Assert.Contains("Production safety evidence written", imageVerifier);
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
        Assert.Contains("lg:block", appNavbar);
        Assert.DoesNotContain("md:block", appNavbar);
        var mobileMenuIndex = normalisedNavbar.IndexOf("{/* Mobile menu */}", StringComparison.Ordinal);
        Assert.True(mobileMenuIndex > 0, "Navbar should keep the mobile menu section marker for source guards.");
        var mobileHeader = normalisedNavbar[..mobileMenuIndex];
        Assert.Contains("logoutError && user", mobileHeader);
        Assert.Contains("lg:hidden", mobileHeader);
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
            "page.tsx")) + File.ReadAllText(Path.Combine(
                root, "frontend", "src", "components", "period", "PeriodWorkspaceRoute.tsx"));
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

    private static void AssertPushRunsOnMainOnly(string workflow)
    {
        Assert.Contains("push:", workflow);

        var pushMatch = Regex.Match(
            workflow,
            @"(?ms)^  push:\s*\r?\n(?<body>.*?)(?=^  [A-Za-z_][A-Za-z0-9_-]*:|^permissions:|^jobs:|\z)");
        Assert.True(pushMatch.Success, "CI workflow should declare a push trigger.");
        Assert.Contains("branches:", pushMatch.Groups["body"].Value);
        Assert.Contains("- main", pushMatch.Groups["body"].Value);
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
    public void CoreCompanyEndpoints_UseDirectCompanyAccessGuards()
    {
        var root = RepositoryRoot();
        var source = File
            .ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Endpoints", "CompanyEndpoints.cs"))
            .Replace("\r\n", "\n");
        var periodStatusEndpoint = File
            .ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Endpoints", "PeriodStatusEndpoint.cs"))
            .Replace("\r\n", "\n");
        // The company DELETE handler is extracted to a named method (like PeriodStatusEndpoint.UpdateAsync).
        var companyDeletionEndpoint = File
            .ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Endpoints", "CompanyDeletionEndpoint.cs"))
            .Replace("\r\n", "\n");
        var companyOnboardingEndpoint = File
            .ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Endpoints", "CompanyOnboardingEndpoint.cs"))
            .Replace("\r\n", "\n");
        var guardedCoreEndpoints = source + "\n" + periodStatusEndpoint + "\n" + companyDeletionEndpoint + "\n" + companyOnboardingEndpoint;

        Assert.Contains("CompanyEndpointAccess.CanAccessCompanyAsync", guardedCoreEndpoints);
        Assert.Contains("CompanyDashboardRows", source);
        Assert.Contains("CompanyDeletionEndpoint.DeleteAsync", source);
        Assert.True(
            Regex.Matches(guardedCoreEndpoints, "CompanyEndpointAccess\\.CanAccessCompanyAsync\\(context, db, id\\)").Count >= 3,
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
            ("DocumentEndpoints.cs", 7)
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

        var revenue = File
            .ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "RevenueEndpoints.cs"))
            .Replace("\r\n", "\n");
        Assert.Equal(8, Regex.Matches(revenue, "\\.MapGet\\(").Count);
        foreach (var marker in new[]
        {
            "group.MapGet(\"/tax-computation\"",
            "group.MapGet(\"/ct1-support\"",
            "group.MapGet(\"/ixbrl\"",
            "group.MapGet(\"/ixbrl/final\""
        })
        {
            var snippet = SourceSegment(revenue, marker, @"\n\s*group\.Map(?:Get|Post|Put|Patch|Delete)\(");
            Assert.Contains("CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", snippet);
        }
        foreach (var marker in new[]
        {
            "public static async Task<IResult> GetCorporationTaxScopeReviewEndpointAsync",
            "public static async Task<IResult> GetCorporationTaxFilingSupportEndpointAsync",
            "public static async Task<IResult> GetCorporationTaxSupportWorksheetEndpointAsync",
            "public static async Task<IResult> ExportCorporationTaxSupportWorksheetEndpointAsync"
        })
        {
            var snippet = SourceSegment(revenue, marker, @"\n    public static");
            Assert.Contains("CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId)", snippet);
        }

        static string SourceSegment(string source, string marker, string nextPattern)
        {
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Expected source marker {marker}.");
            var remaining = source[(start + marker.Length)..];
            var next = Regex.Match(remaining, nextPattern, RegexOptions.None, TimeSpan.FromSeconds(1));
            return next.Success
                ? source[start..(start + marker.Length + next.Index)]
                : source[start..];
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
    public void CoreCompanyWriteEndpoints_UseDirectRequestAuthorization()
    {
        var root = RepositoryRoot();
        var source = File
            .ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Endpoints", "CompanyEndpoints.cs"))
            .Replace("\r\n", "\n");
        var periodStatusEndpoint = File
            .ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Endpoints", "PeriodStatusEndpoint.cs"))
            .Replace("\r\n", "\n");
        // The company DELETE handler is extracted to a named method (like PeriodStatusEndpoint.UpdateAsync).
        var companyDeletionEndpoint = File
            .ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Endpoints", "CompanyDeletionEndpoint.cs"))
            .Replace("\r\n", "\n");
        var companyOnboardingEndpoint = File
            .ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Endpoints", "CompanyOnboardingEndpoint.cs"))
            .Replace("\r\n", "\n");
        var guardedCoreEndpoints = source + "\n" + periodStatusEndpoint + "\n" + companyDeletionEndpoint + "\n" + companyOnboardingEndpoint;

        Assert.Contains("EndpointRequestAuthorization.AuthorizeCurrentRequest", guardedCoreEndpoints);
        Assert.True(
            Regex.Matches(guardedCoreEndpoints, "EndpointRequestAuthorization\\.AuthorizeCurrentRequest\\(context, apiAccess\\)").Count >= 8,
            "Core company write endpoints should guard direct API and role authorization.");

        // companies.MapDelete is wired to the extracted CompanyDeletionEndpoint.DeleteAsync, checked below.
        foreach (var marker in new[]
        {
            "companies.MapPost(\"/\", async",
            "companies.MapPut(\"/{id:int}\", async",
            "companies.MapPost(\"/{id:int}/annual-return-dates\", async",
            "officers.MapPost(\"/\", async",
            "officers.MapPut(\"/{id:int}\", async",
            "officers.MapDelete(\"/{id:int}\", async",
            "periods.MapPost(\"/\", async"
        })
        {
            var snippet = EndpointSnippet(source, marker);
            Assert.Contains("EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", snippet);
            Assert.Contains("ApiAccessService apiAccess", snippet);
        }
        Assert.Contains("periods.MapPut(\"/{id:int}/status\", PeriodStatusEndpoint.UpdateAsync)", source);
        Assert.Contains("ApiAccessService apiAccess", periodStatusEndpoint);
        Assert.Contains("EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", periodStatusEndpoint);
        Assert.Contains("CompanyDeletionEndpoint.DeleteAsync", source);
        Assert.Contains("CompanyDeletionEndpoint.RecoverAsync", source);
        Assert.Contains("ApiAccessService apiAccess", companyDeletionEndpoint);
        Assert.Contains("EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", companyDeletionEndpoint);
        AssertOccursBefore(companyDeletionEndpoint, "CompanyEndpointAccess.CanAccessCompanyAsync(context, db, id)", "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)");
        Assert.Contains("companies.MapPost(\"/onboard\", CompanyOnboardingEndpoint.CreateAsync)", source);
        Assert.Contains("EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)", companyOnboardingEndpoint);
        Assert.Contains("user.Role, \"Owner\"", companyOnboardingEndpoint);

        foreach (var marker in new[]
        {
            "companies.MapPut(\"/{id:int}\", async"
        })
        {
            var snippet = EndpointSnippet(source, marker);
            AssertOccursBefore(snippet, "CompanyEndpointAccess.CanAccessCompanyAsync(context, db, id)", "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)");
        }

        foreach (var marker in new[]
        {
            "officers.MapPost(\"/\", async",
            "officers.MapPut(\"/{id:int}\", async",
            "officers.MapDelete(\"/{id:int}\", async",
            "periods.MapPost(\"/\", async"
        })
        {
            var snippet = EndpointSnippet(source, marker);
            AssertOccursBefore(snippet, "CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId)", "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)");
        }
        AssertOccursBefore(periodStatusEndpoint, "CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId)", "EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess)");

        AssertOccursBefore(EndpointSnippet(source, "companies.MapPut(\"/{id:int}\", async"), "CompanyEndpointAccess.CanAccessCompanyAsync(context, db, id)", "EndpointInputs.ValidateCompany(input)");

        static string EndpointSnippet(string source, string marker)
        {
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Expected to find endpoint marker {marker}.");
            var nextMap = Regex.Match(source[start..], @"\n\s*(?:companies|officers|periods)\.Map(?:Get|Post|Put|Delete)", RegexOptions.None, TimeSpan.FromSeconds(1));
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
        AssertOccursBefore(companyInfoWrite, "writeGuard.BlockIfCompanyMasterDataLockedAsync(companyId)", "service.SaveCharityInfoAsync(");
        AssertOccursBefore(companyInfoWrite, "service.SaveCharityInfoAsync(", "audit.LogAsync(");

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
        Assert.Contains("BACKUP_ENCRYPTION_CERTIFICATE_FILE", backupScript);
        Assert.Contains("CMS/AES-256-CBC", backupScript);
        Assert.Contains("openssl cms -encrypt", backupScript);
        Assert.Contains("plaintextDumpRetained", backupScript);
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
        Assert.Contains("BACKUP_DECRYPTION_PRIVATE_KEY_FILE", restoreScript);
        Assert.Contains("openssl cms -decrypt", restoreScript);
        Assert.Contains("AllowUnencryptedBackupRestore", restoreScript);
        Assert.Contains("accounts-backup-decrypt-", restoreScript);
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
        Assert.Contains("[string]$EvidencePath", verifyScript);
        Assert.Contains("VerifyDatabase must be different from SourceDatabase", verifyScript);
        Assert.Contains(".Equals($SourceDatabase, [StringComparison]::OrdinalIgnoreCase)", verifyScript);
        AssertOccursBefore(verifyScript, "VerifyDatabase must be different from SourceDatabase", "Drop previous restore verification database");
        Assert.Contains("Checksum file not found", verifyScript);
        Assert.Contains("Checksum file is not in sha256 format", verifyScript);
        Assert.Contains("[switch]$RequireNonEmpty", verifyScript);
        Assert.Contains("if ($RequireNonEmpty -and $sourceCount -le 0)", verifyScript);
        Assert.Contains("restoredCount -ne $sourceCount", verifyScript);
        Assert.Contains("table = $TableName", verifyScript);
        Assert.Contains("sourceCount = $sourceCount", verifyScript);
        Assert.Contains("restoredCount = $restoredCount", verifyScript);
        Assert.Contains("backupSha256", verifyScript);
        Assert.Contains("tableChecks", verifyScript);
        Assert.Contains("backupEncryption", verifyScript);
        Assert.Contains("restoredFromEncryptedCopy", verifyScript);
        Assert.Contains("$backupCreatedAtValue -is [DateTime]", verifyScript);
        Assert.Contains("which can discard fractions", verifyScript);
        Assert.Contains("[Globalization.DateTimeStyles]::RoundtripKind", verifyScript);
        Assert.Contains("schemaChecks", verifyScript);
        Assert.Contains("figureChecks", verifyScript);
        Assert.Contains("opening balance net total", verifyScript);
        Assert.Contains("sum(`\"Debit`\" - `\"Credit`\")", verifyScript);
        Assert.DoesNotContain("sum(`\"Amount`\"), 0)::text from opening_balances", verifyScript);
        Assert.Contains("fingerprintChecks", verifyScript);
        Assert.Contains("auditIntegrityChecks", verifyScript);
        Assert.Contains("recoveryMetrics", verifyScript);
        Assert.Contains("[int]$RpoTargetSeconds = 86400", verifyScript);
        Assert.Contains("[int]$RtoTargetSeconds = 14400", verifyScript);
        Assert.Contains("status = if ($rpoTargetMet -and $rtoTargetMet) { \"passed\" } else { \"failed\" }", verifyScript);
        Assert.Contains("if (-not $rpoTargetMet)", verifyScript);
        Assert.Contains("if (-not $rtoTargetMet)", verifyScript);
        Assert.Contains("Restore recovery target failure", verifyScript);
        Assert.Contains("ConvertTo-Json -Depth 7", verifyScript);
        Assert.Contains("Restore evidence written", verifyScript);
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
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        Assert.Contains("restore-drill-report.json", workflow);
        Assert.Contains("postgres-backup-restore-drill", workflow);
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
        Assert.Contains("SMOKE_TOTP_SECRET", runbook);
        Assert.Contains("/health/ready", smokeScript);
        Assert.Contains("/api/auth/login", smokeScript);
        Assert.Contains("/api/auth/mfa/challenge", smokeScript);
        Assert.Contains("New-TotpCode", smokeScript);
        Assert.Contains("ConvertFrom-Base32", smokeScript);
        Assert.Contains("SMOKE_TOTP_SECRET", smokeScript);
        Assert.Contains("Completing privileged-account MFA", smokeScript);
        Assert.Contains("[switch]$AllowEphemeralMfaEnrollment", smokeScript);
        Assert.Contains("-AllowEphemeralMfaEnrollment is only for a disposable CI bootstrap account", smokeScript);
        var mfaChallengeIndex = smokeScript.IndexOf("if ([int]$loginResponse.StatusCode -eq 202)", StringComparison.Ordinal);
        var cookieAssertionIndex = smokeScript.IndexOf("Assert-SetCookieAttribute -Response $loginResponse", StringComparison.Ordinal);
        var authenticatedSessionIndex = smokeScript.IndexOf("Checking authenticated session", StringComparison.Ordinal);
        Assert.True(mfaChallengeIndex >= 0 && mfaChallengeIndex < cookieAssertionIndex,
            "The smoke must complete a privileged MFA challenge before it expects session cookies.");
        Assert.True(mfaChallengeIndex < authenticatedSessionIndex,
            "The smoke must complete a privileged MFA challenge before it calls authenticated endpoints.");
        Assert.True(
            smokeScript.IndexOf(
                "if (-not $AllowEphemeralMfaEnrollment)",
                mfaChallengeIndex,
                StringComparison.Ordinal) > mfaChallengeIndex,
            "MFA enrollment must fail closed unless the caller explicitly identifies a disposable CI account.");
        Assert.Contains("/api/auth/me", smokeScript);
        Assert.Contains("$currentUser.mfaVerified -ne $true", smokeScript);
        Assert.Contains("$currentUser.mfaMethod -cne \"totp\"", smokeScript);
        var postLogoutAssertionIndex = smokeScript.IndexOf(
            "Expected /api/auth/me to be unauthorized after logout",
            StringComparison.Ordinal);
        var ephemeralHandoffWriteIndex = smokeScript.LastIndexOf(
            "Write-EphemeralMfaHandoff `",
            StringComparison.Ordinal);
        Assert.True(postLogoutAssertionIndex >= 0 && ephemeralHandoffWriteIndex > postLogoutAssertionIndex,
            "The disposable MFA seed handoff must not exist until every smoke assertion and logout check has passed.");
        Assert.Contains("accounts_csrf", smokeScript);
        Assert.Contains("Assert-SetCookieAttribute", smokeScript);
        Assert.Contains("$Headers -is [System.Net.WebHeaderCollection]", smokeScript);
        Assert.Contains("$Headers.GetValues($Name)", smokeScript);
        Assert.Contains("accounts_session", smokeScript);
        Assert.Contains("Cookie '$CookieName' from HTTPS login did not include '$Attribute'", smokeScript);
        Assert.Contains("-CookieName \"accounts_session\" -Attribute \"Secure\"", smokeScript);
        Assert.Contains("-CookieName \"accounts_csrf\" -Attribute \"Secure\"", smokeScript);
        Assert.Contains("X-CSRF-Token", smokeScript);
        Assert.Contains("CheckMonitoringErrorRouting", smokeScript);
        Assert.Contains("/api/system/monitoring/error-smoke", smokeScript);
        Assert.Contains("monitoring-error-routing-report.json", smokeScript);
        Assert.Contains("Monitoring error-routing evidence written", smokeScript);
        Assert.Contains("/api/system/production-readiness", smokeScript);
        Assert.Contains("production-readiness-report.json", smokeScript);
        Assert.Contains("Production readiness report written", smokeScript);
        Assert.Contains("verify-production-readiness-report.ps1", runbook);
        Assert.Contains("production-readiness-verification-report.json", runbook);
        Assert.Contains("dotnet ef migrations has-pending-model-changes", runbook);
        Assert.Contains("MigrationUpgradePostgresTests", runbook);
        Assert.Contains("verify-migration-upgrade-evidence.ps1", runbook);
        Assert.Contains("migration-upgrade-report.json", runbook);
        Assert.Contains("migration-upgrade-verification-report.json", runbook);
        Assert.Contains("20260621123340_AddCroSignatories", runbook);
        Assert.Contains("transactional rollback", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verify-structured-logs.ps1", runbook);
        Assert.Contains("structured-log-report.json", runbook);
        Assert.Contains("structured-json-log-sample", runbook);
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
    public void ReleaseEvidenceTemplates_CoverHumanVisualAccountantAndProviderSignoffs()
    {
        var root = RepositoryRoot();
        var runbook = File.ReadAllText(Path.Combine(root, "Docs", "operations", "production-runbook.md"));
        var templateDir = Path.Combine(root, "Docs", "release-evidence");
        var visualPath = Path.Combine(templateDir, "visual-qa-signoff-template.md");
        var sourceLawPath = Path.Combine(templateDir, "source-law-review-template.md");
        var externalRosIxbrlPath = Path.Combine(templateDir, "external-ros-ixbrl-validation-template.md");
        var accountantPath = Path.Combine(templateDir, "qualified-accountant-acceptance-template.md");
        var manualHandoffPath = Path.Combine(templateDir, "manual-handoff-acceptance-template.md");
        var monitoringPath = Path.Combine(templateDir, "monitoring-provider-confirmation-template.md");

        foreach (var path in new[] { visualPath, sourceLawPath, externalRosIxbrlPath, accountantPath, manualHandoffPath, monitoringPath })
        {
            Assert.True(File.Exists(path), $"Missing release evidence template: {path}");
            Assert.Contains(Path.GetFileName(path), runbook);
        }

        var visual = File.ReadAllText(visualPath);
        Assert.Contains("visual-smoke-screenshots", visual);
        Assert.Contains("visual-smoke-manifest.json", visual);
        Assert.Contains("visual-smoke-evidence-report.json", visual);
        Assert.Contains("accountant-workbench-evidence-report.json", visual);
        Assert.Contains("dashboard", visual);
        Assert.Contains("production-readiness", visual);
        Assert.Contains("workbench-preview", visual);
        Assert.Contains("Required formats", visual);
        Assert.Contains("Reviewer signature", visual);
        Assert.Contains("screenshot nonblank pixel diversity evidence", visual);
        Assert.Contains("Minimum PNG IDAT byte size", visual);
        Assert.Contains("Minimum screenshot pixel sample count", visual);
        Assert.Contains("Minimum sampled distinct color count", visual);
        Assert.Contains("Minimum screenshot luminance range", visual);
        Assert.Contains("Minimum automated contrast ratio", visual);
        Assert.Contains("pngIdatByteSize", visual);
        Assert.Contains("pixelSampleCount", visual);
        Assert.Contains("sampledDistinctColorCount", visual);
        Assert.Contains("luminanceRange", visual);
        Assert.Contains("themeContrastResult.minimumContrastRatio", visual);
        Assert.Contains("artifact file fields must be exactly", visual);
        Assert.Contains("Reviewer name, reviewer role and\nreviewer signature fields must be real retained evidence values", visual);
        Assert.Contains("Use exactly `pass`", visual);
        Assert.Contains("rejects", visual);
        Assert.Contains("`accepted`, or other ambiguous", visual);
        Assert.Contains("Each state `Notes` cell must be the exact retained visual evidence reference", visual);
        Assert.Contains("visual-smoke-evidence-report.json#routeCoverage.<state-id>", visual);
        Assert.Contains("Canonical state count", visual);
        Assert.Contains("Retained screenshot count", visual);
        Assert.Contains("Semantic content hash count", visual);

        var sourceLaw = File.ReadAllText(sourceLawPath);
        Assert.Contains("source-law-snapshot-fingerprint", sourceLaw);
        Assert.Contains("source-law-review-ledger", sourceLaw);
        Assert.Contains("source-law-change-review-note", sourceLaw);
        Assert.Contains("qualified-accountant-source-law-signoff", sourceLaw);
        Assert.Contains("cro-financial-statements-requirements", sourceLaw);
        Assert.Contains("revenue-accepted-taxonomies", sourceLaw);
        Assert.Contains("frc-frs-102", sourceLaw);
        Assert.Contains("frc-frs-105", sourceLaw);
        Assert.Contains("charities-regulator-annual-report", sourceLaw);
        Assert.Contains("Qualified accountant source-law sign-off", sourceLaw);
        Assert.Contains("64-character SHA-256 digest", sourceLaw);
        Assert.Contains("source-law-snapshot-fingerprint#<snapshot-id>", sourceLaw);
        Assert.Contains("Reviewer and qualified\naccountant identity", sourceLaw);
        Assert.Contains("fields must be real retained evidence values", sourceLaw);
        Assert.Contains("Use `yes` for `URL reachable`", sourceLaw);
        Assert.Contains("The verifier rejects generic `accepted` placeholders", sourceLaw);
        Assert.Contains("exactly `no change`, `reflected`, or `blocking`", sourceLaw);
        Assert.Contains("trailing\nimpact prose such as `reflected in notes`", sourceLaw);
        Assert.Contains("Each `Notes` cell must be the exact retained per-source review note reference", sourceLaw);
        Assert.Contains("source-law-review-ledger#<source-id>", sourceLaw);

        var externalRosIxbrl = File.ReadAllText(externalRosIxbrlPath);
        Assert.Contains("External ROS/iXBRL validation", externalRosIxbrl);
        Assert.Contains("Internal XML checks are not Revenue acceptance evidence", externalRosIxbrl);
        Assert.Contains("Generated iXBRL SHA-256", externalRosIxbrl);
        Assert.Contains("validation environment", externalRosIxbrl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("real retained evidence values, not placeholders", externalRosIxbrl);
        Assert.Contains("Generated iXBRL artifact name", externalRosIxbrl);
        Assert.Contains("`.xhtml`, `.html`, or\n`.zip` artifact", externalRosIxbrl);
        Assert.Contains("Use a real external validation reference for each scenario", externalRosIxbrl);
        Assert.Contains("in the `Decision` column only when the external reference", externalRosIxbrl);
        Assert.Contains("Record the actual taxonomy package", externalRosIxbrl);
        Assert.Contains("the taxonomy package", externalRosIxbrl);
        Assert.Contains("external-ros-validation-ledger#<scenario>", externalRosIxbrl);
        Assert.Contains("revenue-taxonomy-package-ledger#<scenario>", externalRosIxbrl);
        Assert.Contains("must be the exact retained", externalRosIxbrl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("micro-ltd", externalRosIxbrl);
        Assert.Contains("small-abridged-ltd", externalRosIxbrl);
        Assert.Contains("dac-small", externalRosIxbrl);
        Assert.Contains("clg-charity", externalRosIxbrl);
        Assert.Contains("medium-audit-required", externalRosIxbrl);
        Assert.Contains("64-character SHA-256 digests", externalRosIxbrl);
        Assert.Contains("Reviewer signature", externalRosIxbrl);

        var accountant = File.ReadAllText(accountantPath);
        Assert.Contains("dependency-audit-release", accountant);
        Assert.Contains("production-safety-config", accountant);
        Assert.Contains("postgres-backup-restore-drill", accountant);
        Assert.Contains("production-readiness-report", accountant);
        Assert.Contains("production-readiness-verification-report.json", accountant);
        Assert.Contains("micro-ltd", accountant);
        Assert.Contains("small-abridged-ltd", accountant);
        Assert.DoesNotContain("micro-ltd-standard", accountant);
        Assert.DoesNotContain("small-ltd-abridged", accountant);
        Assert.Contains("dac-small", accountant);
        Assert.Contains("clg-charity", accountant);
        Assert.Contains("medium-audit-required", accountant);
        Assert.Contains("Direct CRO submission remains unsupported", accountant);
        Assert.Contains("Direct ROS submission remains unsupported", accountant);
        Assert.Contains("Qualified accountant signature", accountant);
        Assert.Contains("40-character commit SHA", accountant);
        Assert.Contains("Accountant name, qualification / professional body, firm / reviewer capacity", accountant);
        Assert.Contains("qualified accountant signature fields must be real retained evidence values", accountant);
        Assert.Contains("Use `accepted` in each scenario review cell", accountant);
        Assert.Contains("`Decision` column only when the whole scenario", accountant);
        Assert.Contains("ambiguous scenario scope acceptance cells", accountant);
        Assert.Contains("Use exactly `yes` for `Decision question answered`", accountant);
        Assert.Contains("exact `accepted` in `Evidence accepted`", accountant);
        Assert.Contains("accountant-workbench-evidence-report.json", accountant);
        Assert.Contains("Scenario evidence reference", accountant);
        Assert.Contains("qualified-accountant-walkthrough-ledger#<scenario>", accountant);
        Assert.Contains("must be the exact retained", accountant, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Workbench evidence reference", accountant);
        Assert.Contains("rejects ambiguous route decision/evidence cells", accountant);
        Assert.Contains("must match the route key exactly", accountant);
        Assert.Contains("Each route `Notes` cell must be the exact retained route walkthrough note", accountant);
        Assert.Contains("qualified-accountant-route-walkthrough#<route>", accountant);
        Assert.Contains("do not use", accountant, StringComparison.OrdinalIgnoreCase);

        var manualHandoff = File.ReadAllText(manualHandoffPath);
        Assert.Contains("Manual Handoff Acceptance", manualHandoff);
        Assert.Contains("medium-audit-required", manualHandoff);
        Assert.Contains("Signed auditor report evidence", manualHandoff);
        Assert.Contains("Manual handoff note", manualHandoff);
        Assert.Contains("Filing readiness profile snapshot", manualHandoff);
        Assert.Contains("plc-public-company", manualHandoff);
        Assert.Contains("audit-required-without-auditor-report", manualHandoff);
        Assert.Contains("direct-cro-ros-submission", manualHandoff);
        Assert.Contains("Reviewer signature", manualHandoff);
        Assert.Contains("40-character commit SHA", manualHandoff);
        Assert.Contains("Use real retained evidence references", manualHandoff);
        Assert.Contains("Use exactly `accepted` in the `Decision` and", manualHandoff);
        Assert.Contains("rejects ambiguous decision text", manualHandoff);
        Assert.Contains("signed-auditor-report-evidence#<scenario>", manualHandoff);
        Assert.Contains("manual-handoff-note#<scenario>", manualHandoff);
        Assert.Contains("filing-readiness-snapshot#<scenario>", manualHandoff);
        Assert.Contains("unsupported-path-evidence#<path-code>", manualHandoff);
        Assert.Contains("exact retained reference", manualHandoff, StringComparison.OrdinalIgnoreCase);

        var monitoring = File.ReadAllText(monitoringPath);
        Assert.Contains("monitoring-error-routing-smoke", monitoring);
        Assert.Contains("structured-json-log-sample", monitoring);
        Assert.Contains("monitoring-error-routing-report.json", monitoring);
        Assert.Contains("structured-log-report.json", monitoring);
        Assert.Contains("/api/system/monitoring/error-smoke", monitoring);
        Assert.Contains("/api/system/monitoring/client-event", monitoring);
        Assert.Contains("Client event id", monitoring);
        Assert.Contains("Matched client monitoring line", monitoring);
        Assert.Contains("Synthetic sensitive markers absent", monitoring);
        Assert.Contains("Client provider event URL or reference", monitoring);
        Assert.Contains("No PII or client filing data", monitoring);
        Assert.Contains("positive integer JSON log line count", monitoring);
        Assert.Contains("Use real provider, server/client event, correlation, base URL", monitoring);
        Assert.Contains("Accepted as monitoring-provider confirmation evidence for this release candidate.", monitoring);
        Assert.Contains("Operator signature", monitoring);
    }

    [Fact]
    public void ReleaseEvidenceVerifier_BlocksIncompleteHumanSignoffEvidence()
    {
        var root = RepositoryRoot();
        var runbook = File.ReadAllText(Path.Combine(root, "Docs", "operations", "production-runbook.md"));
        var scriptPath = Path.Combine(root, "scripts", "verify-release-evidence.ps1");
        var workspaceScriptPath = Path.Combine(root, "scripts", "new-release-evidence-workspace.ps1");
        var workspaceVerifierPath = Path.Combine(root, "scripts", "verify-release-evidence-workspace.ps1");
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));

        Assert.True(File.Exists(scriptPath), "Release evidence verifier should make completed human sign-off templates machine-checkable.");
        Assert.True(File.Exists(workspaceScriptPath), "Release evidence workspace preparer should create reviewer-ready template copies without faking sign-off.");
        Assert.True(File.Exists(workspaceVerifierPath), "Release evidence workspace verifier should prove reviewer workspace artifacts are still blocked until human sign-off.");
        var script = File.ReadAllText(scriptPath);
        var workspaceScript = File.ReadAllText(workspaceScriptPath);
        var workspaceVerifier = File.ReadAllText(workspaceVerifierPath);
        var externalRosIxbrl = File.ReadAllText(Path.Combine(root, "Docs", "release-evidence", "external-ros-ixbrl-validation-template.md"));
        var visual = File.ReadAllText(Path.Combine(root, "Docs", "release-evidence", "visual-qa-signoff-template.md"));

        Assert.Contains("verify-release-evidence.ps1", runbook);
        Assert.Contains("new-release-evidence-workspace.ps1", runbook);
        Assert.Contains("verify-release-evidence-workspace.ps1", runbook);
        Assert.Contains("release-evidence-workspace-manifest.json", runbook);
        Assert.Contains("release-evidence-reviewer-index.md", runbook);
        Assert.Contains("release-evidence-reviewer-completion.json", runbook);
        Assert.Contains("release-evidence-reviewer-assignments.json", runbook);
        Assert.Contains("release-evidence-reviewer-blockers.md", runbook);
        Assert.Contains("release-evidence-machine-summary.json", runbook);
        Assert.Contains("accountant-workbench-evidence-report.json", runbook);
        Assert.Contains("structured-log-report.json", runbook);
        Assert.Contains("release-evidence-workspace-verification-report.json", runbook);
        Assert.Contains("release-evidence-verifier-output.txt", runbook);
        Assert.Contains("leaves all reviewer identity, pass/fail/source-review/professional acceptance/manual handoff decisions, signatures", runbook);
        Assert.Contains("release-evidence-report.json", runbook);
        Assert.Contains("visual-smoke-evidence-report.json", runbook);
        Assert.Contains("pickup-file guidance", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reviewerPickupFiles", runbook);
        Assert.Contains("real filing use stays blocked", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Prepare release evidence reviewer workspace", workflow);
        Assert.Contains("new-release-evidence-workspace.ps1", workflow);
        Assert.Contains("ProductionReadinessVerificationReportPath", workflow);
        Assert.Contains("verify-release-evidence-workspace.ps1", workflow);
        Assert.Contains("release-evidence-reviewer-workspace", workflow);
        Assert.Contains("release-evidence-workspace-verification-report.json", workspaceVerifier);
        Assert.Contains("release-evidence-verifier-output.txt", workspaceVerifier);
        Assert.Contains("release-evidence-reviewer-index.md", workspaceVerifier);
        Assert.Contains("release-evidence-reviewer-completion.json", workspaceVerifier);
        Assert.Contains("release-evidence-reviewer-assignments.json", workspaceVerifier);
        Assert.Contains("release-evidence-reviewer-blockers.md", workspaceVerifier);
        Assert.Contains("Release Evidence Reviewer Blockers", workspaceVerifier);
        Assert.Contains("reviewer-action-required", workspaceVerifier);
        Assert.Contains("Write-ReviewerBlockerSummary", workspaceVerifier);
        Assert.Contains("reviewerBlockersPath", workspaceVerifier);
        Assert.Contains("workspaceFiles", workspaceVerifier);
        Assert.Contains("requiredWorkspaceFiles", workspaceVerifier);
        Assert.Contains("requiredMachineEvidenceFiles", workspaceVerifier);
        Assert.Contains("requiredMachineEvidenceProvenance", workspaceVerifier);
        Assert.Contains("Workspace manifest machineEvidenceSummaryFile must be release-evidence-machine-summary.json.", workspaceVerifier);
        Assert.Contains("Workspace must include release-evidence-machine-summary.json.", workspaceVerifier);
        Assert.Contains("Machine evidence summary retainedMachineEvidence must contain exactly", workspaceVerifier);
        Assert.Contains("Machine evidence summary retainedMachineEvidence.$requiredMachineEvidenceFile.$propertyName must match the workspace manifest.", workspaceVerifier);
        Assert.Contains("Machine evidence summary productionReadiness.verificationStatus must be passed.", workspaceVerifier);
        Assert.Contains("Machine evidence summary productionReadiness.verificationFailureCount must be zero.", workspaceVerifier);
        Assert.Contains("Machine evidence summary productionReadiness.humanReleaseEvidenceCloseoutStepCodes", workspaceVerifier);
        Assert.Contains("Machine evidence summary productionReadiness.humanReleaseEvidenceReviewerPickupFiles", workspaceVerifier);
        Assert.Contains("Machine evidence summary monitoringEvidence.$field must be present.", workspaceVerifier);
        Assert.Contains("Machine evidence summary monitoringEvidence.jsonLogLineCount must include both controlled monitoring lines.", workspaceVerifier);
        Assert.Contains("Machine evidence summary monitoringEvidence.matchedMonitoringSmokeLine must be true.", workspaceVerifier);
        Assert.Contains("clientSensitiveInputAbsent", workspaceVerifier);
        Assert.Contains("matchedClientMonitoringLine", workspaceVerifier);
        Assert.Contains("syntheticSensitiveMarkersAbsent", workspaceVerifier);
        Assert.Contains("Assert-VisualQaPreparedRouteReferences", workspaceVerifier);
        Assert.Contains("Prepared visual QA template route row $routeName Notes cell must be", workspaceVerifier);
        Assert.Contains("must leave route pass/fail decision cells blank before named human sign-off", workspaceVerifier);
        Assert.Contains("Assert-SourceLawPreparedEvidenceReferences", workspaceVerifier);
        Assert.Contains("Prepared source-law template source row $sourceId Notes cell must be", workspaceVerifier);
        Assert.Contains("must leave source review decision cells blank before named human sign-off", workspaceVerifier);
        Assert.Contains("Assert-QualifiedAccountantPreparedEvidenceReferences", workspaceVerifier);
        Assert.Contains("Prepared qualified-accountant template scenario row $scenarioCode Scenario evidence reference cell must be", workspaceVerifier);
        Assert.Contains("Prepared qualified-accountant template route row $routeName Workbench evidence reference cell must be", workspaceVerifier);
        Assert.Contains("must leave scenario acceptance cells blank before named professional sign-off", workspaceVerifier);
        Assert.Contains("must leave route acceptance cells blank before named professional sign-off", workspaceVerifier);
        Assert.Contains("Assert-ExternalRosIxbrlPreparedEvidenceReferences", workspaceVerifier);
        Assert.Contains("Prepared external ROS/iXBRL template scenario row $scenarioCode External reference cell must be", workspaceVerifier);
        Assert.Contains("Prepared external ROS/iXBRL template scenario row $scenarioCode Taxonomy package cell must be", workspaceVerifier);
        Assert.Contains("must leave artifact hash, warnings/errors and decision cells blank before named external validation sign-off", workspaceVerifier);
        Assert.Contains("Assert-ManualHandoffPreparedEvidenceReferences", workspaceVerifier);
        Assert.Contains("Prepared manual handoff template scenario row $scenarioCode Auditor evidence cell must be", workspaceVerifier);
        Assert.Contains("Prepared manual handoff template unsupported-path row $pathCode Release evidence reference cell must be", workspaceVerifier);
        Assert.Contains("must leave scenario decision cells blank before named manual handoff sign-off", workspaceVerifier);
        Assert.Contains("must leave reviewer decision cells blank before named manual handoff sign-off", workspaceVerifier);
        Assert.Contains("Assert-MonitoringProviderPreparedEvidenceReferences", workspaceVerifier);
        Assert.Contains("$Context $FieldName field must be $Expected.", workspaceVerifier);
        Assert.Contains("$Context $FieldName field must remain blank before named operator sign-off.", workspaceVerifier);
        Assert.Contains("\"Provider event URL or reference\"", workspaceVerifier);
        Assert.Contains("Prepared monitoring-provider template must leave provider confirmation and decision checkboxes unchecked before named operator sign-off.", workspaceVerifier);
        Assert.Contains("Assert-PreparedTemplateHumanFieldsBlank", workspaceVerifier);
        Assert.Contains("Assert-PreparedHumanFieldBlank", workspaceVerifier);
        Assert.Contains("$Context $FieldName field must remain blank before named human sign-off.", workspaceVerifier);
        Assert.Contains("Prepared visual QA template", workspaceVerifier);
        Assert.Contains("Prepared source-law template", workspaceVerifier);
        Assert.Contains("Prepared external ROS/iXBRL template", workspaceVerifier);
        Assert.Contains("Prepared qualified-accountant template", workspaceVerifier);
        Assert.Contains("Prepared manual handoff template", workspaceVerifier);
        Assert.Contains("must leave human acceptance and evidence checkboxes unchecked before named human sign-off.", workspaceVerifier);
        Assert.Contains("Get-MarkdownFieldValue", workspaceVerifier);
        Assert.Contains("Assert-MarkdownFieldEquals", workspaceVerifier);
        Assert.Contains("Assert-MarkdownFieldBlank", workspaceVerifier);
        Assert.Contains("machineEvidenceSummaryPath", workspaceVerifier);
        Assert.Contains("releaseCandidate", workspaceVerifier);
        Assert.Contains("identityProvided", workspaceVerifier);
        Assert.Contains("Assert-WorkspaceVerificationInventory", script);
        Assert.Contains("Release evidence workspace verification report requiredWorkspaceFiles must contain exactly", script);
        Assert.Contains("Release evidence workspace verification report workspaceFiles", script);
        Assert.Contains("release-evidence-reviewer-index.md", script);
        Assert.Contains("release-evidence-reviewer-completion.json", script);
        Assert.Contains("release-evidence-reviewer-assignments.json", script);
        Assert.Contains("release-evidence-reviewer-blockers.md", script);
        Assert.Contains("release-evidence-verifier-output.txt", script);
        Assert.Contains("Convert-JsonValueToEvidenceString", workspaceScript);
        Assert.Contains("Workspace manifest retainedMachineEvidence must contain exactly", workspaceVerifier);
        Assert.Contains("Workspace must include retained machine evidence file", workspaceVerifier);
        Assert.Contains("Workspace manifest retainedMachineEvidence.$requiredMachineEvidenceFile.sourceArtifactName must be", workspaceVerifier);
        Assert.Contains("Workspace manifest retainedMachineEvidence.$requiredMachineEvidenceFile.sourceArtifactFile must be", workspaceVerifier);
        Assert.Contains("Workspace manifest retainedMachineEvidence.$requiredMachineEvidenceFile.byteSize must be a positive integer.", workspaceVerifier);
        Assert.Contains("Workspace manifest retainedMachineEvidence.$requiredMachineEvidenceFile.sha256 must be a lowercase 64-character SHA-256 digest.", workspaceVerifier);
        Assert.Contains("Workspace manifest retainedMachineEvidence.$requiredMachineEvidenceFile.byteSize must match the retained file byte size.", workspaceVerifier);
        Assert.Contains("Workspace manifest retainedMachineEvidence.$requiredMachineEvidenceFile.sha256 must match the retained file SHA-256 digest.", workspaceVerifier);
        Assert.Contains("production-readiness-verification-report.json", workspaceVerifier);
        Assert.Contains("accountant-workbench-evidence-report.json", workspaceVerifier);
        Assert.Contains("Workspace file inventory must include", workspaceVerifier);
        Assert.Contains("Workspace file inventory must not include unexpected file", workspaceVerifier);
        Assert.Contains("byteSize", workspaceVerifier);
        Assert.Contains("sha256", workspaceVerifier);
        Assert.Contains("Get-FileSha256", workspaceVerifier);
        Assert.Contains("Reviewer completion ledger entries must contain exactly", workspaceVerifier);
        Assert.Contains("Reviewer completion ledger $($expected.TemplateFile).completed must remain false before named human sign-off.", workspaceVerifier);
        Assert.Contains("Reviewer assignment ledger entries must contain exactly", workspaceVerifier);
        Assert.Contains("RequiredPickupFiles", workspaceVerifier);
        Assert.Contains("reviewerPickupFiles", workspaceVerifier);
        Assert.Contains("Reviewer assignment ledger $($expected.TemplateFile).reviewerPickupFiles", workspaceVerifier);
        Assert.Contains("assignmentStatus = \"unassigned\"", workspaceVerifier);
        Assert.Contains("assignedReviewerName", workspaceVerifier);
        Assert.Contains("assignedReviewerEmail", workspaceVerifier);
        Assert.Contains("must be blank before named reviewer routing", workspaceVerifier);
        Assert.Contains("reviewerAssignmentInventory", workspaceVerifier);
        Assert.Contains("Workspace manifest reviewerAssignmentFile must be release-evidence-reviewer-assignments.json.", workspaceVerifier);
        Assert.Contains("Prepared release evidence workspace unexpectedly passed before named human sign-off.", workspaceVerifier);
        Assert.Contains("Prepared release evidence workspace must keep all human evidence entries incomplete.", workspaceVerifier);
        Assert.Contains("pendingHumanEvidenceBlockers", workspaceVerifier);
        Assert.Contains("New-PendingHumanEvidenceBlockerSummary", workspaceVerifier);
        Assert.Contains("Prepared release evidence workspace humanEvidenceCompletion entries must retain at least one blocker before named human sign-off.", workspaceVerifier);
        Assert.Contains("Workspace manifest reviewerIndexFile must be release-evidence-reviewer-index.md.", workspaceVerifier);
        Assert.Contains("Workspace manifest reviewerCompletionFile must be release-evidence-reviewer-completion.json.", workspaceVerifier);
        Assert.Contains("Workspace manifest reviewerQueue must contain exactly", workspaceVerifier);
        Assert.Contains("verify-release-evidence.ps1", workspaceVerifier);
        Assert.Contains("release-evidence-workspace-verification-report.json", workspaceVerifier);
        Assert.Contains("pending-human-evidence", workspaceScript);
        Assert.Contains("humanFieldsLeftBlank", workspaceScript);
        Assert.Contains("reviewerQueue", workspaceScript);
        Assert.Contains("RequiredPickupFiles", workspaceScript);
        Assert.Contains("reviewerPickupFiles = @($_.RequiredPickupFiles)", workspaceScript);
        Assert.Contains("Reviewer pickup files", workspaceScript);
        Assert.Contains("reviewerAssignmentFile", workspaceScript);
        Assert.Contains("machineEvidenceSummaryFile", workspaceScript);
        Assert.Contains("release-evidence-machine-summary.json", workspaceScript);
        Assert.Contains("This summary is machine evidence only", workspaceScript);
        Assert.Contains("Copy-MachineEvidenceInput", workspaceScript);
        Assert.Contains("retainedMachineEvidence", workspaceScript);
        Assert.Contains("sourceArtifactName = $SourceArtifactName", workspaceScript);
        Assert.Contains("sourceArtifactFile = $SourceArtifactFile", workspaceScript);
        Assert.Contains("ProductionReadinessVerificationReportPath", workspaceScript);
        Assert.Contains("production-readiness-report\" \"production-readiness-report.json", workspaceScript);
        Assert.Contains("production-readiness-report\" \"production-readiness-verification-report.json", workspaceScript);
        Assert.Contains("humanReleaseEvidenceCloseoutStepCodes", workspaceScript);
        Assert.Contains("humanReleaseEvidenceReviewerPickupFiles", workspaceScript);
        Assert.Contains("visual-smoke-screenshots\" \"accountant-workbench-evidence-report.json", workspaceScript);
        Assert.Contains("structured-json-log-sample\" \"structured-log-report.json", workspaceScript);
        Assert.Contains("byteSize = $destination.Length", workspaceScript);
        Assert.Contains("sha256 = Get-FileSha256 $destinationPath", workspaceScript);
        Assert.Contains("accountant-workbench-evidence-report.json", workspaceScript);
        Assert.Contains("visual-smoke-manifest.json", workspaceScript);
        Assert.Contains("reviewerCompletionFile", workspaceScript);
        Assert.Contains("release-evidence-reviewer-completion.json", workspaceScript);
        Assert.Contains("completionPolicy", workspaceScript);
        Assert.Contains("incomplete-before-review", workspaceScript);
        Assert.Contains("Release Evidence Reviewer Workspace", workspaceScript);
        Assert.Contains("release-evidence-reviewer-index.md", workspaceScript);
        Assert.Contains("Reviewer Completion Ledger", workspaceScript);
        Assert.Contains("Reviewer Handoff Files", workspaceScript);
        Assert.Contains("Reviewer Closeout Sequence", workspaceScript);
        Assert.Contains("six accepted ``humanEvidenceCompletion`` entries", workspaceScript);
        Assert.Contains("``productionScorecardCompletion`` status ``complete`` at 1,000/1,000", workspaceScript);
        Assert.Contains("scripts/verify-release-artifact-pack.ps1", workspaceScript);
        Assert.Contains("release-evidence-workspace-verification-report.json", workspaceScript);
        Assert.Contains("Reviewer Handoff Files", workspaceVerifier);
        Assert.Contains("Reviewer Closeout Sequence", workspaceVerifier);
        Assert.Contains("six accepted ``humanEvidenceCompletion`` entries", workspaceVerifier);
        Assert.Contains("``productionScorecardCompletion`` status ``complete`` at 1,000/1,000", workspaceVerifier);
        Assert.Contains("scripts/verify-release-artifact-pack.ps1", workspaceVerifier);
        Assert.Contains("This workspace is reviewer preparation only.", workspaceScript);
        Assert.Contains("Copy-PreparedTemplate", workspaceScript);
        Assert.Contains("Get-MinimumVisualMetric", workspaceScript);
        Assert.Contains("Get-VisualRouteNames", workspaceScript);
        Assert.Contains("Set-VisualQaRouteReferenceNotes", workspaceScript);
        Assert.Contains("visual-smoke-evidence-report.json#routeCoverage.$routeName", workspaceScript);
        Assert.Contains("Get-SourceLawSnapshotContentHash", workspaceScript);
        Assert.Contains("Get-SourceLawSourceIds", workspaceScript);
        Assert.Contains("Set-SourceLawReviewNoteReferences", workspaceScript);
        Assert.Contains("source-law-review-ledger#$sourceId", workspaceScript);
        Assert.Contains("Get-GoldenCorpusScenarioCodes", workspaceScript);
        Assert.Contains("Get-AccountantWorkbenchRouteNames", workspaceScript);
        Assert.Contains("Set-ExternalRosIxbrlScenarioReferences", workspaceScript);
        Assert.Contains("external-ros-validation-ledger#$scenarioCode", workspaceScript);
        Assert.Contains("revenue-taxonomy-package-ledger#$scenarioCode", workspaceScript);
        Assert.Contains("Set-ManualHandoffScenarioReferences", workspaceScript);
        Assert.Contains("Set-ManualHandoffUnsupportedPathReferences", workspaceScript);
        Assert.Contains("signed-auditor-report-evidence#$scenarioCode", workspaceScript);
        Assert.Contains("manual-handoff-note#$scenarioCode", workspaceScript);
        Assert.Contains("filing-readiness-snapshot#$scenarioCode", workspaceScript);
        Assert.Contains("unsupported-path-evidence#$pathCode", workspaceScript);
        Assert.Contains("Set-QualifiedAccountantScenarioReferences", workspaceScript);
        Assert.Contains("Set-QualifiedAccountantRouteReferences", workspaceScript);
        Assert.Contains("qualified-accountant-walkthrough-ledger#$scenarioCode", workspaceScript);
        Assert.Contains("accountant-workbench-evidence-report.json#routeAcceptance.$routeName", workspaceScript);
        Assert.Contains("qualified-accountant-route-walkthrough#$routeName", workspaceScript);
        Assert.Contains("Assert-GitHubActionsRunUrl", workspaceScript);
        Assert.Contains("Refusing to overwrite existing evidence file", workspaceScript);
        Assert.Contains("VisualSmokeEvidenceReportPath", workspaceScript);
        Assert.Contains("MonitoringErrorRoutingReportPath", workspaceScript);
        Assert.Contains("StructuredLogReportPath", workspaceScript);
        Assert.Contains("Assert-NoUncheckedBoxes", script);
        Assert.Contains("Assert-CheckedDecision", script);
        Assert.Contains("Assert-UncheckedDecision", script);
        Assert.Contains("Assert-FilledField", script);
        Assert.Contains("[`t ]*$escaped[`t ]*:", script);
        Assert.Contains("Assert-ReleaseIdentityFields", script);
        Assert.Contains("Assert-FieldEquals", script);
        Assert.Contains("Assert-UtcTimestampField", script);
        Assert.Contains("Assert-Sha256Field", script);
        Assert.Contains("Assert-PositiveIntegerField", script);
        Assert.Contains("Read-JsonEvidenceFile", script);
        Assert.Contains("Test-ReleaseWorkspaceControlEvidence", script);
        Assert.Contains("workspaceControlFiles", script);
        Assert.Contains("releaseEvidenceWorkspaceFiles", script);
        Assert.Contains("Assert-JsonStringEquals $WorkspaceManifest \"machineEvidenceSummaryFile\" \"release-evidence-machine-summary.json\" \"Release evidence workspace manifest\"", script);
        Assert.Contains("requiredMachineEvidenceProvenance", script);
        Assert.Contains("Assert-MachineEvidenceEntries (Get-JsonPropertyValue $WorkspaceManifest \"retainedMachineEvidence\") \"Release evidence workspace manifest\" $Failures $requiredMachineEvidenceProvenance", script);
        Assert.Contains("Assert-MachineEvidenceEntries (Get-JsonPropertyValue $MachineEvidenceSummary \"retainedMachineEvidence\") \"Release evidence machine summary\" $Failures $requiredMachineEvidenceProvenance", script);
        Assert.Contains("$Context retainedMachineEvidence must contain exactly", script);
        Assert.Contains("$Context retainedMachineEvidence.$fileName.$propertyName must be $expectedValue", script);
        Assert.Contains("$Context retainedMachineEvidence.$fileName.$propertyName must match $ReferenceContext", script);
        Assert.Contains("Release evidence machine summary completionPolicy", script);
        Assert.Contains("Assert-JsonStringEquals $summaryProductionReadiness \"verificationStatus\" \"passed\" \"Release evidence machine summary productionReadiness\"", script);
        Assert.Contains("Release evidence machine summary productionReadiness.verificationFailureCount must be 0.", script);
        Assert.Contains("Release evidence machine summary productionReadiness.humanReleaseEvidenceCloseoutStepCodes", script);
        Assert.Contains("Release evidence machine summary productionReadiness.humanReleaseEvidenceReviewerPickupFiles", script);
        Assert.Contains("Release evidence machine summary reviewerQueue.$($expected.EvidenceName).RequiredPickupFiles", script);
        Assert.Contains("Release evidence machine summary monitoringEvidence.jsonLogLineCount must include both controlled monitoring lines.", script);
        Assert.Contains("Assert-JsonStringEquals $WorkspaceVerificationReport \"status\" \"passed\" \"Release evidence workspace verification report\"", script);
        Assert.Contains("Release evidence workspace verification report failureCount must be 0.", script);
        Assert.Contains("Release evidence workspace verification report releaseCandidate", script);
        Assert.Contains("Release evidence workspace verification report workspaceFiles", script);
        Assert.Contains("Assert-PreparedHumanTemplateControls", script);
        Assert.Contains("Release evidence workspace verification report preparedHumanTemplateControls must contain exactly", script);
        Assert.Contains("Assert-JsonStringEquals $entry \"checkboxPolicy\" \"unchecked-before-named-human-signoff\"", script);
        Assert.Contains("Assert-PendingHumanEvidenceBlockers", script);
        Assert.Contains("Release evidence workspace verification report pendingHumanEvidenceBlockers must contain exactly", script);
        Assert.Contains("pendingHumanEvidenceBlockers.$expectedEvidenceName.blockingFailureCount must be greater than zero", script);
        Assert.Contains("Assert-ReviewerAssignmentInventory", script);
        Assert.Contains("Release evidence workspace verification report reviewerAssignmentInventory must contain exactly", script);
        Assert.Contains("Assert-JsonStringEquals $entry \"assignmentStatus\" \"unassigned\"", script);
        Assert.Contains("reviewerAssignmentInventory.$expectedEvidenceName.$blankField must be blank before named reviewer routing", script);
        Assert.Contains("reviewerAssignmentInventory.$expectedEvidenceName.reviewerPickupFiles", script);
        Assert.Contains("unchecked-before-named-human-signoff", script);
        Assert.Contains("release-evidence-machine-summary.json", script);
        Assert.Contains("Assert-MinimumIntegerField", script);
        Assert.Contains("Assert-CompletedTableColumnMatches", script);
        Assert.Contains("Assert-ConsistentReleaseIdentity", script);
        Assert.Contains("Get-ReleaseEvidenceIdentity", script);
        Assert.Contains("evidenceIdentityCount", script);
        Assert.Contains("New-EvidenceFileManifestItem", script);
        Assert.Contains("New-HumanEvidenceCompletionItem", script);
        Assert.Contains("New-ProductionScorecardCompletion", script);
        Assert.Contains("hasReleaseIdentity", script);
        Assert.Contains("humanEvidenceCompletion", script);
        Assert.Contains("productionScorecardCompletion", script);
        Assert.Contains("Score reaches 1000/1000 only when every weighted independent-audit control passes for the exact candidate", script);
        Assert.Contains("all-weighted-controls-passed", script);
        Assert.Contains("independent-audit-control-ledger-v1", script);
        Assert.Contains("acceptedHumanEvidenceCount", script);
        Assert.Contains("requiredHumanEvidenceCount", script);
        Assert.Contains("remainingHumanEvidence", script);
        Assert.Contains("targetScore = $targetScore", script);
        Assert.Contains("openEngineeringOrAssuranceControlCount", script);
        Assert.Contains("blockingAuditItemIds", script);
        Assert.Contains("requiredReviewerRole", script);
        Assert.Contains("signOffGate", script);
        Assert.Contains("blockingFailureCount", script);
        Assert.Contains("blockingFailures", script);
        Assert.Contains("missing-template", script);
        Assert.Contains("not-started", script);
        Assert.Contains("accepted", script);
        Assert.Contains("releaseEvidenceTemplateFiles", script);
        Assert.Contains("Release evidence identity mismatch", script);
        Assert.Contains("GitHub Actions run URL", script);
        Assert.Contains("40-character hexadecimal Git commit SHA", script);
        Assert.Contains("64-character hexadecimal SHA-256 digest", script);
        Assert.Contains("Assert-CompletedTableRows", script);
        Assert.Contains("canonicalGoldenCorpusScenarioCodes", script);
        Assert.Contains("requiredCoverage", script);
        Assert.Contains("Test-SourceLawEvidence", script);
        Assert.Contains("sourceLawReview", script);
        Assert.Contains("Named source-law reviewer plus qualified accountant", script);
        Assert.Contains("source-law-change-review", script);
        Assert.Contains("sourceLawSourceIds", script);
        Assert.Contains("requiredSourceLawSourceIds", script);
        Assert.Contains("qualified-accountant-source-law-signoff", script);
        Assert.Contains("\"Source-law snapshot fingerprint\" \"^source-law-snapshot-fingerprint#[A-Za-z0-9._:-]+$\" \"an exact source-law-snapshot-fingerprint retained evidence anchor\"", script);
        Assert.Contains("\"Reviewer name\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\" \"a real reviewer name\"", script);
        Assert.Contains("\"Qualified accountant name\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\" \"a real qualified accountant name\"", script);
        Assert.Contains("\"Qualified accountant source-law sign-off\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\" \"a real qualified-accountant source-law sign-off\"", script);
        Assert.Contains("\"URL reachable\" \"^yes$\"", script);
        Assert.Contains("\"Effective date checked\" \"^([0-9]{4}-[0-9]{2}-[0-9]{2}|not dated)$\"", script);
        Assert.Contains("\"Guidance wording compared\" \"^yes$\"", script);
        Assert.Contains("\"Platform impact\" \"^(no change|reflected|blocking)$\" \"exactly no change, reflected, or blocking\"", script);
        Assert.Contains("\"Decision\" \"^accepted$\"", script);
        Assert.Contains("\"Notes\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\"", script);
        Assert.Contains("Assert-CompletedTableColumnMatchesSourceLawNote $Content $requiredSourceLawSourceIds 6 \"Notes\"", script);
        Assert.Contains("source-law-review-ledger#$label", script);
        Assert.Contains("Test-ExternalRosIxbrlEvidence", script);
        Assert.Contains("externalRosIxbrlValidation", script);
        Assert.Contains("External ROS/iXBRL validation reviewer", script);
        Assert.Contains("external-ros-validation-evidence", script);
        Assert.Contains("externalRosIxbrlScenarioCodes", script);
        Assert.Contains("Generated iXBRL SHA-256", script);
        Assert.Contains("Internal XML checks are not Revenue acceptance evidence", script);
        Assert.Contains("Accepted as external ROS/iXBRL validation evidence for this release candidate.", script);
        Assert.Contains("\"External validation provider\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\" \"a real external validation provider\"", script);
        Assert.Contains("\"Validation environment\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\" \"a real validation environment\"", script);
        Assert.Contains("\"Validation run/reference id\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\" \"a real validation run or reference id\"", script);
        Assert.Contains("\"Validation report file or URL\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\" \"a real validation report file or URL\"", script);
        Assert.Contains("\"Generated iXBRL artifact name\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\\.(xhtml|html|zip)$\" \"a retained .xhtml, .html, or .zip artifact name\"", script);
        Assert.Contains("\"Taxonomy package\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\" \"a real taxonomy package or retained package reference\"", script);
        Assert.Contains("\"Company/period reference\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\" \"a real company/period reference\"", script);
        Assert.Contains("\"External reference\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\"", script);
        Assert.Contains("\"Taxonomy package\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\"", script);
        Assert.Contains("a real retained taxonomy package reference", script);
        Assert.Contains("Assert-CompletedTableColumnMatchesExternalValidationReference $Content $canonicalGoldenCorpusScenarioCodes 1 \"External reference\"", script);
        Assert.Contains("external-ros-validation-ledger#$label", script);
        Assert.Contains("Assert-CompletedTableColumnMatchesTaxonomyPackageReference $Content $canonicalGoldenCorpusScenarioCodes 3 \"Taxonomy package\"", script);
        Assert.Contains("revenue-taxonomy-package-ledger#$label", script);
        Assert.Contains("\"Accountant name\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\" \"a real accountant name\"", script);
        Assert.Contains("\"Firm / reviewer capacity\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\" \"a real firm or reviewer capacity\"", script);
        Assert.Contains("\"Qualified accountant signature\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\" \"a real qualified-accountant signature\"", script);
        Assert.Contains("Use exactly `none`,", externalRosIxbrl);
        Assert.Contains("`accepted` in the `Decision` column", externalRosIxbrl);
        Assert.Contains("accepted with", externalRosIxbrl);
        Assert.Contains("retain details in the validation reference", externalRosIxbrl);
        Assert.Contains("\"Warnings/errors\" \"^(none|accepted|remediated)$\" \"exactly none, accepted, or remediated\"", script);
        Assert.Contains("\"Decision\" \"^accepted$\" \"exactly accepted for this release candidate\"", script);
        Assert.Contains("micro-ltd", script);
        Assert.Contains("small-abridged-ltd", script);
        Assert.Contains("stale non-canonical scenario code", script);
        Assert.Contains("visual-smoke-screenshots", script);
        Assert.Contains("Named visual QA reviewer", script);
        Assert.Contains("visual-qa-screenshot-review", script);
        Assert.Contains("visual-smoke-evidence-report.json", script);
        Assert.Contains("accountant-workbench-evidence-report.json", script);
        Assert.Contains("Assert-FieldEquals $Content \"Visual smoke manifest file\" \"visual-smoke-manifest.json\"", script);
        Assert.Contains("Assert-FieldEquals $Content \"Visual smoke evidence report file\" \"visual-smoke-evidence-report.json\"", script);
        Assert.Contains("Assert-FieldEquals $Content \"Accountant workbench evidence report file\" \"accountant-workbench-evidence-report.json\"", script);
        Assert.Contains("\"Reviewer name\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\" \"a real visual QA reviewer name\"", script);
        Assert.Contains("\"Reviewer role\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\" \"a real visual QA reviewer role\"", script);
        Assert.Contains("\"Reviewer signature\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\" \"a real visual QA reviewer signature\"", script);
        Assert.Contains("screenshot nonblank pixel diversity evidence", script);
        Assert.Contains("Minimum PNG IDAT byte size", script);
        Assert.Contains("Minimum screenshot pixel sample count", script);
        Assert.Contains("Minimum sampled distinct color count", script);
        Assert.Contains("Minimum screenshot luminance range", script);
        Assert.Contains("Minimum automated contrast ratio", script);
        Assert.Contains("\"Minimum sampled distinct color count\" 4", script);
        Assert.Contains("\"Minimum screenshot luminance range\" 10", script);
        Assert.Contains("\"Minimum automated contrast ratio\" ([decimal]3.0)", script);
        Assert.Contains("Use exactly `pass`", visual);
        Assert.Contains("`accepted`, or other ambiguous state acceptance cells", visual);
        Assert.Contains("\"Desktop light\" \"^pass$\" \"exactly pass\"", script);
        Assert.Contains("\"Desktop dark\" \"^pass$\" \"exactly pass\"", script);
        Assert.Contains("\"Mobile light\" \"^pass$\" \"exactly pass\"", script);
        Assert.Contains("\"Mobile dark\" \"^pass$\" \"exactly pass\"", script);
        Assert.Contains("\"Tablet light\" \"^pass$\" \"exactly pass\"", script);
        Assert.Contains("\"Tablet dark\" \"^pass$\" \"exactly pass\"", script);
        Assert.Contains("a real retained visual evidence note or reference", script);
        Assert.Contains("Assert-CompletedTableColumnMatchesVisualRouteReference $Content $requiredVisualStateIds 7 \"Notes\"", script);
        Assert.Contains("visual-smoke-evidence-report.json#routeCoverage.$label", script);
        Assert.Contains("Assert-MinimumDecimalField", script);
        Assert.Contains("integer greater than or equal to $MinimumValue", script);
        Assert.Contains("pngIdatByteSize", script);
        Assert.Contains("pixelSampleCount", script);
        Assert.Contains("sampledDistinctColorCount", script);
        Assert.Contains("luminanceRange", script);
        Assert.Contains("theme-contrast", script);
        Assert.Contains("themeContrastResult.minimumContrastRatio", script);
        Assert.Contains("monitoring-error-routing-report.json", script);
        Assert.Contains("structured-log-report.json", script);
        Assert.Contains("Matched monitoring smoke line", script);
        Assert.Contains("Matched client monitoring line", script);
        Assert.Contains("Synthetic sensitive markers absent", script);
        Assert.Contains("Client provider event URL or reference", script);
        Assert.Contains("\"Provider\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\"", script);
        Assert.Contains("\"Event id\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\"", script);
        Assert.Contains("\"Correlation id\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\"", script);
        Assert.Contains("\"Base URL\" \"^https://.+\"", script);
        Assert.Contains("\"Provider event URL or reference\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\"", script);
        Assert.Contains("Accepted as monitoring-provider confirmation evidence for this release candidate.", script);
        Assert.Contains("Named release operator", script);
        Assert.Contains("production-monitoring", script);
        Assert.Contains("JSON log line count", script);
        Assert.Contains("Direct CRO submission remains unsupported", script);
        Assert.Contains("Direct ROS submission remains unsupported", script);
        Assert.Contains("\"Outputs\" \"^accepted$\"", script);
        Assert.Contains("\"Gates\" \"^accepted$\"", script);
        Assert.Contains("\"Source-law evidence\" \"^accepted$\"", script);
        Assert.Contains("\"Wording\" \"^accepted$\"", script);
        Assert.Contains("\"Workbench journey\" \"^accepted$\"", script);
        Assert.Contains("\"Decision\" \"^accepted$\"", script);
        Assert.Contains("\"Scenario evidence reference\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\"", script);
        Assert.Contains("Assert-CompletedTableColumnMatchesScenarioWalkthroughReference $Content $canonicalGoldenCorpusScenarioCodes 7 \"Scenario evidence reference\"", script);
        Assert.Contains("qualified-accountant-walkthrough-ledger#$label", script);
        Assert.Contains("\"Decision question answered\" \"^yes$\" \"exactly yes\"", script);
        Assert.Contains("\"Evidence accepted\" \"^accepted$\"", script);
        Assert.Contains("\"Workbench evidence reference\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\"", script);
        Assert.Contains("Assert-CompletedTableColumnMatchesRouteReference", script);
        Assert.Contains("accountant-workbench-evidence-report.json#routeAcceptance.$label", script);
        Assert.Contains("a real retained workbench evidence reference", script);
        Assert.Contains("a real retained route walkthrough note or reference", script);
        Assert.Contains("Assert-CompletedTableColumnMatchesRouteWalkthroughNote $Content $requiredRouteCodes 4 \"Notes\"", script);
        Assert.Contains("qualified-accountant-route-walkthrough#$label", script);
        Assert.Contains("accepted for this release candidate", script);
        Assert.Contains("Test-ManualHandoffEvidence", script);
        Assert.Contains("manualHandoffAcceptance", script);
        Assert.Contains("Named manual handoff reviewer", script);
        Assert.Contains("manual-accountant-acceptance", script);
        Assert.Contains("manualHandoffScenarioCodes", script);
        Assert.Contains("manualHandoffPathCodes", script);
        Assert.Contains("Accepted as manual handoff evidence for this release candidate.", script);
        Assert.Contains("\"Auditor evidence\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\"", script);
        Assert.Contains("\"Manual handoff note\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\"", script);
        Assert.Contains("\"Filing readiness snapshot\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\"", script);
        Assert.Contains("\"Release evidence reference\" \"^(?!accepted$|none$|n/a$|pending$|todo$|tbd$).+\"", script);
        Assert.Contains("Assert-CompletedTableColumnMatchesEvidenceAnchor $Content $requiredManualHandoffScenarioCodes 1 \"Auditor evidence\" \"signed-auditor-report-evidence\"", script);
        Assert.Contains("Assert-CompletedTableColumnMatchesEvidenceAnchor $Content $requiredManualHandoffScenarioCodes 2 \"Manual handoff note\" \"manual-handoff-note\"", script);
        Assert.Contains("Assert-CompletedTableColumnMatchesEvidenceAnchor $Content $requiredManualHandoffScenarioCodes 3 \"Filing readiness snapshot\" \"filing-readiness-snapshot\"", script);
        Assert.Contains("Assert-CompletedTableColumnMatchesEvidenceAnchor $Content $requiredManualHandoffPathCodes 1 \"Release evidence reference\" \"unsupported-path-evidence\"", script);
        Assert.Contains("\"Decision\" \"^accepted$\" \"exactly accepted for this release candidate\"", script);
        Assert.Contains("\"Reviewer decision\" \"^accepted$\" \"exactly accepted\"", script);
        Assert.Contains("audit-required-without-auditor-report", script);
        Assert.Contains("No PII or client filing data", script);
        Assert.Contains("Named qualified accountant", script);
        Assert.Contains("qualified-accountant-final-signoff", script);
        Assert.Contains("Accepted for this release candidate.", script);
        Assert.Contains("Accepted for real filing preparation subject to external CRO/ROS processes.", script);
        Assert.Contains("Release evidence verification failed", script);
        Assert.Contains("ConvertTo-Json -Depth 6", script);
    }

    [Fact]
    public void NoDirectFilingSubmissionVerifier_ProvesRecordedWorkflowStateOnlyControl()
    {
        var root = RepositoryRoot();
        var runbook = File.ReadAllText(Path.Combine(root, "Docs", "operations", "production-runbook.md"));
        var reportService = ProductionReadinessSource(root);
        var scriptPath = Path.Combine(root, "scripts", "verify-no-direct-filing-submission.ps1");
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));

        Assert.True(File.Exists(scriptPath), "No-direct filing submission verifier should produce release evidence.");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("verify-no-direct-filing-submission.ps1", runbook);
        Assert.Contains("no-direct-filing-submission-report.json", runbook);
        Assert.Contains("-CommitSha <release-commit-sha>", runbook);
        Assert.Contains("-GitHubActionsRunUrl <ci-run-url>", runbook);
        Assert.Contains("verify-no-direct-filing-submission.ps1", workflow);
        Assert.Contains("no-direct-filing-submission-control", workflow);
        Assert.Contains("no-direct-filing-submission-report.json", workflow);
        Assert.Contains("-CommitSha $env:GITHUB_SHA", workflow);
        Assert.Contains("-GitHubActionsRunUrl $runUrl", workflow);
        Assert.Contains("if-no-files-found: error", workflow);
        Assert.Contains("no outbound CRO/ROS submission client", runbook);
        Assert.Contains("stale no-direct evidence", runbook);
        Assert.Contains("No direct CRO/ROS submission automation", reportService);
        Assert.Contains("verify-no-direct-filing-submission.ps1", reportService);
        Assert.Contains("recorded workflow states", reportService);

        Assert.Contains("FilingWorkflowEndpoints.cs", script);
        Assert.Contains("RevenueEndpoints.cs", script);
        Assert.Contains("FilingReviewCentre.tsx", script);
        Assert.Contains("StatusCodes.Status410Gone", script);
        Assert.Contains("allowedRecordedWorkflowRoutes", script);
        Assert.Contains("\"/cro-status\"", script);
        Assert.Contains("\"/cro-payment\"", script);
        Assert.Contains("\"/validate-ixbrl\"", script);
        Assert.Contains("releaseCandidate", script);
        Assert.Contains("identityProvided", script);
        Assert.Contains("CommitSha is required for no-direct filing submission evidence", script);
        Assert.Contains("GitHubActionsRunUrl is required for no-direct filing submission evidence", script);
        Assert.Contains("CRO and ROS final submission remain external actions recorded in workflow state.", script);
        Assert.Contains("forbiddenOutboundPatterns", script);
        Assert.Contains("IHttpClientFactory", script);
        Assert.Contains(".PostAsync", script);
        Assert.Contains("SubmissionClient", script);
        Assert.Contains("forbiddenRoutePatterns", script);
        Assert.Contains("No-direct filing submission verification failed", script);
        Assert.Contains("ConvertTo-Json -Depth 6", script);
    }

    [Fact]
    public void ReleaseArtifactPackVerifier_RequiresExactOperationalEvidenceReports()
    {
        var root = RepositoryRoot();
        var runbook = File.ReadAllText(Path.Combine(root, "Docs", "operations", "production-runbook.md"));
        var runbookLf = runbook.Replace("\r\n", "\n", StringComparison.Ordinal);
        var reportService = ProductionReadinessSource(root);
        var scriptPath = Path.Combine(root, "scripts", "verify-release-artifact-pack.ps1");
        var machineEvidencePackPath = Path.Combine(root, "scripts", "verify-ci-machine-evidence-pack.ps1");
        var readinessVerifierPath = Path.Combine(root, "scripts", "verify-production-readiness-report.ps1");
        var migrationUpgradeVerifierPath = Path.Combine(root, "scripts", "verify-migration-upgrade-evidence.ps1");
        var postgresTlsVerifierPath = Path.Combine(root, "scripts", "verify-postgres-tls.ps1");
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        var packageJson = File.ReadAllText(Path.Combine(root, "frontend", "package.json"));
        var workbenchVerifierPath = Path.Combine(root, "frontend", "scripts", "verify-accountant-workbench-evidence.mjs");

        Assert.True(File.Exists(scriptPath), "Release artifact pack verifier should make retained operational evidence machine-checkable.");
        Assert.True(File.Exists(machineEvidencePackPath), "CI machine evidence pack verifier should retain exact non-human evidence for the candidate.");
        Assert.True(File.Exists(readinessVerifierPath), "Production readiness verifier should make the captured live readiness report machine-checkable.");
        Assert.True(File.Exists(migrationUpgradeVerifierPath), "Migration upgrade verifier should make fresh, previous-release and rollback evidence machine-checkable.");
        Assert.True(File.Exists(postgresTlsVerifierPath), "PostgreSQL TLS verifier should retain live authenticated transport evidence.");
        Assert.True(File.Exists(workbenchVerifierPath), "Accountant workbench evidence verifier should produce visual-route release evidence.");
        var script = File.ReadAllText(scriptPath);
        var machineEvidencePack = File.ReadAllText(machineEvidencePackPath);
        var readinessVerifier = File.ReadAllText(readinessVerifierPath);
        var migrationUpgradeVerifier = File.ReadAllText(migrationUpgradeVerifierPath);
        var postgresTlsVerifier = File.ReadAllText(postgresTlsVerifierPath);
        var workbenchVerifier = File.ReadAllText(workbenchVerifierPath);

        Assert.Contains("verify-release-artifact-pack.ps1", runbook);
        Assert.Contains("release-artifact-pack-report.json", runbook);
        Assert.Contains("verify-release-artifact-pack.ps1", reportService);
        Assert.Contains("release-artifact-pack-report.json", reportService);
        Assert.Contains("-CommitSha <release-commit-sha>", runbook);
        Assert.Contains("-GitHubActionsRunUrl <ci-run-url>", runbook);
        Assert.Contains("The artifact pack must include `dependency-audit-report.json`,", runbook);
        Assert.Contains("`visual-smoke-manifest.json`,\n`visual-smoke-evidence-report.json`,", runbookLf);
        Assert.Contains("visual smoke manifest 32-state inventory/audits or 192 screenshot rows do not match the retained", runbook);
        Assert.Contains("visual smoke manifest and evidence report", runbook);
        Assert.Contains("verify-accountant-workbench-evidence.mjs", runbook);
        Assert.Contains("accountant-workbench-evidence-report.json", runbook);
        Assert.Contains("production-readiness-report", workflow);
        Assert.Contains("production-readiness-report.json", workflow);
        Assert.Contains("verify-production-readiness-report.ps1", workflow);
        Assert.Contains("production-readiness-verification-report.json", workflow);
        Assert.Contains("verify-ci-machine-evidence-pack.ps1", workflow);
        Assert.Contains("ci-machine-evidence-pack-report.json", workflow);
        Assert.Contains("actions/download-artifact@37930b1c2abaa49bbe596cd826c3c89aef350131", workflow);
        Assert.Contains("test:visual:workbench", packageJson);
        Assert.Contains("test:visual:workbench", workflow);
        Assert.Contains("accountant-workbench-evidence-report.json", workflow);
        Assert.Contains("pg_stat_ssl", postgresTlsVerifier);
        Assert.Contains("sslmode=verify-full", postgresTlsVerifier);
        Assert.Contains("host=not-db hostaddr=127.0.0.1", postgresTlsVerifier);
        Assert.Contains("hostnameMismatchRejected", postgresTlsVerifier);
        Assert.Contains("certificateFingerprintSha256", postgresTlsVerifier);

        foreach (var evidenceFile in new[]
        {
            "dependency-audit-report.json",
            "production-safety-report.json",
            "container-supply-chain-report.json",
            "container-supply-chain-verification-report.json",
            "monitoring-error-routing-report.json",
            "structured-log-report.json",
            "postgres-tls-report.json",
            "restore-drill-report.json",
            "capacity-profile-report.json",
            "production-failover-report.json",
            "migration-upgrade-report.json",
            "migration-upgrade-verification-report.json",
            "no-direct-filing-submission-report.json",
            "production-readiness-report.json",
            "production-readiness-verification-report.json",
            "visual-smoke-manifest.json",
            "visual-smoke-evidence-report.json",
            "accountant-workbench-evidence-report.json",
            "release-evidence-report.json",
            "release-evidence-workspace-manifest.json",
            "release-evidence-machine-summary.json",
            "release-evidence-workspace-verification-report.json"
        })
        {
            Assert.Contains(evidenceFile, script);
            Assert.Contains(evidenceFile, runbook);
        }

        foreach (var machineEvidenceFile in new[]
        {
            "dependency-audit-report.json",
            "production-safety-report.json",
            "container-supply-chain-report.json",
            "container-supply-chain-verification-report.json",
            "monitoring-error-routing-report.json",
            "structured-log-report.json",
            "postgres-tls-report.json",
            "restore-drill-report.json",
            "capacity-profile-report.json",
            "production-failover-report.json",
            "migration-upgrade-report.json",
            "migration-upgrade-verification-report.json",
            "no-direct-filing-submission-report.json",
            "production-readiness-report.json",
            "production-readiness-verification-report.json",
            "visual-smoke-manifest.json",
            "visual-smoke-evidence-report.json",
            "accountant-workbench-evidence-report.json"
        })
        {
            Assert.Contains(machineEvidenceFile, machineEvidencePack);
        }

        Assert.Contains("matchedMonitoringSmokeLine", script);
        Assert.Contains("monitoringCorrelationId", script);
        Assert.Contains("matchedClientMonitoringLine", script);
        Assert.Contains("clientMonitoringCorrelationId", script);
        Assert.Contains("syntheticSensitiveMarkersAbsent", script);
        Assert.Contains("clientEvent.sensitiveInputAbsent", script);
        Assert.Contains("matchedClientMonitoringLine", machineEvidencePack);
        Assert.Contains("clientMonitoringCorrelationId", machineEvidencePack);
        Assert.Contains("syntheticSensitiveMarkersAbsent", machineEvidencePack);
        Assert.Contains("clientEvent.sensitiveInputAbsent", machineEvidencePack);
        Assert.Contains("backupSha256", script);
        Assert.Contains("capacity-profile-report.json release candidate identity must exactly match the release pack", script);
        Assert.Contains("capacity-profile-report.json endpointSeries must contain exactly the two canonical health endpoints", script);
        Assert.Contains("capacity-profile-report.json privacy.$privacyField must be false", script);
        Assert.Contains("production-failover-report.json releaseCandidate.commitSha must exactly match the release-pack commit", script);
        Assert.Contains("production-failover-report.json must prove explicit interruption of the accounts-production ephemeral candidate project", script);
        Assert.Contains("production-failover-report.json observations must contain exactly five failover phases", script);
        Assert.Contains("production-failover-report.json privacy.$privacyField must be false", script);
        Assert.Contains("bounded capacity", runbook);
        Assert.Contains("ephemeral failover", runbook);
        Assert.Contains("previousReleaseMigration", script);
        Assert.Contains("failureRollback", script);
        Assert.Contains("transactionSuppressedSqlOperationCount", script);
        Assert.Contains("encryptedRecoveryIntegration", script);
        Assert.Contains("previousReleaseMigration", machineEvidencePack);
        Assert.Contains("failureRollback", machineEvidencePack);
        Assert.Contains("transactionSuppressedSqlOperationCount", machineEvidencePack);
        Assert.Contains("encryptedRecoveryIntegration", machineEvidencePack);
        Assert.Contains("requiredPreservationChecks", migrationUpgradeVerifier);
        Assert.Contains("freshDatabase.pendingMigrationCount must be zero", migrationUpgradeVerifier);
        Assert.Contains("auditChainCryptographicallyValid must be true", migrationUpgradeVerifier);
        Assert.Contains("failureRollback.transactionSuppressedSqlOperationCount must be zero", migrationUpgradeVerifier);
        Assert.Contains("restore-drill-report.json", migrationUpgradeVerifier);
        Assert.Contains("allowedRecordedWorkflowRoutes", script);
        Assert.Contains("no-direct-filing-submission-report.json releaseCandidate.identityProvided must be true", script);
        Assert.Contains("no-direct-filing-submission-report.json releaseCandidate.commitSha must match CommitSha", script);
        Assert.Contains("no-direct-filing-submission-report.json releaseCandidate.githubActionsRunUrl must match GitHubActionsRunUrl", script);
        Assert.Contains("no-direct-filing-submission-report.json releaseCandidate.identityProvided must be true", machineEvidencePack);
        Assert.Contains("no-direct-filing-submission-report.json releaseCandidate.commitSha must match CommitSha", machineEvidencePack);
        Assert.Contains("no-direct-filing-submission-report.json releaseCandidate.githubActionsRunUrl must match GitHubActionsRunUrl", machineEvidencePack);
        Assert.Contains("ReviewerWorkspaceDirectory", machineEvidencePack);
        Assert.Contains("reviewerWorkspace", machineEvidencePack);
        Assert.Contains("release-evidence-workspace-verification-report.json reviewerAssignmentInventory must include six reviewer assignment rows", machineEvidencePack);
        Assert.Contains("release-evidence-workspace-verification-report.json reviewerAssignmentInventory.$evidenceName.assignmentStatus must be unassigned", machineEvidencePack);
        Assert.Contains("requiredReviewerAssignmentPickupFiles", machineEvidencePack);
        Assert.Contains("reviewerAssignmentPickupFileGuidanceCount", machineEvidencePack);
        Assert.Contains("release-evidence-workspace-verification-report.json reviewerAssignmentInventory must include complete reviewerPickupFiles guidance for all six reviewer assignment rows.", machineEvidencePack);
        Assert.Contains("blankReviewerAssignmentFieldCount", machineEvidencePack);
        Assert.Contains("productionScorecard.targetScore must be 1000", script);
        Assert.Contains("productionScorecard.scoreBasis must be independent-audit-control-ledger-v1", script);
        Assert.Contains("productionScorecard.auditedCommit must identify the exact independently audited baseline commit", script);
        Assert.Contains("productionScorecardCompletion.currentScore must be 1000", script);
        Assert.Contains("productionScorecardCompletion.openEngineeringOrAssuranceControlCount must be zero", script);
        Assert.Contains("scorecardControlCodes", script);
        Assert.Contains("releaseBlockerRegister", script);
        Assert.Contains("sourceLawSnapshot", script);
        Assert.Contains("goldenFilingCorpus", script);
        Assert.Contains("production-readiness-verification-report.json failureCount must be zero", script);
        Assert.Contains("releaseVerificationManifestCodes", script);
        foreach (var manifestCode in new[]
        {
            "backend-golden-corpus",
            "frontend-workbench-contract",
            "frontend-production-build",
            "visual-smoke-light-dark",
            "production-readiness-report-verification",
            "ci-machine-evidence-pack",
            "release-artifact-pack",
            "production-stack-smoke",
            "backup-restore-drill",
            "postgres-migration-upgrade-gate",
            "qualified-accountant-final-signoff",
            "source-law-change-review",
            "external-ros-validation-evidence",
            "no-direct-cro-ros-submission-control",
            "manual-accountant-acceptance"
        })
        {
            Assert.Contains(manifestCode, script);
        }
        Assert.Contains("requiredReadinessManifestCodes", script);
        Assert.Contains("ci-machine-evidence-pack", readinessVerifier);
        Assert.Contains("frontend-production-build", readinessVerifier);
        Assert.Contains("qualified-accountant-final-signoff", readinessVerifier);
        Assert.Contains("source-law-change-review", readinessVerifier);
        Assert.Contains("external-ros-validation-evidence", readinessVerifier);
        Assert.Contains("no-direct-cro-ros-submission-control", readinessVerifier);
        Assert.Contains("manual-accountant-acceptance", readinessVerifier);
        Assert.Contains("requiredDefaultCiManifestCodes", readinessVerifier);
        Assert.Contains("requiredManualManifestCodes", readinessVerifier);
        Assert.Contains("runsInDefaultCi must be true", readinessVerifier);
        Assert.Contains("runsInDefaultCi must be false", readinessVerifier);
        Assert.Contains("verify-production-readiness-report.ps1", readinessVerifier);
        Assert.Contains("verify-ci-machine-evidence-pack.ps1", readinessVerifier);
        Assert.Contains("requiredCoverage", readinessVerifier);
        Assert.Contains("productionScorecard.currentScore must equal the sum of category current scores", readinessVerifier);
        Assert.Contains("sourceLawSnapshot.sourceCount must match sources length", readinessVerifier);
        Assert.Contains("releaseVerificationManifest must include verify-release-artifact-pack.ps1", readinessVerifier);
        Assert.Contains("humanReleaseEvidence", readinessVerifier);
        Assert.Contains("humanReleaseEvidenceCodes", readinessVerifier);
        Assert.Contains("humanReleaseEvidenceReviewerPickupFilePolicy", readinessVerifier);
        Assert.Contains("humanReleaseEvidenceReviewerPickupFiles", readinessVerifier);
        Assert.Contains("visual-smoke-evidence-report.json", readinessVerifier);
        Assert.Contains("structured-log-report.json", readinessVerifier);
        Assert.Contains("reviewerPickupFiles must include expected pickup file", readinessVerifier);
        Assert.Contains("humanReleaseEvidenceCloseout", readinessVerifier);
        Assert.Contains("humanReleaseEvidenceCloseoutStepCodes", readinessVerifier);
        Assert.Contains("pick-up-reviewer-workspace", readinessVerifier);
        Assert.Contains("release-evidence-reviewer-workspace", readinessVerifier);
        Assert.Contains("complete-human-evidence-templates", readinessVerifier);
        Assert.Contains("run-release-evidence-verifier", readinessVerifier);
        Assert.Contains("confirm-human-evidence-completion", readinessVerifier);
        Assert.Contains("verify-release-artifact-pack", readinessVerifier);
        Assert.Contains("humanReleaseEvidenceCloseout.$actualCode.artifact must be", readinessVerifier);
        Assert.Contains("humanReleaseEvidenceCloseout.$actualCode.detail must mention", readinessVerifier);
        Assert.Contains("human-release-evidence", readinessVerifier);
        Assert.Contains("visualQa", readinessVerifier);
        Assert.Contains("monitoringProviderConfirmation", readinessVerifier);
        Assert.Contains("humanReleaseEvidenceCloseoutStepCodes", script);
        Assert.Contains("humanReleaseEvidenceCloseoutStepCodes", machineEvidencePack);
        Assert.Contains("production-readiness-verification-report.json requiredCoverage.humanReleaseEvidenceCodes", script);
        Assert.Contains("production-readiness-verification-report.json requiredCoverage.humanReleaseEvidenceCodes", machineEvidencePack);
        Assert.Contains("production-readiness-verification-report.json requiredCoverage.humanReleaseEvidenceCloseoutStepCodes", script);
        Assert.Contains("production-readiness-verification-report.json requiredCoverage.humanReleaseEvidenceCloseoutStepCodes", machineEvidencePack);
        Assert.Contains("verify-release-artifact-pack", script);
        Assert.Contains("verify-release-artifact-pack", machineEvidencePack);
        Assert.Contains("visualQaCoverage.expectedScreenshotCount must be 192", readinessVerifier);
        Assert.Contains("productionSmokeUsesBuildFlag", script);
        Assert.Contains("requiredCoverage", script);
        Assert.Contains("sourceLawSourceIds", script);
        Assert.Contains("manualHandoffScenarioCodes", script);
        Assert.Contains("manualHandoffPathCodes", script);
        Assert.Contains("accountant-workbench-evidence-report.json routeCount must be 7", script);
        Assert.Contains("accountant-workbench-evidence-report.json routeAcceptanceCount must be 7", script);
        Assert.Contains("routeAcceptanceSignOffGate must be qualified-accountant-route-acceptance", script);
        Assert.Contains("routeAcceptance.requiredAcceptanceEvidence must include qualified-accountant-route-acceptance", script);
        Assert.Contains("Assert-ArrayContainsExactly", script);
        Assert.Contains("Assert-ArrayContainsExactly", machineEvidencePack);
        Assert.Contains("Assert-AccountantWorkbenchRequiredCoverage", script);
        Assert.Contains("Assert-AccountantWorkbenchRequiredCoverage", machineEvidencePack);
        Assert.Contains("expectedAccountantWorkbenchWorkflowStages", script);
        Assert.Contains("expectedAccountantWorkbenchWorkflowStages", machineEvidencePack);
        Assert.Contains("expectedAccountantWorkbenchLayoutChecks", script);
        Assert.Contains("expectedAccountantWorkbenchLayoutChecks", machineEvidencePack);
        Assert.Contains("expectedAccountantWorkbenchExpectedTextChecks", script);
        Assert.Contains("expectedAccountantWorkbenchExpectedTextChecks", machineEvidencePack);
        Assert.Contains("must include exactly $($Expected.Count) item(s)", script);
        Assert.Contains("must include exactly $($Expected.Count) item(s)", machineEvidencePack);
        Assert.Contains("requiredCoverage.workflowStages", script);
        Assert.Contains("requiredCoverage.workflowStages", machineEvidencePack);
        Assert.Contains("requiredCoverage.themes", script);
        Assert.Contains("requiredCoverage.themes", machineEvidencePack);
        Assert.Contains("requiredCoverage.viewports", script);
        Assert.Contains("requiredCoverage.viewports", machineEvidencePack);
        Assert.Contains("requiredCoverage.reviewChecks", script);
        Assert.Contains("requiredCoverage.reviewChecks", machineEvidencePack);
        Assert.Contains("requiredCoverage.layoutChecks", script);
        Assert.Contains("requiredCoverage.layoutChecks", machineEvidencePack);
        Assert.Contains("requiredCoverage.expectedTextChecks", script);
        Assert.Contains("requiredCoverage.expectedTextChecks", machineEvidencePack);
        Assert.Contains("requiredCoverage.layoutCheckEvidence", script);
        Assert.Contains("requiredCoverage.layoutCheckEvidence", machineEvidencePack);
        Assert.Contains("requiredCoverage.contrastCheckEvidence", script);
        Assert.Contains("requiredCoverage.contrastCheckEvidence", machineEvidencePack);
        Assert.Contains("expectedAccountantWorkbenchRouteAcceptance", script);
        Assert.Contains("expectedAccountantWorkbenchRouteAcceptance", machineEvidencePack);
        Assert.Contains("Assert-AccountantWorkbenchRouteAcceptance", script);
        Assert.Contains("Assert-AccountantWorkbenchRouteAcceptance", machineEvidencePack);
        Assert.Contains("routeAcceptance.$($expected.routeName).routeKey must be $($expected.routeKey)", script);
        Assert.Contains("routeAcceptance.$($expected.routeName).routeKey must be $($expected.routeKey)", machineEvidencePack);
        Assert.Contains("routeAcceptance.$($expected.routeName).label must be $($expected.label)", script);
        Assert.Contains("routeAcceptance.$($expected.routeName).label must be $($expected.label)", machineEvidencePack);
        Assert.Contains("routeAcceptance.$($expected.routeName).expectedText must be $($expected.expectedText)", script);
        Assert.Contains("routeAcceptance.$($expected.routeName).expectedText must be $($expected.expectedText)", machineEvidencePack);
        Assert.Contains("routeAcceptance.$($expected.routeName).screenshotReviewEvidence must be $($expected.routeName)-light-dark-mobile-tablet-desktop-screenshot-review", script);
        Assert.Contains("routeAcceptance.$($expected.routeName).screenshotReviewEvidence must be $($expected.routeName)-light-dark-mobile-tablet-desktop-screenshot-review", machineEvidencePack);
        Assert.Contains("routeAcceptance.$($expected.routeName).reviewStatus must be required-review", script);
        Assert.Contains("routeAcceptance.$($expected.routeName).reviewStatus must be required-review", machineEvidencePack);
        Assert.Contains("routeAcceptance.$($expected.routeName).requiredAcceptanceEvidence", script);
        Assert.Contains("routeAcceptance.$($expected.routeName).requiredAcceptanceEvidence", machineEvidencePack);
        Assert.Contains("production-readiness\"; routeKey = \"readiness\"; label = \"Production readiness\"; expectedText = \"Production Readiness Checklist\"", script);
        Assert.Contains("financial-statements\"; routeKey = \"financialStatements\"; label = \"Financial statements\"; expectedText = \"Financial Statements\"", machineEvidencePack);
        Assert.Contains("$($expected.routeName)-accountant-route-acceptance-note", script);
        Assert.Contains("$($expected.routeName)-visual-smoke-screenshots-reviewed", machineEvidencePack);
        Assert.Contains("routeReadiness.expectedTextEvidenceCount must be 6 for every route", script);
        Assert.Contains("routeReadiness.expectedTextEvidenceCount must be 6 for every route", machineEvidencePack);
        Assert.Contains("routeReadiness.$($expected.routeName).screenshotCount must be 6", script);
        Assert.Contains("routeReadiness.$($expected.routeName).screenshotCount must be 6", machineEvidencePack);
        Assert.Contains("routeReadiness.$($expected.routeName).layoutCheckResultCount must be 18", script);
        Assert.Contains("routeReadiness.$($expected.routeName).layoutCheckResultCount must be 18", machineEvidencePack);
        Assert.Contains("routeReadiness.$($expected.routeName).contrastCheckResultCount must be 6", script);
        Assert.Contains("routeReadiness.$($expected.routeName).contrastCheckResultCount must be 6", machineEvidencePack);
        Assert.Contains("routeReadiness.$($expected.routeName).minimumContrastRatio must be at least 3", script);
        Assert.Contains("routeReadiness.$($expected.routeName).minimumContrastRatio must be at least 3", machineEvidencePack);
        Assert.Contains("routeReadiness.$($expected.routeName).reviewStatus must be required-review", script);
        Assert.Contains("routeReadiness.$($expected.routeName).reviewStatus must be required-review", machineEvidencePack);
        Assert.Contains("expectedAccountantWorkbenchReviewChecks", script);
        Assert.Contains("expectedAccountantWorkbenchReviewChecks", machineEvidencePack);
        Assert.Contains("routeReadiness.$($expected.routeName).requiredReviewChecks", script);
        Assert.Contains("routeReadiness.$($expected.routeName).requiredReviewChecks", machineEvidencePack);
        Assert.Contains("visual smoke screenshots carry route expected accountant decision text", script);
        Assert.Contains("visual smoke screenshots carry route expected accountant decision text", machineEvidencePack);
        Assert.Contains("Assert-VisualSmokeDimensionEvidence", script);
        Assert.Contains("Assert-VisualSmokeDimensionEvidence", machineEvidencePack);
        Assert.Contains("Assert-VisualSmokeManifestEvidence", script);
        Assert.Contains("Assert-VisualSmokeManifestEvidence", machineEvidencePack);
        Assert.Contains("visual-smoke-manifest.json routeAudits and stateInventory must each include exactly 32 canonical states", script);
        Assert.Contains("visual-smoke-manifest.json routeAudits and stateInventory must each include exactly 32 canonical states", machineEvidencePack);
        Assert.Contains("visual-smoke-manifest.json screenshots must include exactly 192 retained screenshots", script);
        Assert.Contains("visual-smoke-manifest.json screenshots must include exactly 192 retained screenshots", machineEvidencePack);
        Assert.Contains("visual-smoke-manifest.json screenshots.$stateId.$theme.$($expectedViewport.name).$field must match visual-smoke-evidence-report.json", script);
        Assert.Contains("visual-smoke-manifest.json screenshots.$stateId.$theme.$($expectedViewport.name).$field must match visual-smoke-evidence-report.json", machineEvidencePack);
        Assert.Contains("visual-smoke-evidence-report.json themes", script);
        Assert.Contains("visual-smoke-evidence-report.json themes", machineEvidencePack);
        Assert.Contains("visual-smoke-evidence-report.json viewports", script);
        Assert.Contains("visual-smoke-evidence-report.json viewports", machineEvidencePack);
        Assert.Contains("visual-smoke-evidence-report.json layoutCheckResultCount must be 576", script);
        Assert.Contains("visual-smoke-evidence-report.json layoutCheckResultCount must be 576", machineEvidencePack);
        Assert.Contains("visual-smoke-evidence-report.json layoutChecksPassed must be true", script);
        Assert.Contains("visual-smoke-evidence-report.json layoutChecksPassed must be true", machineEvidencePack);
        Assert.Contains("visual-smoke-evidence-report.json contrastCheckResultCount must be 192", script);
        Assert.Contains("visual-smoke-evidence-report.json contrastCheckResultCount must be 192", machineEvidencePack);
        Assert.Contains("visual-smoke-evidence-report.json themeContrastChecksPassed must be true", script);
        Assert.Contains("visual-smoke-evidence-report.json themeContrastChecksPassed must be true", machineEvidencePack);
        Assert.Contains("visual-smoke-evidence-report.json minimumContrastRatio must be at least 3", script);
        Assert.Contains("visual-smoke-evidence-report.json minimumContrastRatio must be at least 3", machineEvidencePack);
        foreach (var stateIdWithoutUiComponent in new[]
        {
            "state-loading",
            "state-empty",
            "state-permission-denied",
            "state-read-only",
            "state-stale"
        })
        {
            Assert.Contains($"\"{stateIdWithoutUiComponent}\"", script);
            Assert.Contains($"\"{stateIdWithoutUiComponent}\"", machineEvidencePack);
        }
        Assert.Contains("$sampledUiComponentCount -le 0 -and $stateId -notin $uiComponentOptionalStateIds", script);
        Assert.Contains("$sampledUiComponentCount -le 0 -and $stateId -notin $uiComponentOptionalStateIds", machineEvidencePack);
        Assert.Contains("minimumUiComponentContrastRatio must be zero when no UI component is rendered", script);
        Assert.Contains("minimumUiComponentContrastRatio must be zero when no UI component is rendered", machineEvidencePack);
        Assert.Contains("visual-smoke-evidence-report.json totalBytes must prove retained screenshot bytes", script);
        Assert.Contains("visual-smoke-evidence-report.json totalBytes must prove retained screenshot bytes", machineEvidencePack);
        Assert.Contains("visual-smoke-evidence-report.json viewportDimensions must include exactly", script);
        Assert.Contains("visual-smoke-evidence-report.json viewportDimensions must include exactly", machineEvidencePack);
        Assert.Contains("visual-smoke-evidence-report.json routeCoverage must include exactly 32 canonical state rows", script);
        Assert.Contains("visual-smoke-evidence-report.json routeCoverage must include exactly 32 canonical state rows", machineEvidencePack);
        Assert.Contains("routeCoverage.$stateId.routeKey", script);
        Assert.Contains("routeCoverage.$stateId.routeKey", machineEvidencePack);
        Assert.Contains("routeCoverage.$stateId.screenshotCount must be 6", script);
        Assert.Contains("routeCoverage.$stateId.screenshotCount must be 6", machineEvidencePack);
        Assert.Contains("routeCoverage.$stateId.reviewStatus must be required-review", script);
        Assert.Contains("routeCoverage.$stateId.reviewStatus must be required-review", machineEvidencePack);
        Assert.Contains("routeCoverage.$stateId.requiredReviewChecks", script);
        Assert.Contains("routeCoverage.$stateId.requiredReviewChecks", machineEvidencePack);
        Assert.Contains("screenshots must include exactly 192 retained screenshots", script);
        Assert.Contains("screenshots must include exactly 192 retained screenshots", machineEvidencePack);
        Assert.Contains("foreach ($theme in $expectedThemes)", script);
        Assert.Contains("foreach ($theme in $expectedThemes)", machineEvidencePack);
        Assert.Contains("visual-smoke-evidence-report.json screenshots must include $stateId/$theme/$($expectedViewport.name)", script);
        Assert.Contains("visual-smoke-evidence-report.json screenshots must include $stateId/$theme/$($expectedViewport.name)", machineEvidencePack);
        Assert.Contains("visual-smoke-evidence-report.json screenshots.stateId and routeName must identify one canonical state", script);
        Assert.Contains("visual-smoke-evidence-report.json screenshots.stateId and routeName must identify one canonical state", machineEvidencePack);
        Assert.Contains("screenshots.$stateId.$theme.$($expectedViewport.name).fileName must be $expectedFileName", script);
        Assert.Contains("screenshots.$stateId.$theme.$($expectedViewport.name).fileName must be $expectedFileName", machineEvidencePack);
        Assert.Contains("\"expectedText\", \"expectedStateText\", \"canonicalUrlTemplate\"", script);
        Assert.Contains("\"expectedText\", \"expectedStateText\", \"canonicalUrlTemplate\"", machineEvidencePack);
        Assert.Contains("screenshots.$stateId.$theme.$($expectedViewport.name).reviewStatus must be required-review", script);
        Assert.Contains("screenshots.$stateId.$theme.$($expectedViewport.name).reviewStatus must be required-review", machineEvidencePack);
        Assert.Contains("viewportDimensions must include $($expected.name)", script);
        Assert.Contains("viewportDimensions must include $($expected.name)", machineEvidencePack);
        Assert.Contains("screenshots.imageWidth must match planned viewport width", script);
        Assert.Contains("screenshots.imageWidth must match planned viewport width", machineEvidencePack);
        Assert.Contains("screenshots.minimumViewportHeight must match planned viewport height", script);
        Assert.Contains("screenshots.minimumViewportHeight must match planned viewport height", machineEvidencePack);
        Assert.Contains("screenshots.byteSize must prove retained screenshot bytes", script);
        Assert.Contains("screenshots.byteSize must prove retained screenshot bytes", machineEvidencePack);
        Assert.Contains("screenshots.sha256 must be a canonical sha256 checksum", script);
        Assert.Contains("screenshots.sha256 must be a canonical sha256 checksum", machineEvidencePack);
        Assert.Contains("retained PNG file must be present in the evidence pack", script);
        Assert.Contains("retained PNG file must be present in the evidence pack", machineEvidencePack);
        Assert.Contains("byteSize must match the retained PNG file", script);
        Assert.Contains("byteSize must match the retained PNG file", machineEvidencePack);
        Assert.Contains("sha256 must match the retained PNG file", script);
        Assert.Contains("sha256 must match the retained PNG file", machineEvidencePack);
        Assert.Contains("screenshots.pngIdatByteSize must prove retained PNG image data", script);
        Assert.Contains("screenshots.pngIdatByteSize must prove retained PNG image data", machineEvidencePack);
        Assert.Contains("routeReadiness.$($expected.routeName).workflowStages", script);
        Assert.Contains("routeReadiness.$($expected.routeName).workflowStages", machineEvidencePack);
        Assert.Contains("routeReadiness.$($expected.routeName).themeViewportCoverage", script);
        Assert.Contains("routeReadiness.$($expected.routeName).themeViewportCoverage", machineEvidencePack);
        Assert.Contains("routeAcceptance.$($expected.routeName).workflowStages", script);
        Assert.Contains("routeAcceptance.$($expected.routeName).workflowStages", machineEvidencePack);
        Assert.Contains("screenshots.sampledDistinctColorCount must be at least 4", script);
        Assert.Contains("screenshots.sampledDistinctColorCount must be at least 4", machineEvidencePack);
        Assert.Contains("screenshots.luminanceRange must be at least 10", script);
        Assert.Contains("screenshots.luminanceRange must be at least 10", machineEvidencePack);
        Assert.Contains("expectedLayoutChecks", script);
        Assert.Contains("expectedLayoutChecks", machineEvidencePack);
        Assert.Contains("\"browser-console-errors\"", script);
        Assert.Contains("\"browser-console-errors\"", machineEvidencePack);
        Assert.Contains("\"page-horizontal-overflow\"", script);
        Assert.Contains("\"page-horizontal-overflow\"", machineEvidencePack);
        Assert.Contains("\"visible-text-overlap\"", script);
        Assert.Contains("\"visible-text-overlap\"", machineEvidencePack);
        Assert.Contains("screenshots.layoutCheckResults must include $layoutCheck", script);
        Assert.Contains("screenshots.layoutCheckResults must include $layoutCheck", machineEvidencePack);
        Assert.Contains("screenshots.layoutCheckResults.$layoutCheck status must be passed", script);
        Assert.Contains("screenshots.layoutCheckResults.$layoutCheck status must be passed", machineEvidencePack);
        Assert.Contains("expectedContrastCheck", script);
        Assert.Contains("expectedContrastCheck", machineEvidencePack);
        Assert.Contains("\"theme-contrast\"", script);
        Assert.Contains("\"theme-contrast\"", machineEvidencePack);
        Assert.Contains("screenshots.themeContrastResult must be present", script);
        Assert.Contains("screenshots.themeContrastResult must be present", machineEvidencePack);
        Assert.Contains("screenshots.themeContrastResult.minimumContrastRatio must be at least 3", script);
        Assert.Contains("screenshots.themeContrastResult.minimumContrastRatio must be at least 3", machineEvidencePack);
        Assert.Contains("Get-FileHash", script);
        Assert.Contains("releaseCandidate", script);
        Assert.Contains("releaseCandidate.identityConsistent must be true", script);
        Assert.Contains("releaseCandidate.evidenceIdentityCount must be 6", script);
        Assert.Contains("releaseCandidate.commitSha must match CommitSha", script);
        Assert.Contains("releaseCandidate.githubActionsRunUrl must match GitHubActionsRunUrl", script);
        Assert.Contains("Assert-ReleaseEvidenceTemplateManifest", script);
        Assert.Contains("Release artifact pack must include completed release evidence template", script);
        Assert.Contains("evidenceFiles must include retained release evidence template hashes", script);
        Assert.Contains("Assert-ReleaseEvidenceHumanCompletionManifest", script);
        Assert.Contains("humanEvidenceCompletion must include completed human release evidence gate entries", script);
        Assert.Contains("humanEvidenceCompletion.$($required.evidenceName).status must be accepted", script);
        Assert.Contains("humanEvidenceCompletion.$($required.evidenceName).blockingFailureCount must be 0", script);
        Assert.Contains("humanEvidenceCompletion.$($required.evidenceName).blockingFailures must be empty", script);
        Assert.Contains("Assert-ReleaseEvidenceProductionScorecardCompletion", script);
        Assert.Contains("productionScorecardCompletion must be present", script);
        Assert.Contains("productionScorecardCompletion.status must be complete", script);
        Assert.Contains("productionScorecardCompletion.currentScore must be 1000", script);
        Assert.Contains("productionScorecardCompletion.targetScore must be 1000", script);
        Assert.Contains("productionScorecardCompletion.scoreBasis must be independent-audit-control-ledger-v1", script);
        Assert.Contains("productionScorecardCompletion.auditedCommit must identify the exact independently audited baseline commit", script);
        Assert.Contains("productionScorecardCompletion.openEngineeringOrAssuranceControlCount must be zero", script);
        Assert.Contains("productionScorecardCompletion.openEngineeringOrAssuranceControls must be empty", script);
        Assert.Contains("productionScorecardCompletion.acceptedHumanEvidenceCount must equal the required human evidence count", script);
        Assert.Contains("productionScorecardCompletion.remainingHumanEvidence must be empty", script);
        Assert.Contains("productionScorecardCompletion.completionPolicy must mention zero blocking failures", script);
        Assert.Contains("productionScorecardCompletion.categories.$($expected.code).currentScore must be $($expected.currentScore)", script);
        Assert.Contains("releaseEvidenceTemplateFiles", script);
        Assert.Contains("sha256 must match the retained template file", script);
        Assert.Contains("evidenceType = \"release-evidence-template\"", script);
        Assert.Contains("Assert-ReleaseEvidenceWorkspaceControlManifest", script);
        Assert.Contains("workspaceControlFiles must include retained release evidence workspace control hashes", script);
        Assert.Contains("Release artifact pack must include retained release evidence workspace control file", script);
        Assert.Contains("releaseEvidenceWorkspaceFiles", script);
        Assert.Contains("workspaceControlFiles.$($required.fileName).sha256 must match the retained workspace control file", script);
        Assert.Contains("Assert-ReleaseEvidenceMachineSummary", script);
        Assert.Contains("release-evidence-machine-summary.json completionPolicy", script);
        Assert.Contains("release-evidence-machine-summary.json retainedMachineEvidence must contain exactly", script);
        Assert.Contains("retainedMachineEvidence.$expectedFileName.sha256 must match the retained evidence file", script);
        Assert.Contains("release-evidence-machine-summary.json productionReadiness.verificationStatus must be passed", script);
        Assert.Contains("release-evidence-machine-summary.json productionReadiness.verificationFailureCount must be 0", script);
        Assert.Contains("release-evidence-machine-summary.json productionReadiness.humanReleaseEvidenceCloseoutStepCodes", script);
        Assert.Contains("release-evidence-machine-summary.json productionReadiness.humanReleaseEvidenceReviewerPickupFiles.$expectedEvidenceName", script);
        Assert.Contains("release-evidence-machine-summary.json reviewerQueue must contain exactly", script);
        Assert.Contains("release-evidence-machine-summary.json reviewerQueue.$expectedEvidenceName.RequiredPickupFiles", script);
        Assert.Contains("Assert-ReleaseEvidenceWorkspaceVerificationReport", script);
        Assert.Contains("release-evidence-workspace-verification-report.json status must be passed", script);
        Assert.Contains("release-evidence-workspace-verification-report.json releaseCandidate.identityProvided must be true", script);
        Assert.Contains("release-evidence-workspace-verification-report.json releaseCandidate.commitSha must match the release evidence candidate", script);
        Assert.Contains("release-evidence-workspace-verification-report.json requiredWorkspaceFiles", script);
        Assert.Contains("release-evidence-workspace-verification-report.json workspaceFiles", script);
        Assert.Contains("Assert-ReleaseEvidenceWorkspacePreparedHumanControls", script);
        Assert.Contains("release-evidence-workspace-verification-report.json preparedHumanTemplateControls must include exactly", script);
        Assert.Contains("preparedHumanTemplateControls.$expectedFile.checkboxPolicy must be unchecked-before-named-human-signoff", script);
        Assert.Contains("Assert-ReleaseEvidenceWorkspacePendingHumanBlockers", script);
        Assert.Contains("release-evidence-workspace-verification-report.json pendingHumanEvidenceBlockers must include exactly", script);
        Assert.Contains("pendingHumanEvidenceBlockers.$expectedEvidenceName.status must be incomplete", script);
        Assert.Contains("Assert-ReleaseEvidenceWorkspaceReviewerAssignments", script);
        Assert.Contains("release-evidence-workspace-verification-report.json reviewerAssignmentInventory must include exactly", script);
        Assert.Contains("reviewerAssignmentInventory.$expectedEvidenceName.assignmentStatus must be unassigned", script);
        Assert.Contains("releaseEvidenceWorkspaceSummary", script);
        Assert.Contains("releaseEvidenceScorecardSummary", script);
        Assert.Contains("scorecardStatus", script);
        Assert.Contains("acceptedHumanEvidenceCount", script);
        Assert.Contains("remainingHumanEvidenceCount", script);
        Assert.Contains("categoryScores", script);
        Assert.Contains("pendingHumanEvidenceBlockerCount", script);
        Assert.Contains("unassignedReviewerAssignmentCount", script);
        Assert.Contains("blankReviewerAssignmentFieldCount", script);
        Assert.Contains("reviewerAssignmentPickupFileGuidanceCount", script);
        Assert.Contains("reviewerAssignmentPickupFiles", script);
        Assert.Contains("missingPickupFileCount", script);
        Assert.Contains("Assert-ReleaseEvidenceWorkspaceInventoryRetention", script);
        Assert.Contains("Release artifact pack must retain workspace inventory file", script);
        Assert.Contains("retained workspace inventory file $expectedFile sha256 must match release-evidence-workspace-verification-report.json", script);
        Assert.Contains("release-evidence-reviewer-index.md", script);
        Assert.Contains("release-evidence-reviewer-completion.json", script);
        Assert.Contains("release-evidence-reviewer-assignments.json", script);
        Assert.Contains("release-evidence-reviewer-blockers.md", script);
        Assert.Contains("release-evidence-verifier-output.txt", script);
        Assert.Contains("evidenceType = \"release-evidence-workspace-control\"", script);
        Assert.Contains("requiredReleaseEvidenceReviewerHandoffFiles", script);
        Assert.Contains("releaseEvidenceReviewerHandoffManifest", script);
        Assert.Contains("evidenceType = \"release-evidence-reviewer-handoff\"", script);
        Assert.Contains("evidenceFiles", script);
        Assert.Contains("identityProvided", script);
        Assert.Contains("GitHubActionsRunUrl must be an exact GitHub Actions run URL", script);
        Assert.Contains("CommitSha must be a full lowercase 40-character hexadecimal Git commit SHA", script);
        Assert.Contains("workflowStages", script);
        Assert.Contains("routeReadiness", workbenchVerifier);
        Assert.Contains("routeAcceptance", workbenchVerifier);
        Assert.Contains("routeKey must be", workbenchVerifier);
        Assert.Contains("expected accountant decision text", workbenchVerifier);
        Assert.Contains("qualified-accountant-route-acceptance", workbenchVerifier);
        Assert.Contains("CommitSha is required for CI machine evidence packs", machineEvidencePack);
        Assert.Contains("GitHubActionsRunUrl is required for CI machine evidence packs", machineEvidencePack);
        Assert.Contains("humanEvidenceStillRequired", machineEvidencePack);
        Assert.Contains("release-evidence-report.json", machineEvidencePack);
        Assert.Contains("CI machine evidence pack verification failed", machineEvidencePack);
        Assert.Contains("visualSmokeReviewChecks", workbenchVerifier);
        Assert.Contains("ACCOUNTANT_WORKFLOW_STAGES", workbenchVerifier);
        Assert.Contains("Release artifact pack verification failed", script);
        Assert.Contains("ConvertTo-Json -Depth 6", script);
    }

    [Fact]
    public void StructuredLogVerifier_ParsesJsonLogsAndMatchesMonitoringSmokeEvidence()
    {
        var root = RepositoryRoot();
        var scriptPath = Path.Combine(root, "scripts", "verify-structured-logs.ps1");
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        var reportService = ProductionReadinessSource(root);

        Assert.True(File.Exists(scriptPath), "Structured log verifier should turn raw API logs into release evidence.");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("ConvertFrom-Json", script);
        Assert.Contains("Timestamp", script);
        Assert.Contains("LogLevel", script);
        Assert.Contains("Category", script);
        Assert.Contains("MonitoringEvidencePath", script);
        Assert.Contains("correlationId", script);
        Assert.Contains("Controlled monitoring smoke event emitted", script);
        Assert.Contains("Sanitized client monitoring event", script);
        Assert.Contains("matchedClientMonitoringLine", script);
        Assert.Contains("syntheticSensitiveMarkersAbsent", script);
        Assert.Contains("NeverSendThis", script);
        Assert.Contains("clientEvent", File.ReadAllText(Path.Combine(root, "scripts", "smoke-production.ps1")));
        Assert.Contains("structured-log-report.json", workflow);
        Assert.Contains("structured-json-log-sample", workflow);
        Assert.Contains("api-structured.log", workflow);
        Assert.Contains("structured-log-report.json", reportService);
        Assert.Contains("api-structured.log", reportService);
    }

    [Fact]
    public void HealthReadiness_FailsWhenMigrationsOrOwnerBootstrapAreMissing()
    {
        var endpoints = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints", "SystemEndpoints.cs"));
        var probe = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Services", "SystemReadinessProbeService.cs"));

        Assert.Contains("SystemReadinessProbeService readiness", endpoints);
        Assert.Contains("await readiness.GetAsync", endpoints);
        Assert.Contains("GetPendingMigrationsAsync", probe);
        Assert.Contains("HasActiveOwnerAsync", probe);
        Assert.Contains("DatabaseTenantBootstrapResolver", probe);
        Assert.Contains("ResolveLoginTenantAsync(email", probe);
        Assert.DoesNotContain("user.Role == \"Owner\"", probe);
        Assert.Contains("user.Role.Trim().ToLower() == \"owner\"", probe);
        Assert.Contains("user.Tenant.Slug.ToLower() == tenantSlug", probe);
        Assert.Contains("user.Email.ToLower() == email", probe);
        Assert.Contains("new SystemReadinessProbe(false", probe);
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
        var yearEndEndpoints = YearEndEndpointsSource();

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
    public void PeriodStatusEndpoint_IsMappedAndChecksReadinessBeforeMutatingStatus()
    {
        var root = RepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Endpoints", "CompanyEndpoints.cs"));
        var endpoint = File.ReadAllText(Path.Combine(root, "backend", "Accounts.Api", "Endpoints", "PeriodStatusEndpoint.cs"));

        Assert.Contains("periods.MapPut(\"/{id:int}/status\", PeriodStatusEndpoint.UpdateAsync)", program);
        Assert.Contains("FinancialStatementsService statements", endpoint);
        Assert.Contains("AssertFinalOutputReadinessAsync(companyId, id, outputName)", endpoint);
        Assert.Contains("AssertFilingObligationsRecordedAsync(", endpoint);
        Assert.Contains("releaseGate ?? new FilingReleaseGate(db)", endpoint);
        AssertOccursBefore(endpoint, "AssertFinalOutputReadinessAsync(companyId, id, outputName)", "EndpointInputs.ApplyPeriodStatusUpdate");
        AssertOccursBefore(endpoint, "AssertFilingObligationsRecordedAsync(", "EndpointInputs.ApplyPeriodStatusUpdate");
        AssertOccursBefore(endpoint, "AssertFinalOutputReadinessAsync(companyId, id, outputName)", "DomainAuditCoverage.LogAsync(");
        AssertOccursBefore(endpoint, "AssertFilingObligationsRecordedAsync(", "DomainAuditCoverage.LogAsync(");
    }

}
