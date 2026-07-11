using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Accounts.Api.Endpoints;

public static partial class YearEndEndpoints
{
    private static string? AuditUserId(HttpContext context)
    {
        var user = AuthContext.GetUser(context);
        return user is null ? null : AuthenticatedIdentity.AuditUserId(user);
    }

    private static void StampLoanBalanceSnapshot(LoanBalanceSnapshot snapshot, int periodId, HttpContext context)
    {
        snapshot.PeriodId = periodId;
        snapshot.EnteredAt = DateTime.UtcNow;

        var user = AuthContext.GetUser(context);
        snapshot.EnteredBy = user is null
            ? null
            : AuthenticatedIdentity.ReviewerDisplayName(user);
    }

    private static void StampDirectorLoanReview(DirectorLoan loan, HttpContext context)
    {
        if (loan.ReviewDecision == DirectorLoanReviewDecision.Unreviewed)
        {
            loan.ReviewedBy = null;
            loan.ReviewerRole = null;
            loan.ReviewedAtUtc = null;
            return;
        }

        var user = AuthContext.GetUser(context);
        loan.ReviewedBy = user is null ? null : AuthenticatedIdentity.ReviewerDisplayName(user);
        loan.ReviewerRole = user?.Role;
        loan.ReviewedAtUtc = user is null ? null : DateTime.UtcNow;
    }

    private static async Task<IResult?> RequireCompanyWriteAccessAsync(
        AccountsDbContext db,
        int companyId,
        HttpContext context)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
            return Results.NotFound();

