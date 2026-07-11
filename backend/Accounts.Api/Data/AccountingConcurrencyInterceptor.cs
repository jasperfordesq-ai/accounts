using System.Data;
using System.Data.Common;
using System.Globalization;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Accounts.Api.Data;

/// <summary>
/// Last-line serialization for accounting/workflow persistence. Period advisory locks are acquired
/// immediately before EF emits a write, the period lock is re-read with FOR UPDATE in the same
/// transaction, and original scalar values are compared with the current row to reject lost updates.
/// </summary>
public sealed class AccountingConcurrencyInterceptor : SaveChangesInterceptor
{
    private static readonly HashSet<Type> ProtectedTypes =
    [
        typeof(Company), typeof(CompanyOfficer), typeof(AccountingPeriod),
        typeof(SizeClassification), typeof(FilingRegime), typeof(CroFilingPackage),
        typeof(RevenueFilingPackage), typeof(CharityFilingPackage), typeof(BankAccount),
        typeof(ImportBatch), typeof(ImportedTransaction), typeof(TransactionRule),
        typeof(AccountCategory), typeof(Debtor), typeof(Creditor), typeof(FixedAsset),
        typeof(DepreciationEntry), typeof(CapitalAllowanceClaim), typeof(Inventory),
        typeof(Loan), typeof(LoanBalanceSnapshot), typeof(DirectorLoan),
        typeof(DirectorLoanMovement), typeof(PayrollSummary), typeof(CorporationTaxScopeReview),
        typeof(CorporationTaxLossRecord), typeof(CorporationTaxFilingSupportReview),
        typeof(CorporationTaxPaymentRecord),
        typeof(TaxBalance), typeof(Dividend), typeof(OpeningBalance),
        typeof(YearEndReviewConfirmation), typeof(Adjustment), typeof(Report),
        typeof(NotesDisclosure), typeof(ShareCapital), typeof(FilingDeadline),
        typeof(FilingHistory), typeof(PostBalanceSheetEvent), typeof(RelatedPartyTransaction),
        typeof(ContingentLiability), typeof(CharityInfo), typeof(FundBalance)
    ];

    private static readonly HashSet<Type> LockedPeriodWorkflowTypes =
    [
        typeof(CroFilingPackage), typeof(RevenueFilingPackage), typeof(CharityFilingPackage),
        typeof(FilingDeadline), typeof(FilingHistory), typeof(Report)
    ];

    private static readonly HashSet<string> PeriodWorkflowProperties = new(StringComparer.Ordinal)
    {
        nameof(AccountingPeriod.Status),
        nameof(AccountingPeriod.LockedAt),
        nameof(AccountingPeriod.LockedBy),
        nameof(AccountingPeriod.ReopenedAt),
        nameof(AccountingPeriod.ReopenedBy),
        nameof(AccountingPeriod.ReopenReason),
        nameof(AccountingPeriod.ClosingRetainedEarnings),
        nameof(AccountingPeriod.ApprovalDate)
    };

    private static readonly HashSet<string> CompanyQuarantineWorkflowProperties = new(StringComparer.Ordinal)
    {
        nameof(Company.IsQuarantined),
        nameof(Company.QuarantinedAtUtc),
        nameof(Company.QuarantinedByUserId),
        nameof(Company.QuarantinedByDisplayName),
        nameof(Company.QuarantineReason),
        nameof(Company.QuarantineEvidenceSha256),
        nameof(Company.UpdatedAt)
    };

    private IDbContextTransaction? _ownedTransaction;

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        PrepareAsync(eventData.Context, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await PrepareAsync(eventData.Context, cancellationToken);
        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        CompleteOwnedTransactionAsync(commit: true, CancellationToken.None)
            .ConfigureAwait(false).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await CompleteOwnedTransactionAsync(commit: true, cancellationToken);
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        CompleteOwnedTransactionAsync(commit: false, CancellationToken.None)
            .ConfigureAwait(false).GetAwaiter().GetResult();
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default) =>
        CompleteOwnedTransactionAsync(commit: false, cancellationToken);

