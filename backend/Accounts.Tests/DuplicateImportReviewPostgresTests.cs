using System.Text;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

public sealed class DuplicateImportReviewPostgresTests : IAsyncLifetime
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";
    private readonly string? baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar);
    private readonly string schemaName = "duplicate_import_" + Guid.NewGuid().ToString("N");
    private DbContextOptions<AccountsDbContext>? options;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString)) return;
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
        if (string.IsNullOrWhiteSpace(baseConnectionString)) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using var dropSchema = admin.CreateCommand();
        dropSchema.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE";
        await dropSchema.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task RetainedImports_UseRelationalBatchDecisionsAndDatabaseEvidenceGuards()
    {
        await using var db = new AccountsDbContext(
            options ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required."));
        var tenant = new Tenant { Name = "Duplicate PG Firm", Slug = $"duplicate-pg-{Guid.NewGuid():N}" };
        var company = new Company
        {
            Tenant = tenant,
            LegalName = "Duplicate PostgreSQL Limited",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2026, 1, 1),
            IsTrading = true
        };
        var period = new AccountingPeriod
        {
            Company = company,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 12, 31),
            IsFirstYear = true,
            GoingConcernConfirmed = true
        };
        var bank = new BankAccount { Company = company, Name = "EUR Current", Currency = "EUR" };
        var secondBank = new BankAccount { Company = company, Name = "GBP Current", Currency = "GBP" };
        db.AddRange(tenant, company, period, bank, secondBank);
        await db.SaveChangesAsync();

        const string csv = "Date,Description,Amount,Balance,Reference\n05/01/2026,Card Shop,-12.34,987.66,REF-1\n06/01/2026,Client Receipt,120.00,1107.66,REF-2\n";
        var importer = new ImportService(db, Options.Create(new ImportLimitConfig()));
        await ImportAsync(importer, company.Id, bank.Id, period.Id, csv, "january.csv");
        var repeated = await ImportAsync(importer, company.Id, bank.Id, period.Id, csv, "january-copy.csv");
        await ImportAsync(importer, company.Id, secondBank.Id, period.Id,
            "Date,Description,Amount,Balance,Reference\n05/01/2026,Other bank,1.00,10.00,OTHER-1\n", "other-bank.csv");
        db.ChangeTracker.Clear();

        var review = new DuplicateReviewService(db, new AuditService(db), new AccountingConcurrencyCoordinator(db));
        var queue = await review.GetQueueAsync(company.Id, period.Id);
        var batch = Assert.Single(queue.ExactReimportBatches);
        Assert.Equal(repeated.ImportBatchId, batch.ImportBatchId);
        Assert.Equal(2, batch.CandidateCount);
        Assert.All(queue.Items.Where(item => item.ImportBatchId == batch.ImportBatchId), item => Assert.True(item.BatchDecisionAvailable));

        var actor = new AuthenticatedUser(7, tenant.Id, tenant.Name, "accountant@example.ie", "Qualified Accountant", "Accountant");
        var result = await review.DecideExactReimportBatchAsync(
            company.Id, period.Id, batch.ImportBatchId, DuplicateReviewStatus.Discarded,
            "The retained byte-identical statement proves this whole batch is a re-import.",
            batch.CurrentStatus, batch.CandidateCount, batch.DecisionToken, actor, "pg-batch-discard");
        Assert.Equal(2, result.UpdatedCount);
        db.ChangeTracker.Clear();
        var discardedRows = await db.ImportedTransactions
            .Where(item => item.ImportBatchId == batch.ImportBatchId)
            .OrderBy(item => item.Id)
            .ToListAsync();
        Assert.All(discardedRows, row =>
        {
            Assert.True(row.IsDuplicate);
            Assert.Equal(DuplicateReviewStatus.Discarded, row.DuplicateReviewStatus);
            Assert.Equal(1, row.DuplicateDecisionVersion);
        });
        var audit = await db.AuditLogs.SingleAsync(item => item.RequestId == "pg-batch-discard");
        Assert.Equal(AuditEventCodes.DuplicateReviewBatchDecisionRecorded, audit.Action);
        Assert.All(discardedRows, row => Assert.Contains(row.Id.ToString(), audit.NewValueJson));

        var stale = await Assert.ThrowsAsync<AccountingConcurrencyException>(() => review.DecideExactReimportBatchAsync(
            company.Id, period.Id, batch.ImportBatchId, DuplicateReviewStatus.Retained,
            "A stale batch token must not replace the committed relational decision.",
            DuplicateReviewStatus.Pending, batch.CandidateCount, batch.DecisionToken, actor, "pg-stale"));
        Assert.Contains("changed", stale.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), item => item.RequestId == "pg-stale");

        var firstSourceHash = await db.ImportedTransactions
            .Where(item => item.BankAccountId == bank.Id && item.ImportBatchId != batch.ImportBatchId)
            .Select(item => item.SourceRowSha256)
            .FirstAsync();
        var secondBankBatch = await db.ImportBatches.SingleAsync(item => item.BankAccountId == secondBank.Id);
        var crossBankInsert = $"""
            INSERT INTO imported_transactions
                ("BankAccountId", "PeriodId", "ImportBatchId", "Date", "Description", "Amount", "IsDuplicate", "ManualOverride",
                 "SourceRowNumber", "SourceRowSha256", "SourceRowJson", "DuplicateReviewStatus", "DuplicateCandidateKind",
                 "DuplicateConfidence", "DuplicateCandidateReasonsJson", "DuplicateMatchedSourceRowSha256", "DuplicateDecisionVersion")
            VALUES
                ({secondBank.Id}, {period.Id}, {secondBankBatch.Id}, DATE '2026-01-08', 'Cross-bank forged match', 1.00, FALSE, FALSE,
                 2, '{new string('c', 64)}', '["forged"]'::jsonb, 'Pending', 'SameDateAmountDescription',
                 0.55, '["Forged cross-bank match"]'::jsonb, '{firstSourceHash}', 0)
            """;
        var ownershipError = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(crossBankInsert));
        Assert.Equal("23514", ownershipError.SqlState);

        await AssertConstraintAsync(db, $"UPDATE bank_accounts SET \"Currency\" = 'USD' WHERE \"Id\" = {bank.Id}");
        await AssertConstraintAsync(db, $"UPDATE imported_transactions SET \"Description\" = 'Tampered' WHERE \"Id\" = {discardedRows[0].Id}");
        await AssertConstraintAsync(db, $"DELETE FROM import_batches WHERE \"Id\" = {batch.ImportBatchId}");
        await AssertConstraintAsync(db, $"DELETE FROM bank_accounts WHERE \"Id\" = {bank.Id}");

        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = period.Id,
            Date = new DateOnly(2026, 7, 1),
            Description = "Legacy normal row with no source hash",
            Amount = 10m,
            IsDuplicate = false,
            DuplicateReviewStatus = DuplicateReviewStatus.NotCandidate
        });
        await db.SaveChangesAsync();
        Assert.True(await db.ImportedTransactions.AnyAsync(item => item.SourceRowSha256 == null && item.Description.StartsWith("Legacy normal")));
    }

    private static async Task<ImportService.ImportResult> ImportAsync(
        ImportService service, int companyId, int bankId, int periodId, string csv, string filename) =>
        await service.ImportCsvAsync(
            companyId, bankId, periodId,
            new MemoryStream(Encoding.UTF8.GetBytes(csv)), filename,
            new ImportService.ColumnMapping(0, 1, 2, 3, 4));

    private static async Task AssertConstraintAsync(AccountsDbContext db, string sql)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
        Assert.Contains(error.SqlState, new[] { "23514", "23503" });
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
