using Accounts.Api.Data;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Endpoints;

/// <summary>
/// Recoverable company quarantine boundary. No company or dependent accounting row is deleted.
/// </summary>
public static class CompanyDeletionEndpoint
{
    public static async Task<IResult> DeleteAsync(
        int id,
        [FromBody] CompanyQuarantineRequest? input,
        HttpContext context,
        ApiAccessService apiAccess,
        AccountsDbContext db,
        AccountingWriteGuard writeGuard,
        AuditService audit)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, id))
            return Results.NotFound();
        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        var user = AuthContext.RequireUser(context);
        if (!string.Equals(user.Role, "Owner", StringComparison.Ordinal))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (input is null)
            return Results.BadRequest(new { error = "Typed confirmation and a reason are required." });
        if (await writeGuard.BlockIfCompanyMasterDataLockedAsync(id) is { } blocked)
            return blocked;

        try
        {
            var outcome = await new CompanyQuarantineService(db, audit).QuarantineAsync(
                id,
                input,
                user,
                DomainAuditCoverage.RequestId(context),
                context.RequestAborted);
            return Results.Ok(outcome);
        }
        catch (BusinessRuleException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    public static async Task<IResult> RecoverAsync(
        int id,
        CompanyQuarantineRequest? input,
        HttpContext context,
        ApiAccessService apiAccess,
        AccountsDbContext db,
        AuditService audit)
    {
        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        var user = AuthContext.RequireUser(context);
        var existsForTenant = await db.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(company => company.Id == id && company.TenantId == user.TenantId && company.IsQuarantined);
        if (!existsForTenant)
            return Results.NotFound();
        if (!string.Equals(user.Role, "Owner", StringComparison.Ordinal))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (input is null)
            return Results.BadRequest(new { error = "Typed confirmation and a reason are required." });

        try
        {
            var outcome = await new CompanyQuarantineService(db, audit).RecoverAsync(
                id,
                input,
                user,
                DomainAuditCoverage.RequestId(context),
                context.RequestAborted);
            return Results.Ok(outcome);
        }
        catch (BusinessRuleException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    public static async Task<IResult> ListQuarantinedAsync(
        HttpContext context,
        ApiAccessService apiAccess,
        AccountsDbContext db,
        AuditService audit)
    {
        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;
        var user = AuthContext.RequireUser(context);
        if (!string.Equals(user.Role, "Owner", StringComparison.Ordinal))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        var companies = await new CompanyQuarantineService(db, audit)
            .ListAsync(user, context.RequestAborted);
        return Results.Ok(companies);
    }
}
