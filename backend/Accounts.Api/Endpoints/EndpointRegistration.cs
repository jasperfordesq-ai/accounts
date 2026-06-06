namespace Accounts.Api.Endpoints;

public static class EndpointRegistration
{
    public static void MapAllEndpoints(this WebApplication app)
    {
        app.MapAuthEndpoints();
        app.MapClassificationEndpoints();
        app.MapBankingEndpoints();
        app.MapYearEndEndpoints();
        app.MapAdjustmentEndpoints();
        app.MapStatementsEndpoints();
        app.MapDocumentEndpoints();
        app.MapRevenueEndpoints();
        app.MapDeadlineEndpoints();
        app.MapCharityEndpoints();
        app.MapFilingWorkflowEndpoints();
    }
}
