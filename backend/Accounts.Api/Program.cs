using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using System.Collections;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var runMigrationsOnly = args.Any(arg => arg.Equals("--migrate-only", StringComparison.OrdinalIgnoreCase));
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
        options.SendDefaultPii = false;
        options.TracesSampleRate = monitoring.TracesSampleRate;
    });
}

// Database
var connectionString = DatabaseConnectionConfig.Resolve(builder.Configuration, builder.Environment);
builder.Services.AddDbContext<AccountsDbContext>(options =>
    options.UseNpgsql(connectionString));

// Rules engine config
builder.Services.Configure<SizeThresholdConfig>(builder.Configuration.GetSection("SizeThresholds"));
builder.Services.Configure<ImportLimitConfig>(builder.Configuration.GetSection("ImportLimits"));
builder.Services.Configure<DatabaseStartupConfig>(builder.Configuration.GetSection("DatabaseStartup"));
builder.Services.Configure<ApiAccessConfig>(builder.Configuration.GetSection("ApiAccess"));
builder.Services.Configure<AuthSessionConfig>(builder.Configuration.GetSection("AuthSession"));
builder.Services.Configure<BootstrapOwnerConfig>(builder.Configuration.GetSection("BootstrapOwner"));
builder.Services.Configure<AuditIntegrityConfig>(builder.Configuration.GetSection("AuditIntegrity"));

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
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<AdjustmentService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<AuditIntegrityService>();
builder.Services.AddScoped<AuditIntegrityCheckpointService>();
builder.Services.AddScoped<FinancialStatementsService>();
builder.Services.AddScoped<DocumentGeneratorService>();
builder.Services.AddScoped<TaxComputationService>();
builder.Services.AddScoped<IxbrlService>();
builder.Services.AddScoped<NotesDisclosureService>();
builder.Services.AddScoped<DeadlineService>();
builder.Services.AddScoped<DirectorLoanComplianceService>();
builder.Services.AddScoped<DirectorsReportService>();
builder.Services.AddScoped<CharityReportingService>();
builder.Services.AddScoped<FilingReadinessProfileService>();
builder.Services.AddScoped<FilingWorkflowService>();
builder.Services.AddScoped<ProductionReadinessReportService>();
builder.Services.AddScoped<AccountingWriteGuard>();
builder.Services.AddScoped<IPasswordVerifier, Pbkdf2PasswordVerifier>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<BootstrapOwnerService>();
builder.Services.AddSingleton<ApiAccessService>();
builder.Services.AddSingleton<ProductionSafetyService>();
builder.Services.AddSingleton(TimeProvider.System);

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
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Irish Accounts API", Version = "v1" });
});

var app = builder.Build();

app.Services.GetRequiredService<ProductionSafetyService>().ThrowIfUnsafe();

if (runMigrationsOnly)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<BootstrapOwnerService>().EnsureAsync();
    return;
}

// Database startup tasks. In production these must be explicitly opted into.
using (var scope = app.Services.CreateScope())
{
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

// Middleware
app.UseMiddleware<Accounts.Api.Middleware.SecurityHeadersMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.ExceptionMiddleware>();
app.UseCors();
app.UseMiddleware<Accounts.Api.Middleware.ApiAccessMiddleware>();
app.UseRateLimiter();
app.UseMiddleware<Accounts.Api.Middleware.UserSessionMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.AuditTrailMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.CsrfProtectionMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.TenantAccessMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.RoleAuthorizationMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.PeriodOwnershipMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.PeriodLockMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Endpoint groups
app.MapSystemEndpoints();
app.MapCompanyEndpoints();
app.MapAllEndpoints();

app.Run();
