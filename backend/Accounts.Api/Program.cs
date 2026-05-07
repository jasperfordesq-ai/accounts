using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Features;
using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

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
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5433;Database=accounts;Username=accounts;Password=accounts_dev";
builder.Services.AddDbContext<AccountsDbContext>(options =>
    options.UseNpgsql(connectionString));

// Rules engine config
builder.Services.Configure<SizeThresholdConfig>(builder.Configuration.GetSection("SizeThresholds"));
builder.Services.Configure<ImportLimitConfig>(builder.Configuration.GetSection("ImportLimits"));
builder.Services.Configure<DatabaseStartupConfig>(builder.Configuration.GetSection("DatabaseStartup"));

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
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
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
builder.Services.AddSingleton<ProductionSafetyService>();

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
        else
        {
            var origins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
                ?? ["http://localhost:3000"];
            policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader();
        }
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

// Database startup tasks. In production these must be explicitly opted into.
using (var scope = app.Services.CreateScope())
{
    var dbStartup = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseStartupConfig>>().Value;
    if (dbStartup.AutoMigrateOnStartup)
    {
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        await db.Database.MigrateAsync();

        if (dbStartup.SeedDemoData)
            await SeedData.SeedAsync(db);
    }
}

// Middleware
app.UseMiddleware<Accounts.Api.Middleware.SecurityHeadersMiddleware>();
app.UseRateLimiter();
app.UseCors();
app.UseMiddleware<Accounts.Api.Middleware.ExceptionMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
   .WithTags("System");

// Company endpoints
var companies = app.MapGroup("/api/companies").WithTags("Companies");

companies.MapGet("/", async (AccountsDbContext db) =>
    await db.Companies.Select(c => new
    {
        c.Id, c.LegalName, c.TradingName, c.CroNumber, c.CompanyType,
        c.IsTrading, c.IsDormant, c.CreatedAt,
        PeriodCount = c.Periods.Count
    }).ToListAsync());

companies.MapGet("/{id:int}", async (int id, AccountsDbContext db) =>
    await db.Companies
        .Include(c => c.Officers)
        .Include(c => c.Periods)
        .FirstOrDefaultAsync(c => c.Id == id)
    is { } company ? Results.Ok(company) : Results.NotFound());

companies.MapPost("/", async (Accounts.Api.Entities.Company company, AccountsDbContext db) =>
{
    db.Companies.Add(company);
    await db.SaveChangesAsync();
    return Results.Created($"/api/companies/{company.Id}", company);
});

companies.MapPut("/{id:int}", async (int id, Accounts.Api.Entities.Company input, AccountsDbContext db) =>
{
    var company = await db.Companies.FindAsync(id);
    if (company is null) return Results.NotFound();

    company.LegalName = input.LegalName;
    company.TradingName = input.TradingName;
    company.CroNumber = input.CroNumber;
    company.TaxReference = input.TaxReference;
    company.CompanyType = input.CompanyType;
    company.IncorporationDate = input.IncorporationDate;
    company.FinancialYearStartMonth = input.FinancialYearStartMonth;
    company.ArdMonth = input.ArdMonth;
    company.RegisteredOfficeAddress1 = input.RegisteredOfficeAddress1;
    company.RegisteredOfficeAddress2 = input.RegisteredOfficeAddress2;
    company.RegisteredOfficeCity = input.RegisteredOfficeCity;
    company.RegisteredOfficeCounty = input.RegisteredOfficeCounty;
    company.RegisteredOfficeEircode = input.RegisteredOfficeEircode;
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

    await db.SaveChangesAsync();
    return Results.Ok(company);
});

companies.MapDelete("/{id:int}", async (int id, AccountsDbContext db) =>
{
    var company = await db.Companies.FindAsync(id);
    if (company is null) return Results.NotFound();
    db.Companies.Remove(company);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// Officers endpoints
var officers = app.MapGroup("/api/companies/{companyId:int}/officers").WithTags("Officers");

officers.MapGet("/", async (int companyId, AccountsDbContext db) =>
    await db.CompanyOfficers.Where(o => o.CompanyId == companyId).ToListAsync());

officers.MapPost("/", async (int companyId, Accounts.Api.Entities.CompanyOfficer officer, AccountsDbContext db) =>
{
    officer.CompanyId = companyId;
    db.CompanyOfficers.Add(officer);
    await db.SaveChangesAsync();
    return Results.Created($"/api/companies/{companyId}/officers/{officer.Id}", officer);
});

officers.MapPut("/{id:int}", async (int companyId, int id, Accounts.Api.Entities.CompanyOfficer input, AccountsDbContext db) =>
{
    var officer = await db.CompanyOfficers.FirstOrDefaultAsync(o => o.Id == id && o.CompanyId == companyId);
    if (officer is null) return Results.NotFound();
    officer.Name = input.Name;
    officer.Role = input.Role;
    officer.AppointedDate = input.AppointedDate;
    officer.ResignedDate = input.ResignedDate;
    officer.Address = input.Address;
    await db.SaveChangesAsync();
    return Results.Ok(officer);
});

officers.MapDelete("/{id:int}", async (int companyId, int id, AccountsDbContext db) =>
{
    var officer = await db.CompanyOfficers.FirstOrDefaultAsync(o => o.Id == id && o.CompanyId == companyId);
    if (officer is null) return Results.NotFound();
    db.CompanyOfficers.Remove(officer);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// Accounting Periods endpoints
var periods = app.MapGroup("/api/companies/{companyId:int}/periods").WithTags("Periods");

periods.MapGet("/", async (int companyId, AccountsDbContext db) =>
    await db.AccountingPeriods
        .Where(p => p.CompanyId == companyId)
        .Include(p => p.SizeClassification)
        .Include(p => p.FilingRegime)
        .OrderByDescending(p => p.PeriodEnd)
        .ToListAsync());

periods.MapGet("/{id:int}", async (int companyId, int id, AccountsDbContext db) =>
    await db.AccountingPeriods
        .Include(p => p.SizeClassification)
        .Include(p => p.FilingRegime)
        .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId)
    is { } period ? Results.Ok(period) : Results.NotFound());

periods.MapPost("/", async (int companyId, Accounts.Api.Entities.AccountingPeriod period, AccountsDbContext db) =>
{
    period.CompanyId = companyId;
    db.AccountingPeriods.Add(period);
    await db.SaveChangesAsync();
    return Results.Created($"/api/companies/{companyId}/periods/{period.Id}", period);
});

periods.MapPut("/{id:int}/status", async (int companyId, int id, PeriodStatusUpdate update, AccountsDbContext db) =>
{
    var period = await db.AccountingPeriods.FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId);
    if (period is null) return Results.NotFound();

    period.Status = update.Status;
    if (update.Status == Accounts.Api.Entities.PeriodStatus.Finalised)
    {
        period.LockedAt = DateTime.UtcNow;
        period.LockedBy = update.LockedBy;
    }
    await db.SaveChangesAsync();
    return Results.Ok(period);
});

// Rules engine endpoints
app.MapAllEndpoints();

app.Run();

record PeriodStatusUpdate(Accounts.Api.Entities.PeriodStatus Status, string? LockedBy);
