using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using System.Collections;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var runMigrationsOnly = args.Any(arg => arg.Equals("--migrate-only", StringComparison.OrdinalIgnoreCase));
var generatingOpenApiDocument =
    Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider"
    || AppDomain.CurrentDomain.GetAssemblies().Any(
        assembly => assembly.GetName().Name == "GetDocument.Insider")
    || Environment.CommandLine.Contains("dotnet-getdocument", StringComparison.OrdinalIgnoreCase)
    || Environment.CommandLine.Contains("GetDocument.Insider", StringComparison.OrdinalIgnoreCase);
FileBackedConfiguration.AddFileBackedEnvironmentVariables(builder.Configuration);

// JSON: handle circular references + accept string enums in requests
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// QuestPDF Community License
QuestPDF.Settings.License = LicenseType.Community;

builder.Services.Configure<MonitoringConfig>(builder.Configuration.GetSection("Monitoring"));
var monitoring = builder.Configuration.GetSection("Monitoring").Get<MonitoringConfig>() ?? new MonitoringConfig();
if (monitoring.StructuredJsonConsole)
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    });
}

if (!builder.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(monitoring.ErrorTrackingDsn))
{
    builder.WebHost.UseSentry(options =>
    {
        options.Dsn = monitoring.ErrorTrackingDsn;
        options.Environment = builder.Environment.EnvironmentName;
        var releaseCandidate = builder.Configuration["FilingRelease:Candidate"]?.Trim();
        if (!string.IsNullOrWhiteSpace(releaseCandidate))
            options.Release = releaseCandidate;
        options.SendDefaultPii = false;
        options.AttachStacktrace = false;
        options.MaxBreadcrumbs = 0;
        options.CaptureFailedRequests = false;
        options.TracesSampleRate = monitoring.TracesSampleRate;
        options.SetBeforeSend(MonitoringEventSanitizer.ScrubSentryEvent);
    });
}

// Database
// The official build-time OpenAPI host executes Program with a no-op server. It needs endpoint
// metadata, not a live database; retain normal fail-fast connection validation for every real run.
var connectionString = generatingOpenApiDocument
    ? "Host=localhost;Port=5432;Database=accounts_openapi;Username=accounts_openapi;Password=not-used"
    : DatabaseConnectionConfig.Resolve(builder.Configuration, builder.Environment);
