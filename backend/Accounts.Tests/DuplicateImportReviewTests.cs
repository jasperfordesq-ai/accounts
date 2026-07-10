using System.Text;
using System.Text.Json;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounts.Tests;

public sealed class DuplicateImportReviewTests
{
    [Fact]
    public async Task GenuineIdenticalSameDayRows_AreBothRetainedAndOneRequiresReview()
    {
        await using var db = await CreateFixtureAsync();
        var (companyId, periodId, bankId) = await KeysAsync(db);
        const string csv = "Date,Description,Amount\n05/01/2026,Card Shop,-12.34\n05/01/2026,Card Shop,-12.34\n";

        var result = await ImportAsync(db, companyId, periodId, bankId, csv, "same-day.csv");

        Assert.Equal(2, result.ImportedRows);
        Assert.Equal(1, result.DuplicateCandidates);
        var rows = await db.ImportedTransactions.OrderBy(item => item.SourceRowNumber).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(DuplicateReviewStatus.NotCandidate, rows[0].DuplicateReviewStatus);
        Assert.Equal(DuplicateReviewStatus.Pending, rows[1].DuplicateReviewStatus);
        Assert.Equal(DuplicateCandidateKind.SameDateAmountDescription, rows[1].DuplicateCandidateKind);
        Assert.All(rows, row => Assert.False(row.IsDuplicate));
        Assert.NotEqual(rows[0].SourceRowSha256, rows[1].SourceRowSha256);
        Assert.All(rows, row => Assert.False(string.IsNullOrWhiteSpace(row.SourceRowJson)));
    }

