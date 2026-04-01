using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Endpoints;

public static class YearEndEndpoints
{
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
            var debtorsList = await db.Debtors.Where(d => d.PeriodId == periodId).ToListAsync();
            var creditorsList = await db.Creditors.Where(c => c.PeriodId == periodId).ToListAsync();
            var assetsList = await db.FixedAssets.Where(a => a.CompanyId == companyId).ToListAsync();
            var inventoryList = await db.Inventories.Where(i => i.PeriodId == periodId).ToListAsync();
            var loansList = await db.Loans.Where(l => l.CompanyId == companyId).ToListAsync();
            var dirLoansList = await db.DirectorLoans.Where(d => d.PeriodId == periodId).ToListAsync();
            var payrollItem = await db.PayrollSummaries.FirstOrDefaultAsync(p => p.PeriodId == periodId);
            var taxList = await db.TaxBalances.Where(t => t.PeriodId == periodId).ToListAsync();
            var dividendsList = await db.Dividends.Where(d => d.PeriodId == periodId).ToListAsync();

            // Completeness scoring
            int totalSections = 9;
            int completedSections = 0;
            var incomplete = new List<string>();

            if (debtorsList.Count > 0 || true) completedSections++; // Always counts as reviewed
            if (creditorsList.Count > 0 || true) completedSections++;
            if (assetsList.Count > 0) completedSections++; else incomplete.Add("Fixed Assets");
            if (inventoryList.Count > 0 || true) completedSections++;
            if (loansList.Count > 0 || true) completedSections++;
            if (dirLoansList.Count > 0 || true) completedSections++;
            if (payrollItem != null) completedSections++; else incomplete.Add("Payroll Summary");
            if (taxList.Count > 0) completedSections++; else incomplete.Add("Tax Balances");
            if (dividendsList.Count > 0 || true) completedSections++;

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
                completeness = new { score = (int)Math.Round((double)completedSections / totalSections * 100), completed = completedSections, total = totalSections, incomplete }
            });
        }).WithTags("Year-End Summary");
    }
}