        return AuthorizeCurrentWriteRequest(context);
    }

    private static async Task<IResult?> RequirePeriodWriteAccessAsync(
        AccountsDbContext db,
        int companyId,
        int periodId,
        HttpContext context)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        if (AuthorizeCurrentWriteRequest(context) is { } denied)
        {
            return denied;
        }

        var period = await db.AccountingPeriods
            .AsNoTracking()
            .Where(p => p.Id == periodId && p.CompanyId == companyId)
            .Select(p => new { p.Status, p.LockedAt })
            .FirstOrDefaultAsync();

        return period is null
            ? Results.NotFound()
            : period.Status is PeriodStatus.Finalised or PeriodStatus.Filed || period.LockedAt is not null
                ? Results.Conflict(new { error = "Accounting period is locked. Reopen the period before changing year-end evidence." })
                : null;
    }

    private static IResult? AuthorizeCurrentWriteRequest(HttpContext context)
    {
        var apiAccess = ResolveApiAccess(context);
        if (apiAccess is not null)
            return EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess);

        var user = AuthContext.GetUser(context);
        if (user is null)
            return null;

        var roleDecision = RoleAuthorizationService.Authorize(user, context.Request.Path, context.Request.Method);
        return roleDecision.IsAllowed
            ? null
            : Results.Json(new { error = roleDecision.DenialReason }, statusCode: StatusCodes.Status403Forbidden);
    }

    private static ApiAccessService? ResolveApiAccess(HttpContext context) =>
        context.Features
            .Get<IServiceProvidersFeature>()
            ?.RequestServices
            ?.GetService<ApiAccessService>();

    private static async Task<IResult> ListPeriodOwnedRowsAsync<T>(
        AccountsDbContext db,
        int companyId,
        int periodId,
        HttpContext context,
        IQueryable<T> query)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        return Results.Ok(await query.ToListAsync());
    }

    private static Task<bool> AccountCategoryAvailableToCompanyAsync(AccountsDbContext db, int companyId, int categoryId) =>
        db.AccountCategories.AnyAsync(c => c.Id == categoryId && (c.CompanyId == companyId || (c.IsSystem && c.CompanyId == null)));

    private static object OpeningBalanceSnapshot(OpeningBalance balance) => new
    {
        balance.AccountCategoryId,
        balance.Debit,
        balance.Credit,
        balance.SourceNote,
        balance.EnteredBy,
        balance.EnteredAt,
        balance.Reviewed,
        balance.ReviewedBy,
        balance.ReviewedAt
    };

    private static object FixedAssetSnapshot(FixedAsset item) => new
    {
        item.Name,
        item.Category,
        item.Cost,
        item.ResidualValue,
        item.AcquisitionDate,
        item.DisposalDate,
        item.DisposalProceeds,
        item.UsefulLifeYears,
        item.DepreciationMethod,
        item.CapitalAllowanceTreatment,
        item.CapitalAllowanceEvidence,
        item.CapitalAllowanceReviewedBy,
        item.CapitalAllowanceReviewedAtUtc
    };

    private static object LoanSnapshot(Loan item) => new
    {
        item.Lender,
        item.OriginalAmount,
        item.Balance,
        item.DrawdownDate,
        item.BalanceAsOfDate,
        item.InterestRate,
        item.IsDirectorLoan,
        item.DueWithinYear,
        item.DueAfterYear
    };

    private static object LoanBalanceSnapshotSnapshot(LoanBalanceSnapshot item) => new
    {
        item.LoanId,
        item.OpeningBalance,
        item.Drawdowns,
        item.Repayments,
        item.ClosingBalance,
        item.DueWithinYear,
        item.DueAfterYear,
        item.Notes,
        item.EnteredBy,
        item.EnteredAt
    };

    private static object DirectorLoanSnapshot(DirectorLoan item) => new
    {
        item.DirectorId,
        item.CounterpartyType,
        item.CounterpartyName,
        item.ArrangementType,
        item.ArrangementDate,
        item.OpeningBalance,
        item.Advances,
        item.Repayments,
        item.ClosingBalance,
        item.TermsStatus,
        item.InterestRate,
        item.InterestCharged,
        item.AllowanceMade,
        item.Section236PresumptionEvidenceReference,
        item.IsDocumented,
        item.LoanTerms,
        item.MaxBalanceDuringYear,
        item.ComplianceBasis,
        item.RelevantAssetsBasis,
        item.RelevantAssetsAmount,
        item.RelevantAssetsAsOfDate,
        item.RelevantAssetsReference,
        item.NoPriorFinancialStatementsConfirmed,
        item.RelevantAssetsFallReview,
        item.RelevantAssetsReductionAwarenessDate,
        item.TermsAmendedDate,
        item.TermsAmendmentEvidenceReference,
        item.ExceptionEvidenceReference,
        item.SapDeclarationDate,
        item.SapResolutionDate,
        item.SapActivityStartDate,
        item.SapCroFilingDate,
        item.SapDeclarationReference,
        item.SapResolutionReference,
        item.SapCroFilingReference,
        item.SapDeclarationCoversSection203Matters,
        item.ExpenseIncurredDate,
        item.ExpenseDischargedDate,
        item.OrdinaryCourseConfirmed,
        item.NoMoreFavourableTermsConfirmed,
        item.ReviewDecision,
        item.ReviewNote,
        item.ReviewedBy,
        item.ReviewerRole,
        item.ReviewedAtUtc,
        BalanceMovements = item.BalanceMovements
            .OrderBy(movement => movement.MovementDate)
            .ThenBy(movement => movement.Id)
            .Select(movement => new
            {
                movement.MovementDate,
                movement.MovementType,
                movement.Amount,
                movement.EvidenceReference
            })
            .ToArray()
    };

    private static object PostBalanceSheetEventSnapshot(PostBalanceSheetEvent item) => new
    {
        item.Description,
        item.EventDate,
        item.IsAdjusting,
        item.FinancialImpact,
        item.ActionRequired
    };

    private static object RelatedPartyTransactionSnapshot(RelatedPartyTransaction item) => new
    {
        item.PartyName,
        item.Relationship,
        item.TransactionType,
        item.Amount,
        item.BalanceOwed,
        item.Terms
    };

    private static object ContingentLiabilitySnapshot(ContingentLiability item) => new
    {
        item.Description,
        item.Nature,
        item.EstimatedAmount,
        item.Likelihood
    };

    private static object DebtorSnapshot(Debtor item) => new
    {
        item.Name,
        item.Amount,
        item.Type,
        item.Notes
    };

    private static object CreditorSnapshot(Creditor item) => new
    {
        item.Name,
        item.Amount,
        item.Type,
        item.DueWithinYear,
        item.Notes
    };

    private static object InventorySnapshot(Inventory item) => new
    {
        item.Description,
        item.Value,
        item.ValuationMethod
    };

    private static object PayrollSummarySnapshot(PayrollSummary item) => new
    {
        item.GrossWages,
        item.DirectorsFees,
        item.EmployerPrsi,
        item.PensionContributions,
        item.StaffCount
    };

    private static object DividendSnapshot(Dividend item) => new
    {
        item.Amount,
        item.DateDeclared,
        item.DatePaid
    };

    private static object GoingConcernSnapshot(AccountingPeriod period) => new
    {
        period.GoingConcernConfirmed,
        period.GoingConcernNote
    };

    private static object TaxBalanceSnapshot(TaxBalance item) => new
    {
        item.TaxType,
        item.Liability,
        item.Paid,
        item.Balance
    };

    private static object YearEndReviewSnapshot(YearEndReviewConfirmation confirmation) => new
    {
        confirmation.SectionKey,
        confirmation.Confirmed,
        confirmation.ConfirmedBy,
        confirmation.ConfirmedAt,
        confirmation.Note
    };

    private static object NoteSummarySnapshot(NotesDisclosure note) => new
    {
        note.Id,
        note.NoteNumber,
        note.Code,
        note.Title,
        note.IsRequired,
        note.IsIncluded,
        note.ChecklistState,
        note.ReviewEvidence,
        note.ReviewedBy,
        note.ReviewedAt,
        ContentLength = note.Content?.Length ?? 0
    };

    private static IResult? ValidateNoteInput(NotesDisclosureInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
            return Results.BadRequest(new { error = "Note title is required." });
        if ((input.Content?.Length ?? 0) > MaxNoteContentLength)
            return Results.BadRequest(new { error = $"Note content must be {MaxNoteContentLength:N0} characters or fewer." });
        if (StatutoryNoteCodes.ContainsUnsupportedRepresentation(input.Content))
            return Results.BadRequest(new { error = "Unsupported nil/deferred-tax/financial-instrument/derivative/commitment representations must be supplied through the retained manual-review checklist." });

        return null;
    }

    private static object ShareCapitalSnapshot(ShareCapital item) => new
    {
        item.ShareClass,
        item.NominalValue,
        item.NumberIssued,
        item.TotalValue,
        item.IsFullyPaid,
        item.IssueDate,
        item.CancelledDate
    };
}
