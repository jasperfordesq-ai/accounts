using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Endpoints;

public static class AdjustmentEndpoints
{
    private const int AuditLogDefaultPageSize = 50;
    private const int AuditLogMaxPageSize = 100;

    public static void MapAdjustmentEndpoints(this WebApplication app)
    {
        var basePath = "/api/companies/{companyId:int}/periods/{periodId:int}/adjustments";
        var group = app.MapGroup(basePath).WithTags("Adjustments");

        // List all adjustments
        group.MapGet("/", ListAdjustmentsEndpointAsync);

        // Generate auto adjustments
        group.MapPost("/generate", GenerateAdjustmentsEndpointAsync);

        // Create manual adjustment
        group.MapPost("/", CreateAdjustmentEndpointAsync);

        // Update adjustment
        group.MapPut("/{id:int}", UpdateAdjustmentEndpointAsync);

        // Approve adjustment
        group.MapPost("/{id:int}/approve", ApproveAdjustmentEndpointAsync);

        // Delete adjustment
        group.MapDelete("/{id:int}", DeleteAdjustmentEndpointAsync);

        // Adjustment summary
        group.MapGet("/summary", GetAdjustmentSummaryEndpointAsync);

        // Audit log
        var auditGroup = app.MapGroup("/api/companies/{companyId:int}/audit-log").WithTags("Audit");

        auditGroup.MapGet("/", async (int companyId, AccountsDbContext db, HttpContext context, int? periodId, int? page, int? pageSize) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            if (RequireAuditEvidenceAccess(context) is { } denied)
                return denied;

            var query = db.AuditLogs.Where(a => a.CompanyId == companyId);
            if (periodId.HasValue) query = query.Where(a => a.PeriodId == periodId);
            var pageNumber = Math.Max(page ?? 1, 1);
            var take = NormalizeAuditPageSize(pageSize);
            var total = await query.CountAsync();
            var skip = ((long)pageNumber - 1) * take;
            var items = skip >= total
                ? new List<AuditLog>()
                : await query
                    .OrderByDescending(a => a.Timestamp)
                    .Skip((int)skip)
                    .Take(take)
                    .ToListAsync();
            return Results.Ok(new { total, items });
        });

        auditGroup.MapGet("/integrity", async (int companyId, AuditIntegrityService integrity, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            if (RequireAuditEvidenceAccess(context) is { } denied)
                return denied;

            var report = await integrity.VerifyCompanyAsync(companyId);
            return Results.Ok(report);
        });

        auditGroup.MapPost("/integrity/checkpoints", async (int companyId, AuditIntegrityCheckpointService checkpoints, AccountsDbContext db, ApiAccessService apiAccess, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            var user = AuthContext.RequireUser(context);
            if (AuditCheckpointInputs.RequireOwner(user) is { } ownerDenied)
                return ownerDenied;

            try
            {
                var checkpoint = await checkpoints.CreateCompanyCheckpointAsync(
                    companyId,
                    AuthenticatedIdentity.AuditUserId(user),
                    AuthenticatedIdentity.ReviewerDisplayName(user),
                    AuditCheckpointInputs.RequestId(context),
                    user.TenantId,
                    context.RequestAborted);
                return Results.Created(
                    $"/api/companies/{companyId}/audit-log/integrity/checkpoints/{checkpoint.Id}",
                    checkpoint);
            }
            catch (InvalidOperationException)
            {
                return Results.BadRequest(new { error = "Unable to create the audit integrity checkpoint." });
            }
        });

