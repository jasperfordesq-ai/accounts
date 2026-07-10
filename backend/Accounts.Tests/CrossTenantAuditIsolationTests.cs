using System.Text;
using System.Text.Json;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Middleware;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

public sealed class CrossTenantAuditIsolationTests
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";

    [Fact]
    public async Task ValidCsrfCrossTenantMutation_DoesNotChangeVictimAuditChainOrCheckpoint_InMemory()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase($"cross-tenant-audit-{Guid.NewGuid():N}")
            .Options;

        await RunScenarioAsync(options, migrate: false);
    }

    [PostgresFact]
    public async Task ValidCsrfCrossTenantMutation_DoesNotChangeVictimAuditChainOrCheckpoint_Postgres()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar)!;
        var schemaName = $"cross_tenant_audit_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using (var createSchema = admin.CreateCommand())
        {
            createSchema.CommandText = $"CREATE SCHEMA \"{schemaName}\"";
            await createSchema.ExecuteNonQueryAsync();
        }

        try
        {
            var connection = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                SearchPath = schemaName
            };
            var options = new DbContextOptionsBuilder<AccountsDbContext>()
                .UseNpgsql(connection.ConnectionString)
                .Options;

            await RunScenarioAsync(options, migrate: true);
        }
        finally
        {
            await using var dropSchema = admin.CreateCommand();
            dropSchema.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE";
            await dropSchema.ExecuteNonQueryAsync();
        }
    }

    private static async Task RunScenarioAsync(DbContextOptions<AccountsDbContext> options, bool migrate)
    {
        await using var db = new AccountsDbContext(options);
        if (migrate)
            await db.Database.MigrateAsync();
        else
            await db.Database.EnsureCreatedAsync();

        var attackerTenant = new Tenant
        {
            Name = "Attacker Practice",
            Slug = $"attacker-{Guid.NewGuid():N}"
        };
        var victimTenant = new Tenant
        {
            Name = "Victim Practice",
            Slug = $"victim-{Guid.NewGuid():N}"
        };
        var attackerCompany = CompanyFor(attackerTenant, "Attacker Client Limited");
        var victimCompany = CompanyFor(victimTenant, "Victim Client Limited");
        var victimPeriod = new AccountingPeriod
        {
            Company = victimCompany,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = true
        };
        db.Companies.Add(attackerCompany);
        db.AccountingPeriods.Add(victimPeriod);
        await db.SaveChangesAsync();

        var victimAudit = new AuditService(db);
        await victimAudit.LogAsync(
            victimCompany.Id,
            victimPeriod.Id,
            "AccountingPeriod",
            victimPeriod.Id,
            "VictimBaselineCreated",
            newValue: new { State = "baseline" },
            userId: "victim.owner@example.ie",
            tenantId: victimTenant.Id,
            requestId: "victim-baseline");

        var checkpointService = new AuditIntegrityCheckpointService(
            db,
            Options.Create(new AuditIntegrityConfig
            {
                ActiveKeyId = "cross-tenant-test-key",
                SigningKeys =
                [
                    new AuditIntegritySigningKeyConfig
                    {
                        KeyId = "cross-tenant-test-key",
                        SigningKey = AuditIntegrityCheckpointService.DevelopmentSigningKeyBase64
                    }
                ]
            }));
        await checkpointService.CreateCompanyCheckpointAsync(
            victimCompany.Id,
            "victim.owner@example.ie",
            "Victim Owner",
            "victim-checkpoint",
            victimTenant.Id);

        var before = await ReadVictimStateAsync(db, victimCompany.Id);
        var beforeTotalAuditRows = await db.AuditLogs.CountAsync();

        var crossTenant = await ExecuteRequestPipelineAsync(
            db,
            attackerTenant,
            attackerCompany.Id,
            victimCompany.Id,
            victimPeriod.Id);
        var nonexistent = await ExecuteRequestPipelineAsync(
            db,
            attackerTenant,
            attackerCompany.Id,
            int.MaxValue,
            int.MaxValue);

        Assert.Equal(StatusCodes.Status404NotFound, crossTenant.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, nonexistent.StatusCode);
        Assert.False(crossTenant.AuditTrailReached);
        Assert.False(nonexistent.AuditTrailReached);
        Assert.Equal(nonexistent.ResponseBody, crossTenant.ResponseBody);
        Assert.Equal("{\"error\":\"Company not found.\"}", crossTenant.ResponseBody);

        db.ChangeTracker.Clear();
        var after = await ReadVictimStateAsync(db, victimCompany.Id);
        Assert.Equal(before.AuditRowCount, after.AuditRowCount);
        Assert.Equal(before.LatestIntegrityHash, after.LatestIntegrityHash);
        Assert.Equal(before.CheckpointBytes, after.CheckpointBytes);
        Assert.Equal(beforeTotalAuditRows, await db.AuditLogs.CountAsync());

        var integrity = await new AuditIntegrityService(db).VerifyCompanyAsync(victimCompany.Id);
        Assert.True(integrity.IsValid);
        Assert.Equal(before.LatestIntegrityHash, integrity.LastHash);
    }

    private static Company CompanyFor(Tenant tenant, string name) => new()
    {
        Tenant = tenant,
        LegalName = name,
        CompanyType = CompanyType.Private,
        IncorporationDate = new DateOnly(2025, 1, 1),
        AnnualReturnDate = new DateOnly(2024, 9, 15),
        IsTrading = true
    };

    private static async Task<RequestResult> ExecuteRequestPipelineAsync(
        AccountsDbContext db,
        Tenant attackerTenant,
        int attackerCompanyId,
        int requestedCompanyId,
        int requestedPeriodId)
    {
        const string csrfToken = "valid-cross-tenant-csrf-token";
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = $"/api/companies/{requestedCompanyId}/periods/{requestedPeriodId}/adjustments";
        context.Request.Headers[CsrfProtectionMiddleware.HeaderName] = csrfToken;
        context.Request.Headers.Cookie = $"accounts_csrf={csrfToken}";
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 71,
            TenantId: attackerTenant.Id,
            TenantName: attackerTenant.Name,
            Email: "attacker.owner@example.ie",
            DisplayName: "Attacker Owner",
            Role: "Owner",
            AllowedCompanyIds: new HashSet<int> { attackerCompanyId },
            CsrfToken: csrfToken);

        var auditTrailReached = false;
        var audit = new AuditTrailMiddleware(innerContext =>
        {
            auditTrailReached = true;
            innerContext.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        var tenant = new TenantAccessMiddleware(innerContext =>
            audit.InvokeAsync(innerContext, db, new AuditService(db)));
        var csrf = new CsrfProtectionMiddleware(innerContext => tenant.InvokeAsync(innerContext, db));

        await csrf.InvokeAsync(context, Options.Create(new AuthSessionConfig()));
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        var responseBody = await reader.ReadToEndAsync();
        return new RequestResult(context.Response.StatusCode, responseBody, auditTrailReached);
    }

    private static async Task<VictimState> ReadVictimStateAsync(AccountsDbContext db, int companyId)
    {
        var count = await db.AuditLogs
            .AsNoTracking()
            .CountAsync(a => a.CompanyId == companyId);
        var latestHash = await db.AuditLogs
            .AsNoTracking()
            .Where(a => a.CompanyId == companyId)
            .OrderByDescending(a => a.Id)
            .Select(a => a.IntegrityHash)
            .FirstAsync();
        var checkpoint = await db.AuditIntegrityCheckpoints
            .AsNoTracking()
            .SingleAsync(c => c.CompanyId == companyId);
        return new VictimState(count, latestHash, JsonSerializer.SerializeToUtf8Bytes(checkpoint));
    }

    private sealed record RequestResult(int StatusCode, string ResponseBody, bool AuditTrailReached);
    private sealed record VictimState(int AuditRowCount, string? LatestIntegrityHash, byte[] CheckpointBytes);

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvVar)))
                Skip = $"{ConnectionEnvVar} is not set.";
        }
    }
}