    [Fact]
    public void PublicTransactionSerialization_RedactsRawImportAndInternalDecisionEvidence()
    {
        var transaction = new ImportedTransaction
        {
            BankAccountId = 1,
            Date = new DateOnly(2026, 1, 1),
            Description = "Safe description",
            Amount = 1m,
            SourceRowJson = "[\"private bank column\"]",
            SourceRowSha256 = new string('a', 64),
            DuplicateDecisionByUserId = "internal-user@example.ie",
            DuplicateDecisionReason = "Internal reviewer evidence must use the restricted route."
        };

        var json = JsonSerializer.Serialize(transaction);

        Assert.DoesNotContain("private bank column", json, StringComparison.Ordinal);
        Assert.DoesNotContain("internal-user@example.ie", json, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceRow", json, StringComparison.Ordinal);
        Assert.DoesNotContain("DuplicateDecision", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReimportedStatement_IsRetainedAsExactSourceCandidatesWithoutLosingRows()
    {
        await using var db = await CreateFixtureAsync();
        var (companyId, periodId, bankId) = await KeysAsync(db);
        const string csv = "Date,Description,Amount\n05/01/2026,Card Shop,-12.34\n06/01/2026,Client Receipt,120.00\n";

        var first = await ImportAsync(db, companyId, periodId, bankId, csv, "january.csv");
        var second = await ImportAsync(db, companyId, periodId, bankId, csv, "january-copy.csv");

        Assert.Equal(2, first.ImportedRows);
        Assert.Equal(0, first.DuplicateCandidates);
        Assert.Equal(2, second.ImportedRows);
        Assert.Equal(2, second.DuplicateCandidates);
        Assert.Equal(4, await db.ImportedTransactions.CountAsync());
        var candidates = await db.ImportedTransactions
            .Where(item => item.DuplicateReviewStatus == DuplicateReviewStatus.Pending)
            .ToListAsync();
        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, item => Assert.Equal(DuplicateCandidateKind.ExactSourceReimport, item.DuplicateCandidateKind));
        Assert.All(candidates, item => Assert.False(item.IsDuplicate));
        Assert.Equal(first.SourceFileSha256, second.SourceFileSha256);
    }

    [Theory]
    [InlineData("05/01/2026,Card Shop,-12.34\n")]
    [InlineData("05/01/2026,Card Shop,REF-1,-12.34,100.00\n")]
    public async Task HeaderlessStatements_AreRejectedWithoutConsumingTheFirstTransaction(string csv)
    {
        await using var db = await CreateFixtureAsync();
        var (companyId, periodId, bankId) = await KeysAsync(db);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            ImportAsync(db, companyId, periodId, bankId, csv, "headerless.csv"));

        Assert.Contains("headerless", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.ImportedTransactions.ToListAsync());
    }

    [Fact]
    public async Task IrishShortDate_IsParsedInDeclaredDayMonthOrderOnly()
    {
        await using var db = await CreateFixtureAsync();
        var (companyId, periodId, bankId) = await KeysAsync(db);

        await ImportAsync(db, companyId, periodId, bankId,
            "Date,Description,Amount\n1/2/2026,February payment,12.34\n", "short-date.csv");

        var transaction = await db.ImportedTransactions.SingleAsync();
        Assert.Equal(new DateOnly(2026, 2, 1), transaction.Date);
    }

    [Fact]
    public async Task DecimalCommaAndEuropeanThousands_AreParsedWithoutRemovingPunctuationBlindly()
    {
        await using var db = await CreateFixtureAsync();
        var (companyId, periodId, bankId) = await KeysAsync(db);
        const string csv = "Date,Description,Amount\n05/01/2026,Decimal comma,\"12,34\"\n06/01/2026,European thousands,\"1.234,56\"\n07/01/2026,Ambiguous separator,\"1,234\"\n";

        var result = await ImportAsync(db, companyId, periodId, bankId, csv, "decimal-formats.csv");

        Assert.Equal(2, result.ImportedRows);
        Assert.Contains(result.Warnings, warning => warning.Contains("Row 3", StringComparison.Ordinal));
        Assert.Equal(new[] { 12.34m, 1234.56m }, await db.ImportedTransactions.OrderBy(item => item.Date).Select(item => item.Amount).ToArrayAsync());
        var batch = await db.ImportBatches.SingleAsync();
        Assert.Contains("Row 3", batch.ImportWarningsJson);
    }

    [Fact]
    public async Task UnsupportedCurrencyPrecision_IsRejectedBeforeFingerprintAndPersistence()
    {
        await using var db = await CreateFixtureAsync();
        var (companyId, periodId, bankId) = await KeysAsync(db);

        var result = await ImportAsync(db, companyId, periodId, bankId,
            "Date,Description,Amount\n05/01/2026,Too precise,12.345\n", "precision.csv");

        Assert.Equal(0, result.ImportedRows);
        Assert.NotEmpty(result.Warnings);
        Assert.Empty(await db.ImportedTransactions.ToListAsync());
    }

    [Fact]
    public async Task DuplicateDecision_IsAuditedAndReversibleBeforeFinalisation()
    {
        await using var db = await CreateFixtureAsync();
        var (companyId, periodId, bankId) = await KeysAsync(db);
        const string csv = "Date,Description,Amount\n05/01/2026,Card Shop,-12.34\n";
        await ImportAsync(db, companyId, periodId, bankId, csv, "january.csv");
        await ImportAsync(db, companyId, periodId, bankId, csv, "january-copy.csv");
        var candidateId = await db.ImportedTransactions
            .Where(item => item.DuplicateReviewStatus == DuplicateReviewStatus.Pending)
            .Select(item => item.Id)
            .SingleAsync();
        var service = ReviewService(db);
        var actor = Reviewer();

        var discarded = await service.DecideAsync(
            companyId, periodId, candidateId, DuplicateReviewStatus.Discarded,
            "The retained file hash confirms this row is a statement re-import.",
            DuplicateReviewStatus.Pending, 0,
            actor, "request-discard");
        Assert.Equal(DuplicateReviewStatus.Discarded, discarded.Status);
        Assert.Equal(1, discarded.DecisionVersion);
        Assert.True((await db.ImportedTransactions.FindAsync(candidateId))!.IsDuplicate);

        var reopened = await service.DecideAsync(
            companyId, periodId, candidateId, DuplicateReviewStatus.Pending,
            "Reopened because the reviewer must compare the original bank statement.",
            DuplicateReviewStatus.Discarded, 1,
            actor, "request-reopen");
        Assert.Equal(DuplicateReviewStatus.Pending, reopened.Status);
        Assert.Equal(2, reopened.DecisionVersion);
        Assert.False((await db.ImportedTransactions.FindAsync(candidateId))!.IsDuplicate);

        var retained = await service.DecideAsync(
            companyId, periodId, candidateId, DuplicateReviewStatus.Retained,
            "The bank statement confirms this is a second genuine same-day payment.",
            DuplicateReviewStatus.Pending, 2,
            actor, "request-retain");
        Assert.Equal(DuplicateReviewStatus.Retained, retained.Status);
        Assert.Equal(3, retained.DecisionVersion);
        Assert.False((await db.ImportedTransactions.FindAsync(candidateId))!.IsDuplicate);

        var audits = await db.AuditLogs
            .Where(item => item.EntityType == "ImportedTransaction" && item.EntityId == candidateId)
            .OrderBy(item => item.Id)
            .ToListAsync();
        Assert.Equal(3, audits.Count);
        Assert.All(audits, item => Assert.Equal(AuditEventCodes.DuplicateReviewDecisionRecorded, item.Action));
        Assert.Equal(new[] { "request-discard", "request-reopen", "request-retain" }, audits.Select(item => item.RequestId));
    }

    [Fact]
    public async Task StaleDuplicateDecision_IsRejectedWithoutOverwritingNewerEvidence()
    {
        await using var db = await CreateFixtureAsync();
        var (companyId, periodId, bankId) = await KeysAsync(db);
        const string csv = "Date,Description,Amount\n05/01/2026,Card Shop,-12.34\n";
        await ImportAsync(db, companyId, periodId, bankId, csv, "january.csv");
        await ImportAsync(db, companyId, periodId, bankId, csv, "january-copy.csv");
        var candidateId = await db.ImportedTransactions
            .Where(item => item.DuplicateReviewStatus == DuplicateReviewStatus.Pending)
            .Select(item => item.Id)
            .SingleAsync();
        var service = ReviewService(db);

        await service.DecideAsync(
            companyId, periodId, candidateId, DuplicateReviewStatus.Retained,
            "The bank statement proves both rows are genuine transactions.",
            DuplicateReviewStatus.Pending, 0, Reviewer(), "first-decision");

        var error = await Assert.ThrowsAsync<AccountingConcurrencyException>(() => service.DecideAsync(
            companyId, periodId, candidateId, DuplicateReviewStatus.Discarded,
            "A stale browser tab attempted to replace the newer reviewer decision.",
            DuplicateReviewStatus.Pending, 0, Reviewer(), "stale-decision"));

        Assert.Contains("changed", error.Message, StringComparison.OrdinalIgnoreCase);
        var current = await db.ImportedTransactions.FindAsync(candidateId);
        Assert.Equal(DuplicateReviewStatus.Retained, current!.DuplicateReviewStatus);
        Assert.Equal(1, current.DuplicateDecisionVersion);
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), item => item.RequestId == "stale-decision");
    }

    [Fact]
    public async Task DuplicateReason_LengthUsesUnicodeCharactersLikePostgres()
    {
        await using var db = await CreateFixtureAsync();
        var (companyId, periodId, bankId) = await KeysAsync(db);
        const string csv = "Date,Description,Amount\n05/01/2026,Card Shop,-12.34\n";
        await ImportAsync(db, companyId, periodId, bankId, csv, "january.csv");
        await ImportAsync(db, companyId, periodId, bankId, csv, "january-copy.csv");
        var candidateId = await db.ImportedTransactions
            .Where(item => item.DuplicateReviewStatus == DuplicateReviewStatus.Pending)
            .Select(item => item.Id)
            .SingleAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => ReviewService(db).DecideAsync(
            companyId, periodId, candidateId, DuplicateReviewStatus.Retained,
            string.Concat(Enumerable.Repeat("😀", 10)),
            DuplicateReviewStatus.Pending, 0, Reviewer(), "unicode-reason"));

        Assert.Contains("20", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExactReimportBatch_DecidesAndReopensEveryRowAtomicallyWithManifestEvidence()
    {
        await using var db = await CreateFixtureAsync();
        var (companyId, periodId, bankId) = await KeysAsync(db);
        const string csv = "Date,Description,Amount\n05/01/2026,Card Shop,-12.34\n06/01/2026,Client Receipt,120.00\n";
        await ImportAsync(db, companyId, periodId, bankId, csv, "january.csv");
        var second = await ImportAsync(db, companyId, periodId, bankId, csv, "january-copy.csv");
        var service = ReviewService(db);
        var queue = await service.GetQueueAsync(companyId, periodId);
        var batch = Assert.Single(queue.ExactReimportBatches);
        Assert.Equal(second.ImportBatchId, batch.ImportBatchId);
        Assert.Equal(DuplicateReviewStatus.Pending, batch.CurrentStatus);
        Assert.Equal(2, batch.CandidateCount);

        var discarded = await service.DecideExactReimportBatchAsync(
            companyId, periodId, batch.ImportBatchId, DuplicateReviewStatus.Discarded,
            "The whole byte-identical statement is a repeated source-file import.",
            batch.CurrentStatus, batch.CandidateCount, batch.DecisionToken,
            Reviewer(), "batch-discard");
        Assert.Equal(2, discarded.UpdatedCount);
        Assert.Matches("^[0-9a-f]{64}$", discarded.RowEvidenceSha256);
        var rows = await db.ImportedTransactions
            .Where(item => item.ImportBatchId == batch.ImportBatchId)
            .OrderBy(item => item.Id)
            .ToListAsync();
        Assert.All(rows, item =>
        {
            Assert.Equal(DuplicateReviewStatus.Discarded, item.DuplicateReviewStatus);
            Assert.True(item.IsDuplicate);
            Assert.Equal(1, item.DuplicateDecisionVersion);
        });
        var audit = await db.AuditLogs.SingleAsync(item => item.RequestId == "batch-discard");
        Assert.Equal(AuditEventCodes.DuplicateReviewBatchDecisionRecorded, audit.Action);
        Assert.Contains("TransactionIds", audit.NewValueJson);
        Assert.All(rows, item => Assert.Contains(item.Id.ToString(), audit.NewValueJson));

        var resolvedQueue = await service.GetQueueAsync(companyId, periodId);
        var resolvedBatch = Assert.Single(resolvedQueue.ExactReimportBatches);
        Assert.Equal(DuplicateReviewStatus.Discarded, resolvedBatch.CurrentStatus);
        await service.DecideExactReimportBatchAsync(
            companyId, periodId, resolvedBatch.ImportBatchId, DuplicateReviewStatus.Pending,
            "Reopen the whole repeated statement so its treatment can be reconsidered.",
            resolvedBatch.CurrentStatus, resolvedBatch.CandidateCount, resolvedBatch.DecisionToken,
            Reviewer(), "batch-reopen");
        Assert.All(await db.ImportedTransactions.Where(item => item.ImportBatchId == batch.ImportBatchId).ToListAsync(), item =>
        {
            Assert.Equal(DuplicateReviewStatus.Pending, item.DuplicateReviewStatus);
            Assert.False(item.IsDuplicate);
            Assert.Equal(2, item.DuplicateDecisionVersion);
        });
    }

    [Fact]
    public async Task PendingCandidate_BlocksReadinessAndCannotBeSilentlyExcluded()
    {
        await using var db = await CreateFixtureAsync();
        var (companyId, periodId, bankId) = await KeysAsync(db);
        const string csv = "Date,Description,Amount\n05/01/2026,Card Shop,-12.34\n";
        await ImportAsync(db, companyId, periodId, bankId, csv, "january.csv");
        await ImportAsync(db, companyId, periodId, bankId, csv, "january-copy.csv");

        var readiness = await new FinancialStatementsService(db).GetReadinessScoreAsync(companyId, periodId);
        Assert.Contains(readiness.MissingItems, item => item.Contains("duplicate candidates", StringComparison.OrdinalIgnoreCase));

        var candidate = await db.ImportedTransactions
            .SingleAsync(item => item.DuplicateReviewStatus == DuplicateReviewStatus.Pending);
        candidate.IsDuplicate = true;
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => db.SaveChangesAsync());
        Assert.Contains("decision version", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FinalisedPeriod_RejectsFurtherDuplicateDecisionChanges()
    {
        await using var db = await CreateFixtureAsync();
        var (companyId, periodId, bankId) = await KeysAsync(db);
        const string csv = "Date,Description,Amount\n05/01/2026,Card Shop,-12.34\n";
        await ImportAsync(db, companyId, periodId, bankId, csv, "january.csv");
        await ImportAsync(db, companyId, periodId, bankId, csv, "january-copy.csv");
        var candidateId = await db.ImportedTransactions
            .Where(item => item.DuplicateReviewStatus == DuplicateReviewStatus.Pending)
            .Select(item => item.Id)
            .SingleAsync();
        var period = await db.AccountingPeriods.FindAsync(periodId);
        period!.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<AccountingConcurrencyException>(() => ReviewService(db).DecideAsync(
            companyId, periodId, candidateId, DuplicateReviewStatus.Retained,
            "Attempted decision after the accounting period had been finalised.",
            DuplicateReviewStatus.Pending, 0,
            Reviewer(), "request-locked"));

        Assert.Contains("Reopen", error.Message);
        Assert.Empty(await db.AuditLogs.Where(item => item.EntityId == candidateId).ToListAsync());
    }

    [Fact]
    public async Task LegacyLockedExclusion_IsVisibleAndRequiresANamedDecisionAfterReopen()
    {
        await using var db = await CreateFixtureAsync();
        var (companyId, periodId, bankId) = await KeysAsync(db);
        var legacy = new ImportedTransaction
        {
            BankAccountId = bankId,
            PeriodId = periodId,
            Date = new DateOnly(2026, 1, 5),
            Description = "Legacy excluded row",
            Amount = -12.34m,
            IsDuplicate = true,
            DuplicateReviewStatus = DuplicateReviewStatus.LegacyLockedUnverified,
            DuplicateCandidateKind = DuplicateCandidateKind.LegacyUnverified,
            DuplicateConfidence = 0m,
            DuplicateCandidateReasonsJson = "[\"Legacy exclusion requires explicit review.\"]"
        };
        db.ImportedTransactions.Add(legacy);
        await db.SaveChangesAsync();

        var queue = await ReviewService(db).GetQueueAsync(companyId, periodId);
        var candidate = Assert.Single(queue.Items);
        Assert.Equal(1, queue.PendingCount);
        Assert.Equal(DuplicateReviewStatus.LegacyLockedUnverified, candidate.Status);
        Assert.False(candidate.IncludedInLedger);

        var retained = await ReviewService(db).DecideAsync(
            companyId, periodId, legacy.Id, DuplicateReviewStatus.Retained,
            "The original bank statement proves this was a genuine separate payment.",
            DuplicateReviewStatus.LegacyLockedUnverified, 0,
            Reviewer(), "request-legacy-retain");
        Assert.True(retained.IncludedInLedger);
        Assert.Equal(DuplicateReviewStatus.Retained, retained.Status);
        Assert.Equal(1, retained.DecisionVersion);
    }

    [Fact]
    public async Task DuplicateReviewQueue_IsServerPaginatedForLargeStatementReimports()
    {
        await using var db = await CreateFixtureAsync();
        var (companyId, periodId, bankId) = await KeysAsync(db);
        const string csv = "Date,Description,Amount\n05/01/2026,Card Shop,-12.34\n";
        for (var index = 0; index < 12; index++)
            await ImportAsync(db, companyId, periodId, bankId, csv, $"january-{index}.csv");

        var queue = await ReviewService(db).GetQueueAsync(companyId, periodId, page: 2, pageSize: 10);

        Assert.Equal(11, queue.Total);
        Assert.Equal(11, queue.PendingCount);
        Assert.Equal(2, queue.Page);
        Assert.Equal(10, queue.PageSize);
        Assert.Equal(2, queue.TotalPages);
        Assert.Single(queue.Items);
    }

    private static DuplicateReviewService ReviewService(AccountsDbContext db) =>
        new(db, new AuditService(db), new AccountingConcurrencyCoordinator(db));

    private static AuthenticatedUser Reviewer() =>
        new(7, 1, "Practice", "reviewer@example.ie", "Qualified Reviewer", "Accountant");

    private static Task<ImportService.ImportResult> ImportAsync(
        AccountsDbContext db,
        int companyId,
        int periodId,
        int bankId,
        string csv,
        string filename) =>
        new ImportService(db, Options.Create(new ImportLimitConfig())).ImportCsvAsync(
            companyId,
            bankId,
            periodId,
            new MemoryStream(Encoding.UTF8.GetBytes(csv)),
            filename);

    private static async Task<(int CompanyId, int PeriodId, int BankId)> KeysAsync(AccountsDbContext db) =>
        (
            await db.Companies.Select(item => item.Id).SingleAsync(),
            await db.AccountingPeriods.Select(item => item.Id).SingleAsync(),
            await db.BankAccounts.Select(item => item.Id).SingleAsync()
        );

    private static async Task<AccountsDbContext> CreateFixtureAsync()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase($"duplicate-import-{Guid.NewGuid():N}")
            .Options;
        var db = new AccountsDbContext(options);
        var tenant = new Tenant { Name = "Practice", Slug = $"practice-{Guid.NewGuid():N}" };
        var company = new Company
        {
            Tenant = tenant,
            LegalName = "Duplicate Review Limited",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2025, 1, 1),
            FinancialYearStartMonth = 1,
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
        var bank = new BankAccount
        {
            Company = company,
            Name = "Current Account",
            Currency = "EUR",
            OpeningBalance = 0m
        };
        db.AddRange(tenant, company, period, bank);
        await db.SaveChangesAsync();
        return db;
    }
}