        auditGroup.MapGet("/integrity/checkpoints/latest", async (int companyId, AuditIntegrityCheckpointService checkpoints, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            if (RequireAuditEvidenceAccess(context) is { } denied)
                return denied;

            var verification = await checkpoints.VerifyLatestCompanyCheckpointAsync(companyId);
            return Results.Ok(verification);
        });
    }

    public static async Task<IResult> ListAdjustmentsEndpointAsync(
        int companyId,
        int periodId,
        AccountsDbContext db,
        HttpContext context,
        bool? approved,
        bool? isAuto)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        var query = db.Adjustments
            .Include(a => a.DebitCategory)
            .Include(a => a.CreditCategory)
            .Where(a => a.PeriodId == periodId);

        if (approved == true)
            query = query.Where(a => a.ApprovedAt != null);
        else if (approved == false)
            query = query.Where(a => a.ApprovedAt == null);

        if (isAuto.HasValue)
            query = query.Where(a => a.IsAuto == isAuto.Value);

        var adjustments = await query
            .OrderBy(a => a.IsAuto ? 0 : 1)
            .ThenBy(a => a.CreatedAt)
            .ToListAsync();

        return Results.Ok(adjustments);
    }

    public static async Task<IResult> GenerateAdjustmentsEndpointAsync(
        int companyId,
        int periodId,
        AdjustmentService service,
        AccountsDbContext db,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        if (await BlockIfPeriodLockedAsync(db, companyId, periodId) is { } blocked)
            return blocked;

        try
        {
            var result = await service.GenerateAutoAdjustmentsAsync(companyId, periodId);
            return Results.Ok(result);
        }
        catch (InvalidOperationException)
        {
            return Results.BadRequest(new { error = "Unable to generate automatic adjustments for this accounting period." });
        }
    }

    public static async Task<IResult> GetAdjustmentSummaryEndpointAsync(
        int companyId,
        int periodId,
        AccountsDbContext db,
        HttpContext context)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        var adjustments = await db.Adjustments.Where(a => a.PeriodId == periodId).ToListAsync();
        return Results.Ok(new
        {
            autoGenerated = adjustments.Count(a => a.IsAuto),
            manual = adjustments.Count(a => !a.IsAuto),
            pendingApproval = adjustments.Count(a => a.ApprovedAt == null),
            approved = adjustments.Count(a => a.ApprovedAt != null),
            totalImpactOnProfit = adjustments.Sum(a => a.ImpactOnProfit),
            totalImpactOnAssets = adjustments.Sum(a => a.ImpactOnAssets)
        });
    }

    public static async Task<IResult> CreateAdjustmentEndpointAsync(
        int companyId,
        int periodId,
        AdjustmentInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        if (await BlockIfPeriodLockedAsync(db, companyId, periodId) is { } blocked)
            return blocked;

        var user = AuthContext.RequireUser(context);
        if (await AdjustmentInputs.ValidateAsync(db, companyId, input) is { } validationProblem)
            return validationProblem;

        var adjustment = AdjustmentInputs.ToManualAdjustment(input, periodId, user, DateTime.UtcNow);
        db.Adjustments.Add(adjustment);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Adjustment",
            adjustment.Id,
            AuditEventCodes.AdjustmentCreated,
            newValue: AdjustmentSnapshot(adjustment),
            userId: AuthenticatedIdentity.AuditUserId(user));
        return Results.Created($"/api/companies/{companyId}/periods/{periodId}/adjustments/{adjustment.Id}", adjustment);
    }

    public static async Task<IResult> UpdateAdjustmentEndpointAsync(
        int companyId,
        int periodId,
        int id,
        AdjustmentInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        if (await BlockIfPeriodLockedAsync(db, companyId, periodId) is { } blocked)
            return blocked;

        var user = AuthContext.RequireUser(context);
        var item = await db.Adjustments.FirstOrDefaultAsync(a => a.Id == id && a.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        if (await AdjustmentInputs.ValidateAsync(db, companyId, input) is { } validationProblem)
            return validationProblem;

        var oldValue = AdjustmentSnapshot(item);
        AdjustmentInputs.ApplyInput(item, input);
        item.ApprovedBy = null;
        item.ApprovedAt = null;

        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Adjustment",
            id,
            AuditEventCodes.AdjustmentUpdated,
            oldValue,
            AdjustmentSnapshot(item),
            userId: AuthenticatedIdentity.AuditUserId(user));
        return Results.Ok(item);
    }

    public static async Task<IResult> ApproveAdjustmentEndpointAsync(
        int companyId,
        int periodId,
        int id,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        if (await BlockIfPeriodLockedAsync(db, companyId, periodId) is { } blocked)
            return blocked;

        var user = AuthContext.RequireUser(context);
        var item = await db.Adjustments.FirstOrDefaultAsync(a => a.Id == id && a.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        var oldValue = AdjustmentSnapshot(item);
        item.ApprovedBy = AuthenticatedIdentity.ReviewerDisplayName(user);
        item.ApprovedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Adjustment",
            id,
            AuditEventCodes.AdjustmentApproved,
            oldValue,
            AdjustmentSnapshot(item),
            userId: AuthenticatedIdentity.AuditUserId(user));
        return Results.Ok(item);
    }

    public static async Task<IResult> DeleteAdjustmentEndpointAsync(
        int companyId,
        int periodId,
        int id,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        if (await BlockIfPeriodLockedAsync(db, companyId, periodId) is { } blocked)
            return blocked;

        var user = AuthContext.RequireUser(context);
        var item = await db.Adjustments.FirstOrDefaultAsync(a => a.Id == id && a.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        var oldValue = AdjustmentSnapshot(item);
        db.Adjustments.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Adjustment",
            id,
            AuditEventCodes.AdjustmentDeleted,
            oldValue,
            new { Deleted = true },
            userId: AuthenticatedIdentity.AuditUserId(user));
        return Results.NoContent();
    }

    private static IResult? RequireAuditEvidenceAccess(HttpContext context)
    {
        var user = AuthContext.RequireUser(context);
        return user.Role.Trim().Equals("Client", StringComparison.OrdinalIgnoreCase)
            ? Results.StatusCode(StatusCodes.Status403Forbidden)
            : null;
    }

    private static int NormalizeAuditPageSize(int? pageSize)
    {
        if (pageSize is null or <= 0)
            return AuditLogDefaultPageSize;

        return Math.Min(pageSize.Value, AuditLogMaxPageSize);
    }

    private static async Task<IResult?> BlockIfPeriodLockedAsync(
        AccountsDbContext db,
        int companyId,
        int periodId)
    {
        var period = await db.AccountingPeriods
            .AsNoTracking()
            .Where(p => p.Id == periodId && p.CompanyId == companyId)
            .Select(p => new { p.Status, p.LockedAt })
            .FirstOrDefaultAsync();
        if (period is null)
            return Results.NotFound();

        return period.Status is PeriodStatus.Finalised or PeriodStatus.Filed || period.LockedAt is not null
            ? Results.Conflict(new { error = "Accounting period is locked. Reopen the period before changing adjustments." })
            : null;
    }

    private static object AdjustmentSnapshot(Adjustment adjustment) => new
    {
        adjustment.Description,
        adjustment.DebitCategoryId,
        adjustment.CreditCategoryId,
        adjustment.Amount,
        adjustment.Source,
        adjustment.Reason,
        adjustment.LegalBasis,
        adjustment.ImpactOnProfit,
        adjustment.ImpactOnAssets,
        adjustment.CreatedBy,
        adjustment.ApprovedBy,
        adjustment.ApprovedAt,
        adjustment.IsAuto
    };

    private static Task<bool> PeriodBelongsToCompanyAsync(AccountsDbContext db, int companyId, int periodId) =>
        db.AccountingPeriods.AnyAsync(p => p.Id == periodId && p.CompanyId == companyId);
}

