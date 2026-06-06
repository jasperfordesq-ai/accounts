using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Endpoints;

public static class YearEndEndpoints
{
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

        debtors.MapGet("/", async (int companyId, int periodId, AccountsDbContext db) =>
            await db.Debtors.Where(d => d.PeriodId == periodId).ToListAsync());

        debtors.MapPost("/", async (int companyId, int periodId, Debtor input, AccountsDbContext db) =>
        {
            input.PeriodId = periodId;
            db.Debtors.Add(input);
            await db.SaveChangesAsync();
            return Results.Created($"{basePath}/debtors/{input.Id}", input);
        });

        debtors.MapPut("/{id:int}", async (int companyId, int periodId, int id, Debtor input, AccountsDbContext db) =>
        {
            var item = await db.Debtors.FirstOrDefaultAsync(d => d.Id == id && d.PeriodId == periodId);
            if (item == null) return Results.NotFound();
            item.Name = input.Name;
            item.Amount = input.Amount;
            item.Type = input.Type;
            item.Notes = input.Notes;
            await db.SaveChangesAsync();
            return Results.Ok(item);
        });

        debtors.MapDelete("/{id:int}", async (int companyId, int periodId, int id, AccountsDbContext db) =>
        {
            var item = await db.Debtors.FirstOrDefaultAsync(d => d.Id == id && d.PeriodId == periodId);
            if (item == null) return Results.NotFound();
            db.Debtors.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ===== CREDITORS =====
        var creditors = app.MapGroup($"{basePath}/creditors").WithTags("Creditors");

        creditors.MapGet("/", async (int companyId, int periodId, AccountsDbContext db) =>
            await db.Creditors.Where(c => c.PeriodId == periodId).ToListAsync());

        creditors.MapPost("/", async (int companyId, int periodId, Creditor input, AccountsDbContext db) =>
        {
            input.PeriodId = periodId;
            db.Creditors.Add(input);
            await db.SaveChangesAsync();
            return Results.Created($"{basePath}/creditors/{input.Id}", input);
        });

        creditors.MapPut("/{id:int}", async (int companyId, int periodId, int id, Creditor input, AccountsDbContext db) =>
        {
            var item = await db.Creditors.FirstOrDefaultAsync(c => c.Id == id && c.PeriodId == periodId);
            if (item == null) return Results.NotFound();
            item.Name = input.Name;
            item.Amount = input.Amount;
            item.Type = input.Type;
            item.DueWithinYear = input.DueWithinYear;
            item.Notes = input.Notes;
            await db.SaveChangesAsync();
            return Results.Ok(item);
        });

        creditors.MapDelete("/{id:int}", async (int companyId, int periodId, int id, AccountsDbContext db) =>
        {
            var item = await db.Creditors.FirstOrDefaultAsync(c => c.Id == id && c.PeriodId == periodId);
            if (item == null) return Results.NotFound();
            db.Creditors.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ===== FIXED ASSETS =====
        var assets = app.MapGroup("/api/companies/{companyId:int}/fixed-assets").WithTags("Fixed Assets");

        assets.MapGet("/", async (int companyId, AccountsDbContext db) =>
            await db.FixedAssets.Include(a => a.DepreciationEntries).Where(a => a.CompanyId == companyId).ToListAsync());

        assets.MapPost("/", async (int companyId, FixedAsset input, AccountsDbContext db) =>
        {
            input.CompanyId = companyId;
            db.FixedAssets.Add(input);
            await db.SaveChangesAsync();
            return Results.Created($"/api/companies/{companyId}/fixed-assets/{input.Id}", input);
        });

        assets.MapPut("/{id:int}", async (int companyId, int id, FixedAsset input, AccountsDbContext db) =>
        {
            var item = await db.FixedAssets.FirstOrDefaultAsync(a => a.Id == id && a.CompanyId == companyId);
            if (item == null) return Results.NotFound();
            item.Name = input.Name;
            item.Category = input.Category;
            item.Cost = input.Cost;
            item.AcquisitionDate = input.AcquisitionDate;
            item.DisposalDate = input.DisposalDate;
            item.DisposalProceeds = input.DisposalProceeds;
            item.UsefulLifeYears = input.UsefulLifeYears;
            item.DepreciationMethod = input.DepreciationMethod;
            await db.SaveChangesAsync();
            return Results.Ok(item);
        });

        assets.MapDelete("/{id:int}", async (int companyId, int id, AccountsDbContext db) =>
        {
            var item = await db.FixedAssets.FirstOrDefaultAsync(a => a.Id == id && a.CompanyId == companyId);
            if (item == null) return Results.NotFound();
            db.FixedAssets.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ===== INVENTORY =====
        var inventory = app.MapGroup($"{basePath}/inventory").WithTags("Inventory");

        inventory.MapGet("/", async (int companyId, int periodId, AccountsDbContext db) =>
            await db.Inventories.Where(i => i.PeriodId == periodId).ToListAsync());

        inventory.MapPost("/", async (int companyId, int periodId, Inventory input, AccountsDbContext db) =>
        {
            input.PeriodId = periodId;
            db.Inventories.Add(input);
            await db.SaveChangesAsync();
            return Results.Created($"{basePath}/inventory/{input.Id}", input);
        });

        inventory.MapPut("/{id:int}", async (int companyId, int periodId, int id, Inventory input, AccountsDbContext db) =>
        {
            var item = await db.Inventories.FirstOrDefaultAsync(i => i.Id == id && i.PeriodId == periodId);
            if (item == null) return Results.NotFound();
            item.Description = input.Description;
            item.Value = input.Value;
            item.ValuationMethod = input.ValuationMethod;
            await db.SaveChangesAsync();
            return Results.Ok(item);
        });

        inventory.MapDelete("/{id:int}", async (int companyId, int periodId, int id, AccountsDbContext db) =>
        {
            var item = await db.Inventories.FirstOrDefaultAsync(i => i.Id == id && i.PeriodId == periodId);
            if (item == null) return Results.NotFound();
            db.Inventories.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ===== LOANS =====
        var loans = app.MapGroup("/api/companies/{companyId:int}/loans").WithTags("Loans");

        loans.MapGet("/", async (int companyId, AccountsDbContext db) =>
            await db.Loans.Where(l => l.CompanyId == companyId).ToListAsync());

        loans.MapPost("/", async (int companyId, Loan input, AccountsDbContext db) =>
        {
            input.CompanyId = companyId;
            db.Loans.Add(input);
            await db.SaveChangesAsync();
            return Results.Created($"/api/companies/{companyId}/loans/{input.Id}", input);
        });

        loans.MapPut("/{id:int}", async (int companyId, int id, Loan input, AccountsDbContext db) =>
        {
            var item = await db.Loans.FirstOrDefaultAsync(l => l.Id == id && l.CompanyId == companyId);
            if (item == null) return Results.NotFound();
            item.Lender = input.Lender;
            item.OriginalAmount = input.OriginalAmount;
            item.Balance = input.Balance;
            item.InterestRate = input.InterestRate;
            item.IsDirectorLoan = input.IsDirectorLoan;
            item.DueWithinYear = input.DueWithinYear;
            item.DueAfterYear = input.DueAfterYear;
            await db.SaveChangesAsync();
            return Results.Ok(item);
        });

        loans.MapDelete("/{id:int}", async (int companyId, int id, AccountsDbContext db) =>
        {
            var item = await db.Loans.FirstOrDefaultAsync(l => l.Id == id && l.CompanyId == companyId);
            if (item == null) return Results.NotFound();
            db.Loans.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ===== DIRECTOR LOANS =====
        var dirLoans = app.MapGroup($"{basePath}/director-loans").WithTags("Director Loans");

        dirLoans.MapGet("/", async (int companyId, int periodId, AccountsDbContext db) =>
            await db.DirectorLoans.Include(d => d.Director).Where(d => d.PeriodId == periodId).ToListAsync());

        dirLoans.MapPost("/", async (int companyId, int periodId, DirectorLoan input, AccountsDbContext db) =>
        {
            input.PeriodId = periodId;
            db.DirectorLoans.Add(input);
            await db.SaveChangesAsync();
            return Results.Created($"{basePath}/director-loans/{input.Id}", input);
        });

        dirLoans.MapPut("/{id:int}", async (int companyId, int periodId, int id, DirectorLoan input, AccountsDbContext db) =>
        {
            var item = await db.DirectorLoans.FirstOrDefaultAsync(d => d.Id == id && d.PeriodId == periodId);
            if (item == null) return Results.NotFound();
            item.DirectorId = input.DirectorId;
            item.OpeningBalance = input.OpeningBalance;
            item.Advances = input.Advances;
            item.Repayments = input.Repayments;
            item.ClosingBalance = input.ClosingBalance;
            await db.SaveChangesAsync();
            return Results.Ok(item);
        });

        dirLoans.MapDelete("/{id:int}", async (int companyId, int periodId, int id, AccountsDbContext db) =>
        {
            var item = await db.DirectorLoans.FirstOrDefaultAsync(d => d.Id == id && d.PeriodId == periodId);
            if (item == null) return Results.NotFound();
            db.DirectorLoans.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ===== PAYROLL SUMMARY =====
        var payroll = app.MapGroup($"{basePath}/payroll").WithTags("Payroll");

        payroll.MapGet("/", async (int companyId, int periodId, AccountsDbContext db) =>
            await db.PayrollSummaries.FirstOrDefaultAsync(p => p.PeriodId == periodId)
            is { } ps ? Results.Ok(ps) : Results.NotFound());

        payroll.MapPut("/", async (int companyId, int periodId, PayrollSummary input, AccountsDbContext db) =>
        {
            var item = await db.PayrollSummaries.FirstOrDefaultAsync(p => p.PeriodId == periodId);
            if (item == null)
            {
                input.PeriodId = periodId;
                db.PayrollSummaries.Add(input);
            }
            else
            {
                item.GrossWages = input.GrossWages;
                item.EmployerPrsi = input.EmployerPrsi;
                item.PensionContributions = input.PensionContributions;
                item.StaffCount = input.StaffCount;
            }
            await db.SaveChangesAsync();
            return Results.Ok(item ?? input);
        });

        // ===== TAX BALANCES =====
        var taxes = app.MapGroup($"{basePath}/tax-balances").WithTags("Tax Balances");

        taxes.MapGet("/", async (int companyId, int periodId, AccountsDbContext db) =>
            await db.TaxBalances.Where(t => t.PeriodId == periodId).ToListAsync());

        taxes.MapPut("/{taxType}", async (int companyId, int periodId, TaxType taxType, TaxBalance input, AccountsDbContext db) =>
        {
            var item = await db.TaxBalances.FirstOrDefaultAsync(t => t.PeriodId == periodId && t.TaxType == taxType);
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
            return Results.Ok(item);
        });

        // ===== DIVIDENDS =====
        var dividends = app.MapGroup($"{basePath}/dividends").WithTags("Dividends");

        dividends.MapGet("/", async (int companyId, int periodId, AccountsDbContext db) =>
            await db.Dividends.Where(d => d.PeriodId == periodId).ToListAsync());

        dividends.MapPost("/", async (int companyId, int periodId, Dividend input, AccountsDbContext db) =>
        {
            input.PeriodId = periodId;
            db.Dividends.Add(input);
            await db.SaveChangesAsync();
            return Results.Created($"{basePath}/dividends/{input.Id}", input);
        });

        dividends.MapDelete("/{id:int}", async (int companyId, int periodId, int id, AccountsDbContext db) =>
        {
            var item = await db.Dividends.FirstOrDefaultAsync(d => d.Id == id && d.PeriodId == periodId);
            if (item == null) return Results.NotFound();
            db.Dividends.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ===== YEAR-END SUMMARY (read-only overview) =====
        app.MapGet($"{basePath}/year-end-summary", async (int companyId, int periodId, AccountsDbContext db) =>
        {
            var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == companyId);
            if (company == null) return Results.NotFound();

            var debtorsList = await db.Debtors.Where(d => d.PeriodId == periodId).ToListAsync();
            var creditorsList = await db.Creditors.Where(c => c.PeriodId == periodId).ToListAsync();
            var assetsList = await db.FixedAssets.Where(a => a.CompanyId == companyId).ToListAsync();
            var inventoryList = await db.Inventories.Where(i => i.PeriodId == periodId).ToListAsync();
            var loansList = await db.Loans.Where(l => l.CompanyId == companyId).ToListAsync();
            var dirLoansList = await db.DirectorLoans.Where(d => d.PeriodId == periodId).ToListAsync();
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
                loans = new { count = loansList.Count, totalBalance = loansList.Sum(l => l.Balance) },
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

        reviews.MapGet("/", async (int companyId, int periodId, AccountsDbContext db) =>
            await db.YearEndReviewConfirmations
                .Where(r => r.PeriodId == periodId)
                .OrderBy(r => r.SectionKey)
                .ToListAsync());

        reviews.MapPut("/{sectionKey}", async (int companyId, int periodId, string sectionKey, YearEndReviewInput input, AccountsDbContext db, HttpContext context) =>
        {
            var user = AuthContext.RequireUser(context);
            if (!ReviewSectionKeys.Contains(sectionKey))
                return Results.BadRequest(new { error = "Unknown year-end review section." });

            var periodExists = await db.AccountingPeriods.AnyAsync(p => p.Id == periodId && p.CompanyId == companyId);
            if (!periodExists) return Results.NotFound();

            var confirmation = await db.YearEndReviewConfirmations
                .FirstOrDefaultAsync(r => r.PeriodId == periodId && r.SectionKey == sectionKey);

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
            return Results.Ok(confirmation);
        });

        // ===== NOTES DISCLOSURES =====
        var notesGroup = app.MapGroup($"{basePath}/notes").WithTags("Notes");

        notesGroup.MapGet("/", async (int companyId, int periodId, AccountsDbContext db) =>
            await db.NotesDisclosures.Where(n => n.PeriodId == periodId).OrderBy(n => n.NoteNumber).ToListAsync());

        notesGroup.MapPost("/generate", async (int companyId, int periodId, NotesDisclosureService service) =>
        {
            var notes = await service.GenerateNotesAsync(periodId);
            return Results.Ok(notes);
        });

        notesGroup.MapPut("/{id:int}", async (int companyId, int periodId, int id, NotesDisclosure input, AccountsDbContext db) =>
        {
            var note = await db.NotesDisclosures.FirstOrDefaultAsync(n => n.Id == id && n.PeriodId == periodId);
            if (note == null) return Results.NotFound();
            note.Title = input.Title;
            note.Content = input.Content;
            note.IsIncluded = input.IsIncluded;
            await db.SaveChangesAsync();
            return Results.Ok(note);
        });

        notesGroup.MapPost("/", async (int companyId, int periodId, NotesDisclosure input, AccountsDbContext db) =>
        {
            input.PeriodId = periodId;
            var maxNum = await db.NotesDisclosures.Where(n => n.PeriodId == periodId).MaxAsync(n => (int?)n.NoteNumber) ?? 0;
            input.NoteNumber = maxNum + 1;
            db.NotesDisclosures.Add(input);
            await db.SaveChangesAsync();
            return Results.Created($"notes/{input.Id}", input);
        });

        notesGroup.MapDelete("/{id:int}", async (int companyId, int periodId, int id, AccountsDbContext db) =>
        {
            var note = await db.NotesDisclosures.FirstOrDefaultAsync(n => n.Id == id && n.PeriodId == periodId);
            if (note == null) return Results.NotFound();
            db.NotesDisclosures.Remove(note);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ===== SHARE CAPITAL =====
        var shares = app.MapGroup("/api/companies/{companyId:int}/share-capital").WithTags("Share Capital");

        shares.MapGet("/", async (int companyId, AccountsDbContext db) =>
            await db.ShareCapitals.Where(s => s.CompanyId == companyId).ToListAsync());

        shares.MapPost("/", async (int companyId, ShareCapital input, AccountsDbContext db) =>
        {
            input.CompanyId = companyId;
            input.TotalValue = input.NominalValue * input.NumberIssued;
            db.ShareCapitals.Add(input);
            await db.SaveChangesAsync();
            return Results.Created($"/api/companies/{companyId}/share-capital/{input.Id}", input);
        });

        shares.MapPut("/{id:int}", async (int companyId, int id, ShareCapital input, AccountsDbContext db) =>
        {
            var item = await db.ShareCapitals.FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == companyId);
            if (item == null) return Results.NotFound();
            item.ShareClass = input.ShareClass;
            item.NominalValue = input.NominalValue;
            item.NumberIssued = input.NumberIssued;
            item.TotalValue = input.NominalValue * input.NumberIssued;
            item.IsFullyPaid = input.IsFullyPaid;
            await db.SaveChangesAsync();
            return Results.Ok(item);
        });

        shares.MapDelete("/{id:int}", async (int companyId, int id, AccountsDbContext db) =>
        {
            var item = await db.ShareCapitals.FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == companyId);
            if (item == null) return Results.NotFound();
            db.ShareCapitals.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ===== OPENING BALANCES =====
        var openingBalances = app.MapGroup($"{basePath}/opening-balances").WithTags("Opening Balances");

        openingBalances.MapGet("/", async (int companyId, int periodId, AccountsDbContext db) =>
            await db.OpeningBalances
                .Include(o => o.AccountCategory)
                .Where(o => o.PeriodId == periodId)
                .OrderBy(o => o.AccountCategory.Code)
                .ToListAsync());

        openingBalances.MapPut("/{categoryId:int}", async (int companyId, int periodId, int categoryId, OpeningBalanceInput input, AccountsDbContext db) =>
        {
            if (input.Debit < 0 || input.Credit < 0)
                return Results.BadRequest(new { error = "Opening balance debit and credit must not be negative." });
            if (input.Debit > 0 && input.Credit > 0)
                return Results.BadRequest(new { error = "Enter either a debit or a credit opening balance, not both." });

            var periodExists = await db.AccountingPeriods.AnyAsync(p => p.Id == periodId && p.CompanyId == companyId);
            if (!periodExists) return Results.NotFound();

            var categoryExists = await db.AccountCategories
                .AnyAsync(c => c.Id == categoryId && (c.CompanyId == companyId || (c.IsSystem && c.CompanyId == null)));
            if (!categoryExists) return Results.BadRequest(new { error = "Account category is not available for this company." });

            var balance = await db.OpeningBalances
                .FirstOrDefaultAsync(o => o.PeriodId == periodId && o.AccountCategoryId == categoryId);
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
            balance.EnteredBy = string.IsNullOrWhiteSpace(input.EnteredBy) ? "Accounts reviewer" : input.EnteredBy.Trim();
            balance.EnteredAt = DateTime.UtcNow;
            balance.Reviewed = input.Reviewed;
            balance.ReviewedBy = input.Reviewed
                ? string.IsNullOrWhiteSpace(input.EnteredBy) ? "Accounts reviewer" : input.EnteredBy.Trim()
                : null;
            balance.ReviewedAt = input.Reviewed ? DateTime.UtcNow : null;

            await db.SaveChangesAsync();
            await db.Entry(balance).Reference(b => b.AccountCategory).LoadAsync();
            return Results.Ok(balance);
        });

        openingBalances.MapDelete("/{categoryId:int}", async (int companyId, int periodId, int categoryId, AccountsDbContext db) =>
        {
            var balance = await db.OpeningBalances
                .FirstOrDefaultAsync(o => o.PeriodId == periodId && o.AccountCategoryId == categoryId);
            if (balance == null) return Results.NotFound();
            db.OpeningBalances.Remove(balance);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ===== POST BALANCE SHEET EVENTS =====
        var pbse = app.MapGroup($"{basePath}/post-balance-sheet-events").WithTags("Post Balance Sheet Events");
        pbse.MapGet("/", async (int companyId, int periodId, AccountsDbContext db) =>
            await db.PostBalanceSheetEvents.Where(x => x.PeriodId == periodId).OrderBy(x => x.EventDate).ToListAsync());
        pbse.MapPost("/", async (int companyId, int periodId, PostBalanceSheetEvent input, AccountsDbContext db) =>
        {
            input.PeriodId = periodId;
            db.PostBalanceSheetEvents.Add(input);
            await db.SaveChangesAsync();
            return Results.Created($"{basePath}/post-balance-sheet-events/{input.Id}", input);
        });
        pbse.MapDelete("/{id:int}", async (int companyId, int periodId, int id, AccountsDbContext db) =>
        {
            var item = await db.PostBalanceSheetEvents.FirstOrDefaultAsync(x => x.Id == id && x.PeriodId == periodId);
            if (item == null) return Results.NotFound();
            db.PostBalanceSheetEvents.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ===== RELATED PARTY TRANSACTIONS =====
        var rpt = app.MapGroup($"{basePath}/related-party-transactions").WithTags("Related Party Transactions");
        rpt.MapGet("/", async (int companyId, int periodId, AccountsDbContext db) =>
            await db.RelatedPartyTransactions.Where(x => x.PeriodId == periodId).OrderBy(x => x.PartyName).ToListAsync());
        rpt.MapPost("/", async (int companyId, int periodId, RelatedPartyTransaction input, AccountsDbContext db) =>
        {
            input.PeriodId = periodId;
            db.RelatedPartyTransactions.Add(input);
            await db.SaveChangesAsync();
            return Results.Created($"{basePath}/related-party-transactions/{input.Id}", input);
        });
        rpt.MapDelete("/{id:int}", async (int companyId, int periodId, int id, AccountsDbContext db) =>
        {
            var item = await db.RelatedPartyTransactions.FirstOrDefaultAsync(x => x.Id == id && x.PeriodId == periodId);
            if (item == null) return Results.NotFound();
            db.RelatedPartyTransactions.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ===== CONTINGENT LIABILITIES =====
        var cl = app.MapGroup($"{basePath}/contingent-liabilities").WithTags("Contingent Liabilities");
        cl.MapGet("/", async (int companyId, int periodId, AccountsDbContext db) =>
            await db.ContingentLiabilities.Where(x => x.PeriodId == periodId).OrderBy(x => x.Description).ToListAsync());
        cl.MapPost("/", async (int companyId, int periodId, ContingentLiability input, AccountsDbContext db) =>
        {
            input.PeriodId = periodId;
            db.ContingentLiabilities.Add(input);
            await db.SaveChangesAsync();
            return Results.Created($"{basePath}/contingent-liabilities/{input.Id}", input);
        });
        cl.MapDelete("/{id:int}", async (int companyId, int periodId, int id, AccountsDbContext db) =>
        {
            var item = await db.ContingentLiabilities.FirstOrDefaultAsync(x => x.Id == id && x.PeriodId == periodId);
            if (item == null) return Results.NotFound();
            db.ContingentLiabilities.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ===== GOING CONCERN =====
        var gc = app.MapGroup($"{basePath}/going-concern").WithTags("Going Concern");
        gc.MapGet("/", async (int companyId, int periodId, AccountsDbContext db) =>
        {
            var period = await db.AccountingPeriods.FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId);
            if (period == null) return Results.NotFound();
            return Results.Ok(new { period.GoingConcernConfirmed, period.GoingConcernNote });
        });
        gc.MapPut("/", async (int companyId, int periodId, GoingConcernInput input, AccountsDbContext db) =>
        {
            var period = await db.AccountingPeriods.FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId);
            if (period == null) return Results.NotFound();
            period.GoingConcernConfirmed = input.Confirmed;
            period.GoingConcernNote = input.Note;
            await db.SaveChangesAsync();
            return Results.Ok(new { period.GoingConcernConfirmed, period.GoingConcernNote });
        });

        // ===== DIRECTOR LOAN COMPLIANCE (s.239 / s.307) =====
        var dlCompliance = app.MapGroup($"{basePath}/director-loans").WithTags("Director Loans");

        dlCompliance.MapGet("/compliance", async (int companyId, int periodId, DirectorLoanComplianceService service) =>
        {
            try
            {
                var result = await service.GetComplianceStatusAsync(companyId, periodId);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        dlCompliance.MapGet("/section-307-note", async (int companyId, int periodId, DirectorLoanComplianceService service) =>
        {
            try
            {
                var note = await service.GenerateSection307NoteAsync(companyId, periodId);
                return Results.Ok(new { note });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }
}

public record GoingConcernInput(bool Confirmed, string? Note);
public record YearEndReviewInput(bool Confirmed, string? ConfirmedBy, string? Note);
public record OpeningBalanceInput(decimal Debit, decimal Credit, string? SourceNote, string? EnteredBy, bool Reviewed);
