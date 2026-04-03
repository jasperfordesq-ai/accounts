using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Endpoints;

public static class ClassificationEndpoints
{
    public static void MapClassificationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/companies/{companyId:int}/periods/{periodId:int}").WithTags("Classification");

        // Save size classification data
        group.MapPut("/size-classification", async (int companyId, int periodId, SizeClassificationInput input, AccountsDbContext db) =>
        {
            var period = await db.AccountingPeriods
                .Include(p => p.SizeClassification)
                .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId);
            if (period == null) return Results.NotFound();

            var sc = period.SizeClassification;
            if (sc == null)
            {
                sc = new SizeClassification { PeriodId = periodId };
                db.SizeClassifications.Add(sc);
            }

            sc.Turnover = input.Turnover;
            sc.BalanceSheetTotal = input.BalanceSheetTotal;
            sc.AvgEmployees = input.AvgEmployees;
            sc.PriorYearClass = input.PriorYearClass;

            await db.SaveChangesAsync();
            return Results.Ok(sc);
        });

        // Run classification engine
        group.MapPost("/classify", async (int companyId, int periodId, SizeClassificationService service) =>
        {
            try
            {
                var result = await service.ClassifyAsync(periodId);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Determine filing regime
        group.MapPost("/filing-regime", async (int companyId, int periodId, FilingRegimeInput? input, FilingRegimeService service) =>
        {
            try
            {
                var result = await service.DetermineAsync(periodId, input?.ElectedRegime);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Get current filing regime
        group.MapGet("/filing-regime", async (int companyId, int periodId, AccountsDbContext db) =>
        {
            var fr = await db.FilingRegimes.FirstOrDefaultAsync(f => f.PeriodId == periodId);
            return fr != null ? Results.Ok(fr) : Results.NotFound();
        });

        // Save member audit notice (s.334 Companies Act 2014)
        group.MapPut("/member-audit-notice", async (int companyId, int periodId, MemberAuditNoticeInput input, AccountsDbContext db) =>
        {
            var period = await db.AccountingPeriods
                .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId);
            if (period == null) return Results.NotFound();

            period.MemberAuditNoticeReceived = input.Received;
            period.MemberAuditNoticeDate = input.NoticeDate;
            await db.SaveChangesAsync();
            return Results.Ok(new { period.MemberAuditNoticeReceived, period.MemberAuditNoticeDate });
        });
    }
}

public record SizeClassificationInput(decimal Turnover, decimal BalanceSheetTotal, int AvgEmployees, CompanySizeClass? PriorYearClass);
public record FilingRegimeInput(ElectedRegime? ElectedRegime);
public record MemberAuditNoticeInput(bool Received, DateOnly? NoticeDate);
