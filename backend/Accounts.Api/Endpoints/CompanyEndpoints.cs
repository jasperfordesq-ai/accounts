using Microsoft.EntityFrameworkCore;
using Accounts.Api.Data;
using Accounts.Api.Services;

namespace Accounts.Api.Endpoints;

public static class CompanyEndpoints
{
    public static void MapCompanyEndpoints(this WebApplication app)
    {
        // Company endpoints
        var companies = app.MapGroup("/api/companies").WithTags("Companies");

        companies.MapGet("/", async (HttpContext context, AccountsDbContext db) =>
        {
            return await CompanyDashboardRows
                .ForContext(context, db)
                .ToListAsync();
        });

        companies.MapGet("/{id:int}", async (int id, HttpContext context, AccountsDbContext db) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, id))
                return Results.NotFound();

            return await db.Companies
                .Include(c => c.Officers)
                .Include(c => c.Periods)
                .FirstOrDefaultAsync(c => c.Id == id)
            is { } company ? Results.Ok(company) : Results.NotFound();
        });

        companies.MapPost("/", async (CompanyInput input, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db) =>
        {
            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            if (EndpointInputs.ValidateCompany(input) is { } validationProblem)
                return validationProblem;

            var user = AuthContext.RequireUser(context);
            var company = EndpointInputs.ToCompany(input);
            company.TenantId = user.TenantId;
            db.Companies.Add(company);
            await db.SaveChangesAsync();
            return Results.Created($"/api/companies/{company.Id}", company);
        });

        companies.MapPut("/{id:int}", async (int id, CompanyInput input, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, AccountingWriteGuard writeGuard) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, id))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            if (EndpointInputs.ValidateCompany(input) is { } validationProblem)
                return validationProblem;

            var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id);
            if (company is null) return Results.NotFound();

            if (await writeGuard.BlockIfCompanyMasterDataLockedAsync(id) is { } blocked)
                return blocked;

            EndpointInputs.ApplyCompany(company, input);

            await db.SaveChangesAsync();
            return Results.Ok(company);
        });

        companies.MapDelete("/{id:int}", Accounts.Api.Endpoints.CompanyDeletionEndpoint.DeleteAsync);

        // Officers endpoints
        var officers = app.MapGroup("/api/companies/{companyId:int}/officers").WithTags("Officers");

        officers.MapGet("/", async (int companyId, HttpContext context, AccountsDbContext db) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            return Results.Ok(await db.CompanyOfficers.Where(o => o.CompanyId == companyId).ToListAsync());
        });

        officers.MapPost("/", async (int companyId, CompanyOfficerInput input, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, AccountingWriteGuard writeGuard) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            if (EndpointInputs.ValidateOfficer(input) is { } validationProblem)
                return validationProblem;

            if (await writeGuard.BlockIfCompanyMasterDataLockedAsync(companyId) is { } blocked)
                return blocked;

            var officer = EndpointInputs.ToOfficer(companyId, input);
            db.CompanyOfficers.Add(officer);
            await db.SaveChangesAsync();
            return Results.Created($"/api/companies/{companyId}/officers/{officer.Id}", officer);
        });

        officers.MapPut("/{id:int}", async (int companyId, int id, CompanyOfficerInput input, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, AccountingWriteGuard writeGuard) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            if (EndpointInputs.ValidateOfficer(input) is { } validationProblem)
                return validationProblem;

            if (await writeGuard.BlockIfCompanyMasterDataLockedAsync(companyId) is { } blocked)
                return blocked;

            var officer = await db.CompanyOfficers.FirstOrDefaultAsync(o => o.Id == id && o.CompanyId == companyId);
            if (officer is null) return Results.NotFound();
            EndpointInputs.ApplyOfficer(officer, input);
            await db.SaveChangesAsync();
            return Results.Ok(officer);
        });

        officers.MapDelete("/{id:int}", async (int companyId, int id, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, AccountingWriteGuard writeGuard) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            if (await writeGuard.BlockIfCompanyMasterDataLockedAsync(companyId) is { } blocked)
                return blocked;

            var officer = await db.CompanyOfficers.FirstOrDefaultAsync(o => o.Id == id && o.CompanyId == companyId);
            if (officer is null) return Results.NotFound();
            db.CompanyOfficers.Remove(officer);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Accounting Periods endpoints
        var periods = app.MapGroup("/api/companies/{companyId:int}/periods").WithTags("Periods");

        periods.MapGet("/", async (int companyId, HttpContext context, AccountsDbContext db) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            return Results.Ok(await db.AccountingPeriods
                .Where(p => p.CompanyId == companyId)
                .Include(p => p.SizeClassification)
                .Include(p => p.FilingRegime)
                .OrderByDescending(p => p.PeriodEnd)
                .ToListAsync());
        });

        periods.MapGet("/{id:int}", async (int companyId, int id, HttpContext context, AccountsDbContext db) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            return await db.AccountingPeriods
                .Include(p => p.SizeClassification)
                .Include(p => p.FilingRegime)
                .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId)
            is { } period ? Results.Ok(period) : Results.NotFound();
        });

        periods.MapPost("/", async (int companyId, AccountingPeriodInput input, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            if (EndpointInputs.ValidatePeriod(input) is { } validationProblem)
                return validationProblem;

            var period = EndpointInputs.ToPeriod(companyId, input);
            db.AccountingPeriods.Add(period);
            await db.SaveChangesAsync();
            return Results.Created($"/api/companies/{companyId}/periods/{period.Id}", period);
        });

        periods.MapPut("/{id:int}/status", PeriodStatusEndpoint.UpdateAsync);
    }
}
