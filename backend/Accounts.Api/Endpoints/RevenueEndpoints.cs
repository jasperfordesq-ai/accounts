using Accounts.Api.Data;
using Accounts.Api.Services;

namespace Accounts.Api.Endpoints;

public static class RevenueEndpoints
{
    public static void MapRevenueEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/companies/{companyId:int}/periods/{periodId:int}/revenue").WithTags("Revenue & Tax");

        group.MapGet("/tax-computation", async (int companyId, int periodId, TaxComputationService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var result = await service.ComputeAsync(companyId, periodId);
            return Results.Ok(result);
        });

        group.MapGet("/ct1-support", async (int companyId, int periodId, TaxComputationService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var result = await service.GetCt1SupportDataAsync(companyId, periodId);
            return Results.Ok(result);
        });

        group.MapGet("/ixbrl", async (int companyId, int periodId, IxbrlService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var ixbrl = await service.GenerateFinalIxbrlAsync(companyId, periodId);
            return Results.File(ixbrl, "application/xhtml+xml", $"financial_statements_{periodId}.xhtml");
        });
    }
}
