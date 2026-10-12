using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

// Batch B fix round 3 on PostgreSQL: the deadline recalculation that follows a corporation-tax due
// confirmation is atomic. A refusal part-way through (here, after the deadlines were inserted) rolls
// back every partial deadline change, while the confirmation itself stays saved.
public sealed class CorporationTaxDueRecalculationPostgresTests : IAsyncLifetime
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";
    private readonly string? baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar);
    private readonly string schemaName = $"ct_due_recalc_{Guid.NewGuid():N}";
    private string? scopedConnectionString;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString)) return;

        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using var create = admin.CreateCommand();
        create.CommandText = $"CREATE SCHEMA \"{schemaName}\"";
        await create.ExecuteNonQueryAsync();
        scopedConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString) { SearchPath = schemaName }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString)) return;

        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using var drop = admin.CreateCommand();
        drop.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE";
        await drop.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task ARefusedRecalculation_RollsBackPartialDeadlineChanges_AndKeepsTheConfirmation()
    {
        int companyId;
        int periodId;
        await using (var db = CreateDb())
        {
            await db.Database.MigrateAsync();
            (companyId, periodId) = await SeedExemptPeriodAsync(db);
        }

        var logger = new WarningCounter();
        await using (var db = CreateDb(new RefuseDeadlineSaveInterceptor()))
        {
            // As AuditTrailMiddleware does in production: the request runs in one transaction that is
            // committed when the request succeeds.
            await using var requestTransaction = await db.Database.BeginTransactionAsync();
            var result = await YearEndEndpoints.ConfirmCorporationTaxDueEndpointAsync(
                companyId,
                periodId,
                new CorporationTaxDueConfirmationInput("Income outside the exemption", 1_200m),
                db,
                new AuditService(db),
                OwnerRequest(companyId, periodId),
                deadlineLogger: logger);
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
            await requestTransaction.CommitAsync();
        }

        await using (var db = CreateDb())
        {
            // The CRO, Revenue and Charity rows were inserted before the refusal; none survive.
            Assert.Empty(await db.FilingDeadlines.AsNoTracking().Where(deadline => deadline.PeriodId == periodId).ToListAsync());
            Assert.True(await CorporationTaxObligation.IsConfirmedDueAsync(db, periodId));
        }
        Assert.Equal(1, logger.Warnings);
    }

    private static async Task<(int CompanyId, int PeriodId)> SeedExemptPeriodAsync(AccountsDbContext db)
    {
        var tenant = new Tenant { Name = "CT Due Tenant", Slug = $"ct-due-{Guid.NewGuid():N}" };
        var company = new Company
        {
            Tenant = tenant,
            LegalName = "Example Exempt CLG",
            CroNumber = "C70000",
            CompanyType = CompanyType.CompanyLimitedByGuarantee,
            IncorporationDate = new DateOnly(2025, 1, 1),
            AnnualReturnDate = new DateOnly(2025, 9, 15),
            IsCharitableOrganisation = true,
            HoldsCharitableTaxExemption = true,
            CharitableTaxExemptionReference = "CHY-00000",
            CharitableTaxExemptionConfirmedDate = new DateOnly(2025, 3, 1)
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
        await db.SaveChangesAsync();
        db.TaxBalances.Add(new TaxBalance { PeriodId = period.Id, TaxType = TaxType.CorporationTax, Liability = 150m, Balance = 150m });
        await db.SaveChangesAsync();
        return (company.Id, period.Id);
    }

    private static DefaultHttpContext OwnerRequest(int companyId, int periodId)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = $"/api/companies/{companyId}/periods/{periodId}/tax-balances/CorporationTax/confirm-due";
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 1,
            TenantId: 1,
            TenantName: "CT Due Tenant",
            Email: "owner@example.ie",
            DisplayName: "Example Owner",
            Role: "Owner");
        return context;
    }

    private AccountsDbContext CreateDb(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<AccountsDbContext>().UseNpgsql(scopedConnectionString).AddInterceptors(interceptors).Options);

    // Refuses the deadline service's own save, after its SQL inserts have already run.
    private sealed class RefuseDeadlineSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<FilingDeadline>().Any())
                throw new BusinessRuleException("Fictitious refusal after the deadlines were inserted.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class WarningCounter : ILogger<DeadlineService>
    {
        public int Warnings { get; private set; }
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullLogger.Instance.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings++;
        }
    }

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvVar)))
                Skip = $"{ConnectionEnvVar} is not set.";
        }
    }
}
