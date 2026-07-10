using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public sealed record DuplicateReviewQueue(
    int PendingCount,
    int RetainedCount,
    int DiscardedCount,
    int Total,
    int Page,
    int PageSize,
    int TotalPages,
    int ExactReimportBatchTotal,
    int ExactReimportBatchPage,
    int ExactReimportBatchPageSize,
    int ExactReimportBatchTotalPages,
    IReadOnlyList<DuplicateExactReimportBatchReview> ExactReimportBatches,
    IReadOnlyList<DuplicateCandidateReview> Items);

public sealed record DuplicateExactReimportBatchReview(
    int ImportBatchId,
    int BankAccountId,
    string BankAccountName,
    string Currency,
    string SourceFilename,
    string SourceFileSha256,
    DateTime ImportedAtUtc,
    DuplicateReviewStatus CurrentStatus,
    int CandidateCount,
    string DecisionToken);

public sealed record DuplicateBatchDecisionResult(
    int ImportBatchId,
    DuplicateReviewStatus Decision,
    int UpdatedCount,
    string RowEvidenceSha256);

public sealed record DuplicateCandidateReview(
    int TransactionId,
    int BankAccountId,
    string BankAccountName,
    string Currency,
    int? ImportBatchId,
    string SourceFilename,
    string? SourceFileSha256,
    DateTime? SourceImportedAtUtc,
    int? SourceRowNumber,
    string? SourceRowSha256,
    DateOnly Date,
    string Description,
    decimal Amount,
    decimal? Balance,
    string? Reference,
    DuplicateReviewStatus Status,
    bool IncludedInLedger,
    DuplicateCandidateKind CandidateKind,
    decimal Confidence,
    IReadOnlyList<string> Reasons,
    int? MatchedTransactionId,
    int? MatchedBankAccountId,
    string? MatchedBankAccountName,
    string? MatchedCurrency,
    int? MatchedImportBatchId,
    string? MatchedSourceFilename,
    string? MatchedSourceFileSha256,
    DateTime? MatchedSourceImportedAtUtc,
    int? MatchedSourceRowNumber,
    string? MatchedSourceRowSha256,
    DateOnly? MatchedDate,
    string? MatchedDescription,
    decimal? MatchedAmount,
    decimal? MatchedBalance,
    string? MatchedReference,
    string? DecidedByDisplayName,
    DateTime? DecidedAtUtc,
    string? DecisionReason,
    int DecisionVersion,
    bool BatchDecisionAvailable);

