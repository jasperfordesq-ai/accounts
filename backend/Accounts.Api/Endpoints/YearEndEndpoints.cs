using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Accounts.Api.Endpoints;

public static class YearEndEndpoints
{
    private const int MaxNoteContentLength = 20_000;

    private static readonly HashSet<string> ReviewSectionKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "debtors",
        "creditors",
        "fixed-assets",
        "inventory",
        "loans",
        "director-loans",
        "payroll",
        "tax",
        "dividends",
        "post-balance-sheet-events",
        "related-parties",
        "contingent-liabilities",
        "going-concern"
    };

    public static void MapYearEndEndpoints(this WebApplication app)
    {
        var basePath = "/api/companies/{companyId:int}/periods/{periodId:int}";

        // ===== DEBTORS =====
        var debtors = app.MapGroup($"{basePath}/debtors").WithTags("Debtors");

        debtors.MapGet("/", async (int companyId, int periodId, AccountsDbContext db, HttpContext context) =>
            await ListPeriodOwnedRowsAsync(db, companyId, periodId, context, db.Debtors.Where(d => d.PeriodId == periodId)));

        debtors.MapPost("/", CreateDebtorEndpointAsync);

        debtors.MapPut("/{id:int}", UpdateDebtorEndpointAsync);

        debtors.MapDelete("/{id:int}", DeleteDebtorEndpointAsync);

        // ===== CREDITORS =====
        var creditors = app.MapGroup($"{basePath}/creditors").WithTags("Creditors");

        creditors.MapGet("/", async (int companyId, int periodId, AccountsDbContext db, HttpContext context) =>
            await ListPeriodOwnedRowsAsync(db, companyId, periodId, context, db.Creditors.Where(c => c.PeriodId == periodId)));

        creditors.MapPost("/", CreateCreditorEndpointAsync);

        creditors.MapPut("/{id:int}", UpdateCreditorEndpointAsync);

        creditors.MapDelete("/{id:int}", DeleteCreditorEndpointAsync);

        // ===== FIXED ASSETS =====
        var assets = app.MapGroup("/api/companies/{companyId:int}/fixed-assets").WithTags("Fixed Assets");

        assets.MapGet("/", async (int companyId, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            return Results.Ok(await db.FixedAssets.Include(a => a.DepreciationEntries).Where(a => a.CompanyId == companyId).ToListAsync());
        });

        assets.MapPost("/", CreateFixedAssetEndpointAsync);

        assets.MapPut("/{id:int}", UpdateFixedAssetEndpointAsync);

        assets.MapDelete("/{id:int}", DeleteFixedAssetEndpointAsync);

        // ===== INVENTORY =====
        var inventory = app.MapGroup($"{basePath}/inventory").WithTags("Inventory");

        inventory.MapGet("/", async (int companyId, int periodId, AccountsDbContext db, HttpContext context) =>
            await ListPeriodOwnedRowsAsync(db, companyId, periodId, context, db.Inventories.Where(i => i.PeriodId == periodId)));

        inventory.MapPost("/", CreateInventoryEndpointAsync);

        inventory.MapPut("/{id:int}", UpdateInventoryEndpointAsync);

        inventory.MapDelete("/{id:int}", DeleteInventoryEndpointAsync);

        // ===== LOANS =====
        var loans = app.MapGroup("/api/companies/{companyId:int}/loans").WithTags("Loans");

        loans.MapGet("/", async (int companyId, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            return Results.Ok(await db.Loans.Where(l => l.CompanyId == companyId).ToListAsync());
        });

        loans.MapPost("/", CreateLoanEndpointAsync);

        loans.MapPut("/{id:int}", UpdateLoanEndpointAsync);

        loans.MapDelete("/{id:int}", DeleteLoanEndpointAsync);

        // ===== LOAN BALANCE SNAPSHOTS =====
        var loanSnapshots = app.MapGroup($"{basePath}/loan-balance-snapshots").WithTags("Loan Balance Snapshots");

        loanSnapshots.MapGet("/", async (int companyId, int periodId, AccountsDbContext db, HttpContext context) =>
            await ListPeriodOwnedRowsAsync(
                db,
                companyId,
                periodId,
                context,
                db.LoanBalanceSnapshots
                    .Include(s => s.Loan)
                    .Where(s => s.PeriodId == periodId && s.Loan.CompanyId == companyId)));

        loanSnapshots.MapPost("/", CreateLoanBalanceSnapshotEndpointAsync);

        loanSnapshots.MapPut("/{id:int}", UpdateLoanBalanceSnapshotEndpointAsync);

        loanSnapshots.MapDelete("/{id:int}", DeleteLoanBalanceSnapshotEndpointAsync);

        // ===== DIRECTOR LOANS =====
        var dirLoans = app.MapGroup($"{basePath}/director-loans").WithTags("Director Loans");

        dirLoans.MapGet("/", async (int companyId, int periodId, AccountsDbContext db, HttpContext context) =>
            await ListPeriodOwnedRowsAsync(
                db,
                companyId,
                periodId,
                context,
                db.DirectorLoans
                    .Include(d => d.Director)
                    .Where(d => d.PeriodId == periodId && d.Director.CompanyId == companyId)));

        dirLoans.MapPost("/", CreateDirectorLoanEndpointAsync);

        dirLoans.MapPut("/{id:int}", UpdateDirectorLoanEndpointAsync);

        dirLoans.MapDelete("/{id:int}", DeleteDirectorLoanEndpointAsync);

        // ===== PAYROLL SUMMARY =====
        var payroll = app.MapGroup($"{basePath}/payroll").WithTags("Payroll");

        payroll.MapGet("/", async (int companyId, int periodId, AccountsDbContext db, HttpContext context) =>
            await GetPeriodOwnedValueAsync(
                db,
                companyId,
                periodId,
                context,
                db.PayrollSummaries.Where(p => p.PeriodId == periodId)));

        payroll.MapPut("/", UpsertPayrollSummaryEndpointAsync);

        // ===== TAX BALANCES =====
        var taxes = app.MapGroup($"{basePath}/tax-balances").WithTags("Tax Balances");

        taxes.MapGet("/", async (int companyId, int periodId, AccountsDbContext db, HttpContext context) =>
            await ListPeriodOwnedRowsAsync(db, companyId, periodId, context, db.TaxBalances.Where(t => t.PeriodId == periodId)));

        taxes.MapPut("/{taxType}", UpsertTaxBalanceEndpointAsync);

        // ===== DIVIDENDS =====
        var dividends = app.MapGroup($"{basePath}/dividends").WithTags("Dividends");

        dividends.MapGet("/", async (int companyId, int periodId, AccountsDbContext db, HttpContext context) =>
            await ListPeriodOwnedRowsAsync(db, companyId, periodId, context, db.Dividends.Where(d => d.PeriodId == periodId)));

        dividends.MapPost("/", CreateDividendEndpointAsync);

        dividends.MapPut("/{id:int}", UpdateDividendEndpointAsync);

        dividends.MapDelete("/{id:int}", DeleteDividendEndpointAsync);

        // ===== YEAR-END SUMMARY (read-only overview) =====
        app.MapGet($"{basePath}/year-end-summary", async (int companyId, int periodId, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == companyId);
            if (company == null) return Results.NotFound();

            var period = await db.AccountingPeriods.FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId);
            if (period == null) return Results.NotFound();

            var debtorsList = await db.Debtors.Where(d => d.PeriodId == periodId).ToListAsync();
            var creditorsList = await db.Creditors.Where(c => c.PeriodId == periodId).ToListAsync();
            var assetsList = await db.FixedAssets
                .Where(a => a.CompanyId == companyId
                    && a.AcquisitionDate <= period.PeriodEnd
                    && (a.DisposalDate == null || a.DisposalDate > period.PeriodEnd))
                .ToListAsync();
            var inventoryList = await db.Inventories.Where(i => i.PeriodId == periodId).ToListAsync();
            var loanSnapshots = await db.LoanBalanceSnapshots
                .Include(s => s.Loan)
                .Where(s => s.PeriodId == periodId && s.Loan.CompanyId == companyId)
                .ToListAsync();
            var loanSnapshotIds = loanSnapshots.Select(s => s.LoanId).ToHashSet();
            var loansList = (await db.Loans
                .Where(l => l.CompanyId == companyId)
                .ToListAsync())
                .Where(l => loanSnapshotIds.Contains(l.Id)
                    || (l.DrawdownDate is not null
                        && l.BalanceAsOfDate is not null
                        && l.DrawdownDate <= period.PeriodEnd
                        && l.BalanceAsOfDate >= period.PeriodStart
                        && l.BalanceAsOfDate <= period.PeriodEnd))
                .ToList();
            var snapshotClosingBalance = loanSnapshots.Sum(s => s.ClosingBalance);
            var fallbackLoanBalance = loansList.Where(l => !loanSnapshotIds.Contains(l.Id)).Sum(l => l.Balance);
            var dirLoansList = await db.DirectorLoans
                .Include(d => d.Director)
                .Where(d => d.PeriodId == periodId && d.Director.CompanyId == companyId)
                .ToListAsync();
            var payrollItem = await db.PayrollSummaries.FirstOrDefaultAsync(p => p.PeriodId == periodId);
            var taxList = await db.TaxBalances.Where(t => t.PeriodId == periodId).ToListAsync();
            var dividendsList = await db.Dividends.Where(d => d.PeriodId == periodId).ToListAsync();
            var postBalanceSheetEvents = await db.PostBalanceSheetEvents.Where(e => e.PeriodId == periodId).ToListAsync();
            var relatedPartyTransactions = await db.RelatedPartyTransactions.Where(r => r.PeriodId == periodId).ToListAsync();
            var contingentLiabilities = await db.ContingentLiabilities.Where(c => c.PeriodId == periodId).ToListAsync();
            var confirmations = await db.YearEndReviewConfirmations
                .Where(r => r.PeriodId == periodId)
                .OrderBy(r => r.SectionKey)
                .ToListAsync();
            var reviewed = confirmations
                .Where(r => r.Confirmed)
                .Select(r => r.SectionKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Completeness scoring
            var sections = new (string Key, string Label, bool HasEvidence)[]
            {
                ("debtors", "Debtors and other receivables", debtorsList.Count > 0),
                ("creditors", "Creditors, accruals and payables", creditorsList.Count > 0),
                ("fixed-assets", "Fixed assets", assetsList.Count > 0),
                ("inventory", "Stock / inventory", inventoryList.Count > 0),
                ("loans", "Loans and borrowings", loansList.Count > 0),
                ("director-loans", "Director loans", dirLoansList.Count > 0),
                ("payroll", "Payroll and staff", payrollItem != null),
                ("tax", "Tax balances", taxList.Count > 0),
                ("dividends", "Dividends", dividendsList.Count > 0),
                ("post-balance-sheet-events", "Post balance sheet events", postBalanceSheetEvents.Count > 0),
                ("related-parties", "Related party transactions", relatedPartyTransactions.Count > 0),
                ("contingent-liabilities", "Contingent liabilities", contingentLiabilities.Count > 0),
                ("going-concern", "Going concern", reviewed.Contains("going-concern"))
            };
            var incomplete = sections
                .Where(s => !s.HasEvidence && !reviewed.Contains(s.Key))
                .Select(s => s.Label)
                .ToList();
            var totalSections = sections.Length;
            var completedSections = totalSections - incomplete.Count;

            return Results.Ok(new
            {
                debtors = new { count = debtorsList.Count, total = debtorsList.Sum(d => d.Amount) },
                creditors = new { count = creditorsList.Count, total = creditorsList.Sum(c => c.Amount) },
                fixedAssets = new { count = assetsList.Count, totalCost = assetsList.Sum(a => a.Cost) },
                inventory = new { count = inventoryList.Count, totalValue = inventoryList.Sum(i => i.Value) },
                loans = new { count = loansList.Count, totalBalance = snapshotClosingBalance + fallbackLoanBalance },
                directorLoans = new { count = dirLoansList.Count },
                payroll = payrollItem != null ? new { payrollItem.GrossWages, payrollItem.StaffCount } : null,
                taxes = new { count = taxList.Count, totalLiability = taxList.Sum(t => t.Liability), totalBalance = taxList.Sum(t => t.Balance) },
                dividends = new { count = dividendsList.Count, total = dividendsList.Sum(d => d.Amount) },
                reviewConfirmations = confirmations,
                completeness = new { score = (int)Math.Round((double)completedSections / totalSections * 100), completed = completedSections, total = totalSections, incomplete }
            });
        }).WithTags("Year-End Summary");

        // ===== YEAR-END REVIEW CONFIRMATIONS =====
        var reviews = app.MapGroup($"{basePath}/year-end-reviews").WithTags("Year-End Review");

        reviews.MapGet("/", async (int companyId, int periodId, AccountsDbContext db, HttpContext context) =>
            await ListPeriodOwnedRowsAsync(
                db,
                companyId,
                periodId,
                context,
                db.YearEndReviewConfirmations
                    .Where(r => r.PeriodId == periodId)
                    .OrderBy(r => r.SectionKey)));

        reviews.MapPut("/{sectionKey}", UpdateYearEndReviewEndpointAsync);

        // ===== NOTES DISCLOSURES =====
        var notesGroup = app.MapGroup($"{basePath}/notes").WithTags("Notes");

        notesGroup.MapGet("/", async (int companyId, int periodId, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var notes = await db.NotesDisclosures
                .Where(n => n.PeriodId == periodId)
                .OrderBy(n => n.NoteNumber)
                .ToListAsync();
            return Results.Ok(notes);
        });

        notesGroup.MapPost("/generate", GenerateNotesEndpointAsync);

        notesGroup.MapPut("/{id:int}", UpdateNoteEndpointAsync);

        notesGroup.MapPost("/", CreateNoteEndpointAsync);

        notesGroup.MapDelete("/{id:int}", DeleteNoteEndpointAsync);

        // ===== SHARE CAPITAL =====
        var shares = app.MapGroup("/api/companies/{companyId:int}/share-capital").WithTags("Share Capital");

        shares.MapGet("/", async (int companyId, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            return Results.Ok(await db.ShareCapitals.Where(s => s.CompanyId == companyId).ToListAsync());
        });

        shares.MapPost("/", CreateShareCapitalEndpointAsync);

        shares.MapPut("/{id:int}", UpdateShareCapitalEndpointAsync);

        shares.MapDelete("/{id:int}", DeleteShareCapitalEndpointAsync);

        // ===== OPENING BALANCES =====
        var openingBalances = app.MapGroup($"{basePath}/opening-balances").WithTags("Opening Balances");

        openingBalances.MapGet("/", async (int companyId, int periodId, AccountsDbContext db, HttpContext context) =>
            await ListPeriodOwnedRowsAsync(
                db,
                companyId,
                periodId,
                context,
                db.OpeningBalances
                    .Include(o => o.AccountCategory)
                    .Where(o => o.PeriodId == periodId)
                    .OrderBy(o => o.AccountCategory.Code)));

        openingBalances.MapPut("/{categoryId:int}", UpsertOpeningBalanceEndpointAsync);

        openingBalances.MapDelete("/{categoryId:int}", DeleteOpeningBalanceEndpointAsync);

        // ===== POST BALANCE SHEET EVENTS =====
        var pbse = app.MapGroup($"{basePath}/post-balance-sheet-events").WithTags("Post Balance Sheet Events");
        pbse.MapGet("/", async (int companyId, int periodId, AccountsDbContext db, HttpContext context) =>
            await ListPeriodOwnedRowsAsync(db, companyId, periodId, context, db.PostBalanceSheetEvents.Where(x => x.PeriodId == periodId).OrderBy(x => x.EventDate)));
        pbse.MapPost("/", CreatePostBalanceSheetEventEndpointAsync);
        pbse.MapDelete("/{id:int}", DeletePostBalanceSheetEventEndpointAsync);

        // ===== RELATED PARTY TRANSACTIONS =====
        var rpt = app.MapGroup($"{basePath}/related-party-transactions").WithTags("Related Party Transactions");
        rpt.MapGet("/", async (int companyId, int periodId, AccountsDbContext db, HttpContext context) =>
            await ListPeriodOwnedRowsAsync(db, companyId, periodId, context, db.RelatedPartyTransactions.Where(x => x.PeriodId == periodId).OrderBy(x => x.PartyName)));
        rpt.MapPost("/", CreateRelatedPartyTransactionEndpointAsync);
        rpt.MapDelete("/{id:int}", DeleteRelatedPartyTransactionEndpointAsync);

        // ===== CONTINGENT LIABILITIES =====
        var cl = app.MapGroup($"{basePath}/contingent-liabilities").WithTags("Contingent Liabilities");
        cl.MapGet("/", async (int companyId, int periodId, AccountsDbContext db, HttpContext context) =>
            await ListPeriodOwnedRowsAsync(db, companyId, periodId, context, db.ContingentLiabilities.Where(x => x.PeriodId == periodId).OrderBy(x => x.Description)));
        cl.MapPost("/", CreateContingentLiabilityEndpointAsync);
        cl.MapDelete("/{id:int}", DeleteContingentLiabilityEndpointAsync);

        // ===== GOING CONCERN =====
        var gc = app.MapGroup($"{basePath}/going-concern").WithTags("Going Concern");
        gc.MapGet("/", async (int companyId, int periodId, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var period = await db.AccountingPeriods.FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId);
            if (period == null) return Results.NotFound();
            return Results.Ok(new { period.GoingConcernConfirmed, period.GoingConcernNote });
        });
        gc.MapPut("/", UpdateGoingConcernEndpointAsync);

        // ===== DIRECTOR LOAN COMPLIANCE (s.239 / s.307) =====
        var dlCompliance = app.MapGroup($"{basePath}/director-loans").WithTags("Director Loans");

        dlCompliance.MapGet("/compliance", async (int companyId, int periodId, DirectorLoanComplianceService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            try
            {
                var result = await service.GetComplianceStatusAsync(companyId, periodId);
                return Results.Ok(result);
            }
            catch (InvalidOperationException)
            {
                return Results.BadRequest(new { error = "Unable to load director loan compliance for this accounting period." });
            }
        });

        dlCompliance.MapGet("/section-307-note", async (int companyId, int periodId, DirectorLoanComplianceService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            try
            {
                var note = await service.GenerateSection307NoteAsync(companyId, periodId);
                return Results.Ok(new { note });
            }
            catch (InvalidOperationException)
            {
                return Results.BadRequest(new { error = "Unable to generate the Section 307 director loan note for this accounting period." });
            }
        });
    }

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

    public static async Task<IResult> CreateFixedAssetEndpointAsync(
        int companyId,
        FixedAsset input,
        AccountsDbContext db,
        AccountingWriteGuard writeGuard,
        AuditService audit,
        HttpContext context)
    {
        if (await RequireCompanyWriteAccessAsync(db, companyId, context) is { } denied)
            return denied;

        if (YearEndFigureInputs.ForFixedAsset(input) is { } invalid)
            return invalid;

        if (await writeGuard.BlockIfCompanyAccountingLockedAsync(companyId, input.AcquisitionDate) is { } blocked)
            return blocked;

        input.Id = 0;
        input.CompanyId = companyId;
        input.DepreciationEntries = [];
        db.FixedAssets.Add(input);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            null,
            "FixedAsset",
            input.Id,
            AuditEventCodes.FixedAssetCreated,
            null,
            FixedAssetSnapshot(input),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/fixed-assets/{input.Id}", input);
    }

    public static async Task<IResult> UpdateFixedAssetEndpointAsync(
        int companyId,
        int id,
        FixedAsset input,
        AccountsDbContext db,
        AccountingWriteGuard writeGuard,
        AuditService audit,
        HttpContext context)
    {
        if (await RequireCompanyWriteAccessAsync(db, companyId, context) is { } denied)
            return denied;

        var item = await db.FixedAssets.FirstOrDefaultAsync(a => a.Id == id && a.CompanyId == companyId);
        if (item == null) return Results.NotFound();

        if (YearEndFigureInputs.ForFixedAsset(input) is { } invalid)
            return invalid;

        var oldValue = FixedAssetSnapshot(item);
        var effectiveDate = BankingEndpointInputs.Earliest(item.AcquisitionDate, input.AcquisitionDate);
        if (await writeGuard.BlockIfCompanyAccountingLockedAsync(companyId, effectiveDate) is { } blocked)
            return blocked;

        input.DepreciationEntries = [];
        item.Name = input.Name;
        item.Category = input.Category;
        item.Cost = input.Cost;
        item.AcquisitionDate = input.AcquisitionDate;
        item.DisposalDate = input.DisposalDate;
        item.DisposalProceeds = input.DisposalProceeds;
        item.UsefulLifeYears = input.UsefulLifeYears;
        item.DepreciationMethod = input.DepreciationMethod;
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            null,
            "FixedAsset",
            item.Id,
            AuditEventCodes.FixedAssetUpdated,
            oldValue,
            FixedAssetSnapshot(item),
            AuditUserId(context));
        return Results.Ok(item);
    }

    public static async Task<IResult> DeleteFixedAssetEndpointAsync(
        int companyId,
        int id,
        AccountsDbContext db,
        AccountingWriteGuard writeGuard,
        AuditService audit,
        HttpContext context)
    {
        if (await RequireCompanyWriteAccessAsync(db, companyId, context) is { } denied)
            return denied;

        var item = await db.FixedAssets.FirstOrDefaultAsync(a => a.Id == id && a.CompanyId == companyId);
        if (item == null) return Results.NotFound();

        var oldValue = FixedAssetSnapshot(item);
        if (await writeGuard.BlockIfCompanyAccountingLockedAsync(companyId, item.AcquisitionDate) is { } blocked)
            return blocked;

        db.FixedAssets.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            null,
            "FixedAsset",
            id,
            AuditEventCodes.FixedAssetDeleted,
            oldValue,
            new { Deleted = true },
            AuditUserId(context));
        return Results.NoContent();
    }

    public static async Task<IResult> CreateLoanEndpointAsync(
        int companyId,
        Loan input,
        AccountsDbContext db,
        AccountingWriteGuard writeGuard,
        AuditService audit,
        HttpContext context)
    {
        if (await RequireCompanyWriteAccessAsync(db, companyId, context) is { } denied)
            return denied;

        if (LoanInputs.Validate(input) is { } validationProblem)
            return validationProblem;

        var effectiveDate = BankingEndpointInputs.Earliest(input.DrawdownDate, input.BalanceAsOfDate);
        if (await writeGuard.BlockIfCompanyAccountingLockedAsync(companyId, effectiveDate) is { } blocked)
            return blocked;

        input.CompanyId = companyId;
        db.Loans.Add(input);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            null,
            "Loan",
            input.Id,
            AuditEventCodes.LoanCreated,
            null,
            LoanSnapshot(input),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/loans/{input.Id}", input);
    }

    public static async Task<IResult> UpdateLoanEndpointAsync(
        int companyId,
        int id,
        Loan input,
        AccountsDbContext db,
        AccountingWriteGuard writeGuard,
        AuditService audit,
        HttpContext context)
    {
        if (await RequireCompanyWriteAccessAsync(db, companyId, context) is { } denied)
            return denied;

        var item = await db.Loans.FirstOrDefaultAsync(l => l.Id == id && l.CompanyId == companyId);
        if (item == null) return Results.NotFound();
        if (LoanInputs.Validate(input) is { } validationProblem)
            return validationProblem;

        var oldValue = LoanSnapshot(item);
        var existingEffectiveDate = BankingEndpointInputs.Earliest(item.DrawdownDate, item.BalanceAsOfDate);
        var inputEffectiveDate = BankingEndpointInputs.Earliest(input.DrawdownDate, input.BalanceAsOfDate);
        var effectiveDate = BankingEndpointInputs.Earliest(existingEffectiveDate, inputEffectiveDate);
        if (await writeGuard.BlockIfCompanyAccountingLockedAsync(companyId, effectiveDate) is { } blocked)
            return blocked;

        item.Lender = input.Lender;
        item.OriginalAmount = input.OriginalAmount;
        item.Balance = input.Balance;
        item.DrawdownDate = input.DrawdownDate;
        item.BalanceAsOfDate = input.BalanceAsOfDate;
        item.InterestRate = input.InterestRate;
        item.IsDirectorLoan = input.IsDirectorLoan;
        item.DueWithinYear = input.DueWithinYear;
        item.DueAfterYear = input.DueAfterYear;
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            null,
            "Loan",
            item.Id,
            AuditEventCodes.LoanUpdated,
            oldValue,
            LoanSnapshot(item),
            AuditUserId(context));
        return Results.Ok(item);
    }

    public static async Task<IResult> DeleteLoanEndpointAsync(
        int companyId,
        int id,
        AccountsDbContext db,
        AccountingWriteGuard writeGuard,
        AuditService audit,
        HttpContext context)
    {
        if (await RequireCompanyWriteAccessAsync(db, companyId, context) is { } denied)
            return denied;

        var item = await db.Loans.FirstOrDefaultAsync(l => l.Id == id && l.CompanyId == companyId);
        if (item == null) return Results.NotFound();

        var oldValue = LoanSnapshot(item);
        var effectiveDate = BankingEndpointInputs.Earliest(item.DrawdownDate, item.BalanceAsOfDate);
        if (await writeGuard.BlockIfCompanyAccountingLockedAsync(companyId, effectiveDate) is { } blocked)
            return blocked;

        db.Loans.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            null,
            "Loan",
            id,
            AuditEventCodes.LoanDeleted,
            oldValue,
            new { Deleted = true },
            AuditUserId(context));
        return Results.NoContent();
    }

    public static async Task<IResult> CreateLoanBalanceSnapshotEndpointAsync(
        int companyId,
        int periodId,
        LoanBalanceSnapshot input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        if (await LoanBalanceSnapshotInputs.ValidateAsync(db, companyId, input) is { } validationProblem)
            return validationProblem;

        StampLoanBalanceSnapshot(input, periodId, context);
        db.LoanBalanceSnapshots.Add(input);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "LoanBalanceSnapshot",
            input.Id,
            AuditEventCodes.LoanBalanceSnapshotCreated,
            null,
            LoanBalanceSnapshotSnapshot(input),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/periods/{periodId}/loan-balance-snapshots/{input.Id}", input);
    }

    public static async Task<IResult> UpdateLoanBalanceSnapshotEndpointAsync(
        int companyId,
        int periodId,
        int id,
        LoanBalanceSnapshot input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        if (await LoanBalanceSnapshotInputs.ValidateAsync(db, companyId, input) is { } validationProblem)
            return validationProblem;

        var item = await db.LoanBalanceSnapshots
            .Include(s => s.Loan)
            .FirstOrDefaultAsync(s => s.Id == id && s.PeriodId == periodId && s.Loan.CompanyId == companyId);
        if (item == null) return Results.NotFound();

        var oldValue = LoanBalanceSnapshotSnapshot(item);
        item.LoanId = input.LoanId;
        item.OpeningBalance = input.OpeningBalance;
        item.Drawdowns = input.Drawdowns;
        item.Repayments = input.Repayments;
        item.ClosingBalance = input.ClosingBalance;
        item.DueWithinYear = input.DueWithinYear;
        item.DueAfterYear = input.DueAfterYear;
        item.Notes = input.Notes;
        StampLoanBalanceSnapshot(item, periodId, context);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "LoanBalanceSnapshot",
            item.Id,
            AuditEventCodes.LoanBalanceSnapshotUpdated,
            oldValue,
            LoanBalanceSnapshotSnapshot(item),
            AuditUserId(context));
        return Results.Ok(item);
    }

    public static async Task<IResult> DeleteLoanBalanceSnapshotEndpointAsync(
        int companyId,
        int periodId,
        int id,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.LoanBalanceSnapshots
            .Include(s => s.Loan)
            .FirstOrDefaultAsync(s => s.Id == id && s.PeriodId == periodId && s.Loan.CompanyId == companyId);
        if (item == null) return Results.NotFound();

        var oldValue = LoanBalanceSnapshotSnapshot(item);
        db.LoanBalanceSnapshots.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "LoanBalanceSnapshot",
            id,
            AuditEventCodes.LoanBalanceSnapshotDeleted,
            oldValue,
            new { Deleted = true },
            AuditUserId(context));
        return Results.NoContent();
    }

    public static async Task<IResult> CreateDirectorLoanEndpointAsync(
        int companyId,
        int periodId,
        DirectorLoanInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        if (await DirectorLoanInputs.ValidateAsync(db, companyId, periodId, input) is { } validationProblem)
            return validationProblem;

        var loan = DirectorLoanInputs.ToEntity(periodId, input);
        db.DirectorLoans.Add(loan);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "DirectorLoan",
            loan.Id,
            AuditEventCodes.DirectorLoanCreated,
            null,
            DirectorLoanSnapshot(loan),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/periods/{periodId}/director-loans/{loan.Id}", loan);
    }

    public static async Task<IResult> UpdateDirectorLoanEndpointAsync(
        int companyId,
        int periodId,
        int id,
        DirectorLoanInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        if (await DirectorLoanInputs.ValidateAsync(db, companyId, periodId, input) is { } validationProblem)
            return validationProblem;

        var item = await db.DirectorLoans.FirstOrDefaultAsync(d => d.Id == id && d.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        var oldValue = DirectorLoanSnapshot(item);
        item.DirectorId = input.DirectorId;
        item.OpeningBalance = input.OpeningBalance;
        item.Advances = input.Advances;
        item.Repayments = input.Repayments;
        item.ClosingBalance = input.ClosingBalance;
        item.InterestRate = input.InterestRate;
        item.InterestCharged = input.InterestCharged;
        item.IsDocumented = input.IsDocumented;
        item.LoanTerms = string.IsNullOrWhiteSpace(input.LoanTerms) ? null : input.LoanTerms.Trim();
        item.MaxBalanceDuringYear = input.MaxBalanceDuringYear;
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "DirectorLoan",
            item.Id,
            AuditEventCodes.DirectorLoanUpdated,
            oldValue,
            DirectorLoanSnapshot(item),
            AuditUserId(context));
        return Results.Ok(item);
    }

    public static async Task<IResult> DeleteDirectorLoanEndpointAsync(
        int companyId,
        int periodId,
        int id,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.DirectorLoans.FirstOrDefaultAsync(d => d.Id == id && d.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        var oldValue = DirectorLoanSnapshot(item);
        db.DirectorLoans.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "DirectorLoan",
            id,
            AuditEventCodes.DirectorLoanDeleted,
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

    public static async Task<IResult> CreateDebtorEndpointAsync(
        int companyId,
        int periodId,
        Debtor input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        if (YearEndFigureInputs.ForDebtor(input) is { } invalid)
            return invalid;

        input.Id = 0;
        input.PeriodId = periodId;
        db.Debtors.Add(input);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Debtor",
            input.Id,
            AuditEventCodes.DebtorCreated,
            null,
            DebtorSnapshot(input),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/periods/{periodId}/debtors/{input.Id}", input);
    }

    public static async Task<IResult> UpdateDebtorEndpointAsync(
        int companyId,
        int periodId,
        int id,
        Debtor input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.Debtors.FirstOrDefaultAsync(d => d.Id == id && d.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        if (YearEndFigureInputs.ForDebtor(input) is { } invalid)
            return invalid;

        var oldValue = DebtorSnapshot(item);
        item.Name = input.Name;
        item.Amount = input.Amount;
        item.Type = input.Type;
        item.Notes = input.Notes;
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Debtor",
            item.Id,
            AuditEventCodes.DebtorUpdated,
            oldValue,
            DebtorSnapshot(item),
            AuditUserId(context));
        return Results.Ok(item);
    }

    public static async Task<IResult> DeleteDebtorEndpointAsync(
        int companyId,
        int periodId,
        int id,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.Debtors.FirstOrDefaultAsync(d => d.Id == id && d.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        var oldValue = DebtorSnapshot(item);
        db.Debtors.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Debtor",
            id,
            AuditEventCodes.DebtorDeleted,
            oldValue,
            new { Deleted = true },
            AuditUserId(context));
        return Results.NoContent();
    }

    public static async Task<IResult> CreateCreditorEndpointAsync(
        int companyId,
        int periodId,
        Creditor input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        if (YearEndFigureInputs.ForCreditor(input) is { } invalid)
            return invalid;

        input.Id = 0;
        input.PeriodId = periodId;
        db.Creditors.Add(input);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Creditor",
            input.Id,
            AuditEventCodes.CreditorCreated,
            null,
            CreditorSnapshot(input),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/periods/{periodId}/creditors/{input.Id}", input);
    }

    public static async Task<IResult> UpdateCreditorEndpointAsync(
        int companyId,
        int periodId,
        int id,
        Creditor input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.Creditors.FirstOrDefaultAsync(c => c.Id == id && c.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        if (YearEndFigureInputs.ForCreditor(input) is { } invalid)
            return invalid;

        var oldValue = CreditorSnapshot(item);
        item.Name = input.Name;
        item.Amount = input.Amount;
        item.Type = input.Type;
        item.DueWithinYear = input.DueWithinYear;
        item.Notes = input.Notes;
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Creditor",
            item.Id,
            AuditEventCodes.CreditorUpdated,
            oldValue,
            CreditorSnapshot(item),
            AuditUserId(context));
        return Results.Ok(item);
    }

    public static async Task<IResult> DeleteCreditorEndpointAsync(
        int companyId,
        int periodId,
        int id,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.Creditors.FirstOrDefaultAsync(c => c.Id == id && c.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        var oldValue = CreditorSnapshot(item);
        db.Creditors.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Creditor",
            id,
            AuditEventCodes.CreditorDeleted,
            oldValue,
            new { Deleted = true },
            AuditUserId(context));
        return Results.NoContent();
    }

    public static async Task<IResult> CreateInventoryEndpointAsync(
        int companyId,
        int periodId,
        Inventory input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        if (YearEndFigureInputs.ForInventory(input) is { } invalid)
            return invalid;

        input.Id = 0;
        input.PeriodId = periodId;
        db.Inventories.Add(input);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Inventory",
            input.Id,
            AuditEventCodes.InventoryCreated,
            null,
            InventorySnapshot(input),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/periods/{periodId}/inventory/{input.Id}", input);
    }

    public static async Task<IResult> UpdateInventoryEndpointAsync(
        int companyId,
        int periodId,
        int id,
        Inventory input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.Inventories.FirstOrDefaultAsync(i => i.Id == id && i.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        if (YearEndFigureInputs.ForInventory(input) is { } invalid)
            return invalid;

        var oldValue = InventorySnapshot(item);
        item.Description = input.Description;
        item.Value = input.Value;
        item.ValuationMethod = input.ValuationMethod;
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Inventory",
            item.Id,
            AuditEventCodes.InventoryUpdated,
            oldValue,
            InventorySnapshot(item),
            AuditUserId(context));
        return Results.Ok(item);
    }

    public static async Task<IResult> DeleteInventoryEndpointAsync(
        int companyId,
        int periodId,
        int id,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.Inventories.FirstOrDefaultAsync(i => i.Id == id && i.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        var oldValue = InventorySnapshot(item);
        db.Inventories.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Inventory",
            id,
            AuditEventCodes.InventoryDeleted,
            oldValue,
            new { Deleted = true },
            AuditUserId(context));
        return Results.NoContent();
    }

    public static async Task<IResult> UpsertPayrollSummaryEndpointAsync(
        int companyId,
        int periodId,
        PayrollSummary input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.PayrollSummaries.FirstOrDefaultAsync(p => p.PeriodId == periodId);
        var wasCreated = item is null;
        var oldValue = item is null ? null : PayrollSummarySnapshot(item);

        if (item == null)
        {
            input.PeriodId = periodId;
            db.PayrollSummaries.Add(input);
            item = input;
        }
        else
        {
            item.GrossWages = input.GrossWages;
            item.EmployerPrsi = input.EmployerPrsi;
            item.PensionContributions = input.PensionContributions;
            item.StaffCount = input.StaffCount;
        }

        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "PayrollSummary",
            item.Id,
            AuditEventCodes.PayrollSummaryUpserted,
            oldValue,
            new
            {
                item.GrossWages,
                item.EmployerPrsi,
                item.PensionContributions,
                item.StaffCount,
                WasCreated = wasCreated
            },
            AuditUserId(context));
        return Results.Ok(item);
    }

    public static async Task<IResult> CreateDividendEndpointAsync(
        int companyId,
        int periodId,
        Dividend input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        if (YearEndFigureInputs.ForDividend(input) is { } invalid)
            return invalid;

        input.Id = 0;
        input.PeriodId = periodId;
        db.Dividends.Add(input);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Dividend",
            input.Id,
            AuditEventCodes.DividendCreated,
            null,
            DividendSnapshot(input),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/periods/{periodId}/dividends/{input.Id}", input);
    }

    public static async Task<IResult> UpdateDividendEndpointAsync(
        int companyId,
        int periodId,
        int id,
        Dividend input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.Dividends.FirstOrDefaultAsync(d => d.Id == id && d.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        if (YearEndFigureInputs.ForDividend(input) is { } invalid)
            return invalid;

        var oldValue = DividendSnapshot(item);
        item.Amount = input.Amount;
        item.DateDeclared = input.DateDeclared;
        item.DatePaid = input.DatePaid;
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Dividend",
            item.Id,
            AuditEventCodes.DividendUpdated,
            oldValue,
            DividendSnapshot(item),
            AuditUserId(context));
        return Results.Ok(item);
    }

    public static async Task<IResult> DeleteDividendEndpointAsync(
        int companyId,
        int periodId,
        int id,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        var item = await db.Dividends.FirstOrDefaultAsync(d => d.Id == id && d.PeriodId == periodId);
        if (item == null) return Results.NotFound();

        var oldValue = DividendSnapshot(item);
        db.Dividends.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "Dividend",
            id,
            AuditEventCodes.DividendDeleted,
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

    public static async Task<IResult> UpsertTaxBalanceEndpointAsync(
        int companyId,
        int periodId,
        TaxType taxType,
        TaxBalance input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context)
    {
        if (await RequirePeriodWriteAccessAsync(db, companyId, periodId, context) is { } denied)
            return denied;

        // accounting-tax-balance-internal-consistency: the triple must be self-consistent. A verbatim
        // inconsistent triple (e.g. Balance != Liability - Paid) mis-states creditors and profit-after-
        // tax downstream. Liability and Paid are amounts and cannot be negative; the outstanding Balance
        // must equal Liability - Paid (a negative Balance is a legitimate overpayment / refund due).
        if (input.Liability < 0 || input.Paid < 0)
            return Results.BadRequest(new { error = "Tax liability and amount paid must not be negative." });
        if (Math.Abs(input.Balance - (input.Liability - input.Paid)) > 0.005m)
            return Results.BadRequest(new { error = "Tax balance must equal liability minus amount paid." });

        var item = await db.TaxBalances.FirstOrDefaultAsync(t => t.PeriodId == periodId && t.TaxType == taxType);
        var wasCreated = item is null;
        var oldValue = item is null ? null : TaxBalanceSnapshot(item);
        if (item == null)
        {
            input.PeriodId = periodId;
            input.TaxType = taxType;
            db.TaxBalances.Add(input);
            item = input;
        }
        else
        {
            item.Liability = input.Liability;
            item.Paid = input.Paid;
            item.Balance = input.Balance;
        }

        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            periodId,
            "TaxBalance",
            item.Id,
            AuditEventCodes.TaxBalanceUpserted,
            oldValue,
            new
            {
                item.TaxType,
                item.Liability,
                item.Paid,
                item.Balance,
                WasCreated = wasCreated
            },
            AuditUserId(context));
        return Results.Ok(item);
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

    public static async Task<IResult> CreateShareCapitalEndpointAsync(
        int companyId,
        ShareCapital input,
        AccountsDbContext db,
        AccountingWriteGuard writeGuard,
        AuditService audit,
        HttpContext context)
    {
        if (await RequireCompanyWriteAccessAsync(db, companyId, context) is { } denied)
            return denied;

        if (ShareCapitalInputs.Validate(input) is { } validationProblem)
            return validationProblem;

        var effectiveDate = input.IssueDate;
        if (await writeGuard.BlockIfCompanyAccountingLockedAsync(companyId, effectiveDate) is { } blocked)
            return blocked;

        input.CompanyId = companyId;
        input.TotalValue = input.NominalValue * input.NumberIssued;
        db.ShareCapitals.Add(input);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            null,
            "ShareCapital",
            input.Id,
            AuditEventCodes.ShareCapitalCreated,
            null,
            ShareCapitalSnapshot(input),
            AuditUserId(context));
        return Results.Created($"/api/companies/{companyId}/share-capital/{input.Id}", input);
    }

    public static async Task<IResult> UpdateShareCapitalEndpointAsync(
        int companyId,
        int id,
        ShareCapital input,
        AccountsDbContext db,
        AccountingWriteGuard writeGuard,
        AuditService audit,
        HttpContext context)
    {
        if (await RequireCompanyWriteAccessAsync(db, companyId, context) is { } denied)
            return denied;

        var item = await db.ShareCapitals.FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == companyId);
        if (item == null) return Results.NotFound();
        if (ShareCapitalInputs.Validate(input) is { } validationProblem)
            return validationProblem;

        var oldValue = ShareCapitalSnapshot(item);
        var existingEffectiveDate = BankingEndpointInputs.Earliest(item.IssueDate, item.CancelledDate);
        var inputEffectiveDate = BankingEndpointInputs.Earliest(input.IssueDate, input.CancelledDate);
        var effectiveDate = BankingEndpointInputs.Earliest(existingEffectiveDate, inputEffectiveDate);
        if (await writeGuard.BlockIfCompanyAccountingLockedAsync(companyId, effectiveDate) is { } blocked)
            return blocked;

        item.ShareClass = input.ShareClass;
        item.NominalValue = input.NominalValue;
        item.NumberIssued = input.NumberIssued;
        item.TotalValue = input.NominalValue * input.NumberIssued;
        item.IsFullyPaid = input.IsFullyPaid;
        item.IssueDate = input.IssueDate;
        item.CancelledDate = input.CancelledDate;
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            null,
            "ShareCapital",
            item.Id,
            AuditEventCodes.ShareCapitalUpdated,
            oldValue,
            ShareCapitalSnapshot(item),
            AuditUserId(context));
        return Results.Ok(item);
    }

    public static async Task<IResult> DeleteShareCapitalEndpointAsync(
        int companyId,
        int id,
        AccountsDbContext db,
        AccountingWriteGuard writeGuard,
        AuditService audit,
        HttpContext context)
    {
        if (await RequireCompanyWriteAccessAsync(db, companyId, context) is { } denied)
            return denied;

        var item = await db.ShareCapitals.FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == companyId);
        if (item == null) return Results.NotFound();

        var oldValue = ShareCapitalSnapshot(item);
        var effectiveDate = BankingEndpointInputs.Earliest(item.IssueDate, item.CancelledDate);
        if (await writeGuard.BlockIfCompanyAccountingLockedAsync(companyId, effectiveDate) is { } blocked)
            return blocked;

        db.ShareCapitals.Remove(item);
        await db.SaveChangesAsync();
        await audit.LogAsync(
            companyId,
            null,
            "ShareCapital",
            id,
            AuditEventCodes.ShareCapitalDeleted,
            oldValue,
            new { Deleted = true },
            AuditUserId(context));
        return Results.NoContent();
    }

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

    private static async Task<IResult> GetPeriodOwnedValueAsync<T>(
        AccountsDbContext db,
        int companyId,
        int periodId,
        HttpContext context,
        IQueryable<T> query)
        where T : class
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
            return Results.NotFound();

        return await query.FirstOrDefaultAsync() is { } value
            ? Results.Ok(value)
            : Results.NotFound();
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
        item.AcquisitionDate,
        item.DisposalDate,
        item.DisposalProceeds,
        item.UsefulLifeYears,
        item.DepreciationMethod
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
        item.OpeningBalance,
        item.Advances,
        item.Repayments,
        item.ClosingBalance,
        item.InterestRate,
        item.InterestCharged,
        item.IsDocumented,
        item.LoanTerms,
        item.MaxBalanceDuringYear
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
        note.Title,
        note.IsRequired,
        note.IsIncluded,
        ContentLength = note.Content?.Length ?? 0
    };

    private static IResult? ValidateNoteInput(NotesDisclosure input)
    {
        if ((input.Content?.Length ?? 0) > MaxNoteContentLength)
            return Results.BadRequest(new { error = $"Note content must be {MaxNoteContentLength:N0} characters or fewer." });

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

public record GoingConcernInput(bool Confirmed, string? Note);
public record YearEndReviewInput(bool Confirmed, string? ConfirmedBy, string? Note);
public record OpeningBalanceInput(decimal Debit, decimal Credit, string? SourceNote, string? EnteredBy, bool Reviewed);
public record DirectorLoanInput(
    int DirectorId,
    decimal OpeningBalance,
    decimal Advances,
    decimal Repayments,
    decimal ClosingBalance,
    decimal InterestRate,
    decimal InterestCharged,
    bool IsDocumented,
    string? LoanTerms,
    decimal MaxBalanceDuringYear);

public static class DirectorLoanInputs
{
    public static async Task<IResult?> ValidateAsync(AccountsDbContext db, int companyId, int periodId, DirectorLoanInput input)
    {
        var period = await db.AccountingPeriods
            .AsNoTracking()
            .Where(p => p.Id == periodId && p.CompanyId == companyId)
            .Select(p => new { p.PeriodStart, p.PeriodEnd })
            .FirstOrDefaultAsync();
        if (period is null)
            return Results.NotFound(new { error = "Accounting period not found for this company." });

        var directorServedDuringPeriod = await db.CompanyOfficers.AnyAsync(o =>
            o.Id == input.DirectorId
            && o.CompanyId == companyId
            && o.Role == OfficerRole.Director
            && (o.AppointedDate == null || o.AppointedDate <= period.PeriodEnd)
            && (o.ResignedDate == null || o.ResignedDate >= period.PeriodStart));

        return directorServedDuringPeriod
            ? null
            : Results.BadRequest(new { error = "Director loan director must have served as a director of this company during the accounting period." });
    }

    public static DirectorLoan ToEntity(int periodId, DirectorLoanInput input) => new()
    {
        PeriodId = periodId,
        DirectorId = input.DirectorId,
        OpeningBalance = input.OpeningBalance,
        Advances = input.Advances,
        Repayments = input.Repayments,
        ClosingBalance = input.ClosingBalance,
        InterestRate = input.InterestRate,
        InterestCharged = input.InterestCharged,
        IsDocumented = input.IsDocumented,
        LoanTerms = string.IsNullOrWhiteSpace(input.LoanTerms) ? null : input.LoanTerms.Trim(),
        MaxBalanceDuringYear = input.MaxBalanceDuringYear
    };
}

public static class LoanInputs
{
    public static IResult? Validate(Loan input)
    {
        if (string.IsNullOrWhiteSpace(input.Lender))
            return Results.BadRequest(new { error = "Loan lender is required." });
        if (input.DrawdownDate is null)
            return Results.BadRequest(new { error = "Loan drawdown date is required." });
        if (input.BalanceAsOfDate is null)
            return Results.BadRequest(new { error = "Loan balance as-of date is required." });
        if (input.BalanceAsOfDate < input.DrawdownDate)
            return Results.BadRequest(new { error = "Loan balance as-of date cannot be before the drawdown date." });

        return null;
    }
}

public static class LoanBalanceSnapshotInputs
{
    public static async Task<IResult?> ValidateAsync(AccountsDbContext db, int companyId, LoanBalanceSnapshot input)
    {
        var loanBelongsToCompany = await db.Loans.AnyAsync(l => l.Id == input.LoanId && l.CompanyId == companyId);
        if (!loanBelongsToCompany)
            return Results.BadRequest(new { error = "Loan is not available for this company." });

        if (input.OpeningBalance < 0
            || input.Drawdowns < 0
            || input.Repayments < 0
            || input.ClosingBalance < 0
            || input.DueWithinYear < 0
            || input.DueAfterYear < 0)
            return Results.BadRequest(new { error = "Loan snapshot amounts cannot be negative." });

        var expectedClosing = input.OpeningBalance + input.Drawdowns - input.Repayments;
        if (Math.Abs(expectedClosing - input.ClosingBalance) > 0.01m)
            return Results.BadRequest(new { error = "Loan snapshot closing balance must equal opening balance plus drawdowns less repayments." });

        if (Math.Abs(input.DueWithinYear + input.DueAfterYear - input.ClosingBalance) > 0.01m)
            return Results.BadRequest(new { error = "Loan due split must agree to the closing balance." });

        return null;
    }
}

public static class ShareCapitalInputs
{
    public static IResult? Validate(ShareCapital input)
    {
        if (string.IsNullOrWhiteSpace(input.ShareClass))
            return Results.BadRequest(new { error = "Share class is required." });
        if (input.IssueDate is null)
            return Results.BadRequest(new { error = "Share issue date is required." });
        if (input.CancelledDate is not null && input.CancelledDate < input.IssueDate)
            return Results.BadRequest(new { error = "Share cancellation date cannot be before the issue date." });

        return null;
    }
}

// Guards the figure-bearing year-end rows (debtors/creditors/inventory/fixed assets/dividends) so a
// customer fat-fingering a negative amount, a blank description or a zero useful life gets a clear 400
// instead of a silently corrupted balance sheet or a downstream 500 (G3 — customer inputs are safe).
public static class YearEndFigureInputs
{
    public static IResult? ForDebtor(Debtor input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return Results.BadRequest(new { error = "Debtor name is required." });
        if (input.Amount < 0)
            return Results.BadRequest(new { error = "Debtor amount cannot be negative." });
        return null;
    }

    public static IResult? ForCreditor(Creditor input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return Results.BadRequest(new { error = "Creditor name is required." });
        if (input.Amount < 0)
            return Results.BadRequest(new { error = "Creditor amount cannot be negative." });
        return null;
    }

    public static IResult? ForInventory(Inventory input)
    {
        if (string.IsNullOrWhiteSpace(input.Description))
            return Results.BadRequest(new { error = "Inventory description is required." });
        if (input.Value < 0)
            return Results.BadRequest(new { error = "Inventory value cannot be negative." });
        return null;
    }

    public static IResult? ForFixedAsset(FixedAsset input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return Results.BadRequest(new { error = "Fixed asset name is required." });
        if (string.IsNullOrWhiteSpace(input.Category))
            return Results.BadRequest(new { error = "Fixed asset category is required." });
        if (input.Cost < 0)
            return Results.BadRequest(new { error = "Fixed asset cost cannot be negative." });
        if (input.UsefulLifeYears < 1)
            return Results.BadRequest(new { error = "Fixed asset useful life must be at least one year." });
        if (input.DisposalProceeds is < 0)
            return Results.BadRequest(new { error = "Fixed asset disposal proceeds cannot be negative." });
        if (input.DisposalDate is { } disposal && disposal < input.AcquisitionDate)
            return Results.BadRequest(new { error = "Fixed asset disposal date cannot be before the acquisition date." });
        return null;
    }

    public static IResult? ForDividend(Dividend input)
    {
        if (input.Amount < 0)
            return Results.BadRequest(new { error = "Dividend amount cannot be negative." });
        if (input.DateDeclared is { } declared && input.DatePaid is { } paid && paid < declared)
            return Results.BadRequest(new { error = "Dividend payment date cannot be before the declaration date." });
        return null;
    }
}
