using System.Data;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Accounts.Api.Services;

/// <summary>
/// Owns accounting-period creation and prior-period selection. Period topology is validated inside
/// the same serialized transaction as the insert so endpoint pre-checks cannot race each other.
/// A PostgreSQL trigger provides the corresponding raw-SQL boundary.
/// </summary>
public sealed class PeriodChronologyService(AccountsDbContext db)
{
    public async Task<AccountingPeriod> CreateAsync(
        AccountingPeriod period,
        CancellationToken cancellationToken = default)
    {
        IDbContextTransaction? transaction = null;
        try
        {
            if (db.Database.IsRelational())
            {
                if (db.Database.CurrentTransaction is null)
                {
                    transaction = await db.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken);
                }
                if (string.Equals(
                        db.Database.ProviderName,
                        "Npgsql.EntityFrameworkCore.PostgreSQL",
                        StringComparison.Ordinal))
                {
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT pg_advisory_xact_lock({period.CompanyId})",
                        cancellationToken);
                }
            }

            var failures = await ValidateAsync(period, cancellationToken);
            if (failures.Count > 0)
                throw new PeriodChronologyException(string.Join(" ", failures));

            db.AccountingPeriods.Add(period);
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return period;
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    public async Task<IReadOnlyList<string>> ValidateAsync(
        AccountingPeriod proposed,
        CancellationToken cancellationToken = default)
    {
        var company = await db.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(company => company.Id == proposed.CompanyId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Company {proposed.CompanyId} not found");
        var periods = await db.AccountingPeriods
            .AsNoTracking()
            .Where(period => period.CompanyId == proposed.CompanyId && period.Id != proposed.Id)
            .OrderBy(period => period.PeriodStart)
            .ThenBy(period => period.PeriodEnd)
            .ThenBy(period => period.Id)
            .ToListAsync(cancellationToken);
        var failures = new List<string>();

        if (proposed.PeriodStart == default || proposed.PeriodEnd == default)
            failures.Add("Period start and end are required.");
        if (proposed.PeriodEnd < proposed.PeriodStart)
            failures.Add("Period end cannot be before period start.");
        if (proposed.PeriodStart != default
            && proposed.PeriodEnd > proposed.PeriodStart.AddMonths(18).AddDays(-1))
        {
            failures.Add("An accounting period cannot exceed 18 months.");
        }
        if (proposed.PeriodStart < company.IncorporationDate)
            failures.Add("An accounting period cannot begin before the company incorporation date.");

        if (periods.Any(period =>
                proposed.PeriodStart <= period.PeriodEnd
                && proposed.PeriodEnd >= period.PeriodStart))
        {
            failures.Add("Accounting periods for a company cannot overlap.");
        }

        var existingFirstYears = periods.Where(period => period.IsFirstYear).ToList();
        if (existingFirstYears.Count > 1)
            failures.Add("Existing accounting periods contain more than one first year and must be corrected.");
        if (proposed.IsFirstYear && existingFirstYears.Count > 0)
            failures.Add("A company can have only one first-year accounting period.");
        if (proposed.IsFirstYear && proposed.PeriodStart != company.IncorporationDate)
            failures.Add("The first-year accounting period must begin on the incorporation date.");

        if (periods.Count == 0)
        {
            if (!proposed.IsFirstYear)
                failures.Add("The first accounting period must be marked as the first year.");
            if (proposed.PeriodStart != company.IncorporationDate)
                failures.Add("The first accounting period must begin on the incorporation date.");
            return failures.Distinct(StringComparer.Ordinal).ToArray();
        }

        if (!proposed.IsFirstYear && existingFirstYears.Count == 0)
            failures.Add("A first-year accounting period must exist before a later period is created.");

        var previous = periods
            .Where(period => period.PeriodEnd < proposed.PeriodStart)
            .OrderByDescending(period => period.PeriodEnd)
            .ThenByDescending(period => period.Id)
            .FirstOrDefault();
        var next = periods
            .Where(period => period.PeriodStart > proposed.PeriodEnd)
            .OrderBy(period => period.PeriodStart)
            .ThenBy(period => period.Id)
            .FirstOrDefault();

        if (previous is null)
        {
            if (!proposed.IsFirstYear)
                failures.Add("A period inserted before the current history must be the first year.");
        }
        else
        {
            if (proposed.IsFirstYear)
                failures.Add("A first-year period cannot have an earlier accounting period.");
            if (proposed.PeriodStart != previous.PeriodEnd.AddDays(1))
                failures.Add($"The period must start on {previous.PeriodEnd.AddDays(1):yyyy-MM-dd}; unexplained chronology gaps are not allowed.");
        }

        if (next is not null && proposed.PeriodEnd.AddDays(1) != next.PeriodStart)
            failures.Add($"The period must end on {next.PeriodStart.AddDays(-1):yyyy-MM-dd}; unexplained chronology gaps are not allowed.");

        return failures.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static IQueryable<AccountingPeriod> PriorPeriodQuery(
        AccountsDbContext db,
        int companyId,
        DateOnly currentPeriodStart)
    {
        var expectedPriorEnd = currentPeriodStart.AddDays(-1);
        return db.AccountingPeriods
            .Where(period => period.CompanyId == companyId && period.PeriodEnd == expectedPriorEnd)
            .OrderByDescending(period => period.PeriodEnd)
            .ThenByDescending(period => period.Id);
    }
}

public sealed class PeriodChronologyException(string message) : BusinessRuleException(message);
