using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Accounts.Api.Endpoints;

public static partial class YearEndEndpoints
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
}