public sealed class DuplicateReviewService(
    AccountsDbContext db,
    AuditService audit,
    AccountingConcurrencyCoordinator concurrency)
{
    public async Task<DuplicateReviewQueue> GetQueueAsync(
        int companyId,
        int periodId,
        int page = 1,
        int pageSize = 50,
        int batchPage = 1,
        int batchPageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) throw new BusinessRuleException("Duplicate review page must be at least 1.");
        if (pageSize is < 10 or > 100)
            throw new BusinessRuleException("Duplicate review page size must be between 10 and 100.");
        if (batchPage < 1) throw new BusinessRuleException("Exact re-import batch page must be at least 1.");
        if (batchPageSize is < 5 or > 25)
            throw new BusinessRuleException("Exact re-import batch page size must be between 5 and 25.");
        var periodExists = await db.AccountingPeriods
            .AsNoTracking()
            .AnyAsync(period => period.Id == periodId && period.CompanyId == companyId, cancellationToken);
        if (!periodExists) throw new ResourceNotFoundException($"Period {periodId} not found");

        var candidateQuery = db.ImportedTransactions
            .AsNoTracking()
            .Where(item => item.PeriodId == periodId
                && item.BankAccount.CompanyId == companyId
                && item.DuplicateReviewStatus != DuplicateReviewStatus.NotCandidate);
        var pendingCount = await candidateQuery.CountAsync(item =>
            item.DuplicateReviewStatus == DuplicateReviewStatus.Pending
            || item.DuplicateReviewStatus == DuplicateReviewStatus.LegacyLockedUnverified,
            cancellationToken);
        var retainedCount = await candidateQuery.CountAsync(item =>
            item.DuplicateReviewStatus == DuplicateReviewStatus.Retained,
            cancellationToken);
        var discardedCount = await candidateQuery.CountAsync(item =>
            item.DuplicateReviewStatus == DuplicateReviewStatus.Discarded,
            cancellationToken);
        var total = pendingCount + retainedCount + discardedCount;
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, totalPages);
        var candidates = await LoadCandidateRowsAsync(companyId, periodId, page, pageSize, cancellationToken);
        var items = await BuildDtosAsync(companyId, periodId, candidates, cancellationToken);
        var exactBatchPage = await LoadExactReimportBatchesAsync(
            companyId, periodId, batchPage, batchPageSize, cancellationToken);
        return new DuplicateReviewQueue(
            pendingCount,
            retainedCount,
            discardedCount,
            total,
            page,
            pageSize,
            totalPages,
            exactBatchPage.Total,
            exactBatchPage.Page,
            batchPageSize,
            exactBatchPage.TotalPages,
            exactBatchPage.Items,
            items);
    }

    public async Task<DuplicateBatchDecisionResult> DecideExactReimportBatchAsync(
        int companyId,
        int periodId,
        int importBatchId,
        DuplicateReviewStatus decision,
        string reason,
        DuplicateReviewStatus expectedStatus,
        int expectedCandidateCount,
        string expectedDecisionToken,
        AuthenticatedUser actor,
        string? requestId,
        CancellationToken cancellationToken = default)
    {
        var validTransition = expectedStatus == DuplicateReviewStatus.Pending
            ? decision is DuplicateReviewStatus.Retained or DuplicateReviewStatus.Discarded
            : expectedStatus is DuplicateReviewStatus.Retained or DuplicateReviewStatus.Discarded
                && decision == DuplicateReviewStatus.Pending;
        if (!validTransition)
            throw new BusinessRuleException("Resolve a pending exact re-import batch, or reopen a resolved batch before changing it.");
        var normalizedReason = reason?.Trim() ?? string.Empty;
        if (normalizedReason.EnumerateRunes().Count() is < 20 or > 1000)
            throw new BusinessRuleException("Duplicate decision reason must contain between 20 and 1,000 characters.");
        if (expectedCandidateCount < 1 || !IsSha256(expectedDecisionToken))
            throw new BusinessRuleException("The exact re-import batch decision token is invalid. Reload the review queue.");

        await using var lease = await concurrency.AcquirePeriodAsync(companyId, periodId, cancellationToken);
        var period = await db.AccountingPeriods
            .SingleOrDefaultAsync(item => item.Id == periodId && item.CompanyId == companyId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");
        if (period.Status is PeriodStatus.Finalised or PeriodStatus.Filed || period.LockedAt is not null)
            throw new AccountingConcurrencyException("Reopen the accounting period before changing duplicate review decisions.");

        IQueryable<ImportedTransaction> transactionQuery = db.ImportedTransactions
            .Include(item => item.ImportBatch)
            .Where(item => item.ImportBatchId == importBatchId
                && item.PeriodId == periodId
                && item.BankAccount.CompanyId == companyId);
        if (db.Database.IsRelational()) transactionQuery = transactionQuery.AsNoTracking();
        var transactions = await transactionQuery
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken);
        if (transactions.Count == 0)
            throw new ResourceNotFoundException($"Import batch {importBatchId} not found");

        var eligible = transactions.All(item =>
            item.DuplicateReviewStatus == expectedStatus
            && item.DuplicateCandidateKind == DuplicateCandidateKind.ExactSourceReimport
            && item.ImportBatch?.SourceFileSha256 is not null);
        var sourceFileSha256 = transactions[0].ImportBatch!.SourceFileSha256!;
        var transactionIds = transactions.Select(item => item.Id).ToArray();
        var currentToken = ComputeBatchDecisionToken(importBatchId, sourceFileSha256, transactions);
        var oldRowStateSha256 = ComputeAuditStateDigest(transactions);
        if (!eligible
            || transactions.Count != expectedCandidateCount
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(currentToken),
                Encoding.ASCII.GetBytes(expectedDecisionToken.ToLowerInvariant())))
        {
            throw new AccountingConcurrencyException(
                "The exact re-import batch changed after this review was loaded. Reload and reconcile before retrying.");
        }

        var actorUserId = AuthenticatedIdentity.AuditUserId(actor);
        var actorDisplayName = AuthenticatedIdentity.ReviewerDisplayName(actor);
        var decidedAtUtc = DateTime.UtcNow;
        foreach (var transaction in transactions)
        {
            transaction.DuplicateReviewStatus = decision;
            transaction.IsDuplicate = decision == DuplicateReviewStatus.Discarded;
            transaction.DuplicateDecisionVersion++;
            transaction.DuplicateDecisionByUserId = actorUserId;
            transaction.DuplicateDecisionByDisplayName = actorDisplayName;
            transaction.DuplicateDecisionAtUtc = decidedAtUtc;
            transaction.DuplicateDecisionReason = normalizedReason;
        }

        if (db.Database.IsRelational())
        {
            var updated = await db.ImportedTransactions
                .Where(item => item.ImportBatchId == importBatchId
                    && item.PeriodId == periodId
                    && item.BankAccount.CompanyId == companyId
                    && item.DuplicateReviewStatus == expectedStatus
                    && item.DuplicateCandidateKind == DuplicateCandidateKind.ExactSourceReimport)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.DuplicateReviewStatus, decision)
                    .SetProperty(item => item.IsDuplicate, decision == DuplicateReviewStatus.Discarded)
                    .SetProperty(item => item.DuplicateDecisionVersion, item => item.DuplicateDecisionVersion + 1)
                    .SetProperty(item => item.DuplicateDecisionByUserId, actorUserId)
                    .SetProperty(item => item.DuplicateDecisionByDisplayName, actorDisplayName)
                    .SetProperty(item => item.DuplicateDecisionAtUtc, decidedAtUtc)
                    .SetProperty(item => item.DuplicateDecisionReason, normalizedReason),
                    cancellationToken);
            if (updated != expectedCandidateCount)
                throw new AccountingConcurrencyException(
                    "The exact re-import batch changed while the decision was being recorded. Reload and reconcile before retrying.");
        }

        var rowEvidenceSha256 = ComputeRowEvidenceDigest(transactions, decision, actorUserId, decidedAtUtc, normalizedReason);
        await audit.LogAsync(
            companyId,
            periodId,
            "ImportBatch",
            importBatchId,
            AuditEventCodes.DuplicateReviewBatchDecisionRecorded,
            new
            {
                Status = expectedStatus,
                CandidateCount = transactions.Count,
                TransactionIds = transactionIds,
                RowStateSha256 = oldRowStateSha256,
                ExpectedDecisionToken = currentToken
            },
            new
            {
                Status = decision,
                CandidateCount = transactions.Count,
                TransactionIds = transactionIds,
                RowEvidenceSha256 = rowEvidenceSha256,
                DecisionVersionMin = transactions.Min(item => item.DuplicateDecisionVersion),
                DecisionVersionMax = transactions.Max(item => item.DuplicateDecisionVersion),
                DecisionReason = normalizedReason,
                DecisionByUserId = actorUserId,
                DecisionByDisplayName = actorDisplayName,
                DecisionAtUtc = decidedAtUtc
            },
            actorUserId,
            requestId: requestId,
            actorDisplayName: actorDisplayName,
            cancellationToken: cancellationToken);

        await lease.CommitIfOwnedAsync(cancellationToken);
        return new DuplicateBatchDecisionResult(importBatchId, decision, transactions.Count, rowEvidenceSha256);
    }

    public async Task<DuplicateCandidateReview> DecideAsync(
        int companyId,
        int periodId,
        int transactionId,
        DuplicateReviewStatus decision,
        string reason,
        DuplicateReviewStatus expectedStatus,
        int expectedDecisionVersion,
        AuthenticatedUser actor,
        string? requestId,
        CancellationToken cancellationToken = default)
    {
        if (decision is not (DuplicateReviewStatus.Pending or DuplicateReviewStatus.Retained or DuplicateReviewStatus.Discarded))
            throw new BusinessRuleException("Duplicate review decision must be Pending, Retained, or Discarded.");
        var normalizedReason = reason?.Trim() ?? string.Empty;
        if (normalizedReason.EnumerateRunes().Count() is < 20 or > 1000)
            throw new BusinessRuleException("Duplicate decision reason must contain between 20 and 1,000 characters.");

        await using var lease = await concurrency.AcquirePeriodAsync(companyId, periodId, cancellationToken);
        var period = await db.AccountingPeriods
            .SingleOrDefaultAsync(item => item.Id == periodId && item.CompanyId == companyId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");
        if (period.Status is PeriodStatus.Finalised or PeriodStatus.Filed || period.LockedAt is not null)
            throw new AccountingConcurrencyException("Reopen the accounting period before changing a duplicate review decision.");

        var transaction = await db.ImportedTransactions
            .Include(item => item.ImportBatch)
            .Include(item => item.BankAccount)
            .SingleOrDefaultAsync(item =>
                item.Id == transactionId
                && item.PeriodId == periodId
                && item.BankAccount.CompanyId == companyId,
                cancellationToken)
            ?? throw new ResourceNotFoundException($"Imported transaction {transactionId} not found");
        if (transaction.DuplicateReviewStatus == DuplicateReviewStatus.NotCandidate)
            throw new BusinessRuleException("The imported transaction is not a duplicate review candidate.");
        if (transaction.DuplicateReviewStatus != expectedStatus
            || transaction.DuplicateDecisionVersion != expectedDecisionVersion)
        {
            throw new AccountingConcurrencyException(
                "The duplicate decision changed after this review was loaded. Reload and reconcile before retrying.");
        }
        if (transaction.DuplicateReviewStatus == decision)
            throw new BusinessRuleException($"The duplicate review candidate is already {decision}.");
        var currentUnresolved = transaction.DuplicateReviewStatus is
            DuplicateReviewStatus.Pending or DuplicateReviewStatus.LegacyLockedUnverified;
        if (currentUnresolved && decision == DuplicateReviewStatus.Pending
            || !currentUnresolved && decision != DuplicateReviewStatus.Pending)
        {
            throw new BusinessRuleException(
                "Resolve a pending candidate with retain/discard, or reopen a resolved decision before changing it.");
        }

        var oldValue = new
        {
            Status = transaction.DuplicateReviewStatus,
            transaction.IsDuplicate,
            transaction.DuplicateDecisionVersion,
            transaction.DuplicateDecisionByUserId,
            transaction.DuplicateDecisionByDisplayName,
            transaction.DuplicateDecisionAtUtc,
            transaction.DuplicateDecisionReason
        };
        var decidedAtUtc = DateTime.UtcNow;
        transaction.DuplicateReviewStatus = decision;
        transaction.IsDuplicate = decision == DuplicateReviewStatus.Discarded;
        transaction.DuplicateDecisionVersion++;
        transaction.DuplicateDecisionByUserId = AuthenticatedIdentity.AuditUserId(actor);
        transaction.DuplicateDecisionByDisplayName = AuthenticatedIdentity.ReviewerDisplayName(actor);
        transaction.DuplicateDecisionAtUtc = decidedAtUtc;
        transaction.DuplicateDecisionReason = normalizedReason;

        await audit.LogAsync(
            companyId,
            periodId,
            "ImportedTransaction",
            transaction.Id,
            AuditEventCodes.DuplicateReviewDecisionRecorded,
            oldValue,
            new
            {
                Status = transaction.DuplicateReviewStatus,
                transaction.IsDuplicate,
                transaction.DuplicateDecisionVersion,
                transaction.DuplicateDecisionByUserId,
                transaction.DuplicateDecisionByDisplayName,
                transaction.DuplicateDecisionAtUtc,
                transaction.DuplicateDecisionReason,
                transaction.DuplicateCandidateKind,
                transaction.DuplicateConfidence,
                transaction.DuplicateMatchedTransactionId,
                transaction.DuplicateMatchedSourceRowSha256
            },
            AuthenticatedIdentity.AuditUserId(actor),
            requestId: requestId,
            actorDisplayName: AuthenticatedIdentity.ReviewerDisplayName(actor),
            cancellationToken: cancellationToken);

        var item = (await BuildDtosAsync(companyId, periodId, [transaction], cancellationToken)).Single();
        await lease.CommitIfOwnedAsync(cancellationToken);
        return item;
    }

    private async Task<List<ImportedTransaction>> LoadCandidateRowsAsync(
        int companyId,
        int periodId,
        int page,
        int pageSize,
        CancellationToken cancellationToken) =>
        await db.ImportedTransactions
            .AsNoTracking()
            .Include(item => item.ImportBatch)
            .Include(item => item.BankAccount)
            .Where(item => item.PeriodId == periodId
                && item.BankAccount.CompanyId == companyId
                && item.DuplicateReviewStatus != DuplicateReviewStatus.NotCandidate)
            .OrderBy(item => item.DuplicateReviewStatus == DuplicateReviewStatus.Pending
                || item.DuplicateReviewStatus == DuplicateReviewStatus.LegacyLockedUnverified ? 0 : 1)
            .ThenByDescending(item => item.Date)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<DuplicateCandidateReview>> BuildDtosAsync(
        int companyId,
        int periodId,
        IReadOnlyCollection<ImportedTransaction> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0) return [];

        var matchedIds = candidates
            .Select(item => item.DuplicateMatchedTransactionId)
            .OfType<int>()
            .Distinct()
            .ToArray();
        var matchedHashes = candidates
            .Select(item => item.DuplicateMatchedSourceRowSha256)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var matches = matchedIds.Length == 0 && matchedHashes.Length == 0
            ? []
            : await db.ImportedTransactions
                .AsNoTracking()
                .Include(item => item.ImportBatch)
                .Include(item => item.BankAccount)
                .Where(item => item.PeriodId == periodId
                    && item.BankAccount.CompanyId == companyId
                    && (matchedIds.Contains(item.Id)
                        || item.SourceRowSha256 != null && matchedHashes.Contains(item.SourceRowSha256)))
                .ToListAsync(cancellationToken);
        var matchById = matches.ToDictionary(item => item.Id);
        var candidateBatchIds = candidates
            .Select(item => item.ImportBatchId)
            .OfType<int>()
            .Distinct()
            .ToArray();
        var eligibleBatchIds = candidateBatchIds.Length == 0
            ? []
            : await db.ImportedTransactions
                .AsNoTracking()
                .Where(item => item.ImportBatchId != null && candidateBatchIds.Contains(item.ImportBatchId.Value))
                .GroupBy(item => item.ImportBatchId!.Value)
                .Select(group => new
                {
                    ImportBatchId = group.Key,
                    Total = group.Count(),
                    Pending = group.Count(item => item.DuplicateCandidateKind == DuplicateCandidateKind.ExactSourceReimport && item.DuplicateReviewStatus == DuplicateReviewStatus.Pending),
                    Retained = group.Count(item => item.DuplicateCandidateKind == DuplicateCandidateKind.ExactSourceReimport && item.DuplicateReviewStatus == DuplicateReviewStatus.Retained),
                    Discarded = group.Count(item => item.DuplicateCandidateKind == DuplicateCandidateKind.ExactSourceReimport && item.DuplicateReviewStatus == DuplicateReviewStatus.Discarded)
                })
                .Where(group => group.Total == group.Pending || group.Total == group.Retained || group.Total == group.Discarded)
                .Select(group => group.ImportBatchId)
                .ToListAsync(cancellationToken);
        var eligibleBatchIdSet = eligibleBatchIds.ToHashSet();

        return candidates.Select(candidate =>
        {
            ImportedTransaction? match = null;
            if (candidate.DuplicateMatchedTransactionId is { } matchedId)
                matchById.TryGetValue(matchedId, out match);
            if (match is null && candidate.DuplicateMatchedSourceRowSha256 is { } matchedHash)
            {
                match = matches
                    .Where(item => item.Id != candidate.Id
                        && item.BankAccountId == candidate.BankAccountId
                        && string.Equals(item.SourceRowSha256, matchedHash, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(item => item.Id)
                    .FirstOrDefault();
            }

            return new DuplicateCandidateReview(
                candidate.Id,
                candidate.BankAccountId,
                candidate.BankAccount.Name,
                candidate.BankAccount.Currency,
                candidate.ImportBatchId,
                candidate.ImportBatch?.Filename ?? "Legacy import (filename unavailable)",
                candidate.ImportBatch?.SourceFileSha256,
                candidate.ImportBatch?.ImportedAt,
                candidate.SourceRowNumber,
                candidate.SourceRowSha256,
                candidate.Date,
                candidate.Description,
                candidate.Amount,
                candidate.Balance,
                candidate.Reference,
                candidate.DuplicateReviewStatus,
                !candidate.IsDuplicate,
                candidate.DuplicateCandidateKind ?? DuplicateCandidateKind.LegacyUnverified,
                candidate.DuplicateConfidence ?? 0m,
                ParseReasons(candidate.DuplicateCandidateReasonsJson),
                match?.Id ?? candidate.DuplicateMatchedTransactionId,
                match?.BankAccountId,
                match?.BankAccount.Name,
                match?.BankAccount.Currency,
                match?.ImportBatchId,
                match?.ImportBatch?.Filename,
                match?.ImportBatch?.SourceFileSha256,
                match?.ImportBatch?.ImportedAt,
                match?.SourceRowNumber,
                match?.SourceRowSha256 ?? candidate.DuplicateMatchedSourceRowSha256,
                match?.Date,
                match?.Description,
                match?.Amount,
                match?.Balance,
                match?.Reference,
                candidate.DuplicateDecisionByDisplayName,
                candidate.DuplicateDecisionAtUtc,
                candidate.DuplicateDecisionReason,
                candidate.DuplicateDecisionVersion,
                candidate.ImportBatchId is { } batchId && eligibleBatchIdSet.Contains(batchId));
        }).ToArray();
    }

    private async Task<ExactReimportBatchPage> LoadExactReimportBatchesAsync(
        int companyId,
        int periodId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var eligibleQuery = db.ImportedTransactions
            .AsNoTracking()
            .Where(item => item.PeriodId == periodId
                && item.BankAccount.CompanyId == companyId
                && item.ImportBatchId != null
                && item.ImportBatch!.SourceFileSha256 != null)
            .GroupBy(item => new
            {
                ImportBatchId = item.ImportBatchId!.Value,
                item.BankAccountId,
                BankAccountName = item.BankAccount.Name,
                item.BankAccount.Currency,
                SourceFilename = item.ImportBatch!.Filename,
                SourceFileSha256 = item.ImportBatch.SourceFileSha256!,
                ImportedAtUtc = item.ImportBatch.ImportedAt
            })
            .Select(group => new
            {
                group.Key,
                TotalCount = group.Count(),
                PendingExactCount = group.Count(item =>
                    item.DuplicateReviewStatus == DuplicateReviewStatus.Pending
                    && item.DuplicateCandidateKind == DuplicateCandidateKind.ExactSourceReimport),
                RetainedExactCount = group.Count(item =>
                    item.DuplicateReviewStatus == DuplicateReviewStatus.Retained
                    && item.DuplicateCandidateKind == DuplicateCandidateKind.ExactSourceReimport),
                DiscardedExactCount = group.Count(item =>
                    item.DuplicateReviewStatus == DuplicateReviewStatus.Discarded
                    && item.DuplicateCandidateKind == DuplicateCandidateKind.ExactSourceReimport)
            })
            .Where(group => group.TotalCount > 0
                && (group.TotalCount == group.PendingExactCount
                    || group.TotalCount == group.RetainedExactCount
                    || group.TotalCount == group.DiscardedExactCount));

        var total = await eligibleQuery.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, totalPages);
        var groups = await eligibleQuery
            .OrderBy(group => group.TotalCount == group.PendingExactCount ? 0 : 1)
            .ThenByDescending(group => group.Key.ImportedAtUtc)
            .ThenByDescending(group => group.Key.ImportBatchId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var batchIds = groups.Select(group => group.Key.ImportBatchId).ToArray();
        var tokenRows = batchIds.Length == 0
            ? []
            : await db.ImportedTransactions
                .AsNoTracking()
                .Where(item => item.ImportBatchId != null && batchIds.Contains(item.ImportBatchId.Value))
                .OrderBy(item => item.ImportBatchId)
                .ThenBy(item => item.Id)
                .Select(item => new BatchTokenRow(
                    item.ImportBatchId!.Value,
                    item.Id,
                    item.SourceRowSha256,
                    item.DuplicateReviewStatus,
                    item.DuplicateDecisionVersion))
                .ToListAsync(cancellationToken);
        var rowsByBatch = tokenRows.ToLookup(item => item.ImportBatchId);

        var items = groups.Select(group =>
        {
            var currentStatus = group.TotalCount == group.PendingExactCount
                ? DuplicateReviewStatus.Pending
                : group.TotalCount == group.RetainedExactCount
                    ? DuplicateReviewStatus.Retained
                    : DuplicateReviewStatus.Discarded;
            return new DuplicateExactReimportBatchReview(
            group.Key.ImportBatchId,
            group.Key.BankAccountId,
            group.Key.BankAccountName,
            group.Key.Currency,
            group.Key.SourceFilename,
            group.Key.SourceFileSha256,
            group.Key.ImportedAtUtc,
            currentStatus,
            group.TotalCount,
            ComputeBatchDecisionToken(
                group.Key.ImportBatchId,
                group.Key.SourceFileSha256,
                rowsByBatch[group.Key.ImportBatchId]));
        })
            .ToArray();
        return new ExactReimportBatchPage(total, page, totalPages, items);
    }

    private static string ComputeBatchDecisionToken(
        int importBatchId,
        string sourceFileSha256,
        IEnumerable<ImportedTransaction> transactions) =>
        ComputeBatchDecisionToken(importBatchId, sourceFileSha256, transactions.Select(item =>
            new BatchTokenRow(
                importBatchId,
                item.Id,
                item.SourceRowSha256,
                item.DuplicateReviewStatus,
                item.DuplicateDecisionVersion)));

    private static string ComputeBatchDecisionToken(
        int importBatchId,
        string sourceFileSha256,
        IEnumerable<BatchTokenRow> rows)
    {
        var canonical = new StringBuilder($"{importBatchId}|{sourceFileSha256.ToLowerInvariant()}");
        foreach (var row in rows.OrderBy(item => item.TransactionId))
            canonical.Append('\n').Append(row.TransactionId).Append('|')
                .Append(row.SourceRowSha256).Append('|')
                .Append(row.Status).Append('|')
                .Append(row.DecisionVersion);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static string ComputeRowEvidenceDigest(
        IEnumerable<ImportedTransaction> transactions,
        DuplicateReviewStatus decision,
        string actorUserId,
        DateTime decidedAtUtc,
        string reason)
    {
        var canonical = string.Join('\n', transactions.OrderBy(item => item.Id).Select(item =>
            $"{item.Id}|{item.SourceRowSha256}|{item.DuplicateDecisionVersion}|{decision}|{actorUserId}|{decidedAtUtc:O}|{reason}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string ComputeAuditStateDigest(IEnumerable<ImportedTransaction> transactions)
    {
        var canonical = string.Join('\n', transactions.OrderBy(item => item.Id).Select(item =>
            $"{item.Id}|{item.SourceRowSha256}|{item.DuplicateReviewStatus}|{item.DuplicateDecisionVersion}|{item.DuplicateDecisionByUserId}|{item.DuplicateDecisionByDisplayName}|{item.DuplicateDecisionAtUtc:O}|{item.DuplicateDecisionReason}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool IsSha256(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private sealed record ExactReimportBatchPage(
        int Total,
        int Page,
        int TotalPages,
        IReadOnlyList<DuplicateExactReimportBatchReview> Items);

    private sealed record BatchTokenRow(
        int ImportBatchId,
        int TransactionId,
        string? SourceRowSha256,
        DuplicateReviewStatus Status,
        int DecisionVersion);

    private static IReadOnlyList<string> ParseReasons(string? json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json ?? string.Empty)
                ?.Where(reason => !string.IsNullOrWhiteSpace(reason))
                .ToArray()
                ?? ["Duplicate candidate evidence is unavailable and requires manual review."];
        }
        catch (JsonException)
        {
            return ["Duplicate candidate evidence is unavailable and requires manual review."];
        }
    }
}
