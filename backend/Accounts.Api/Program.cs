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
builder.Services.AddScoped<FilingWorkflowService>();
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

// Health checks
app.MapGet("/health/live", () => Results.Ok(new { status = "alive", timestamp = DateTime.UtcNow }))
   .WithTags("System");

app.MapGet("/health/ready", async (AccountsDbContext db, IOptions<BootstrapOwnerConfig> bootstrapOptions, CancellationToken cancellationToken) =>
{
    try
    {
        var databaseReady = await db.Database.CanConnectAsync(cancellationToken);
        if (!databaseReady)
        {
            return Results.Json(
                new
                {
                    status = "unready",
                    checks = new { database = "unavailable" },
                    timestamp = DateTime.UtcNow
                },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var pendingMigrations = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        var bootstrap = bootstrapOptions.Value;
        var bootstrapOwnerEmail = bootstrap.OwnerEmail.Trim().ToLowerInvariant();
        var bootstrapTenantSlug = bootstrap.TenantSlug.Trim().ToLowerInvariant();
        var hasConfiguredBootstrapOwner = !string.IsNullOrWhiteSpace(bootstrapOwnerEmail)
            && !string.IsNullOrWhiteSpace(bootstrapTenantSlug);
        var ownerReady = hasConfiguredBootstrapOwner
            ? await db.UserAccounts.AnyAsync(
                u => u.IsActive
                    && u.Role.Trim().ToLower() == "owner"
                    && u.Email.ToLower() == bootstrapOwnerEmail
                    && u.Tenant.Slug == bootstrapTenantSlug,
                cancellationToken)
            : await db.UserAccounts.AnyAsync(u => u.IsActive && u.Role.Trim().ToLower() == "owner", cancellationToken);
        if (pendingMigrations.Length > 0 || !ownerReady)
        {
            return Results.Json(
                new
                {
                    status = "unready",
                    checks = new
                    {
                        database = "reachable",
                        migrations = pendingMigrations.Length == 0 ? "current" : "pending",
                        owner = ownerReady ? "configured" : "missing"
                    },
                    timestamp = DateTime.UtcNow
                },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(new
        {
            status = "ready",
            checks = new
            {
                database = "reachable",
                migrations = "current",
                owner = "configured"
            },
            timestamp = DateTime.UtcNow
        });
    }
    catch
    {
        return Results.Json(
            new
            {
                status = "unready",
                checks = new { database = "schema_unavailable" },
                timestamp = DateTime.UtcNow
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
})
   .WithTags("System");

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
   .WithTags("System");

// Company endpoints
var companies = app.MapGroup("/api/companies").WithTags("Companies");

companies.MapGet("/", async (HttpContext context, AccountsDbContext db) =>
{
    var query = CompanyListQuery.ForContext(context, db.Companies);

    return await query.Select(c => new
    {
        c.Id, c.LegalName, c.TradingName, c.CroNumber, c.CompanyType,
        c.IsTrading, c.IsDormant, c.CreatedAt,
        PeriodCount = c.Periods.Count
    }).ToListAsync();
});

companies.MapGet("/{id:int}", async (int id, HttpContext context, AccountsDbContext db) =>
{
    if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, id))
        return Results.NotFound();

    return await db.Companies
        .Include(c => c.Officers)
        .Include(c => c.Periods)
        .FirstOrDefaultAsync(c => c.Id == id)
    is { } company ? Results.Ok(company) : Results.NotFound();
});

companies.MapPost("/", async (CompanyInput input, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db) =>
{
    if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
        return denied;

    if (EndpointInputs.ValidateCompany(input) is { } validationProblem)
        return validationProblem;

    var user = AuthContext.RequireUser(context);
    var company = EndpointInputs.ToCompany(input);
    company.TenantId = user.TenantId;
    db.Companies.Add(company);
    await db.SaveChangesAsync();
    return Results.Created($"/api/companies/{company.Id}", company);
});

companies.MapPut("/{id:int}", async (int id, CompanyInput input, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, AccountingWriteGuard writeGuard) =>
{
    if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, id))
        return Results.NotFound();

    if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
        return denied;

    if (EndpointInputs.ValidateCompany(input) is { } validationProblem)
        return validationProblem;

    var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id);
    if (company is null) return Results.NotFound();

    if (await writeGuard.BlockIfCompanyMasterDataLockedAsync(id) is { } blocked)
        return blocked;

    EndpointInputs.ApplyCompany(company, input);

    await db.SaveChangesAsync();
    return Results.Ok(company);
});

companies.MapDelete("/{id:int}", Accounts.Api.Endpoints.CompanyDeletionEndpoint.DeleteAsync);

// Officers endpoints
var officers = app.MapGroup("/api/companies/{companyId:int}/officers").WithTags("Officers");

officers.MapGet("/", async (int companyId, HttpContext context, AccountsDbContext db) =>
{
    if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
        return Results.NotFound();

    return Results.Ok(await db.CompanyOfficers.Where(o => o.CompanyId == companyId).ToListAsync());
});

officers.MapPost("/", async (int companyId, CompanyOfficerInput input, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, AccountingWriteGuard writeGuard) =>
{
    if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
        return Results.NotFound();

    if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
        return denied;

    if (EndpointInputs.ValidateOfficer(input) is { } validationProblem)
        return validationProblem;

    if (await writeGuard.BlockIfCompanyMasterDataLockedAsync(companyId) is { } blocked)
        return blocked;

    var officer = EndpointInputs.ToOfficer(companyId, input);
    db.CompanyOfficers.Add(officer);
    await db.SaveChangesAsync();
    return Results.Created($"/api/companies/{companyId}/officers/{officer.Id}", officer);
});

officers.MapPut("/{id:int}", async (int companyId, int id, CompanyOfficerInput input, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, AccountingWriteGuard writeGuard) =>
{
    if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
        return Results.NotFound();

    if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
        return denied;

    if (EndpointInputs.ValidateOfficer(input) is { } validationProblem)
        return validationProblem;

    if (await writeGuard.BlockIfCompanyMasterDataLockedAsync(companyId) is { } blocked)
        return blocked;

    var officer = await db.CompanyOfficers.FirstOrDefaultAsync(o => o.Id == id && o.CompanyId == companyId);
    if (officer is null) return Results.NotFound();
    EndpointInputs.ApplyOfficer(officer, input);
    await db.SaveChangesAsync();
    return Results.Ok(officer);
});

officers.MapDelete("/{id:int}", async (int companyId, int id, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, AccountingWriteGuard writeGuard) =>
{
    if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
        return Results.NotFound();

    if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
        return denied;

    if (await writeGuard.BlockIfCompanyMasterDataLockedAsync(companyId) is { } blocked)
        return blocked;

    var officer = await db.CompanyOfficers.FirstOrDefaultAsync(o => o.Id == id && o.CompanyId == companyId);
    if (officer is null) return Results.NotFound();
    db.CompanyOfficers.Remove(officer);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// Accounting Periods endpoints
var periods = app.MapGroup("/api/companies/{companyId:int}/periods").WithTags("Periods");

periods.MapGet("/", async (int companyId, HttpContext context, AccountsDbContext db) =>
{
    if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
        return Results.NotFound();

    return Results.Ok(await db.AccountingPeriods
        .Where(p => p.CompanyId == companyId)
        .Include(p => p.SizeClassification)
        .Include(p => p.FilingRegime)
        .OrderByDescending(p => p.PeriodEnd)
        .ToListAsync());
});

periods.MapGet("/{id:int}", async (int companyId, int id, HttpContext context, AccountsDbContext db) =>
{
    if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
        return Results.NotFound();

    return await db.AccountingPeriods
        .Include(p => p.SizeClassification)
        .Include(p => p.FilingRegime)
        .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId)
    is { } period ? Results.Ok(period) : Results.NotFound();
});

periods.MapPost("/", async (int companyId, AccountingPeriodInput input, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db) =>
{
    if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
        return Results.NotFound();

    if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
        return denied;

    if (EndpointInputs.ValidatePeriod(input) is { } validationProblem)
        return validationProblem;

    var period = EndpointInputs.ToPeriod(companyId, input);
    db.AccountingPeriods.Add(period);
    await db.SaveChangesAsync();
    return Results.Created($"/api/companies/{companyId}/periods/{period.Id}", period);
});

periods.MapPut("/{id:int}/status", PeriodStatusEndpoint.UpdateAsync);

// Rules engine endpoints
app.MapAllEndpoints();

app.Run();

public record PeriodStatusUpdate(Accounts.Api.Entities.PeriodStatus Status, string? LockedBy, string? ReopenReason, DateOnly? ApprovalDate = null);

public static class DatabaseConnectionConfig
{
    private const string DevelopmentConnectionString = "Host=localhost;Port=5433;Database=accounts;Username=accounts;Password=accounts_dev";

    public static string Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim();

        if (environment.IsDevelopment())
            return DevelopmentConnectionString;

        throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required outside Development.");
    }
}

public static class CorsOriginConfig
{
    private static readonly string[] DevelopmentOrigins =
    [
        "http://localhost:3000",
        "http://localhost:5173",
        "http://localhost:5174"
    ];

    public static string[] Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        var configuredOrigins = configuration
            .GetSection("AllowedOrigins")
            .Get<string[]>()
            ?.Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        if (configuredOrigins.Length > 0)
            return configuredOrigins;

        return environment.IsDevelopment() ? DevelopmentOrigins : [];
    }
}

public static class FileBackedConfiguration
{
    private const string FileSuffix = "_FILE";

    public static void AddFileBackedEnvironmentVariables(ConfigurationManager configuration)
    {
        var values = LoadFromEnvironment();
        if (values.Count > 0)
            configuration.AddInMemoryCollection(values);
    }

    public static IReadOnlyDictionary<string, string?> LoadFromEnvironment()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var environmentName = entry.Key?.ToString();
            if (string.IsNullOrWhiteSpace(environmentName)
                || !environmentName.EndsWith(FileSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            var filePath = entry.Value?.ToString();
            if (string.IsNullOrWhiteSpace(filePath))
                throw new InvalidOperationException($"{environmentName} must point to a readable secret file.");

            if (!File.Exists(filePath))
                throw new InvalidOperationException($"{environmentName} points to a secret file that does not exist.");

            var configKey = environmentName[..^FileSuffix.Length].Replace("__", ":");
            values[configKey] = File.ReadAllText(filePath).TrimEnd('\r', '\n');
        }

        return values;
    }
}

public class CompanyInput
{
    public string? LegalName { get; set; }
    public string? TradingName { get; set; }
    public string? CroNumber { get; set; }
    public string? TaxReference { get; set; }
    public Accounts.Api.Entities.CompanyType CompanyType { get; set; }
    public DateOnly IncorporationDate { get; set; }
    public int FinancialYearStartMonth { get; set; } = 1;
    public int ArdMonth { get; set; }
    public string? RegisteredOfficeAddress1 { get; set; }
    public string? RegisteredOfficeAddress2 { get; set; }
    public string? RegisteredOfficeCity { get; set; }
    public string? RegisteredOfficeCounty { get; set; }
    public string? RegisteredOfficeEircode { get; set; }
    public bool IsGroupMember { get; set; }
    public bool IsHolding { get; set; }
    public bool IsInvestment { get; set; }
    public bool IsSubsidiary { get; set; }
    public bool IsDormant { get; set; }
    public bool IsTrading { get; set; }
    public bool IsVatRegistered { get; set; }
    public bool IsEmployer { get; set; }
    public bool HasStock { get; set; }
    public bool OwnsAssets { get; set; }
    public bool HasBorrowings { get; set; }
    public bool HasDirectorLoans { get; set; }
    public bool IsListedSecurities { get; set; }
    public bool IsCreditInstitution { get; set; }
    public bool IsInsuranceUndertaking { get; set; }
    public bool IsPensionFund { get; set; }
    public bool IsCharitableOrganisation { get; set; }
}

public class CompanyOfficerInput
{
    public string? Name { get; set; }
    public Accounts.Api.Entities.OfficerRole Role { get; set; }
    public DateOnly? AppointedDate { get; set; }
    public DateOnly? ResignedDate { get; set; }
    public string? Address { get; set; }
}

public class AccountingPeriodInput
{
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public bool IsFirstYear { get; set; }
    public bool MemberAuditNoticeReceived { get; set; }
    public DateOnly? MemberAuditNoticeDate { get; set; }
    public bool GoingConcernConfirmed { get; set; } = true;
    public string? GoingConcernNote { get; set; }
}

public static class EndpointInputs
{
    public static IResult? ValidateCompany(CompanyInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(input.LegalName))
            errors["legalName"] = ["Legal name is required."];
        if (input.LegalName?.Length > 200)
            errors["legalName"] = ["Legal name must be 200 characters or fewer."];
        if (input.IncorporationDate == default)
            errors["incorporationDate"] = ["Incorporation date is required."];
        if (input.FinancialYearStartMonth is < 1 or > 12)
            errors["financialYearStartMonth"] = ["Financial year start month must be between 1 and 12."];
        if (input.ArdMonth is < 1 or > 12)
            errors["ardMonth"] = ["Annual return date month must be between 1 and 12."];
        if (!string.IsNullOrWhiteSpace(input.CroNumber) && input.CroNumber.Length > 20)
            errors["croNumber"] = ["CRO number must be 20 characters or fewer."];

        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }

    public static IResult? ValidateOfficer(CompanyOfficerInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(input.Name))
            errors["name"] = ["Officer name is required."];
        if (input.Name?.Length > 200)
            errors["name"] = ["Officer name must be 200 characters or fewer."];
        if (input.ResignedDate is not null && input.AppointedDate is not null && input.ResignedDate < input.AppointedDate)
            errors["resignedDate"] = ["Resigned date cannot be before appointed date."];

        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }

    public static IResult? ValidatePeriod(AccountingPeriodInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (input.PeriodStart == default)
            errors["periodStart"] = ["Period start is required."];
        if (input.PeriodEnd == default)
            errors["periodEnd"] = ["Period end is required."];
        if (input.PeriodEnd < input.PeriodStart)
            errors["periodEnd"] = ["Period end cannot be before period start."];
        if (input.PeriodStart != default && input.PeriodEnd > input.PeriodStart.AddMonths(18).AddDays(-1))
            errors["periodEnd"] = ["Accounting period cannot exceed 18 months."];
        if (input.MemberAuditNoticeReceived && input.MemberAuditNoticeDate is null)
            errors["memberAuditNoticeDate"] = ["Member audit notice date is required when notice was received."];

        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }

    public static IResult? ValidatePeriodStatusUpdate(
        Accounts.Api.Entities.AccountingPeriod period,
        PeriodStatusUpdate update,
        AuthenticatedUser user)
    {
        var errors = new Dictionary<string, string[]>();
        var locking = update.Status is Accounts.Api.Entities.PeriodStatus.Finalised or Accounts.Api.Entities.PeriodStatus.Filed;
        var reopening = (period.Status is Accounts.Api.Entities.PeriodStatus.Finalised or Accounts.Api.Entities.PeriodStatus.Filed || period.LockedAt is not null) && !locking;

        if (reopening && (string.IsNullOrWhiteSpace(update.ReopenReason) || update.ReopenReason.Trim().Length < 10))
            errors["reopenReason"] = ["A reopen reason of at least 10 characters is required."];
        if (reopening && !user.Role.Trim().Equals("Owner", StringComparison.OrdinalIgnoreCase))
            errors["status"] = ["Only owner users can reopen a locked accounting period."];

        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }

    public static void ApplyPeriodStatusUpdate(
        Accounts.Api.Entities.AccountingPeriod period,
        PeriodStatusUpdate update,
        AuthenticatedUser user,
        DateTime now)
    {
        var locking = update.Status is Accounts.Api.Entities.PeriodStatus.Finalised or Accounts.Api.Entities.PeriodStatus.Filed;
        var wasLocked = period.Status is Accounts.Api.Entities.PeriodStatus.Finalised or Accounts.Api.Entities.PeriodStatus.Filed || period.LockedAt is not null;

        period.Status = update.Status;
        if (locking)
        {
            period.LockedAt ??= now;
            period.LockedBy = AuthenticatedIdentity.ReviewerDisplayName(user);
        }
        else if (wasLocked)
        {
            period.LockedAt = null;
            period.LockedBy = null;
            period.ReopenedAt = now;
            period.ReopenedBy = AuthenticatedIdentity.ReviewerDisplayName(user);
            period.ReopenReason = update.ReopenReason?.Trim();
        }
    }

    public static Accounts.Api.Entities.Company ToCompany(CompanyInput input)
    {
        var company = new Accounts.Api.Entities.Company { LegalName = input.LegalName!.Trim() };
        ApplyCompany(company, input);
        return company;
    }

    public static void ApplyCompany(Accounts.Api.Entities.Company company, CompanyInput input)
    {
        company.LegalName = input.LegalName!.Trim();
        company.TradingName = TrimToNull(input.TradingName);
        company.CroNumber = TrimToNull(input.CroNumber);
        company.TaxReference = TrimToNull(input.TaxReference);
        company.CompanyType = input.CompanyType;
        company.IncorporationDate = input.IncorporationDate;
        company.FinancialYearStartMonth = input.FinancialYearStartMonth;
        company.ArdMonth = input.ArdMonth;
        company.RegisteredOfficeAddress1 = TrimToNull(input.RegisteredOfficeAddress1);
        company.RegisteredOfficeAddress2 = TrimToNull(input.RegisteredOfficeAddress2);
        company.RegisteredOfficeCity = TrimToNull(input.RegisteredOfficeCity);
        company.RegisteredOfficeCounty = TrimToNull(input.RegisteredOfficeCounty);
        company.RegisteredOfficeEircode = TrimToNull(input.RegisteredOfficeEircode);
        company.IsGroupMember = input.IsGroupMember;
        company.IsHolding = input.IsHolding;
        company.IsInvestment = input.IsInvestment;
        company.IsSubsidiary = input.IsSubsidiary;
        company.IsDormant = input.IsDormant;
        company.IsTrading = input.IsTrading;
        company.IsVatRegistered = input.IsVatRegistered;
        company.IsEmployer = input.IsEmployer;
        company.HasStock = input.HasStock;
        company.OwnsAssets = input.OwnsAssets;
        company.HasBorrowings = input.HasBorrowings;
        company.HasDirectorLoans = input.HasDirectorLoans;
        company.IsListedSecurities = input.IsListedSecurities;
        company.IsCreditInstitution = input.IsCreditInstitution;
        company.IsInsuranceUndertaking = input.IsInsuranceUndertaking;
        company.IsPensionFund = input.IsPensionFund;
        company.IsCharitableOrganisation = input.IsCharitableOrganisation;
        company.UpdatedAt = DateTime.UtcNow;
    }

    public static Accounts.Api.Entities.CompanyOfficer ToOfficer(int companyId, CompanyOfficerInput input)
    {
        var officer = new Accounts.Api.Entities.CompanyOfficer { CompanyId = companyId, Name = input.Name!.Trim() };
        ApplyOfficer(officer, input);
        return officer;
    }

    public static void ApplyOfficer(Accounts.Api.Entities.CompanyOfficer officer, CompanyOfficerInput input)
    {
        officer.Name = input.Name!.Trim();
        officer.Role = input.Role;
        officer.AppointedDate = input.AppointedDate;
        officer.ResignedDate = input.ResignedDate;
        officer.Address = TrimToNull(input.Address);
    }

    public static Accounts.Api.Entities.AccountingPeriod ToPeriod(int companyId, AccountingPeriodInput input) => new()
    {
        CompanyId = companyId,
        PeriodStart = input.PeriodStart,
        PeriodEnd = input.PeriodEnd,
        IsFirstYear = input.IsFirstYear,
        MemberAuditNoticeReceived = input.MemberAuditNoticeReceived,
        MemberAuditNoticeDate = input.MemberAuditNoticeDate,
        GoingConcernConfirmed = input.GoingConcernConfirmed,
        GoingConcernNote = TrimToNull(input.GoingConcernNote)
    };

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
