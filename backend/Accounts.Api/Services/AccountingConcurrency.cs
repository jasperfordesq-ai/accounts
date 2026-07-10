using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Accounts.Api.Services;

public sealed class AccountingConcurrencyException(
    string message,
    string? currentETag = null) : Exception(message)
{
    public string Code => "accounting_concurrency_conflict";
    public string? CurrentETag { get; } = currentETag;
}

public sealed record AccountingConflictResponse(
    string Error,
    string Code,
    bool ReloadRequired,
    bool ReconcileRequired,
    string? CurrentETag,
    string CorrelationId);

public static class AccountingConflict
{
    public const string SafeMessage =
        "Accounting data changed or the period was finalised. Reload the latest data and reconcile your changes before retrying.";
    public const string QuarantinedSafeMessage =
        "The company is quarantined. Reload the latest company state before attempting any further accounting changes.";

    public static AccountingConflictResponse Response(HttpContext context, string? currentETag = null) =>
        new(
            SafeMessage,
            "accounting_concurrency_conflict",
            ReloadRequired: true,
            ReconcileRequired: true,
            currentETag,
            context.TraceIdentifier);
}

public sealed class PeriodConcurrencyTokenService(AccountsDbContext db)
{
    public async Task<string?> GetAsync(
        int companyId,
        int periodId,
        CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsRelational())
        {
            var period = await db.AccountingPeriods
                .AsNoTracking()
                .Where(item => item.Id == periodId && item.CompanyId == companyId)
                .Select(item => new
                {
                    item.Id,
                    item.Status,
                    item.LockedAt,
                    item.ApprovalDate,
                    item.ClosingRetainedEarnings
                })
                .SingleOrDefaultAsync(cancellationToken);
            if (period is null) return null;

            var latestAudit = await db.AuditLogs
                .AsNoTracking()
                .Where(item => item.CompanyId == companyId && item.PeriodId == periodId)
                .OrderByDescending(item => item.Id)
                .Select(item => new { item.Id, item.IntegrityHash })
                .FirstOrDefaultAsync(cancellationToken);
            return Quote(Hash(string.Join('|',
                period.Id,
                period.Status,
                period.LockedAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                period.ApprovalDate?.ToString("O", CultureInfo.InvariantCulture),
                period.ClosingRetainedEarnings?.ToString(CultureInfo.InvariantCulture),
                latestAudit?.Id,
                latestAudit?.IntegrityHash)));
        }

        var connection = db.Database.GetDbConnection();
        var closeAfter = connection.State != ConnectionState.Open;
        if (closeAfter) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = """
                SELECT p.xmin::text,
                       p."Status",
                       p."LockedAt",
                       p."ApprovalDate",
                       p."ClosingRetainedEarnings",
                       COALESCE(a."Id", 0),
                       COALESCE(a."IntegrityHash", '')
                FROM accounting_periods AS p
                LEFT JOIN LATERAL (
                    SELECT l."Id", l."IntegrityHash"
                    FROM audit_logs AS l
                    WHERE l."CompanyId" = p."CompanyId" AND l."PeriodId" = p."Id"
                    ORDER BY l."Id" DESC
                    LIMIT 1
                ) AS a ON TRUE
                WHERE p."Id" = @periodId AND p."CompanyId" = @companyId
                """;
            AddParameter(command, "periodId", periodId);
            AddParameter(command, "companyId", companyId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;

            var values = new string[7];
            for (var index = 0; index < values.Length; index++)
                values[index] = reader.IsDBNull(index)
                    ? string.Empty
                    : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture) ?? string.Empty;
            return Quote(Hash(string.Join('|', values)));
        }
        finally
        {
            if (closeAfter) await connection.CloseAsync();
        }
    }

    internal static string Normalize(string value) => value.Trim();

    internal static bool Matches(string supplied, string current) =>
        supplied.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(candidate => candidate == "*" || string.Equals(Normalize(candidate), current, StringComparison.Ordinal));

    private static string Quote(string value) => $"\"{value}\"";

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}

public sealed class AccountingConcurrencyCoordinator(AccountsDbContext db)
{
    internal const int AdvisoryLockFamily = 41001;

    public async Task<AccountingConcurrencyLease> AcquirePeriodAsync(
        int companyId,
        int periodId,
        CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsRelational())
        {
            var exists = await db.AccountingPeriods
                .AsNoTracking()
                .AnyAsync(item => item.Id == periodId && item.CompanyId == companyId, cancellationToken);
            if (!exists) throw new ResourceNotFoundException($"Period {periodId} not found");
            return new AccountingConcurrencyLease(null);
        }

        IDbContextTransaction? ownedTransaction = null;
        if (db.Database.CurrentTransaction is null)
            ownedTransaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            await AcquireCompanyAdvisoryLockAsync(db, companyId, cancellationToken);
            await AcquireAdvisoryLockAsync(db, periodId, cancellationToken);
            var connection = db.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            command.CommandText = """
                SELECT "Id"
                FROM accounting_periods
                WHERE "Id" = @periodId AND "CompanyId" = @companyId
                FOR UPDATE
                """;
            PeriodConcurrencyTokenService.AddParameter(command, "periodId", periodId);
            PeriodConcurrencyTokenService.AddParameter(command, "companyId", companyId);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            if (result is null)
            {
                if (ownedTransaction is not null)
                    await ownedTransaction.RollbackAsync(CancellationToken.None);
                throw new ResourceNotFoundException($"Period {periodId} not found");
            }

            return new AccountingConcurrencyLease(ownedTransaction);
        }
        catch
        {
            if (ownedTransaction is not null)
                await ownedTransaction.DisposeAsync();
            throw;
        }
    }

    internal static async Task AcquireAdvisoryLockAsync(
        AccountsDbContext context,
        int periodId,
        CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({AdvisoryLockFamily}, {periodId})",
            cancellationToken);
    }

    internal static async Task AcquireCompanyAdvisoryLockAsync(
        AccountsDbContext context,
        int companyId,
        CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({companyId})",
            cancellationToken);
    }
}

public sealed class AccountingConcurrencyLease(IDbContextTransaction? ownedTransaction) : IAsyncDisposable
{
    private bool _completed;

    public async Task CommitIfOwnedAsync(CancellationToken cancellationToken = default)
    {
        if (ownedTransaction is null || _completed) return;
        await ownedTransaction.CommitAsync(cancellationToken);
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (ownedTransaction is null) return;
        if (!_completed)
            await ownedTransaction.RollbackAsync(CancellationToken.None);
        await ownedTransaction.DisposeAsync();
    }
}
