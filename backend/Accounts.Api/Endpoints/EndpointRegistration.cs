namespace Accounts.Api.Endpoints;

public static class EndpointRegistration
{
    public static void MapAllEndpoints(this WebApplication app)
    {
        app.MapAuthEndpoints();
        app.MapUserAdministrationEndpoints();
        app.MapClassificationEndpoints();
        app.MapBankingEndpoints();
        app.MapYearEndEndpoints();
        app.MapAdjustmentEndpoints();
        app.MapStatementsEndpoints();
        app.MapAccountantWorkingPaperEndpoints();
        app.MapDocumentEndpoints();
        app.MapRevenueEndpoints();
        app.MapDeadlineEndpoints();
        app.MapCharityEndpoints();
        app.MapFilingWorkflowEndpoints();
        app.MapExternalFilingHandoffEndpoints();
        app.MapPrivacyEndpoints();
        app.MapOperationsEndpoints();
    }
}
