using Accounts.Api.Services;

namespace Accounts.Api.Endpoints;

public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/companies/{companyId:int}/periods/{periodId:int}/documents").WithTags("Documents");

        group.MapGet("/accounts-package", async (int companyId, int periodId, DocumentGeneratorService service) =>
        {
            try
            {
                var pdf = await service.GenerateAccountsPackageAsync(periodId);
                return Results.File(pdf, "application/pdf", $"accounts_{periodId}.pdf");
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }
}
