using Accounts.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public sealed record SearchHit(
    string Type,
    long Id,
    string Title,
    string Ref,
    int? CompanyId,
    int? PeriodId);

public sealed record SearchResponse(IReadOnlyList<SearchHit> Results, IReadOnlyList<string> SearchedTypes);

/// <summary>
/// Tenant- and assignment-scoped lookup used by agents to find records by name. It returns
/// references only (type, id, a public title) — callers read the record through its normal route.
/// Transactions are only searched inside one named period to keep the query bounded.
/// </summary>
public sealed class SearchService(AccountsDbContext db)
{
    public static readonly IReadOnlyList<string> AllTypes = ["company", "period", "officer", "category", "note", "transaction"];

    public async Task<SearchResponse> SearchAsync(
        HttpContext context,
        string? query,
        IReadOnlyCollection<string>? types,
        int? companyId,
        int? periodId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var text = (query ?? "").Trim();
        if (text.Length is < 1 or > 200)
            throw new BusinessRuleException("Search text must be between 1 and 200 characters.");
        limit = Math.Clamp(limit <= 0 ? 20 : limit, 1, 50);
        var wanted = (types is { Count: > 0 } ? types : AllTypes)
            .Select(type => type.Trim().ToLowerInvariant())
            .Where(AllTypes.Contains)
            .Distinct()
            .ToList();
        if (wanted.Count == 0)
            throw new BusinessRuleException($"types must be some of: {string.Join(", ", AllTypes)}.");

        var pattern = "%" + EscapeLike(text.ToLowerInvariant()) + "%";
        var companies = CompanyListQuery.ForContext(context, db.Companies.AsNoTracking());
        if (companyId is not null) companies = companies.Where(company => company.Id == companyId);
        var companyIds = companies.Select(company => company.Id);
        var hits = new List<SearchHit>();
        var searched = new List<string>();

        if (wanted.Contains("company"))
        {
            searched.Add("company");
            var rows = await companies
                .Where(company => EF.Functions.Like(company.LegalName.ToLower(), pattern)
                    || (company.TradingName != null && EF.Functions.Like(company.TradingName.ToLower(), pattern))
                    || (company.CroNumber != null && EF.Functions.Like(company.CroNumber.ToLower(), pattern))
                    || (company.TaxReference != null && EF.Functions.Like(company.TaxReference.ToLower(), pattern)))
                .OrderBy(company => company.LegalName)
                .Take(limit)
                .Select(company => new { company.Id, company.LegalName, company.CroNumber })
                .ToListAsync(cancellationToken);
            hits.AddRange(rows.Select(row => new SearchHit(
                "company", row.Id,
                row.CroNumber is null ? row.LegalName : $"{row.LegalName} (CRO {row.CroNumber})",
                $"filingbridge://company/{row.Id}", row.Id, null)));
        }

        if (wanted.Contains("period"))
        {
            searched.Add("period");
            var rows = await db.AccountingPeriods.AsNoTracking()
                .Where(period => companyIds.Contains(period.CompanyId)
                    && (EF.Functions.Like(period.Company.LegalName.ToLower(), pattern)
                        || EF.Functions.Like(period.PeriodEnd.Year.ToString(), pattern)))
                .OrderByDescending(period => period.PeriodEnd)
                .Take(limit)
                .Select(period => new { period.Id, period.CompanyId, period.PeriodStart, period.PeriodEnd, period.Company.LegalName })
                .ToListAsync(cancellationToken);
            hits.AddRange(rows.Select(row => new SearchHit(
                "period", row.Id,
                $"{row.LegalName}: {row.PeriodStart:yyyy-MM-dd} to {row.PeriodEnd:yyyy-MM-dd}",
                $"filingbridge://period/{row.Id}?companyId={row.CompanyId}", row.CompanyId, row.Id)));
        }

        if (wanted.Contains("officer"))
        {
            searched.Add("officer");
            var rows = await db.CompanyOfficers.AsNoTracking()
                .Where(officer => companyIds.Contains(officer.CompanyId)
                    && EF.Functions.Like(officer.Name.ToLower(), pattern))
                .OrderBy(officer => officer.Name)
                .Take(limit)
                .Select(officer => new { officer.Id, officer.CompanyId, officer.Name, officer.Role })
                .ToListAsync(cancellationToken);
            hits.AddRange(rows.Select(row => new SearchHit(
                "officer", row.Id, $"{row.Name} ({row.Role})",
                $"filingbridge://officer/{row.Id}?companyId={row.CompanyId}", row.CompanyId, null)));
        }

        if (wanted.Contains("category") && companyId is not null)
        {
            searched.Add("category");
            var rows = await db.AccountCategories.AsNoTracking()
                .Where(category => (category.CompanyId == null || companyIds.Contains(category.CompanyId.Value))
                    && (EF.Functions.Like(category.Name.ToLower(), pattern) || EF.Functions.Like(category.Code.ToLower(), pattern)))
                .OrderBy(category => category.Code)
                .Take(limit)
                .Select(category => new { category.Id, category.Code, category.Name })
                .ToListAsync(cancellationToken);
            hits.AddRange(rows.Select(row => new SearchHit(
                "category", row.Id, $"{row.Code} {row.Name}",
                $"filingbridge://category/{row.Id}?companyId={companyId}", companyId, null)));
        }

        if (wanted.Contains("note") && periodId is not null)
        {
            searched.Add("note");
            var rows = await db.NotesDisclosures.AsNoTracking()
                .Where(note => note.PeriodId == periodId
                    && companyIds.Contains(note.Period.CompanyId)
                    && EF.Functions.Like(note.Title.ToLower(), pattern))
                .OrderBy(note => note.Title)
                .Take(limit)
                .Select(note => new { note.Id, note.Title, note.Period.CompanyId })
                .ToListAsync(cancellationToken);
            hits.AddRange(rows.Select(row => new SearchHit(
                "note", row.Id, row.Title,
                $"filingbridge://note/{row.Id}?companyId={row.CompanyId}&periodId={periodId}", row.CompanyId, periodId)));
        }

        if (wanted.Contains("transaction") && periodId is not null)
        {
            searched.Add("transaction");
            var rows = await db.ImportedTransactions.AsNoTracking()
                .Where(transaction => transaction.PeriodId == periodId
                    && companyIds.Contains(transaction.BankAccount.CompanyId)
                    && (EF.Functions.Like(transaction.Description.ToLower(), pattern)
                        || (transaction.Reference != null && EF.Functions.Like(transaction.Reference.ToLower(), pattern))))
                .OrderByDescending(transaction => transaction.Date)
                .Take(limit)
                .Select(transaction => new { transaction.Id, transaction.Date, transaction.Description, transaction.Amount, transaction.BankAccount.CompanyId })
                .ToListAsync(cancellationToken);
            hits.AddRange(rows.Select(row => new SearchHit(
                "transaction", row.Id, $"{row.Date:yyyy-MM-dd} {row.Description} {row.Amount:0.00}",
                $"filingbridge://transaction/{row.Id}?companyId={row.CompanyId}&periodId={periodId}", row.CompanyId, periodId)));
        }

        return new SearchResponse(hits.Take(limit).ToList(), searched);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
