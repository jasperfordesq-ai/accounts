using Accounts.Api.Services;

namespace Accounts.Api.Endpoints;

public static class RevenueEndpoints
{
    public static void MapRevenueEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/companies/{companyId:int}/periods/{periodId:int}/revenue").WithTags("Revenue & Tax");

        group.MapGet("/tax-computation", async (int companyId, int periodId, TaxComputationService service) =>
        {
            try
            {
                var result = await service.ComputeAsync(periodId);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapGet("/ct1-support", async (int companyId, int periodId, TaxComputationService service) =>
        {
            try
            {
                var result = await service.GetCt1SupportDataAsync(periodId);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapGet("/ixbrl", async (int companyId, int periodId, IxbrlService service) =>
        {
            try
            {
                var ixbrl = await service.GenerateIxbrlAsync(periodId);
                return Results.File(ixbrl, "application/xhtml+xml", $"financial_statements_{periodId}.xhtml");
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }
}
