using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

public sealed class CorporationTaxFilingSupportPostgresTests : IAsyncLifetime
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";
    private const string MigrationId = "20260711000000_AddCorporationTaxFilingSupport";
    private readonly string? baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar);
    private readonly string schemaName = "corporation_tax_filing_support_" + Guid.NewGuid().ToString("N");
    private DbContextOptions<AccountsDbContext>? options;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString))
            return;

        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using (var createSchema = admin.CreateCommand())
        {
            createSchema.CommandText = $"CREATE SCHEMA \"{schemaName}\"";
            await createSchema.ExecuteNonQueryAsync();
        }

        var scoped = new NpgsqlConnectionStringBuilder(baseConnectionString) { SearchPath = schemaName };
        options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseNpgsql(scoped.ConnectionString)
            .Options;
        await using var db = new AccountsDbContext(options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString))
            return;

        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using var dropSchema = admin.CreateCommand();
        dropSchema.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE";
        await dropSchema.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task Migration_PersistsTenantScopedReviewAndSoftVoidedPayment_WithDatabaseConstraints()
    {
        var configuredOptions = options
            ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required.");

        int firstTenantId;
        int firstCompanyId;
        int firstPeriodId;
        int firstPaymentId;

        await using (var setup = new AccountsDbContext(configuredOptions))
        {
            Assert.Contains(MigrationId, await setup.Database.GetAppliedMigrationsAsync());

            var first = CreateCompany("PostgreSQL Filing Support Firm", "filing-support-a", "FS765431");
            var second = CreateCompany("Other PostgreSQL Filing Support Firm", "filing-support-b", "FS765432");
            setup.AddRange(first.Tenant, second.Tenant);
            await setup.SaveChangesAsync();
            setup.AddRange(first.Company, second.Company);
            await setup.SaveChangesAsync();

            var firstPeriod = CreatePeriod(first.Company.Id);
            var secondPeriod = CreatePeriod(second.Company.Id);
            setup.AddRange(firstPeriod, secondPeriod);
            await setup.SaveChangesAsync();

            var firstReview = CreateReview(firstPeriod.Id, "PostgreSQL retained review evidence for tenant A.");
            var firstPayment = CreatePayment(firstPeriod.Id, "ROS payment receipt retained for tenant A.");
            setup.AddRange(
                firstReview,
                firstPayment,
                CreateReview(secondPeriod.Id, "PostgreSQL retained review evidence for tenant B."),
                CreatePayment(secondPeriod.Id, "ROS payment receipt retained for tenant B."));
            await setup.SaveChangesAsync();

            firstPayment.IsVoided = true;
            firstPayment.VoidedBy = "postgres-fixture-reviewer";
            firstPayment.VoidedAtUtc = DateTime.UtcNow;
            firstPayment.VoidReason = "Duplicate payment evidence was retained and explicitly voided.";
            await setup.SaveChangesAsync();

            firstTenantId = first.Tenant.Id;
            firstCompanyId = first.Company.Id;
            firstPeriodId = firstPeriod.Id;
            firstPaymentId = firstPayment.Id;
        }

        await using (var fresh = new AccountsDbContext(configuredOptions))
        {
            var retainedReview = await fresh.CorporationTaxFilingSupportReviews
                .SingleAsync(review => review.PeriodId == firstPeriodId);
            var retainedPayment = await fresh.CorporationTaxPaymentRecords
                .SingleAsync(payment => payment.Id == firstPaymentId);

            Assert.Equal("postgres-fixture-reviewer", retainedReview.PreparedBy);
            Assert.True(retainedPayment.IsVoided);
            Assert.NotNull(retainedPayment.VoidedAtUtc);
            Assert.Contains("explicitly voided", retainedPayment.VoidReason, StringComparison.Ordinal);
        }

        await using (var tenantScoped = CreateTenantContext(
                         configuredOptions,
                         firstTenantId,
                         firstCompanyId))
        {
            var visibleReviews = await tenantScoped.CorporationTaxFilingSupportReviews.ToListAsync();
            var visiblePayments = await tenantScoped.CorporationTaxPaymentRecords.ToListAsync();
            Assert.Single(visibleReviews);
            Assert.Equal(firstPeriodId, visibleReviews[0].PeriodId);
            Assert.Single(visiblePayments);
            Assert.Equal(firstPeriodId, visiblePayments[0].PeriodId);
            Assert.Equal(
                2,
                await tenantScoped.CorporationTaxFilingSupportReviews
                    .IgnoreQueryFilters()
                    .CountAsync());
        }

        await using (var invalid = new AccountsDbContext(configuredOptions))
        {
            invalid.CorporationTaxPaymentRecords.Add(new CorporationTaxPaymentRecord
            {
                PeriodId = firstPeriodId,
                PaymentDate = new DateOnly(2026, 11, 23),
                Amount = -1m,
                Kind = CorporationTaxPaymentKind.PreliminarySecondOrSingle,
                EvidenceReference = "Invalid negative payment evidence retained for constraint proof.",
                RecordedBy = "postgres-fixture-reviewer",
                RecordedAtUtc = DateTime.UtcNow
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync());
        }
    }

    private static AccountsDbContext CreateTenantContext(
        DbContextOptions<AccountsDbContext> configuredOptions,
        int tenantId,
        int companyId)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            902,
            tenantId,
            "PostgreSQL Filing Support Firm",
            "postgres-fixture@example.invalid",
            "PostgreSQL Fixture Reviewer",
            "Accountant",
            new HashSet<int> { companyId });
        return new AccountsDbContext(
            configuredOptions,
            new HttpContextAccessor { HttpContext = httpContext });
    }

    private static (Tenant Tenant, Company Company) CreateCompany(
        string tenantName,
        string slugPrefix,
        string croNumber)
    {
        var tenant = new Tenant
        {
            Name = tenantName,
            Slug = $"{slugPrefix}-{Guid.NewGuid():N}"
        };
        return (
            tenant,
            new Company
            {
                Tenant = tenant,
                LegalName = tenantName + " Limited",
                CroNumber = croNumber,
                TaxReference = croNumber + "T",
                CompanyType = CompanyType.Private,
                IncorporationDate = new DateOnly(2026, 1, 1),
                IsTrading = true
            });
    }

    private static AccountingPeriod CreatePeriod(int companyId) => new()
    {
        CompanyId = companyId,
        PeriodStart = new DateOnly(2026, 1, 1),
        PeriodEnd = new DateOnly(2026, 12, 31),
        IsFirstYear = true
    };

    private static CorporationTaxFilingSupportReview CreateReview(int periodId, string evidenceNote) => new()
    {
        PeriodId = periodId,
        CurrentPeriodSection239IncomeTax = 0m,
        PreparedBy = "postgres-fixture-reviewer",
        PreparedAtUtc = DateTime.UtcNow,
        EvidenceNote = evidenceNote
    };

    private static CorporationTaxPaymentRecord CreatePayment(int periodId, string evidenceReference) => new()
    {
        PeriodId = periodId,
        PaymentDate = new DateOnly(2026, 11, 23),
        Amount = 1_250m,
        Kind = CorporationTaxPaymentKind.PreliminarySecondOrSingle,
        EvidenceReference = evidenceReference,
        ExternalPaymentReference = "ROS-FIXTURE-2026",
        RecordedBy = "postgres-fixture-reviewer",
        RecordedAtUtc = DateTime.UtcNow
    };

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvVar)))
                Skip = $"{ConnectionEnvVar} is not set.";
        }
    }
}
