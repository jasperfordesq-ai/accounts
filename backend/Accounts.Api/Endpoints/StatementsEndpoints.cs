using Accounts.Api.Data;
using Accounts.Api.Services;

namespace Accounts.Api.Endpoints;

public static class StatementsEndpoints
{
    public static void MapStatementsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/companies/{companyId:int}/periods/{periodId:int}/statements").WithTags("Financial Statements");

        group.MapGet("/trial-balance", async (int companyId, int periodId, FinancialStatementsService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var result = await service.GetTrialBalanceAsync(companyId, periodId);
            return Results.Ok(result);
        });

        group.MapGet("/profit-and-loss", async (int companyId, int periodId, FinancialStatementsService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var result = await service.GetProfitAndLossAsync(companyId, periodId);
            return Results.Ok(result);
        });

        group.MapGet("/balance-sheet", async (int companyId, int periodId, FinancialStatementsService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var result = await service.GetBalanceSheetAsync(companyId, periodId);
            return Results.Ok(result);
        });

        group.MapGet("/readiness", async (int companyId, int periodId, FinancialStatementsService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var result = await service.GetReadinessScoreAsync(companyId, periodId);
            return Results.Ok(result);
        });

        group.MapGet("/sources", async (int companyId, int periodId, FinancialStatementsService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var result = await service.GetStatementSourcesAsync(companyId, periodId);
            return Results.Ok(result);
        });

        group.MapGet("/cash-flow", async (int companyId, int periodId, FinancialStatementsService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var result = await service.GetCashFlowStatementAsync(companyId, periodId);
            return Results.Ok(result);
        });

        group.MapGet("/equity-changes", async (int companyId, int periodId, FinancialStatementsService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var result = await service.GetEquityChangesAsync(companyId, periodId);
            return Results.Ok(result);
        });
    }
}