    private async Task PrepareAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is not AccountsDbContext db || !db.Database.IsRelational()) return;

        var entries = db.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(entry => ProtectedTypes.Contains(entry.Metadata.ClrType))
            .ToArray();
        if (entries.Length == 0) return;

        try
        {
            if (db.Database.CurrentTransaction is null)
                _ownedTransaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

            var initialScopes = await ResolveInitialScopesAsync(db, entries, cancellationToken);
            var companyIds = initialScopes.SelectMany(scope => scope.CompanyIds).Distinct().Order().ToArray();
            foreach (var companyId in companyIds)
                await AccountingConcurrencyCoordinator.AcquireCompanyAdvisoryLockAsync(db, companyId, cancellationToken);

            var companyStates = new Dictionary<int, bool>();
            foreach (var companyId in companyIds)
                companyStates[companyId] = await ReadCompanyQuarantineStateAsync(db, companyId, cancellationToken);

            // Company-scoped records must be expanded only after the company lock is held, otherwise
            // period creation could race the initial inventory and leave a new period unlocked.
            var scopes = await ExpandCompanyPeriodScopesAsync(db, initialScopes, cancellationToken);
            var periodIds = scopes.SelectMany(scope => scope.PeriodIds).Distinct().Order().ToArray();
            foreach (var periodId in periodIds)
                await AccountingConcurrencyCoordinator.AcquireAdvisoryLockAsync(db, periodId, cancellationToken);

            var periodStates = new Dictionary<int, PeriodDatabaseState>();
            foreach (var periodId in periodIds)
            {
                var state = await ReadPeriodForUpdateAsync(db, periodId, cancellationToken);
                if (state is not null) periodStates[periodId] = state;
            }

            foreach (var scope in scopes)
            {
                var scopedStates = scope.PeriodIds
                    .Where(periodStates.ContainsKey)
                    .Select(periodId => periodStates[periodId])
                    .ToArray();
                var quarantineTransition = IsCompanyQuarantineTransition(scope.Entry);
                if (scope.CompanyIds.Any(companyId =>
                        companyStates.TryGetValue(companyId, out var quarantined) && quarantined)
                    && !quarantineTransition)
                {
                    throw await ConflictAsync(
                        db,
                        scope.PeriodIds,
                        periodStates,
                        cancellationToken,
                        AccountingConflict.QuarantinedSafeMessage);
                }

                if (!quarantineTransition
                    && !IsLockedPeriodWorkflow(scope.Entry)
                    && scopedStates.Any(state => state.IsLocked))
                {
                    throw await ConflictAsync(db, scope.PeriodIds, periodStates, cancellationToken);
                }
            }

            foreach (var entry in entries.Where(entry => entry.State is EntityState.Modified or EntityState.Deleted))
            {
                if (!await OriginalValuesStillCurrentAsync(db, entry, cancellationToken))
                {
                    var scope = scopes.First(candidate => ReferenceEquals(candidate.Entry, entry));
                    throw await ConflictAsync(db, scope.PeriodIds, periodStates, cancellationToken);
                }
            }

            // A no-op row update advances PostgreSQL xmin. The period-wide ETag therefore changes
            // for every protected accounting/workflow write, including child rows whose own
            // endpoint representation does not expose a version column.
            foreach (var periodId in periodIds)
                await TouchPeriodVersionAsync(db, periodId, cancellationToken);
        }
        catch
        {
            await CompleteOwnedTransactionAsync(commit: false, CancellationToken.None);
            throw;
        }
    }

    private static async Task<AccountingConcurrencyException> ConflictAsync(
        AccountsDbContext db,
        IReadOnlyCollection<int> periodIds,
        IReadOnlyDictionary<int, PeriodDatabaseState> periodStates,
        CancellationToken cancellationToken,
        string message = AccountingConflict.SafeMessage)
    {
        string? etag = null;
        var firstPeriodId = periodIds.Order().FirstOrDefault();
        if (firstPeriodId > 0 && periodStates.TryGetValue(firstPeriodId, out var periodState))
        {
            etag = await new PeriodConcurrencyTokenService(db)
                .GetAsync(periodState.CompanyId, firstPeriodId, cancellationToken);
        }
        return new AccountingConcurrencyException(message, etag);
    }

    private static bool IsLockedPeriodWorkflow(EntityEntry entry)
    {
        if (LockedPeriodWorkflowTypes.Contains(entry.Metadata.ClrType)) return true;
        if (entry.Entity is not AccountingPeriod || entry.State != EntityState.Modified) return false;

        var changed = entry.Properties
            .Where(property => property.IsModified)
            .Select(property => property.Metadata.Name)
            .ToArray();
        return changed.Length > 0 && changed.All(PeriodWorkflowProperties.Contains);
    }

    private static bool IsCompanyQuarantineTransition(EntityEntry entry)
    {
        if (entry.Entity is not Company || entry.State != EntityState.Modified) return false;
        var changed = entry.Properties
            .Where(property => property.IsModified)
            .Select(property => property.Metadata.Name)
            .ToArray();
        return changed.Length > 0 && changed.All(CompanyQuarantineWorkflowProperties.Contains);
    }

    private static async Task<IReadOnlyList<InitialEntryScope>> ResolveInitialScopesAsync(
        AccountsDbContext db,
        IReadOnlyList<EntityEntry> entries,
        CancellationToken cancellationToken)
    {
        var directPeriodIds = new Dictionary<EntityEntry, HashSet<int>>();
        var companyIds = new Dictionary<EntityEntry, HashSet<int>>();
        foreach (var entry in entries)
        {
            directPeriodIds[entry] = [];
            companyIds[entry] = [];

            if (entry.Entity is AccountingPeriod period && period.Id > 0)
                directPeriodIds[entry].Add(period.Id);
            AddIntValue(entry, "PeriodId", directPeriodIds[entry]);

            if (entry.Entity is Company company && company.Id > 0)
                companyIds[entry].Add(company.Id);
            else
                AddIntValue(entry, "CompanyId", companyIds[entry]);

            if (entry.Entity is ImportBatch batch)
            {
                var bankCompany = await ResolveBankCompanyAsync(db, batch.BankAccountId, cancellationToken);
                if (bankCompany is > 0) companyIds[entry].Add(bankCompany.Value);
            }
            else if (entry.Entity is ImportedTransaction transaction
                     && directPeriodIds[entry].Count == 0)
            {
                var bankCompany = await ResolveBankCompanyAsync(db, transaction.BankAccountId, cancellationToken);
                if (bankCompany is > 0) companyIds[entry].Add(bankCompany.Value);
            }
            else if (entry.Entity is DirectorLoanMovement movement
                     && directPeriodIds[entry].Count == 0)
            {
                if (movement.DirectorLoan is { PeriodId: > 0 } trackedLoan)
                    directPeriodIds[entry].Add(trackedLoan.PeriodId);
                else
                {
                    var movementPeriodId = await ResolveDirectorLoanPeriodAsync(
                        db,
                        movement.DirectorLoanId,
                        cancellationToken);
                    if (movementPeriodId is > 0) directPeriodIds[entry].Add(movementPeriodId.Value);
                }
            }
        }

        var allDirectPeriodIds = directPeriodIds.Values.SelectMany(value => value).Distinct().ToArray();
        if (allDirectPeriodIds.Length > 0)
        {
            var companyByPeriod = await db.AccountingPeriods
                .IgnoreQueryFilters()
                .Where(period => allDirectPeriodIds.Contains(period.Id))
                .Select(period => new { period.Id, period.CompanyId })
                .ToDictionaryAsync(period => period.Id, period => period.CompanyId, cancellationToken);
            foreach (var entry in entries)
            {
                foreach (var periodId in directPeriodIds[entry])
                {
                    if (companyByPeriod.TryGetValue(periodId, out var companyId))
                        companyIds[entry].Add(companyId);
                }
            }
        }

        return entries.Select(entry => new InitialEntryScope(
            entry,
            directPeriodIds[entry].Order().ToArray(),
            companyIds[entry].Order().ToArray(),
            ExpandCompanyPeriods: entry.Entity is not (AccountingPeriod or ImportBatch or ImportedTransaction or TransactionRule or FilingHistory)
                && directPeriodIds[entry].Count == 0,
            AffectedFromDate: CompanyAccountingAffectedFromDate(entry))).ToArray();
    }

    private static async Task<IReadOnlyList<EntryScope>> ExpandCompanyPeriodScopesAsync(
        AccountsDbContext db,
        IReadOnlyList<InitialEntryScope> scopes,
        CancellationToken cancellationToken)
    {
        var expansionCompanyIds = scopes
            .Where(scope => scope.ExpandCompanyPeriods)
            .SelectMany(scope => scope.CompanyIds)
            .Distinct()
            .ToArray();
        var periodsByCompany = expansionCompanyIds.Length == 0
            ? new Dictionary<int, CompanyPeriodScope[]>()
            : (await db.AccountingPeriods
                .IgnoreQueryFilters()
                .Where(period => expansionCompanyIds.Contains(period.CompanyId))
                .Select(period => new CompanyPeriodScope(period.CompanyId, period.Id, period.PeriodEnd))
                .ToListAsync(cancellationToken))
                .GroupBy(period => period.CompanyId)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderBy(period => period.Id).ToArray());

        return scopes.Select(scope =>
        {
            var periodIds = scope.DirectPeriodIds.ToHashSet();
            if (scope.ExpandCompanyPeriods)
            {
                foreach (var companyId in scope.CompanyIds)
                {
                    if (periodsByCompany.TryGetValue(companyId, out var companyPeriods))
                    {
                        periodIds.UnionWith(companyPeriods
                            .Where(period => scope.AffectedFromDate is null
                                || period.PeriodEnd >= scope.AffectedFromDate.Value)
                            .Select(period => period.Id));
                    }
                }
            }
            return new EntryScope(scope.Entry, periodIds.Order().ToArray(), scope.CompanyIds);
        }).ToArray();
    }

    private static DateOnly? CompanyAccountingAffectedFromDate(EntityEntry entry)
    {
        if (entry.Entity is BankAccount)
        {
            var openingBalanceWasOrIsNonZero = ScalarValues(entry, nameof(BankAccount.OpeningBalance))
                .OfType<decimal>()
                .Any(value => value != 0);
            return openingBalanceWasOrIsNonZero
                ? EarliestDate(entry, nameof(BankAccount.OpeningBalanceDate))
                : DateOnly.MaxValue;
        }
        if (entry.Entity is FixedAsset)
            return EarliestDate(entry, nameof(FixedAsset.AcquisitionDate));
        if (entry.Entity is Loan)
            return EarliestDate(entry, nameof(Loan.DrawdownDate), nameof(Loan.BalanceAsOfDate));
        if (entry.Entity is ShareCapital)
            return EarliestDate(entry, nameof(ShareCapital.IssueDate), nameof(ShareCapital.CancelledDate));
        if (entry.Entity is AccountCategory && entry.State == EntityState.Added)
            return DateOnly.MaxValue;
        return null;
    }

    private static DateOnly? EarliestDate(EntityEntry entry, params string[] propertyNames) =>
        propertyNames
            .SelectMany(propertyName => ScalarValues(entry, propertyName))
            .OfType<DateOnly>()
            .Cast<DateOnly?>()
            .Order()
            .FirstOrDefault();

    private static IEnumerable<object?> ScalarValues(EntityEntry entry, string propertyName)
    {
        var property = entry.Metadata.FindProperty(propertyName);
        if (property is null) yield break;
        yield return entry.CurrentValues[property];
        if (entry.State is EntityState.Modified or EntityState.Deleted)
            yield return entry.OriginalValues[property];
    }

    private static void AddIntValue(EntityEntry entry, string propertyName, HashSet<int> values)
    {
        var property = entry.Metadata.FindProperty(propertyName);
        if (property is null) return;
        var value = entry.State == EntityState.Deleted
            ? entry.OriginalValues[property]
            : entry.CurrentValues[property];
        if (value is int intValue && intValue > 0) values.Add(intValue);
    }

    private static async Task<int?> ResolveBankCompanyAsync(
        AccountsDbContext db,
        int bankAccountId,
        CancellationToken cancellationToken) =>
        await db.BankAccounts.IgnoreQueryFilters()
            .Where(bank => bank.Id == bankAccountId)
            .Select(bank => (int?)bank.CompanyId)
            .SingleOrDefaultAsync(cancellationToken);

    private static async Task<int?> ResolveDirectorLoanPeriodAsync(
        AccountsDbContext db,
        int directorLoanId,
        CancellationToken cancellationToken) =>
        await db.DirectorLoans.IgnoreQueryFilters()
            .Where(loan => loan.Id == directorLoanId)
            .Select(loan => (int?)loan.PeriodId)
            .SingleOrDefaultAsync(cancellationToken);

    private static async Task<PeriodDatabaseState?> ReadPeriodForUpdateAsync(
        AccountsDbContext db,
        int periodId,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = """
            SELECT p."CompanyId", p."Status", p."LockedAt"
            FROM accounting_periods AS p
            WHERE p."Id" = @periodId
            FOR UPDATE OF p
            """;
        PeriodConcurrencyTokenService.AddParameter(command, "periodId", periodId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new PeriodDatabaseState(
            reader.GetInt32(0),
            Enum.Parse<PeriodStatus>(reader.GetString(1), ignoreCase: false),
            reader.IsDBNull(2) ? null : reader.GetDateTime(2));
    }

    private static async Task<bool> ReadCompanyQuarantineStateAsync(
        AccountsDbContext db,
        int companyId,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = """
            SELECT "IsQuarantined"
            FROM companies
            WHERE "Id" = @companyId
            """;
        PeriodConcurrencyTokenService.AddParameter(command, "companyId", companyId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is true;
    }

    private static async Task TouchPeriodVersionAsync(
        AccountsDbContext db,
        int periodId,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = """
            UPDATE accounting_periods
            SET "Status" = "Status"
            WHERE "Id" = @periodId
            """;
        PeriodConcurrencyTokenService.AddParameter(command, "periodId", periodId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> OriginalValuesStillCurrentAsync(
        AccountsDbContext db,
        EntityEntry entry,
        CancellationToken cancellationToken)
    {
        var tableName = entry.Metadata.GetTableName();
        if (string.IsNullOrWhiteSpace(tableName)) return true;
        var schema = entry.Metadata.GetSchema();
        var table = StoreObjectIdentifier.Table(tableName, schema);
        var properties = entry.Metadata.GetProperties()
            .Where(property => property.GetColumnName(table) is not null)
            .ToArray();
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null || key.Properties.Count == 0) return true;

        var selectColumns = string.Join(", ", properties.Select(property => Quote(property.GetColumnName(table)!)));
        var predicates = new List<string>();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        for (var index = 0; index < key.Properties.Count; index++)
        {
            var keyProperty = key.Properties[index];
            var parameterName = $"key{index}";
            predicates.Add($"{Quote(keyProperty.GetColumnName(table)!)} = @{parameterName}");
            var keyValue = entry.OriginalValues[keyProperty] ?? entry.CurrentValues[keyProperty]
                ?? throw new InvalidOperationException($"Missing key value for {entry.Metadata.ClrType.Name}.");
            PeriodConcurrencyTokenService.AddParameter(command, parameterName, ToProviderValue(keyProperty, keyValue));
        }

        command.CommandText = $"SELECT {selectColumns} FROM {QualifiedTable(schema, tableName)} WHERE {string.Join(" AND ", predicates)}";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return false;

        for (var index = 0; index < properties.Length; index++)
        {
            var property = properties[index];
            var original = ToProviderValue(property, entry.OriginalValues[property]);
            var currentDatabase = reader.IsDBNull(index) ? null : reader.GetValue(index);
            if (!ValuesEqual(original, currentDatabase)) return false;
        }
        return true;
    }

    private static object? ToProviderValue(IProperty property, object? value)
    {
        if (value is null) return null;
        return property.GetTypeMapping().Converter?.ConvertToProvider(value) ?? value;
    }

    private static bool ValuesEqual(object? expected, object? actual)
    {
        if (expected is null || actual is null) return expected is null && actual is null;
        if (expected is byte[] leftBytes && actual is byte[] rightBytes)
            return leftBytes.AsSpan().SequenceEqual(rightBytes);
        if (expected is DateTime leftDate && actual is DateTime rightDate)
            // PostgreSQL timestamp precision is one microsecond while DateTime carries 100 ns ticks.
            // EF retains the original in-memory ticks after an insert, so an immediate legitimate
            // second save must compare at the provider precision instead of reporting a false conflict.
            return leftDate.ToUniversalTime().Ticks / TimeSpan.TicksPerMicrosecond
                == rightDate.ToUniversalTime().Ticks / TimeSpan.TicksPerMicrosecond;
        if (expected is decimal || actual is decimal)
            return Convert.ToDecimal(expected, CultureInfo.InvariantCulture)
                == Convert.ToDecimal(actual, CultureInfo.InvariantCulture);
        return Equals(expected, actual)
            || string.Equals(
                Convert.ToString(expected, CultureInfo.InvariantCulture),
                Convert.ToString(actual, CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
    }

    private async Task CompleteOwnedTransactionAsync(bool commit, CancellationToken cancellationToken)
    {
        if (_ownedTransaction is null) return;
        var transaction = _ownedTransaction;
        _ownedTransaction = null;
        try
        {
            if (commit) await transaction.CommitAsync(cancellationToken);
            else await transaction.RollbackAsync(CancellationToken.None);
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    private static string QualifiedTable(string? schema, string table) =>
        string.IsNullOrWhiteSpace(schema) ? Quote(table) : $"{Quote(schema)}.{Quote(table)}";

    private sealed record InitialEntryScope(
        EntityEntry Entry,
        int[] DirectPeriodIds,
        int[] CompanyIds,
        bool ExpandCompanyPeriods,
        DateOnly? AffectedFromDate);

    private sealed record EntryScope(EntityEntry Entry, int[] PeriodIds, int[] CompanyIds);

    private sealed record CompanyPeriodScope(int CompanyId, int Id, DateOnly PeriodEnd);

    private sealed record PeriodDatabaseState(
        int CompanyId,
        PeriodStatus Status,
        DateTime? LockedAt)
    {
        public bool IsLocked => Status is PeriodStatus.Finalised or PeriodStatus.Filed || LockedAt is not null;
    }
}
