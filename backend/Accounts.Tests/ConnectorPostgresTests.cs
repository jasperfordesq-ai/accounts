using System.Security.Cryptography;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

/// <summary>Release-gate PostgreSQL checks for the connector tables, trigger and race behaviour.</summary>
public sealed class ConnectorPostgresTests
{
    [ConnectorPostgresFact]
    public async Task ConcurrentSpends_OfOneApproval_ExactlyOneWins()
    {
        await using var database = await MigratedSchema.CreateAsync();
        var (user, familyId, approvalId, digest) = await SeedGrantedApprovalAsync(database);
        var context = new ConnectorSessionContext(Guid.NewGuid(), familyId, "Admin", "Withheld", "0.1.0");

        var attempts = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = database.NewContext();
            return await NewService(db).TrySpendApprovalAsync(user, context, approvalId, digest);
        }).ToArray();
        var outcomes = await Task.WhenAll(attempts);

        Assert.Equal(1, outcomes.Count(outcome => outcome));
        await using var verify = database.NewContext();
        Assert.NotNull((await verify.ConnectorActionApprovals.SingleAsync()).ConsumedAtUtc);
    }

    [ConnectorPostgresFact]
    public async Task SessionPosture_CannotBeWidenedInPlace()
    {
        await using var database = await MigratedSchema.CreateAsync();
        var (user, familyId, _, _) = await SeedGrantedApprovalAsync(database);
        await using (var db = database.NewContext())
        {
            db.ConnectorSessions.Add(new ConnectorSession
            {
                Id = Guid.NewGuid(),
                TenantId = user.TenantId,
                UserId = user.UserId,
                FamilyId = familyId,
                FamilyCreatedAtUtc = DateTime.UtcNow,
                AccessLevel = ConnectorAccessLevels.Read,
                DataScope = ConnectorDataScopes.Withheld,
                RefreshTokenHash = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)),
                PasswordLastChangedAtTicks = 1,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
            });
            await db.SaveChangesAsync();
        }

        await using var update = database.NewContext();
        var widen = await Assert.ThrowsAsync<PostgresException>(() => update.ConnectorSessions
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.AccessLevel, ConnectorAccessLevels.Admin)));
        Assert.Equal("CK_connector_sessions_posture_immutable", widen.ConstraintName);

        var revoked = await update.ConnectorSessions
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.RevokedAtUtc, DateTime.UtcNow));
        Assert.Equal(1, revoked);
    }

    [ConnectorPostgresFact]
    public async Task ConnectorTables_HaveForcedRowLevelSecurity()
    {
        await using var database = await MigratedSchema.CreateAsync();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.relname, c.relrowsecurity, c.relforcerowsecurity
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema() AND c.relname LIKE 'connector_%' AND c.relkind = 'r'
            ORDER BY c.relname
            """;
        var rows = new List<(string Name, bool Enabled, bool Forced)>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                rows.Add((reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2)));

        Assert.Equal(["connector_action_approvals", "connector_activity_events", "connector_sessions"], rows.Select(row => row.Name));
        Assert.All(rows, row => Assert.True(row.Enabled && row.Forced, row.Name));
    }

    [ConnectorPostgresFact]
    public async Task ConcurrentExchanges_OfOneAuthorizationCode_IssueExactlyOneSession()
    {
        await using var database = await MigratedSchema.CreateAsync();
        var (user, _, _, _) = await SeedGrantedApprovalAsync(database);
        var keys = Keys.Create();
        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var challenge = Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        const string redirect = "http://127.0.0.1:53682/callback";
        string clientId;
        string rawCode;
        await using (var db = database.NewContext())
        {
            var oauth = NewOAuth(db, keys);
            clientId = (string)((IReadOnlyDictionary<string, object>)oauth.Register(new OAuthRegisterInput([redirect], "Race", null, null, null)).Value!)["client_id"];
            var passwordTicks = (await db.UserAccounts.AsNoTracking().SingleAsync(account => account.Id == user.UserId)).PasswordLastChangedAt.Ticks;
            rawCode = "fbac1." + Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(user.TenantId.ToString())).TrimEnd('=') + "." + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            db.OAuthAuthorizationCodes.Add(new OAuthAuthorizationCode
            {
                Id = Guid.NewGuid(),
                TenantId = user.TenantId,
                UserId = user.UserId,
                CodeHash = new MfaSecurityService(keys.Auth).HashOpaqueToken("oauth-code:" + rawCode),
                ClientIdHash = OAuthClientCodec.Hash(clientId),
                ClientName = "Race",
                RedirectUri = redirect,
                CodeChallenge = challenge,
                Resource = "https://filingbridge.example/mcp",
                AccessLevel = ConnectorAccessLevels.Write,
                DataScope = ConnectorDataScopes.Withheld,
                PasswordLastChangedAtTicks = passwordTicks,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(60)
            });
            await db.SaveChangesAsync();
        }

        var attempts = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = database.NewContext();
            var form = new Microsoft.AspNetCore.Http.FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = rawCode,
                ["redirect_uri"] = redirect,
                ["client_id"] = clientId,
                ["code_verifier"] = verifier
            });
            return (await NewOAuth(db, keys).ExchangeAsync(form)).Error is null;
        }).ToArray();
        var outcomes = await Task.WhenAll(attempts);

        Assert.Equal(1, outcomes.Count(outcome => outcome));
        await using var verify = database.NewContext();
        Assert.Single(await verify.ConnectorSessions.ToListAsync());
    }

    private sealed record Keys(IOptions<AuthSessionConfig> Auth, IOptions<ConnectorConfig> Connector)
    {
        public static Keys Create() => new(
            Options.Create(new AuthSessionConfig { SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)) }),
            Options.Create(new ConnectorConfig
            {
                Enabled = true,
                SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
                PublicBaseUrl = "https://filingbridge.example"
            }));
    }

    private static OAuthService NewOAuth(AccountsDbContext db, Keys keys)
    {
        var auth = new AuthService(db, keys.Auth, new HostEnvironment(), new Pbkdf2PasswordVerifier());
        var mfa = new MfaSecurityService(keys.Auth);
        var audit = new AuditService(db);
        var connectors = new ConnectorSessionService(
            db, auth, new IdentityAccessService(db, auth, mfa, audit, keys.Auth, TimeProvider.System), mfa, audit, keys.Connector, TimeProvider.System);
        return new OAuthService(db, connectors, mfa, keys.Connector, TimeProvider.System);
    }

    private static async Task<(AuthenticatedUser User, Guid FamilyId, Guid ApprovalId, string Digest)> SeedGrantedApprovalAsync(MigratedSchema database)
    {
        await using var db = database.NewContext();
        var tenant = new Tenant { Name = "Connector Firm", Slug = "connector-" + Guid.NewGuid().ToString("N")[..10] };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var hash = PasswordHasher.HashPassword("Correct Horse Battery Staple 1!");
        var account = new UserAccount
        {
            TenantId = tenant.Id,
            Email = "owner@connector.example",
            DisplayName = "Owner",
            Role = "Owner",
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            PasswordAlgorithm = AuthService.PasswordAlgorithm
        };
        db.UserAccounts.Add(account);
        await db.SaveChangesAsync();
        var familyId = Guid.NewGuid();
        var digest = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var approval = new ConnectorActionApproval
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = account.Id,
            SessionFamilyId = familyId,
            RequestDigest = digest,
            Summary = "Delete officers",
            Method = "DELETE",
            RoutePattern = "/api/companies/{companyId}/officers/{id}",
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            ApprovedAtUtc = DateTime.UtcNow
        };
        db.ConnectorActionApprovals.Add(approval);
        await db.SaveChangesAsync();
        var user = new AuthenticatedUser(account.Id, tenant.Id, tenant.Name, account.Email, account.DisplayName, "Owner");
        return (user, familyId, approval.Id, digest);
    }

    private static ConnectorSessionService NewService(AccountsDbContext db)
    {
        var authOptions = Options.Create(new AuthSessionConfig { SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)) });
        var auth = new AuthService(db, authOptions, new HostEnvironment(), new Pbkdf2PasswordVerifier());
        var mfa = new MfaSecurityService(authOptions);
        var audit = new AuditService(db);
        return new ConnectorSessionService(
            db,
            auth,
            new IdentityAccessService(db, auth, mfa, audit, authOptions, TimeProvider.System),
            mfa,
            audit,
            Options.Create(new ConnectorConfig { Enabled = true, SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)) }),
            TimeProvider.System);
    }

    private sealed class MigratedSchema : IAsyncDisposable
    {
        private readonly string baseConnection;
        private readonly string schema;

        private MigratedSchema(string baseConnection, string schema, string connectionString)
        {
            this.baseConnection = baseConnection;
            this.schema = schema;
            ConnectionString = connectionString;
        }

        public string ConnectionString { get; }

        public static async Task<MigratedSchema> CreateAsync()
        {
            var baseConnection = Environment.GetEnvironmentVariable("ACCOUNTS_POSTGRES_TEST_CONNECTION")!;
            var schema = "connector_" + Guid.NewGuid().ToString("N");
            await using (var admin = new NpgsqlConnection(baseConnection))
            {
                await admin.OpenAsync();
                await using var create = admin.CreateCommand();
                create.CommandText = $"CREATE SCHEMA \"{schema}\"";
                await create.ExecuteNonQueryAsync();
            }

            var scoped = new NpgsqlConnectionStringBuilder(baseConnection) { SearchPath = schema };
            var database = new MigratedSchema(baseConnection, schema, scoped.ConnectionString);
            await using var db = database.NewContext();
            await db.Database.MigrateAsync();
            return database;
        }

        public AccountsDbContext NewContext() =>
            new(new DbContextOptionsBuilder<AccountsDbContext>().UseNpgsql(ConnectionString).Options);

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(baseConnection);
            await admin.OpenAsync();
            await using var drop = admin.CreateCommand();
            drop.CommandText = $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class HostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Accounts.Tests";
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class ConnectorPostgresFactAttribute : FactAttribute
    {
        public ConnectorPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACCOUNTS_POSTGRES_TEST_CONNECTION")))
                Skip = "ACCOUNTS_POSTGRES_TEST_CONNECTION is not set.";
        }
    }
}
