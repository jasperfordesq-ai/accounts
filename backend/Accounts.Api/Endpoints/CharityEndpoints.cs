using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Endpoints;

public static class CharityEndpoints
{
    public static void MapCharityEndpoints(this WebApplication app)
    {
        var companyGroup = app.MapGroup("/api/companies/{companyId:int}/charity").WithTags("Charity");
        var periodGroup = app.MapGroup("/api/companies/{companyId:int}/periods/{periodId:int}/charity").WithTags("Charity");

        // Get charity info
        companyGroup.MapGet("/info", async (int companyId, AccountsDbContext db) =>
        {
            var info = await db.CharityInfos.FirstOrDefaultAsync(c => c.CompanyId == companyId);
            return info != null ? Results.Ok(info) : Results.Ok(new { message = "No charity info configured" });
        });

        // Save charity info
        companyGroup.MapPut("/info", async (int companyId, CharityInfo input, CharityReportingService service) =>
        {
            var result = await service.SaveCharityInfoAsync(companyId, input);
            return Results.Ok(result);
        });

        // Get SoFA (Statement of Financial Activities)
        periodGroup.MapGet("/sofa", async (int companyId, int periodId, CharityReportingService service) =>
        {
            try
            {
                var sofa = await service.GenerateSofaAsync(companyId, periodId);
                return Results.Ok(sofa);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Get Trustees' Annual Report data
        periodGroup.MapGet("/trustees-report", async (int companyId, int periodId, CharityReportingService service) =>
        {
            try
            {
                var tar = await service.GenerateTarAsync(companyId, periodId);
                return Results.Ok(tar);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Fund balances CRUD
        periodGroup.MapGet("/funds", async (int companyId, int periodId, AccountsDbContext db) =>
            await db.FundBalances.Where(f => f.PeriodId == periodId).OrderBy(f => f.FundType).ThenBy(f => f.FundName).ToListAsync());

        periodGroup.MapPost("/funds", async (int companyId, int periodId, FundBalance input, AccountsDbContext db) =>
        {
            input.PeriodId = periodId;
            input.ClosingBalance = input.OpeningBalance + input.IncomingResources - input.ResourcesExpended + input.Transfers + input.GainsLosses;
            db.FundBalances.Add(input);
            await db.SaveChangesAsync();
            return Results.Created($"/api/companies/{companyId}/periods/{periodId}/charity/funds/{input.Id}", input);
        });

        periodGroup.MapPut("/funds/{id:int}", async (int companyId, int periodId, int id, FundBalance input, AccountsDbContext db) =>
        {
            var item = await db.FundBalances.FirstOrDefaultAsync(f => f.Id == id && f.PeriodId == periodId);
            if (item == null) return Results.NotFound();
            item.FundName = input.FundName;
            item.FundType = input.FundType;
            item.OpeningBalance = input.OpeningBalance;
            item.IncomingResources = input.IncomingResources;
            item.ResourcesExpended = input.ResourcesExpended;
            item.Transfers = input.Transfers;
            item.GainsLosses = input.GainsLosses;
            item.ClosingBalance = input.OpeningBalance + input.IncomingResources - input.ResourcesExpended + input.Transfers + input.GainsLosses;
            item.Notes = input.Notes;
            await db.SaveChangesAsync();
            return Results.Ok(item);
        });

        periodGroup.MapDelete("/funds/{id:int}", async (int companyId, int periodId, int id, AccountsDbContext db) =>
        {
            var item = await db.FundBalances.FirstOrDefaultAsync(f => f.Id == id && f.PeriodId == periodId);
            if (item == null) return Results.NotFound();
            db.FundBalances.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
