using Accounts.Api.Services;

namespace Accounts.Api.Endpoints;

public static class StatementsEndpoints
{
    public static void MapStatementsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/companies/{companyId:int}/periods/{periodId:int}/statements").WithTags("Financial Statements");

        group.MapGet("/trial-balance", async (int companyId, int periodId, FinancialStatementsService service) =>
        {
            var result = await service.GetTrialBalanceAsync(periodId);
            return Results.Ok(result);
        });

        group.MapGet("/profit-and-loss", async (int companyId, int periodId, FinancialStatementsService service) =>
        {
            var result = await service.GetProfitAndLossAsync(periodId);
            return Results.Ok(result);
        });

        group.MapGet("/balance-sheet", async (int companyId, int periodId, FinancialStatementsService service) =>
        {
            var result = await service.GetBalanceSheetAsync(periodId);
            return Results.Ok(result);
        });

        group.MapGet("/readiness", async (int companyId, int periodId, FinancialStatementsService service) =>
        {
            var result = await service.GetReadinessScoreAsync(periodId);
            return Results.Ok(result);
        });

        group.MapGet("/sources", async (int companyId, int periodId, FinancialStatementsService service) =>
        {
            var result = await service.GetStatementSourcesAsync(periodId);
            return Results.Ok(result);
        });

        group.MapGet("/cash-flow", async (int companyId, int periodId, FinancialStatementsService service) =>
        {
            var result = await service.GetCashFlowStatementAsync(periodId);
            return Results.Ok(result);
        });

        group.MapGet("/equity-changes", async (int companyId, int periodId, FinancialStatementsService service) =>
        {
            var result = await service.GetEquityChangesAsync(periodId);
            return Results.Ok(result);
        });
    }
}
