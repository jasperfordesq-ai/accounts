using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

// Chart task C6 on PostgreSQL: the adoption runs in one transaction under the company lock, through the
// accounting concurrency interceptor and the one-role-per-company unique index, with two-phase codes.
public sealed class ChartAdoptionPostgresTests : IAsyncLifetime
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";
    // Advisory locks are database-wide while these tests isolate tables by schema: process-unique keys.
    private static int nextCompanyId = 7_000_000;
    private static int nextPeriodId = 7_100_000;
    private readonly string? baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar);
    private readonly string schemaName = $"chart_adoption_{Guid.NewGuid():N}";
    private string? scopedConnectionString;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString)) return;

        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using (var create = admin.CreateCommand())
        {
            create.CommandText = $"CREATE SCHEMA \"{schemaName}\"";
            await create.ExecuteNonQueryAsync();
        }
        scopedConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString) { SearchPath = schemaName }.ConnectionString;
        await using var db = CreateDb();
        await db.Database.MigrateAsync();
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
    public async Task LiveShape_AdoptsInOneTransaction_ThroughTheConcurrencyInterceptor()
    {
        var shape = await SeedAsync();
        await using var db = CreateDb();

        await ChartAdoptionFixture.AdoptAndAssertLiveShapeAsync(db, shape);
    }

    [PostgresFact]
    public async Task CodesPermute_AndRolesMove_WithinTheUniqueRoleIndex()
    {
        var shape = await SeedAsync();
        await using (var seed = CreateDb())
            await ChartAdoptionFixture.AddTransactionAsync(seed, shape, shape.PeriodId, "Insurance paid in advance", -120m, shape.Ids["1200"]);
        await using var db = CreateDb();
        var service = new ChartAdoptionService(db, new AuditService(db));

        var plan = await service.PlanAsync(shape.CompanyId);
        var outcome = await service.AdoptAsync(shape.CompanyId, Input(plan), ChartAdoptionFixture.Owner(shape));

        Assert.True(outcome.Succeeded, outcome.Message);
        var rows = await db.AccountCategories.AsNoTracking().Where(c => c.CompanyId == shape.CompanyId).ToListAsync();
        Assert.Equal("G-1200", rows.Single(c => c.Id == shape.Ids["1200"]).Code);
        Assert.Equal(shape.Ids["1400"], rows.Single(c => c.Code == "1200").Id);
        Assert.Equal(AccountSystemRole.Prepayments, rows.Single(c => c.Code == "1120").SystemRole);
        Assert.Equal(AccountSystemRole.DirectorLoanLiability, rows.Single(c => c.Code == "2230").SystemRole);
        Assert.Null(rows.Single(c => c.Id == shape.Ids["2500"]).SystemRole);
        var roles = rows.Where(c => c.SystemRole != null).Select(c => c.SystemRole).ToList();
        Assert.Equal(roles.Count, roles.Distinct().Count());
        Assert.DoesNotContain(rows, c => c.Code.StartsWith('~'));
    }

    [PostgresFact]
    public async Task ALockedPeriodWithoutEntries_DoesNotBlockAdoption_WhileOrdinaryCategoryEditsStayLocked()
    {
        var shape = await SeedAsync(withPriorPeriod: true);
        await using (var seed = CreateDb())
            await ChartAdoptionFixture.LockAsync(seed, shape.PriorPeriodId!.Value, PeriodStatus.Finalised, lockedAt: true);

        // The interceptor still refuses an ordinary change to a company account while a period is locked.
        await using (var ordinary = CreateDb())
        {
            var row = await ordinary.AccountCategories.SingleAsync(c => c.Id == shape.Ids["6600"]);
            row.Name = "Renamed outside the chart adoption";
            await Assert.ThrowsAsync<AccountingConcurrencyException>(() => ordinary.SaveChangesAsync());
        }

        await using var db = CreateDb();
        var service = new ChartAdoptionService(db, new AuditService(db));
        var plan = await service.PlanAsync(shape.CompanyId);
        Assert.Empty(plan.BlockedBy);
        var outcome = await service.AdoptAsync(shape.CompanyId, Input(plan), ChartAdoptionFixture.Owner(shape));

        Assert.True(outcome.Succeeded, outcome.Message);
        Assert.Equal(ChartOfAccountsVariant.CharityClg, (await db.Companies.AsNoTracking().SingleAsync(c => c.Id == shape.CompanyId)).ChartOfAccountsVariant);
    }

    [PostgresFact]
    public async Task AFailureDuringAdoption_RollsBackEveryChange()
    {
        var shape = await SeedAsync();
        string before;
        await using (var snapshot = CreateDb())
            before = await ChartAdoptionFixture.CategorySnapshotAsync(snapshot, shape.CompanyId);
        // Fail the last phase (inserting the charity rows) after the re-codes have been written.
        await ExecuteAsync($"""
            CREATE FUNCTION "{schemaName}".c6_fail_insert() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW."Code" = '8200' THEN RAISE EXCEPTION 'injected chart adoption failure'; END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER c6_fail_insert BEFORE INSERT ON "{schemaName}".account_categories
                FOR EACH ROW EXECUTE FUNCTION "{schemaName}".c6_fail_insert();
            """);

        await using (var db = CreateDb())
        {
            var service = new ChartAdoptionService(db, new AuditService(db));
            var plan = await service.PlanAsync(shape.CompanyId);
            await Assert.ThrowsAnyAsync<Exception>(() => service.AdoptAsync(shape.CompanyId, Input(plan), ChartAdoptionFixture.Owner(shape)));
            // The interceptor allowance never outlives the adoption, even when it fails.
            Assert.Null(db.ChartAdoptionAllowance);
        }

        await using var check = CreateDb();
        Assert.Equal(before, await ChartAdoptionFixture.CategorySnapshotAsync(check, shape.CompanyId));
        Assert.Equal(ChartOfAccountsVariant.GenericTrading, (await check.Companies.AsNoTracking().SingleAsync(c => c.Id == shape.CompanyId)).ChartOfAccountsVariant);
        Assert.False(await check.AuditLogs.AnyAsync(log => log.Action == AuditEventCodes.AccountCategoriesChartAdopted));
        Assert.Equal(61, await check.ImportedTransactions.CountAsync(t => t.PeriodId == shape.PeriodId && t.CategoryId != null));
    }

    [PostgresFact]
    public async Task TheAllowance_LetsOnlyAccountAndVariantChangesThrough_NeverOtherProtectedWrites()
    {
        var shape = await SeedAsync(withPriorPeriod: true);
        var prior = shape.PriorPeriodId!.Value;
        int priorTransactionId;
        await using (var seed = CreateDb())
        {
            priorTransactionId = await ChartAdoptionFixture.AddTransactionAsync(seed, shape, prior, "Prior-year fee", -5m, shape.Ids["6900"]);
            await ChartAdoptionFixture.LockAsync(seed, prior, PeriodStatus.Finalised, lockedAt: true);
        }

        // Simulate an allowance that names the locked period as verified (the plan would refuse it, since
        // it has an entry; this isolates the interceptor's own narrowness).
        await using var db = CreateDb();
        SetAllowance(db, new ChartAdoptionWriteAllowance(shape.CompanyId, new HashSet<int> { prior }));

        var transaction = await db.ImportedTransactions.SingleAsync(t => t.Id == priorTransactionId);
        transaction.Description = "Edited while the allowance is set";
        await Assert.ThrowsAsync<AccountingConcurrencyException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var company = await db.Companies.SingleAsync(c => c.Id == shape.CompanyId);
        company.LegalName = "Renamed while the allowance is set";
        await Assert.ThrowsAsync<AccountingConcurrencyException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        // The two kinds of change the allowance exists for do go through.
        var category = await db.AccountCategories.SingleAsync(c => c.Id == shape.Ids["6600"]);
        category.IsArchived = true;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await using var check = CreateDb();
        Assert.Equal("Prior-year fee", (await check.ImportedTransactions.AsNoTracking().SingleAsync(t => t.Id == priorTransactionId)).Description);
        Assert.NotEqual("Renamed while the allowance is set", (await check.Companies.AsNoTracking().SingleAsync(c => c.Id == shape.CompanyId)).LegalName);
        Assert.True((await check.AccountCategories.AsNoTracking().SingleAsync(c => c.Id == shape.Ids["6600"])).IsArchived);
    }

    private static void SetAllowance(AccountsDbContext db, ChartAdoptionWriteAllowance allowance) =>
        typeof(AccountsDbContext).GetProperty(nameof(AccountsDbContext.ChartAdoptionAllowance))!.SetValue(db, allowance);

    private static ChartAdoptionInput Input(ChartAdoptionPlan plan) =>
        new(ChartOfAccountsVariant.CharityClg, plan.PlanSha256, false);

    private async Task<ChartAdoptionFixture.LiveShape> SeedAsync(bool withPriorPeriod = false)
    {
        await using var db = CreateDb();
        return await ChartAdoptionFixture.SeedLiveShapeAsync(
            db,
            withPriorPeriod,
            companyId: Interlocked.Increment(ref nextCompanyId),
            periodId: Interlocked.Increment(ref nextPeriodId),
            priorPeriodId: withPriorPeriod ? Interlocked.Increment(ref nextPeriodId) : null);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ScopedConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private AccountsDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AccountsDbContext>()
            .UseNpgsql(ScopedConnectionString)
            .AddInterceptors(new AccountingConcurrencyInterceptor())
            .Options);

    private string ScopedConnectionString => scopedConnectionString
        ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required for PostgreSQL integration tests.");

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvVar)))
                Skip = $"{ConnectionEnvVar} is not set.";
        }
    }
}
