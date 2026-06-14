using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Middleware;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

public sealed class AuditTrailPostgresIntegrationTests : IAsyncLifetime
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";
    private readonly string? baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar);
    private readonly string schemaName = "audit_it_" + Guid.NewGuid().ToString("N");
    private ServiceProvider? services;
    private string? testConnectionString;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString))
            return;

        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            SearchPath = schemaName
        };
        testConnectionString = builder.ConnectionString;

        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using (var createSchema = admin.CreateCommand())
        {
            createSchema.CommandText = $"CREATE SCHEMA \"{schemaName}\"";
            await createSchema.ExecuteNonQueryAsync();
        }

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddDbContext<AccountsDbContext>(options => options.UseNpgsql(testConnectionString));
        serviceCollection.AddScoped<AuditService>();
        services = serviceCollection.BuildServiceProvider();

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (services is not null)
            await services.DisposeAsync();

        if (string.IsNullOrWhiteSpace(baseConnectionString))
            return;

        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using var dropSchema = admin.CreateCommand();
        dropSchema.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE";
        await dropSchema.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task RejectedWriteRollsBackBusinessChangesButPersistsAuditRow()
    {
        var periodId = await SeedPeriodAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<AuditService>();
        var period = await db.AccountingPeriods.SingleAsync(p => p.Id == periodId);
        var context = WriteAuditContext(
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/adjustments",
            "pg-rejected-rollback");
        var middleware = new AuditTrailMiddleware(async innerContext =>
        {
            period.Status = PeriodStatus.Finalised;
            await db.SaveChangesAsync();
            innerContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        });

        await middleware.InvokeAsync(context, db, audit, Services.GetRequiredService<IServiceScopeFactory>());

        await using var verifyScope = Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var reloadedPeriod = await verifyDb.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == periodId);
        var auditRow = await verifyDb.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityType == "ApiRequest");

        Assert.Equal(PeriodStatus.Draft, reloadedPeriod.Status);
        Assert.Equal("ApiWriteRejected", auditRow.Action);
        Assert.Equal("pg-rejected-rollback", auditRow.RequestId);
        Assert.Equal("reviewer@example.ie", auditRow.UserId);
        Assert.NotNull(auditRow.IntegrityHash);
    }

    [PostgresFact]
    public async Task FailedWriteRollsBackBusinessChangesButPersistsAuditRow()
    {
        var periodId = await SeedPeriodAsync();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<AuditService>();
        var period = await db.AccountingPeriods.SingleAsync(p => p.Id == periodId);
        var context = WriteAuditContext(
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/filing/cro-status",
            "pg-failed-rollback");
        var middleware = new AuditTrailMiddleware(async _ =>
        {
            period.Status = PeriodStatus.Finalised;
            await db.SaveChangesAsync();
            throw new InvalidOperationException("simulated handler failure");
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            middleware.InvokeAsync(context, db, audit, Services.GetRequiredService<IServiceScopeFactory>()));

        await using var verifyScope = Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var reloadedPeriod = await verifyDb.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == periodId);
        var auditRow = await verifyDb.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityType == "ApiRequest");

        Assert.Equal(PeriodStatus.Draft, reloadedPeriod.Status);
        Assert.Equal("ApiWriteFailed", auditRow.Action);
        Assert.Equal("pg-failed-rollback", auditRow.RequestId);
        Assert.Equal("reviewer@example.ie", auditRow.UserId);
        Assert.Contains("InvalidOperationException", auditRow.NewValueJson);
        Assert.DoesNotContain("simulated handler failure", auditRow.NewValueJson);
        Assert.NotNull(auditRow.IntegrityHash);
    }

    private ServiceProvider Services =>
        services ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required for PostgreSQL integration tests.");

    private async Task<int> SeedPeriodAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var company = new Company
        {
            LegalName = "Postgres Audit Test Limited",
            CroNumber = Guid.NewGuid().ToString("N")[..20],
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2025, 1, 1),
            ArdMonth = 9,
            IsTrading = true,
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        var period = new AccountingPeriod
        {
            Company = company,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = true
        };
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();
        return period.Id;
    }

    private static DefaultHttpContext WriteAuditContext(string method, string path, string correlationId)
    {
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Headers["X-Correlation-ID"] = correlationId;
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 7,
            TenantId: 17,
            TenantName: "Firm A",
            Email: "reviewer@example.ie",
            DisplayName: "Maeve Reviewer",
            Role: "Reviewer");
        return context;
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