builder.Services.AddHttpContextAccessor();
builder.Services.AddOptions<DatabaseTenantIsolationConfig>()
    .Bind(builder.Configuration.GetSection("DatabaseTenantIsolation"))
    .Validate(DatabaseTenantIsolationConfig.IsValid, "Database tenant isolation configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddScoped<DatabaseTenantContext>();
builder.Services.AddScoped<TenantRlsConnectionInterceptor>();
builder.Services.AddScoped<DatabaseTenantBootstrapResolver>();
builder.Services.AddScoped<DatabaseTenantIsolationProvisioner>();
builder.Services.AddScoped<DatabaseTenantIsolationVerifier>();
builder.Services.AddScoped<AccountingConcurrencyInterceptor>();
builder.Services.AddScoped<PlatformMetricsConnectionInterceptor>();
builder.Services.AddDbContext<AccountsDbContext>((services, options) =>
    options
        .UseNpgsql(connectionString)
        .AddInterceptors(
            services.GetRequiredService<AccountingConcurrencyInterceptor>(),
            services.GetRequiredService<PlatformMetricsConnectionInterceptor>(),
            services.GetRequiredService<TenantRlsConnectionInterceptor>()));

// Rules engine config
builder.Services.Configure<SizeThresholdConfig>(builder.Configuration.GetSection("SizeThresholds"));
builder.Services.Configure<ImportLimitConfig>(builder.Configuration.GetSection("ImportLimits"));
builder.Services.Configure<DatabaseStartupConfig>(builder.Configuration.GetSection("DatabaseStartup"));
builder.Services.Configure<ApiAccessConfig>(builder.Configuration.GetSection("ApiAccess"));
builder.Services.Configure<AuthSessionConfig>(builder.Configuration.GetSection("AuthSession"));
builder.Services.Configure<IdentitySecurityConfig>(builder.Configuration.GetSection("IdentitySecurity"));
builder.Services.Configure<BootstrapOwnerConfig>(builder.Configuration.GetSection("BootstrapOwner"));
builder.Services.Configure<AuditIntegrityConfig>(builder.Configuration.GetSection("AuditIntegrity"));
builder.Services.AddOptions<IdempotencyConfig>()
    .Bind(builder.Configuration.GetSection("Idempotency"))
    .Validate(IdempotencyConfig.IsValid, "Idempotency retention configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddOptions<PrivacyGovernanceConfig>()
    .Bind(builder.Configuration.GetSection("PrivacyGovernance"))
    .Validate(PrivacyGovernanceConfig.IsValid, "Privacy governance retention configuration is invalid.")
    .ValidateOnStart();
builder.Services.Configure<DeadlineDeliveryConfig>(builder.Configuration.GetSection("DeadlineDelivery"));
builder.Services.Configure<PlatformMetricsConfig>(builder.Configuration.GetSection("PlatformMetrics"));

var importLimits = builder.Configuration.GetSection("ImportLimits").Get<ImportLimitConfig>() ?? new ImportLimitConfig();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = importLimits.MaxCsvBytes;
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = importLimits.MaxCsvBytes + 1024 * 1024;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var permitLimit = builder.Configuration.GetValue("RateLimits:PermitLimitPerMinute", 300);
    var trustForwardedFor = builder.Configuration.GetValue("RateLimits:TrustForwardedFor", false);
    options.AddPolicy(AuthEndpoints.LoginRateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitClientKey.FromHttpContext(context, trustForwardedFor),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy(AuthEndpoints.MfaRateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitClientKey.FromHttpContext(context, trustForwardedFor),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitClientKey.FromHttpContext(context, trustForwardedFor),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

// Services
builder.Services.AddScoped<SizeClassificationService>();
builder.Services.AddScoped<FilingRegimeService>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<DuplicateReviewService>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<CompanyOnboardingService>();
builder.Services.AddScoped<IdempotencyService>();
builder.Services.AddScoped<IdempotencyRetentionService>();
builder.Services.AddHostedService<IdempotencyRetentionWorker>();
builder.Services.AddScoped<PrivacyGovernanceService>();
builder.Services.AddHostedService<PrivacyRetentionWorker>();
builder.Services.AddScoped<AnnualReturnDateService>();
builder.Services.AddScoped<AdjustmentService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<AuditIntegrityService>();
builder.Services.AddScoped<AuditIntegrityCheckpointService>();
builder.Services.AddScoped<FinancialStatementsService>();
builder.Services.AddScoped<AccountantWorkingPaperService>();
builder.Services.AddScoped<DocumentGeneratorService>();
builder.Services.AddScoped<TaxComputationService>();
builder.Services.AddScoped<CorporationTaxFilingSupportService>();
builder.Services.AddScoped<ExternalFilingHandoffRepository>();
builder.Services.AddScoped<ExternalFilingHandoffService>();
builder.Services.AddScoped<IxbrlService>();
builder.Services.AddScoped<NotesDisclosureService>();
builder.Services.AddScoped<DeadlineService>();
builder.Services.AddScoped<DashboardDeadlineService>();
builder.Services.AddScoped<DeadlineReminderPlanner>();
builder.Services.AddScoped<DeadlineReminderService>();
builder.Services.AddScoped<IOperatorAlertSink, MonitoringOperatorAlertSink>();
builder.Services.AddHttpClient<IDeadlineReminderProvider, WebhookDeadlineReminderProvider>(client =>
    client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHostedService<DeadlineReminderWorker>();
builder.Services.AddScoped<PeriodChronologyService>();
builder.Services.AddScoped<DirectorLoanComplianceService>();
builder.Services.AddScoped<DirectorsReportService>();
builder.Services.AddScoped<CharityReportingService>();
builder.Services.AddScoped<CharityPdfService>();
builder.Services.AddScoped<CharitySorpDecisionService>();
builder.Services.AddScoped<FilingReadinessProfileService>();
builder.Services.AddSingleton<FilingReleaseIdentityProvider>();
builder.Services.AddScoped<FilingReleaseGate>();
builder.Services.AddScoped<FilingWorkflowService>();
builder.Services.AddScoped<ProductionReadinessReportService>();
builder.Services.AddScoped<AccountingWriteGuard>();
builder.Services.AddScoped<PeriodConcurrencyTokenService>();
builder.Services.AddScoped<AccountingConcurrencyCoordinator>();
builder.Services.AddScoped<IPasswordVerifier, Pbkdf2PasswordVerifier>();
builder.Services.AddHttpClient<IPasswordSafetyService, PwnedPasswordSafetyService>(client =>
    client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<MfaSecurityService>();
builder.Services.AddScoped<IdentityAccessService>();
builder.Services.AddScoped<UserLifecycleService>();
builder.Services.AddScoped<BootstrapOwnerService>();
builder.Services.AddSingleton<ApiAccessService>();
builder.Services.AddSingleton<ProductionSafetyService>();
builder.Services.AddSingleton<IErrorReporter, SentryErrorReporter>();
builder.Services.AddSingleton<PlatformMetrics>();
builder.Services.AddSingleton<PlatformMetricAlertState>();
builder.Services.AddSingleton<IPlatformMetricAlertSink, MonitoringPlatformMetricAlertSink>();
builder.Services.AddHostedService<PlatformMetricsAlertWorker>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SystemReadinessProbeService>();

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = CorsOriginConfig.Resolve(builder.Configuration, builder.Environment);
        policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader();
    });
});

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi("v1", options =>
{
    options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "Accounts.Api | v1";
        document.Info.Version = "v1";
        return Task.CompletedTask;
    });
});
builder.Services.AddSwaggerGen(c =>
{
    // Keep the development Swagger UI on a distinct document name so the build-time Microsoft
    // OpenAPI provider remains the single source for the committed v1 contract.
    c.SwaggerDoc("swagger-v1", new() { Title = "Irish Accounts API", Version = "v1" });
});

