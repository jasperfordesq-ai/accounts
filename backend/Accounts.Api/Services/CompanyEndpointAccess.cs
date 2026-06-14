using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public static class CompanyListQuery
{
    public static IQueryable<Company> ForContext(
        HttpContext context,
        IQueryable<Company> companies)
    {
        var user = AuthContext.RequireUser(context);
        var query = companies.Where(c => c.TenantId == user.TenantId);
        query = UserCompanyAccessPolicy.ApplyToQuery(user, query);
        var allowedCompanyIds = ApiAccessService.GetAllowedCompanyIds(context);
        if (allowedCompanyIds is { Count: > 0 })
        {
            var ids = allowedCompanyIds.ToArray();
            query = query.Where(c => ids.Contains(c.Id));
        }

        return query;
    }
}

public static class CompanyEndpointAccess
{
    public static Task<bool> CanAccessCompanyAsync(HttpContext context, AccountsDbContext db, int companyId) =>
        CompanyListQuery.ForContext(context, db.Companies).AnyAsync(c => c.Id == companyId);

    public static Task<bool> CanAccessCompanyPeriodAsync(HttpContext context, AccountsDbContext db, int companyId, int periodId)
    {
        var visibleCompanyIds = CompanyListQuery
            .ForContext(context, db.Companies)
            .Where(c => c.Id == companyId)
            .Select(c => c.Id);

        return db.AccountingPeriods
            .AsNoTracking()
            .AnyAsync(p => p.Id == periodId && visibleCompanyIds.Contains(p.CompanyId));
    }
}
