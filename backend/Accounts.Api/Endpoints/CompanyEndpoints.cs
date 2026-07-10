using Microsoft.EntityFrameworkCore;
using Accounts.Api.Data;
using Accounts.Api.Entities;
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

        companies.MapGet("/quarantined", CompanyDeletionEndpoint.ListQuarantinedAsync);
        companies.MapPost("/onboard", CompanyOnboardingEndpoint.CreateAsync);
        companies.MapPost("/{id:int}/recover", CompanyDeletionEndpoint.RecoverAsync);

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

        companies.MapPost("/", async (CompanyInput input, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, AnnualReturnDateService annualReturnDateService, IdempotencyService idempotency, AuditService audit) =>
        {
            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            if (EndpointInputs.ValidateCompany(input) is { } validationProblem)
                return validationProblem;

            var user = AuthContext.RequireUser(context);
            try
            {
                var command = await IdempotencyHttpContract.ExecuteAsync(
                    context,
                    idempotency,
                    user,
                    IdempotencyOperations.CompanyCreate,
                    input,
                    async cancellationToken =>
                    {
                        var company = EndpointInputs.ToCompany(input);
                        company.TenantId = user.TenantId;
                        db.Companies.Add(company);
                        annualReturnDateService.PrepareInitial(
                            company,
                            EndpointInputs.ToAnnualReturnDateChange(input),
                            user);
                        await db.SaveChangesAsync(cancellationToken);
                        await DomainAuditCoverage.LogAsync(
                            audit,
                            context,
                            company.Id,
                            null,
                            nameof(Company),
                            company.Id,
                            AuditEventCodes.CompanyCreated,
                            null,
                            DomainAuditCoverage.CompanySnapshot(company),
                            cancellationToken);
                        return new IdempotencyOperationOutcome<Company>(
                            company,
                            nameof(Company),
                            company.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            StatusCodes.Status201Created);
                    });
                if (command.Error is not null)
                    return command.Error;
                var execution = command.Execution!;
                return IdempotencyHttpContract.JsonResult(
                    execution,
                    $"/api/companies/{execution.Result.Id}");
            }
            catch (AnnualReturnDateValidationException ex)
            {
                return Results.ValidationProblem(ex.Errors);
            }
        });

        companies.MapPut("/{id:int}", async (int id, CompanyInput input, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, AccountingWriteGuard writeGuard, AuditService audit) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, id))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            if (EndpointInputs.ValidateCompany(input) is { } validationProblem)
                return validationProblem;

            var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == id);
            if (company is null) return Results.NotFound();

            if (input.AnnualReturnDate != company.AnnualReturnDate)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["annualReturnDate"] = ["Use the evidence-backed Annual Return Date change action; company profile edits cannot replace an ARD silently."]
                });
            }

            if (await writeGuard.BlockIfCompanyMasterDataLockedAsync(id) is { } blocked)
                return blocked;

            var oldValue = DomainAuditCoverage.CompanySnapshot(company);
            EndpointInputs.ApplyCompany(company, input);
            await InvalidateCompanyCharityArtifactsAsync(db, id);

            await db.SaveChangesAsync();
            await DomainAuditCoverage.LogAsync(
                audit,
                context,
                id,
                null,
                nameof(Company),
                id,
                AuditEventCodes.CompanyUpdated,
                oldValue,
                DomainAuditCoverage.CompanySnapshot(company),
                context.RequestAborted);
            return Results.Ok(company);
        });

        companies.MapGet("/{id:int}/annual-return-dates", async (
            int id,
            HttpContext context,
            AccountsDbContext db,
            AnnualReturnDateService service,
            CancellationToken cancellationToken) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, id))
                return Results.NotFound();

            return Results.Ok(await service.GetHistoryAsync(id, cancellationToken));
        });

        companies.MapPost("/{id:int}/annual-return-dates", async (
            int id,
            AnnualReturnDateChangeInput input,
            HttpContext context,
            ApiAccessService apiAccess,
            AccountsDbContext db,
            AnnualReturnDateService service,
            CancellationToken cancellationToken) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, id))
                return Results.NotFound();
            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            try
            {
                var actor = AuthContext.RequireUser(context);
                return Results.Ok(await service.RecordChangeAsync(id, input, actor, cancellationToken));
            }
            catch (AnnualReturnDateValidationException ex)
            {
                return Results.ValidationProblem(ex.Errors);
            }
        });

        companies.MapDelete("/{id:int}", CompanyDeletionEndpoint.DeleteAsync);

        // Officers endpoints
        var officers = app.MapGroup("/api/companies/{companyId:int}/officers").WithTags("Officers");

        officers.MapGet("/", async (int companyId, HttpContext context, AccountsDbContext db) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            return Results.Ok(await db.CompanyOfficers.Where(o => o.CompanyId == companyId).ToListAsync());
        });

        officers.MapPost("/", async (int companyId, CompanyOfficerInput input, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, AccountingWriteGuard writeGuard, AuditService audit) =>
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
            await InvalidateCompanyCharityPackagesAsync(db, companyId);
            await db.SaveChangesAsync();
            await DomainAuditCoverage.LogAsync(
                audit, context, companyId, null, nameof(CompanyOfficer), officer.Id,
                AuditEventCodes.CompanyOfficerCreated, null, DomainAuditCoverage.OfficerSnapshot(officer),
                context.RequestAborted);
            return Results.Created($"/api/companies/{companyId}/officers/{officer.Id}", officer);
        });

        officers.MapPut("/{id:int}", async (int companyId, int id, CompanyOfficerInput input, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, AccountingWriteGuard writeGuard, AuditService audit) =>
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
            var oldValue = DomainAuditCoverage.OfficerSnapshot(officer);
            EndpointInputs.ApplyOfficer(officer, input);
            await InvalidateCompanyCharityPackagesAsync(db, companyId);
            await db.SaveChangesAsync();
            await DomainAuditCoverage.LogAsync(
                audit, context, companyId, null, nameof(CompanyOfficer), officer.Id,
                AuditEventCodes.CompanyOfficerUpdated, oldValue, DomainAuditCoverage.OfficerSnapshot(officer),
                context.RequestAborted);
            return Results.Ok(officer);
        });

        officers.MapDelete("/{id:int}", async (int companyId, int id, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, AccountingWriteGuard writeGuard, AuditService audit) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            if (await writeGuard.BlockIfCompanyMasterDataLockedAsync(companyId) is { } blocked)
                return blocked;

            var officer = await db.CompanyOfficers.FirstOrDefaultAsync(o => o.Id == id && o.CompanyId == companyId);
            if (officer is null) return Results.NotFound();
            var oldValue = DomainAuditCoverage.OfficerSnapshot(officer);
            db.CompanyOfficers.Remove(officer);
            await InvalidateCompanyCharityPackagesAsync(db, companyId);
            await db.SaveChangesAsync();
            await DomainAuditCoverage.LogAsync(
                audit, context, companyId, null, nameof(CompanyOfficer), id,
                AuditEventCodes.CompanyOfficerDeleted, oldValue, null,
                context.RequestAborted);
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

        periods.MapPost("/", async (int companyId, AccountingPeriodInput input, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, PeriodChronologyService chronology, IdempotencyService idempotency, AuditService audit) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            if (EndpointInputs.ValidatePeriod(input) is { } validationProblem)
                return validationProblem;

            var user = AuthContext.RequireUser(context);
            var command = await IdempotencyHttpContract.ExecuteAsync(
                context,
                idempotency,
                user,
                IdempotencyOperations.PeriodCreate,
                new { companyId, input },
                async cancellationToken =>
                {
                    var period = EndpointInputs.ToPeriod(companyId, input);
                    await chronology.CreateAsync(period, cancellationToken);
                    await DomainAuditCoverage.LogAsync(
                        audit, context, companyId, period.Id, nameof(AccountingPeriod), period.Id,
                        AuditEventCodes.AccountingPeriodCreated, null, DomainAuditCoverage.PeriodSnapshot(period),
                        cancellationToken);
                    return new IdempotencyOperationOutcome<AccountingPeriod>(
                        period,
                        nameof(AccountingPeriod),
                        period.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        StatusCodes.Status201Created);
                });
            if (command.Error is not null)
                return command.Error;
            var execution = command.Execution!;
            return IdempotencyHttpContract.JsonResult(
                execution,
                $"/api/companies/{companyId}/periods/{execution.Result.Id}");
        });

        periods.MapPut("/{id:int}/status", PeriodStatusEndpoint.UpdateAsync);
    }

    private static async Task InvalidateCompanyCharityPackagesAsync(AccountsDbContext db, int companyId)
    {
        var packages = await db.CharityFilingPackages
            .Where(p => p.Period.CompanyId == companyId)
            .ToListAsync();
        foreach (var package in packages)
            CharityReportingService.InvalidateTrusteeReview(package);
    }

    private static async Task InvalidateCompanyCharityArtifactsAsync(AccountsDbContext db, int companyId)
    {
        var packages = await db.CharityFilingPackages
            .Where(p => p.Period.CompanyId == companyId)
            .ToListAsync();
        foreach (var package in packages)
            CharityReportingService.InvalidateArtifacts(package);
    }
}
