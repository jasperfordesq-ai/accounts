using Accounts.Api.Data;
using Accounts.Api.Services;

namespace Accounts.Api.Endpoints;

public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/companies/{companyId:int}/periods/{periodId:int}/documents").WithTags("Documents");

        // Full accounts package for AGM/member approval.
        group.MapGet("/accounts-package", async (int companyId, int periodId, DocumentGeneratorService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var pdf = await service.GenerateAccountsPackageAsync(companyId, periodId);
            return Results.File(pdf, "application/pdf", $"accounts_{periodId}.pdf");
        });

        group.MapGet("/agm-pack", async (int companyId, int periodId, DocumentGeneratorService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var pdf = await service.GenerateAgmApprovalPackAsync(companyId, periodId);
            return Results.File(pdf, "application/pdf", $"agm_pack_{periodId}.pdf");
        });

        group.MapGet("/cro-filing-pack", async (int companyId, int periodId, DocumentGeneratorService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var pdf = await service.GenerateCroFilingPackAsync(companyId, periodId);

            return Results.File(pdf, "application/pdf", $"cro_filing_{periodId}.pdf");
        });

        group.MapPost("/cro-filing-pack", async (int companyId, int periodId, DocumentGeneratorService service, FilingWorkflowService workflow, AccountsDbContext db, HttpContext context, ApiAccessService apiAccess) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            var pdf = await service.GenerateCroFilingPackAsync(companyId, periodId);
            await workflow.RecordCroDocumentGeneratedAsync(companyId, periodId, "accounts", AuditUserId(context));
            return Results.File(pdf, "application/pdf", $"cro_filing_{periodId}.pdf");
        });

        group.MapGet("/signature-page", async (int companyId, int periodId, DocumentGeneratorService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var pdf = await service.GenerateSignaturePageAsync(companyId, periodId);

            return Results.File(pdf, "application/pdf", $"signature_page_{periodId}.pdf");
        });

        group.MapPost("/signature-page", async (int companyId, int periodId, DocumentGeneratorService service, FilingWorkflowService workflow, AccountsDbContext db, HttpContext context, ApiAccessService apiAccess) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            var pdf = await service.GenerateSignaturePageAsync(companyId, periodId);
            await workflow.RecordCroDocumentGeneratedAsync(companyId, periodId, "signature", AuditUserId(context));
            return Results.File(pdf, "application/pdf", $"signature_page_{periodId}.pdf");
        });

        group.MapGet("/directors-report-data", async (int companyId, int periodId, DirectorsReportService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var result = await service.GenerateAsync(companyId, periodId);
            return Results.Ok(result);
        });
    }

    private static string? AuditUserId(HttpContext context)
    {
        var user = AuthContext.GetUser(context);
        return user is null ? null : AuthenticatedIdentity.AuditUserId(user);
    }

}