public record ApprovalInput(string ApprovedBy);
public record AdjustmentInput(
    string Description,
    int? DebitCategoryId,
    int? CreditCategoryId,
    decimal Amount,
    string? Reason,
    string? LegalBasis,
    decimal ImpactOnProfit,
    decimal ImpactOnAssets);

public static class AdjustmentInputs
{
    public static void PrepareManualAdjustment(Adjustment input, int periodId, AuthenticatedUser user, DateTime now)
    {
        input.PeriodId = periodId;
        input.IsAuto = false;
        input.Source = AdjustmentSource.Manual;
        input.CreatedBy = AuthenticatedIdentity.ReviewerDisplayName(user);
        input.CreatedAt = now;
        input.ApprovedBy = null;
        input.ApprovedAt = null;
    }

    public static async Task<IResult?> ValidateAsync(AccountsDbContext db, int companyId, AdjustmentInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Description))
            return Results.BadRequest(new { error = "Adjustment description is required." });

        if (input.DebitCategoryId is null && input.CreditCategoryId is null)
            return Results.BadRequest(new { error = "Select at least one debit or credit category." });

        var categoryIds = new[] { input.DebitCategoryId, input.CreditCategoryId }
            .OfType<int>()
            .Distinct()
            .ToArray();
        var validCount = await db.AccountCategories.CountAsync(c =>
            categoryIds.Contains(c.Id)
            && (c.CompanyId == companyId || (c.IsSystem && c.CompanyId == null)));

        return validCount == categoryIds.Length
            ? null
            : Results.BadRequest(new { error = "Adjustment categories must be available for this company." });
    }

    public static Adjustment ToManualAdjustment(AdjustmentInput input, int periodId, AuthenticatedUser user, DateTime now)
    {
        var adjustment = new Adjustment
        {
            Description = input.Description.Trim()
        };
        ApplyInput(adjustment, input);
        PrepareManualAdjustment(adjustment, periodId, user, now);
        return adjustment;
    }

    public static void ApplyInput(Adjustment adjustment, AdjustmentInput input)
    {
        adjustment.Description = input.Description.Trim();
        adjustment.DebitCategoryId = input.DebitCategoryId;
        adjustment.CreditCategoryId = input.CreditCategoryId;
        adjustment.Amount = input.Amount;
        adjustment.Reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim();
        adjustment.LegalBasis = string.IsNullOrWhiteSpace(input.LegalBasis) ? null : input.LegalBasis.Trim();
        adjustment.ImpactOnProfit = input.ImpactOnProfit;
        adjustment.ImpactOnAssets = input.ImpactOnAssets;
    }
}

public static class AuditCheckpointInputs
{
    public static IResult? RequireOwner(AuthenticatedUser user) =>
        user.Role.Trim().Equals("Owner", StringComparison.OrdinalIgnoreCase)
            ? null
            : Results.StatusCode(StatusCodes.Status403Forbidden);

    public static string? RequestId(HttpContext context)
    {
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(correlationId))
            return correlationId;

        var requestId = context.Request.Headers["X-Request-ID"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(requestId))
            return requestId;

        return context.TraceIdentifier;
    }
}
