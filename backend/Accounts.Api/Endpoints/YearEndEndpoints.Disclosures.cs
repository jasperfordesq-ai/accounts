using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Accounts.Api.Endpoints;

public static partial class YearEndEndpoints
{
    public static void StampOpeningBalanceIdentity(
        OpeningBalance balance,
        OpeningBalanceInput input,
        AuthenticatedUser user,
        DateTime timestamp)
    {
        var reviewerName = AuthenticatedIdentity.ReviewerDisplayName(user);
        balance.EnteredBy = reviewerName;
        balance.EnteredAt = timestamp;
        balance.Reviewed = input.Reviewed;
        balance.ReviewedBy = input.Reviewed ? reviewerName : null;
        balance.ReviewedAt = input.Reviewed ? timestamp : null;
    }

    public static async Task<IResult> UpsertOpeningBalanceEndpointAsync(
        int companyId,
        int periodId,
        int categoryId,
        OpeningBalanceInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var user = AuthContext.RequireUser(context);

        if (input.Debit < 0 || input.Credit < 0)
            return Results.BadRequest(new { error = "Opening balance debit and credit must not be negative." });
        if (input.Debit > 0 && input.Credit > 0)
            return Results.BadRequest(new { error = "Enter either a debit or a credit opening balance, not both." });

        var category = await db.AccountCategories
            .FirstOrDefaultAsync(c => c.Id == categoryId
                && (c.CompanyId == companyId || (c.IsSystem && c.CompanyId == null)));
        if (category is null)
            return Results.BadRequest(new { error = "Account category is not available for this company." });

        // accounting-opening-balance-pl-accounts: an opening balance on an income/expense account folds
        // a brought-forward figure into the current year's turnover or expenses (what a mid-year
        // migration does), silently mis-stating the P&L. Brought-forward profit/loss belongs in retained
        // earnings; opening balances are only valid on balance-sheet accounts.
        if (category.Type is AccountCategoryType.Income or AccountCategoryType.Expense)
            return Results.BadRequest(new
            {
                error = "Opening balances cannot be posted to income or expense accounts. "
                    + "Carry the brought-forward profit or loss to retained earnings instead."
            });

        var balance = await db.OpeningBalances
            .FirstOrDefaultAsync(o => o.PeriodId == periodId && o.AccountCategoryId == categoryId);
        var wasCreated = balance is null;
        var oldValue = balance is null ? null : OpeningBalanceSnapshot(balance);

        if (balance == null)
        {
            balance = new OpeningBalance
            {
                PeriodId = periodId,
                AccountCategoryId = categoryId
            };
            db.OpeningBalances.Add(balance);
        }

        balance.Debit = input.Debit;
        balance.Credit = input.Credit;
        balance.SourceNote = string.IsNullOrWhiteSpace(input.SourceNote) ? null : input.SourceNote.Trim();
        StampOpeningBalanceIdentity(balance, input, user, DateTime.UtcNow);

        await db.SaveChangesAsync();
        await db.Entry(balance).Reference(b => b.AccountCategory).LoadAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "OpeningBalance",
            balance.Id,
            AuditEventCodes.OpeningBalanceUpserted,
            oldValue,
            new
            {
                balance.AccountCategoryId,
                balance.Debit,
                balance.Credit,
                balance.SourceNote,
                balance.EnteredBy,
                balance.EnteredAt,
                balance.Reviewed,
                balance.ReviewedBy,
                balance.ReviewedAt,
                WasCreated = wasCreated
            },
            AuthenticatedIdentity.AuditUserId(user));
        return Results.Ok(balance);
    }

    public static async Task<IResult> DeleteOpeningBalanceEndpointAsync(
        int companyId,
        int periodId,
        int categoryId,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        if (!await AccountCategoryAvailableToCompanyAsync(db, companyId, categoryId))
            return Results.BadRequest(new { error = "Account category is not available for this company." });

        var balance = await db.OpeningBalances
            .FirstOrDefaultAsync(o => o.PeriodId == periodId && o.AccountCategoryId == categoryId);
        if (balance == null) return Results.NotFound();

        var oldValue = OpeningBalanceSnapshot(balance);
        db.OpeningBalances.Remove(balance);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "OpeningBalance",
            balance.Id,
            AuditEventCodes.OpeningBalanceDeleted,
            oldValue,
            new { Deleted = true },
            AuditUserId(context));
        return Results.NoContent();
    }

    public static async Task<IResult> CreateNoteEndpointAsync(
        int companyId,
        int periodId,
        NotesDisclosure input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;
        if (ValidateNoteInput(input) is { } validationError)
            return validationError;

        input.PeriodId = periodId;
        input.IsRequired = false;
        var maxNum = await db.NotesDisclosures.Where(n => n.PeriodId == periodId).MaxAsync(n => (int?)n.NoteNumber) ?? 0;
        input.NoteNumber = maxNum + 1;
        db.NotesDisclosures.Add(input);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "NotesDisclosure",
            input.Id,
            AuditEventCodes.NoteDisclosureCreated,
            null,
            NoteSummarySnapshot(input),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/periods/{periodId}/notes/{input.Id}", input);
    }

    public static async Task<IResult> DeleteNoteEndpointAsync(
        int companyId,
        int periodId,
        int id,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var note = await db.NotesDisclosures.FirstOrDefaultAsync(n => n.Id == id && n.PeriodId == periodId);
        if (note == null) return Results.NotFound();
        if (note.IsRequired)
            return Results.BadRequest(new { error = "Required generated notes cannot be deleted. Exclude the note from the accounts instead." });

        var oldValue = NoteSummarySnapshot(note);
        db.NotesDisclosures.Remove(note);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "NotesDisclosure",
            id,
            AuditEventCodes.NoteDisclosureDeleted,
            oldValue,
            new { Deleted = true },
            AuditUserId(context));
        return Results.NoContent();
    }

    public static async Task<IResult> CreatePostBalanceSheetEventEndpointAsync(
        int companyId,
        int periodId,
        PostBalanceSheetEvent input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        input.PeriodId = periodId;
        db.PostBalanceSheetEvents.Add(input);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "PostBalanceSheetEvent",
            input.Id,
            AuditEventCodes.PostBalanceSheetEventCreated,
            null,
            PostBalanceSheetEventSnapshot(input),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/periods/{periodId}/post-balance-sheet-events/{input.Id}", input);
    }

    public static async Task<IResult> DeletePostBalanceSheetEventEndpointAsync(
        int companyId,
        int periodId,
        int id,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.PostBalanceSheetEvents.FirstOrDefaultAsync(x => x.Id == id && x.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        var oldValue = PostBalanceSheetEventSnapshot(item);
        db.PostBalanceSheetEvents.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "PostBalanceSheetEvent",
            id,
            AuditEventCodes.PostBalanceSheetEventDeleted,
            oldValue,
            new { Deleted = true },
            AuditUserId(context));
        return Results.NoContent();
    }

    public static async Task<IResult> CreateRelatedPartyTransactionEndpointAsync(
        int companyId,
        int periodId,
        RelatedPartyTransaction input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        input.PeriodId = periodId;
        db.RelatedPartyTransactions.Add(input);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "RelatedPartyTransaction",
            input.Id,
            AuditEventCodes.RelatedPartyTransactionCreated,
            null,
            RelatedPartyTransactionSnapshot(input),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/periods/{periodId}/related-party-transactions/{input.Id}", input);
    }

    public static async Task<IResult> DeleteRelatedPartyTransactionEndpointAsync(
        int companyId,
        int periodId,
        int id,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.RelatedPartyTransactions.FirstOrDefaultAsync(x => x.Id == id && x.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        var oldValue = RelatedPartyTransactionSnapshot(item);
        db.RelatedPartyTransactions.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "RelatedPartyTransaction",
            id,
            AuditEventCodes.RelatedPartyTransactionDeleted,
            oldValue,
            new { Deleted = true },
            AuditUserId(context));
        return Results.NoContent();
    }

    public static async Task<IResult> CreateContingentLiabilityEndpointAsync(
        int companyId,
        int periodId,
        ContingentLiability input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        input.PeriodId = periodId;
        db.ContingentLiabilities.Add(input);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "ContingentLiability",
            input.Id,
            AuditEventCodes.ContingentLiabilityCreated,
            null,
            ContingentLiabilitySnapshot(input),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/periods/{periodId}/contingent-liabilities/{input.Id}", input);
    }

    public static async Task<IResult> DeleteContingentLiabilityEndpointAsync(
        int companyId,
        int periodId,
        int id,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.ContingentLiabilities.FirstOrDefaultAsync(x => x.Id == id && x.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        var oldValue = ContingentLiabilitySnapshot(item);
        db.ContingentLiabilities.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "ContingentLiability",
            id,
            AuditEventCodes.ContingentLiabilityDeleted,
            oldValue,
            new { Deleted = true },
            AuditUserId(context));
        return Results.NoContent();
    }

    public static async Task<IResult> UpdateGoingConcernEndpointAsync(
        int companyId,
        int periodId,
        GoingConcernInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var period = await db.AccountingPeriods.FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId);
        if (period == null) return Results.NotFound();

        var oldValue = GoingConcernSnapshot(period);
        period.GoingConcernConfirmed = input.Confirmed;
        period.GoingConcernNote = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "AccountingPeriod",
            period.Id,
            AuditEventCodes.GoingConcernUpdated,
            oldValue,
            GoingConcernSnapshot(period),
            AuditUserId(context));
        return Results.Ok(new { period.GoingConcernConfirmed, period.GoingConcernNote });
    }

    public static async Task<IResult> UpdateYearEndReviewEndpointAsync(
        int companyId,
        int periodId,
        string sectionKey,
        YearEndReviewInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var user = AuthContext.RequireUser(context);
        if (!ReviewSectionKeys.Contains(sectionKey))
            return Results.BadRequest(new { error = "Unknown year-end review section." });

        var confirmation = await db.YearEndReviewConfirmations
            .FirstOrDefaultAsync(r => r.PeriodId == periodId && r.SectionKey == sectionKey);
        var oldValue = confirmation is null ? null : YearEndReviewSnapshot(confirmation);

        if (confirmation == null)
        {
            confirmation = new YearEndReviewConfirmation
            {
                PeriodId = periodId,
                SectionKey = sectionKey
            };
            db.YearEndReviewConfirmations.Add(confirmation);
        }

        confirmation.Confirmed = input.Confirmed;
        confirmation.ConfirmedBy = AuthenticatedIdentity.ReviewerDisplayName(user);
        confirmation.ConfirmedAt = DateTime.UtcNow;
        confirmation.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();

        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "YearEndReviewConfirmation",
            confirmation.Id,
            AuditEventCodes.YearEndReviewConfirmationUpdated,
            oldValue,
            YearEndReviewSnapshot(confirmation),
            AuthenticatedIdentity.AuditUserId(user));
        return Results.Ok(confirmation);
    }

    public static async Task<IResult> GenerateNotesEndpointAsync(
        int companyId,
        int periodId,
        NotesDisclosureService service,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var existing = await db.NotesDisclosures
            .AsNoTracking()
            .Where(n => n.PeriodId == periodId)
            .OrderBy(n => n.NoteNumber)
            .ToListAsync();

        var notes = await service.GenerateNotesAsync(companyId, periodId);
        await audit.LogAsync(
            companyId,
            periodId,
            "NotesDisclosureBatch",
            periodId,
            AuditEventCodes.NotesGenerated,
            new
            {
                ExistingCount = existing.Count,
                Notes = existing.Select(NoteSummarySnapshot).ToList()
            },
            new
            {
                GeneratedCount = notes.Count,
                Notes = notes.OrderBy(n => n.NoteNumber).Select(NoteSummarySnapshot).ToList()
            },
            AuditUserId(context));
        return Results.Ok(notes);
    }

    public static async Task<IResult> UpdateNoteEndpointAsync(
        int companyId,
        int periodId,
        int id,
        NotesDisclosure input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var note = await db.NotesDisclosures.FirstOrDefaultAsync(n => n.Id == id && n.PeriodId == periodId);
        if (note == null) return Results.NotFound();
        if (ValidateNoteInput(input) is { } validationError)
            return validationError;

        var oldValue = NoteSummarySnapshot(note);
        note.Title = input.Title;
        note.Content = input.Content;
        note.IsIncluded = input.IsIncluded;
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "NotesDisclosure",
            note.Id,
            AuditEventCodes.NoteDisclosureUpdated,
            oldValue,
            NoteSummarySnapshot(note),
            AuditUserId(context));
        return Results.Ok(note);
    }

}
