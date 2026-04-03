using Accounts.Api.Services;

namespace Accounts.Api.Endpoints;

public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/companies/{companyId:int}/periods/{periodId:int}/documents").WithTags("Documents");

        // Full accounts package (existing — used for AGM approval pack)
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

        // AGM approval pack (full statutory accounts for directors/members)
        group.MapGet("/agm-pack", async (int companyId, int periodId, DocumentGeneratorService service) =>
        {
            try
            {
                var pdf = await service.GenerateAccountsPackageAsync(periodId);
                return Results.File(pdf, "application/pdf", $"agm_pack_{periodId}.pdf");
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // CRO filing pack (abridged/filleted per regime — no P&L for SmallAbridged, no directors' report for Micro)
        group.MapGet("/cro-filing-pack", async (int companyId, int periodId, DocumentGeneratorService service) =>
        {
            try
            {
                var pdf = await service.GenerateCroFilingPackAsync(periodId);
                return Results.File(pdf, "application/pdf", $"cro_filing_{periodId}.pdf");
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Signature page (separate PDF for CRO upload — s.347 typeset signatures)
        group.MapGet("/signature-page", async (int companyId, int periodId, DocumentGeneratorService service) =>
        {
            try
            {
                var pdf = await service.GenerateSignaturePageAsync(periodId);
                return Results.File(pdf, "application/pdf", $"signature_page_{periodId}.pdf");
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Directors' report data (JSON for frontend preview)
        group.MapGet("/directors-report-data", async (int companyId, int periodId, DirectorsReportService service) =>
        {
            try
            {
                var result = await service.GenerateAsync(periodId);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }
}