var app = builder.Build();

if (!generatingOpenApiDocument)
    app.Services.GetRequiredService<ProductionSafetyService>().ThrowIfUnsafe();

if (runMigrationsOnly)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DatabaseTenantIsolationProvisioner>().EnsureAsync();
    await scope.ServiceProvider.GetRequiredService<BootstrapOwnerService>().EnsureAsync();
    return;
}

// Database startup tasks. In production these must be explicitly opted into.
if (!generatingOpenApiDocument)
{
    using var scope = app.Services.CreateScope();
    var dbStartup = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseStartupConfig>>().Value;
    if (dbStartup.AutoMigrateOnStartup)
    {
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        await db.Database.MigrateAsync();

        if (dbStartup.SeedDemoData)
            await SeedData.SeedAsync(db, dbStartup.SeedDemoUsers, dbStartup.SeedSampleCompanies);

        await scope.ServiceProvider.GetRequiredService<BootstrapOwnerService>().EnsureAsync();
    }
}

if (!generatingOpenApiDocument)
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseTenantIsolationVerifier>().VerifyAsync();
}

// Middleware
app.UseMiddleware<Accounts.Api.Middleware.SecurityHeadersMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.ExceptionMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.PlatformMetricsMiddleware>();
app.UseCors();
app.UseMiddleware<Accounts.Api.Middleware.ApiAccessMiddleware>();
app.UseRateLimiter();
app.UseMiddleware<Accounts.Api.Middleware.UserSessionMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.CsrfProtectionMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.TenantAccessMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.AuditTrailMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.RecentAuthenticationMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.RoleAuthorizationMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.PeriodOwnershipMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.PeriodConcurrencyMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.PeriodLockMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("/swagger/swagger-v1/swagger.json", "Irish Accounts API v1"));
}

// Endpoint groups
app.MapSystemEndpoints();
app.MapCompanyEndpoints();
app.MapAllEndpoints();

app.Run();
