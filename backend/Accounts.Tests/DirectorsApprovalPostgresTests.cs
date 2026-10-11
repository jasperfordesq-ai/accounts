using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

public sealed class DirectorsApprovalPostgresTests
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";

    [PostgresFact]
    public async Task EventChain_HasExactlyOneGenesisEventPerPeriodAndNoForks()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar)!;
        var schemaName = $"directors_approval_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using (var create = admin.CreateCommand())
        {
            create.CommandText = $"CREATE SCHEMA \"{schemaName}\"";
            await create.ExecuteNonQueryAsync();
        }
        var scopedConnection = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            SearchPath = schemaName
        }.ConnectionString;

        try
        {
            var options = new DbContextOptionsBuilder<AccountsDbContext>().UseNpgsql(scopedConnection).Options;
            int periodA;
            int periodB;
            int firstDirector;
            int secondDirector;
            await using (var setup = new AccountsDbContext(options))
            {
                await setup.Database.MigrateAsync();
                var tenant = new Tenant { Name = "Approval Chain Firm", Slug = Guid.NewGuid().ToString("N") };
                var company = new Company
                {
                    Tenant = tenant,
                    LegalName = "Approval Chain Limited",
                    CompanyType = CompanyType.Private,
                    IncorporationDate = new DateOnly(2024, 1, 1),
                    AnnualReturnDate = new DateOnly(2025, 9, 15)
                };
                setup.Companies.Add(company);
                await setup.SaveChangesAsync();
                var d1 = new CompanyOfficer { CompanyId = company.Id, Name = "Director One", Role = OfficerRole.Director };
                var d2 = new CompanyOfficer { CompanyId = company.Id, Name = "Director Two", Role = OfficerRole.Director };
                var a = new AccountingPeriod { CompanyId = company.Id, PeriodStart = new(2024, 1, 1), PeriodEnd = new(2024, 12, 31), IsFirstYear = true };
                var b = new AccountingPeriod { CompanyId = company.Id, PeriodStart = new(2025, 1, 1), PeriodEnd = new(2025, 12, 31) };
                setup.CompanyOfficers.AddRange(d1, d2);
                setup.AccountingPeriods.AddRange(a, b);
                await setup.SaveChangesAsync();
                periodA = a.Id;
                periodB = b.Id;
                firstDirector = d1.Id;
                secondDirector = d2.Id;
            }

            async Task<DirectorsApprovalEvent> AddEventAsync(int periodId, string? previous, int minute)
            {
                await using var db = new AccountsDbContext(options);
                var page = DirectorsApprovalTests.SamplePage(periodId, firstDirector, secondDirector);
                db.DirectorsApprovalPages.Add(page);
                await db.SaveChangesAsync();
                var approval = new DirectorsApprovalEvent
                {
                    PeriodId = periodId,
                    Kind = DirectorsApprovalEventKind.Approved,
                    ApprovalPageId = page.Id,
                    ApprovalPageSha256 = page.PageSha256,
                    SignedPageArtifact = [0x25, 0x50, 0x44, 0x46],
                    SignedPageSha256 = new string('d', 64),
                    ResolutionArtifact = [0x25, 0x50, 0x44, 0x46],
                    ResolutionSha256 = new string('e', 64),
                    RecordedByUserId = "user:1",
                    RecordedByDisplayName = "Owner",
                    RecordedAtUtc = new DateTime(2026, 11, 21, 10, minute, 0, DateTimeKind.Utc),
                    PreviousRecordSha256 = previous,
                    RecordSha256 = ""
                };
                approval.RecordSha256 = DirectorsApprovalIntegrity.ComputeEventHash(approval);
                db.DirectorsApprovalEvents.Add(approval);
                await db.SaveChangesAsync();
                return approval;
            }

            var genesisA = await AddEventAsync(periodA, null, 0);

            // A second genesis event for the same period would create two chain heads.
            var secondGenesis = await Assert.ThrowsAsync<DbUpdateException>(() => AddEventAsync(periodA, null, 1));
            Assert.IsType<PostgresException>(secondGenesis.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)secondGenesis.InnerException!).SqlState);

            // A genesis event in a different period is independent.
            await AddEventAsync(periodB, null, 2);

            // The chain can extend once from the head, but not fork from it.
            var next = await AddEventAsync(periodA, genesisA.RecordSha256, 3);
            Assert.NotNull(next);
            var fork = await Assert.ThrowsAsync<DbUpdateException>(() => AddEventAsync(periodA, genesisA.RecordSha256, 4));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)fork.InnerException!).SqlState);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var drop = admin.CreateCommand();
            drop.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE";
            await drop.ExecuteNonQueryAsync();
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

/// <summary>
/// Live-PostgreSQL proof that the directors' approval tables are tenant-isolated by forced RLS for the
/// least-privileged application role, and that their check constraints reject malformed rows.
/// </summary>
public sealed class DirectorsApprovalTenantIsolationPostgresTests : IAsyncLifetime
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";
    private const string PagesCheck = "CK_directors_approval_pages_directors";
    private const string EventsKindCheck = "CK_directors_approval_events_kind";
    private readonly string? baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar);
    private readonly string schemaName = "approval_rls_" + Guid.NewGuid().ToString("N");
    private readonly string applicationRole = "approval_rls_" + Guid.NewGuid().ToString("N")[..16];
    private readonly string applicationPassword = Convert.ToHexString(Guid.NewGuid().ToByteArray());
    private readonly string signingKey = Convert.ToBase64String(
        Enumerable.Range(1, 64).Select(value => (byte)value).ToArray());
    private string? administratorConnectionString;
    private string? applicationConnectionString;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString)) return;
        administratorConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            SearchPath = schemaName,
            Pooling = false
        }.ConnectionString;

        await using (var admin = new NpgsqlConnection(baseConnectionString))
        {
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = $"""
                CREATE ROLE "{applicationRole}" LOGIN PASSWORD '{applicationPassword}'
                    NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION INHERIT;
                CREATE SCHEMA "{schemaName}";
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using (var db = CreateDb(administratorConnectionString))
        {
            await db.Database.MigrateAsync();
            await new DatabaseTenantIsolationProvisioner(db, Options.Create(Configuration())).EnsureAsync();
            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                REVOKE CREATE ON SCHEMA "{schemaName}" FROM PUBLIC;
                REVOKE CREATE ON SCHEMA "{schemaName}" FROM "{TenantIsolationPolicyCatalog.ApplicationGroupRole}";
                GRANT USAGE ON SCHEMA "{schemaName}" TO "{TenantIsolationPolicyCatalog.ApplicationGroupRole}";
                """;
            await command.ExecuteNonQueryAsync();
        }

        applicationConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Username = applicationRole,
            Password = applicationPassword,
            SearchPath = schemaName,
            Pooling = false
        }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString)) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = $"""
            DROP SCHEMA IF EXISTS "{schemaName}" CASCADE;
            DROP ROLE IF EXISTS "{applicationRole}";
            """;
        await command.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task DirectorsApprovalRows_AreInvisibleToAnotherTenantAndRejectCrossTenantWrites()
    {
        var adminConnection = administratorConnectionString
            ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required.");
        var appConnection = applicationConnectionString
            ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required.");

        Seeded a;
        Seeded b;
        int pageId;
        await using (var setup = CreateDb(adminConnection))
        {
            a = await SeedAsync(setup, "Approval RLS A");
            b = await SeedAsync(setup, "Approval RLS B");
            var page = DirectorsApprovalTests.SamplePage(a.PeriodId, a.FirstDirectorId, a.SecondDirectorId);
            setup.DirectorsApprovalPages.Add(page);
            await setup.SaveChangesAsync();
            pageId = page.Id;
            var approval = new DirectorsApprovalEvent
            {
                PeriodId = a.PeriodId,
                Kind = DirectorsApprovalEventKind.Approved,
                ApprovalPageId = page.Id,
                ApprovalPageSha256 = page.PageSha256,
                SignedPageArtifact = [0x25, 0x50, 0x44, 0x46],
                SignedPageSha256 = new string('d', 64),
                ResolutionArtifact = [0x25, 0x50, 0x44, 0x46],
                ResolutionSha256 = new string('e', 64),
                RecordedByUserId = "user:1",
                RecordedByDisplayName = "Owner",
                RecordedAtUtc = new DateTime(2026, 11, 21, 10, 0, 0, DateTimeKind.Utc),
                RecordSha256 = ""
            };
            approval.RecordSha256 = DirectorsApprovalIntegrity.ComputeEventHash(approval);
            setup.DirectorsApprovalEvents.Add(approval);
            await setup.SaveChangesAsync();
        }

        var contextA = new DatabaseTenantContext(new HttpContextAccessor());
        contextA.SetResolvedTenant(a.TenantId);
        var interceptorA = new TenantRlsConnectionInterceptor(contextA, Options.Create(Configuration()));
        var contextB = new DatabaseTenantContext(new HttpContextAccessor());
        contextB.SetResolvedTenant(b.TenantId);
        var interceptorB = new TenantRlsConnectionInterceptor(contextB, Options.Create(Configuration()));

        // EF with query filters ignored: only forced RLS stands between the role and the other tenant.
        await using (var dbB = CreateAppDb(appConnection, interceptorB))
        {
            Assert.Equal(0, await dbB.DirectorsApprovalPages.IgnoreQueryFilters().CountAsync());
            Assert.Equal(0, await dbB.DirectorsApprovalEvents.IgnoreQueryFilters().CountAsync());
        }
        await using (var dbA = CreateAppDb(appConnection, interceptorA))
        {
            Assert.Equal(1, await dbA.DirectorsApprovalPages.IgnoreQueryFilters().CountAsync());
            Assert.Equal(1, await dbA.DirectorsApprovalEvents.IgnoreQueryFilters().CountAsync());
        }

        // Raw SQL as the application role.
        await using (var connection = new NpgsqlConnection(appConnection))
        {
            await connection.OpenAsync();
            await interceptorB.ApplyToOpenConnectionAsync(connection);
            Assert.Equal(b.TenantId, await ScalarIntAsync(connection, "SELECT accounts_current_tenant_id()"));
            Assert.Equal(0, await ScalarIntAsync(connection, "SELECT count(*)::integer FROM directors_approval_pages"));
            Assert.Equal(0, await ScalarIntAsync(connection, "SELECT count(*)::integer FROM directors_approval_events"));

            // The policy is FOR ALL ... USING (...) WITH CHECK (...), so a cross-tenant INSERT is a
            // row-level-security violation (42501), and UPDATE/DELETE of the other tenant's rows match nothing.
            var crossPage = await Assert.ThrowsAsync<PostgresException>(() =>
                InsertPageAsync(connection, a.PeriodId, a.FirstDirectorId, a.SecondDirectorId, 3));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, crossPage.SqlState);
            var crossEvent = await Assert.ThrowsAsync<PostgresException>(() =>
                InsertEventAsync(connection, a.PeriodId, pageId, "Revoked", null, null, "test", new string('1', 64)));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, crossEvent.SqlState);
            Assert.Equal(0, await ExecuteAsync(connection, "DELETE FROM directors_approval_events"));
            Assert.Equal(0, await ExecuteAsync(connection, "DELETE FROM directors_approval_pages"));
        }

        // Positive control: the same statements succeed for the owning tenant, so the rejection above
        // is the RLS policy and not a malformed statement.
        await using (var connection = new NpgsqlConnection(appConnection))
        {
            await connection.OpenAsync();
            await interceptorA.ApplyToOpenConnectionAsync(connection);
            await InsertPageAsync(connection, a.PeriodId, a.FirstDirectorId, a.SecondDirectorId, 3);
            await InsertEventAsync(connection, a.PeriodId, pageId, "Revoked", null, null, "test", new string('2', 64));
            Assert.Equal(2, await ScalarIntAsync(connection, "SELECT count(*)::integer FROM directors_approval_pages"));
            Assert.Equal(2, await ScalarIntAsync(connection, "SELECT count(*)::integer FROM directors_approval_events"));
        }

        // Nothing from tenant B's attempts reached the tables (admin view).
        await using var admin = new NpgsqlConnection(adminConnection);
        await admin.OpenAsync();
        Assert.Equal(2, await ScalarIntAsync(admin, "SELECT count(*)::integer FROM directors_approval_pages"));
        Assert.Equal(2, await ScalarIntAsync(admin, "SELECT count(*)::integer FROM directors_approval_events"));
    }

    [PostgresFact]
    public async Task DirectorsApprovalCheckConstraints_RejectMalformedRows()
    {
        var adminConnection = administratorConnectionString
            ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required.");
        Seeded seeded;
        int pageId;
        await using (var setup = CreateDb(adminConnection))
        {
            seeded = await SeedAsync(setup, "Approval Constraints");
            var page = DirectorsApprovalTests.SamplePage(seeded.PeriodId, seeded.FirstDirectorId, seeded.SecondDirectorId);
            setup.DirectorsApprovalPages.Add(page);
            await setup.SaveChangesAsync();
            pageId = page.Id;
        }

        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();

        // An Approved event must carry the signed page hash.
        var approvedWithoutSignedPage = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertEventAsync(connection, seeded.PeriodId, pageId, "Approved", null, new string('e', 64), null, new string('a', 64)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, approvedWithoutSignedPage.SqlState);
        Assert.Equal(EventsKindCheck, approvedWithoutSignedPage.ConstraintName);

        // A Revoked event must not carry a signed page hash.
        var revokedWithSignedPage = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertEventAsync(connection, seeded.PeriodId, pageId, "Revoked", new string('d', 64), null, "test", new string('b', 64)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, revokedWithSignedPage.SqlState);
        Assert.Equal(EventsKindCheck, revokedWithSignedPage.ConstraintName);

        // The second director must be a different officer from the first.
        var sameDirector = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertPageAsync(connection, seeded.PeriodId, seeded.FirstDirectorId, seeded.FirstDirectorId, 3));
        Assert.Equal(PostgresErrorCodes.CheckViolation, sameDirector.SqlState);
        Assert.Equal(PagesCheck, sameDirector.ConstraintName);

        // A sole-director page must record exactly one director in office.
        var soleDirectorWithManyInOffice = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertPageAsync(connection, seeded.PeriodId, seeded.FirstDirectorId, null, 3));
        Assert.Equal(PostgresErrorCodes.CheckViolation, soleDirectorWithManyInOffice.SqlState);
        Assert.Equal(PagesCheck, soleDirectorWithManyInOffice.ConstraintName);

        // Positive controls: well-formed rows through the same statements are accepted.
        await InsertPageAsync(connection, seeded.PeriodId, seeded.FirstDirectorId, null, 1);
        await InsertPageAsync(connection, seeded.PeriodId, seeded.FirstDirectorId, seeded.SecondDirectorId, 3);
        await InsertEventAsync(connection, seeded.PeriodId, pageId, "Approved", new string('d', 64), new string('e', 64), null, null);
    }

    [PostgresFact]
    public async Task DirectorsApprovalRecords_RoundTripWithValidIntegrityHashes()
    {
        var adminConnection = administratorConnectionString
            ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required.");
        Seeded seeded;
        int pageId;
        int eventId;
        await using (var setup = CreateDb(adminConnection))
        {
            seeded = await SeedAsync(setup, "Approval Round Trip");

            // Sub-microsecond ticks, truncated the way DirectorsApprovalService does before hashing.
            var instant = new DateTime(2026, 11, 20, 10, 30, 15, DateTimeKind.Utc).AddTicks(1234567);
            Assert.NotEqual(0, instant.Ticks % 10);
            var truncated = new DateTime(instant.Ticks - instant.Ticks % 10, DateTimeKind.Utc);

            var page = DirectorsApprovalTests.SamplePage(seeded.PeriodId, seeded.FirstDirectorId, seeded.SecondDirectorId);
            page.GeneratedAtUtc = truncated;
            page.RecordSha256 = DirectorsApprovalIntegrity.ComputePageHash(page);
            setup.DirectorsApprovalPages.Add(page);
            await setup.SaveChangesAsync();
            pageId = page.Id;

            var approval = new DirectorsApprovalEvent
            {
                PeriodId = seeded.PeriodId,
                Kind = DirectorsApprovalEventKind.Approved,
                ApprovalPageId = page.Id,
                ApprovalPageSha256 = page.PageSha256,
                SignedPageArtifact = [0x25, 0x50, 0x44, 0x46],
                SignedPageSha256 = new string('d', 64),
                ResolutionArtifact = [0x25, 0x50, 0x44, 0x46],
                ResolutionSha256 = new string('e', 64),
                RecordedByUserId = "user:1",
                RecordedByDisplayName = "Owner",
                RecordedAtUtc = truncated.AddTicks(30),
                RecordSha256 = ""
            };
            approval.RecordSha256 = DirectorsApprovalIntegrity.ComputeEventHash(approval);
            setup.DirectorsApprovalEvents.Add(approval);
            await setup.SaveChangesAsync();
            eventId = approval.Id;
        }

        await using var fresh = CreateDb(adminConnection);
        var storedPage = await fresh.DirectorsApprovalPages.AsNoTracking().SingleAsync(p => p.Id == pageId);
        var storedEvent = await fresh.DirectorsApprovalEvents.AsNoTracking().SingleAsync(e => e.Id == eventId);
        Assert.True(DirectorsApprovalIntegrity.IsValid(storedPage));
        Assert.True(DirectorsApprovalIntegrity.IsValid(storedEvent));
    }

    private DatabaseTenantIsolationConfig Configuration() => new()
    {
        Required = true,
        ApplicationLoginRole = applicationRole,
        ContextSigningKey = signingKey
    };

    private static AccountsDbContext CreateDb(string connectionString) => new(
        new DbContextOptionsBuilder<AccountsDbContext>().UseNpgsql(connectionString).Options);

    private static AccountsDbContext CreateAppDb(string connectionString, TenantRlsConnectionInterceptor interceptor) => new(
        new DbContextOptionsBuilder<AccountsDbContext>().UseNpgsql(connectionString).AddInterceptors(interceptor).Options);

    private sealed record Seeded(int TenantId, int PeriodId, int FirstDirectorId, int SecondDirectorId);

    private static async Task<Seeded> SeedAsync(AccountsDbContext db, string name)
    {
        var tenant = new Tenant { Name = name, Slug = "approval-" + Guid.NewGuid().ToString("N") };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var company = new Company
        {
            TenantId = tenant.Id,
            LegalName = name + " Limited",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2025, 1, 1)
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var first = new CompanyOfficer { CompanyId = company.Id, Name = "Director One", Role = OfficerRole.Director };
        var second = new CompanyOfficer { CompanyId = company.Id, Name = "Director Two", Role = OfficerRole.Director };
        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = true
        };
        db.CompanyOfficers.AddRange(first, second);
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();
        return new Seeded(tenant.Id, period.Id, first.Id, second.Id);
    }

    private static async Task InsertPageAsync(
        NpgsqlConnection connection, int periodId, int firstDirectorId, int? secondDirectorId, int inOffice)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO directors_approval_pages
                ("PeriodId", "ApprovalDate", "FirstDirectorOfficerId", "SecondDirectorOfficerId", "FirstDirectorName",
                 "SecondDirectorName", "DirectorsInOfficeCount", "ResolutionReference", "AccountsPdfSha256",
                 "ArtifactSourceFingerprintSha256", "PageArtifact", "PageSha256", "GeneratedByUserId",
                 "GeneratedByDisplayName", "GeneratedAtUtc", "RecordSha256")
            VALUES
                (@period, DATE '2026-11-20', @first, @second, 'Director One', NULL, @count, 'WR-raw', repeat('a', 64),
                 repeat('b', 64), @artifact, repeat('c', 64), 'user:1', 'Owner', TIMESTAMPTZ '2026-11-20 10:00:00+00',
                 repeat('f', 64))
            """;
        command.Parameters.AddWithValue("period", periodId);
        command.Parameters.AddWithValue("first", firstDirectorId);
        command.Parameters.Add(new NpgsqlParameter("second", NpgsqlTypes.NpgsqlDbType.Integer) { Value = (object?)secondDirectorId ?? DBNull.Value });
        command.Parameters.AddWithValue("count", inOffice);
        command.Parameters.AddWithValue("artifact", new byte[] { 0x25, 0x50, 0x44, 0x46 });
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertEventAsync(
        NpgsqlConnection connection,
        int periodId,
        int pageId,
        string kind,
        string? signedPageSha256,
        string? resolutionSha256,
        string? revocationReason,
        string? previousRecordSha256)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO directors_approval_events
                ("PeriodId", "Kind", "ApprovalPageId", "ApprovalPageSha256", "SignedPageSha256", "ResolutionSha256",
                 "RevocationReason", "RecordedByUserId", "RecordedByDisplayName", "RecordedAtUtc",
                 "PreviousRecordSha256", "RecordSha256")
            VALUES
                (@period, @kind, @page, repeat('c', 64), @signed, @resolution, @reason, 'user:1', 'Owner',
                 TIMESTAMPTZ '2026-11-21 10:00:00+00', @previous, repeat('f', 64))
            """;
        command.Parameters.AddWithValue("period", periodId);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("page", pageId);
        AddText(command, "signed", signedPageSha256);
        AddText(command, "resolution", resolutionSha256);
        AddText(command, "reason", revocationReason);
        AddText(command, "previous", previousRecordSha256);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddText(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)value ?? DBNull.Value });

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarIntAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
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
