using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Endpoints;

public static class BankingEndpoints
{
    public static void MapBankingEndpoints(this WebApplication app)
    {
        // Bank Accounts
        var banks = app.MapGroup("/api/companies/{companyId:int}/bank-accounts").WithTags("Bank Accounts");

        banks.MapGet("/", async (int companyId, AccountsDbContext db) =>
            await db.BankAccounts.Where(b => b.CompanyId == companyId).ToListAsync());

        banks.MapPost("/", async (int companyId, BankAccount input, AccountsDbContext db) =>
        {
            input.CompanyId = companyId;
            db.BankAccounts.Add(input);
            await db.SaveChangesAsync();
            return Results.Created($"/api/companies/{companyId}/bank-accounts/{input.Id}", input);
        });

        banks.MapPut("/{id:int}", async (int companyId, int id, BankAccount input, AccountsDbContext db) =>
        {
            var bank = await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == id && b.CompanyId == companyId);
            if (bank == null) return Results.NotFound();
            bank.Name = input.Name;
            bank.Iban = input.Iban;
            bank.Currency = input.Currency;
            bank.OpeningBalance = input.OpeningBalance;
            bank.OpeningBalanceDate = input.OpeningBalanceDate;
            await db.SaveChangesAsync();
            return Results.Ok(bank);
        });

        banks.MapDelete("/{id:int}", async (int companyId, int id, AccountsDbContext db) =>
        {
            var bank = await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == id && b.CompanyId == companyId);
            if (bank == null) return Results.NotFound();
            db.BankAccounts.Remove(bank);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // CSV Import
        banks.MapPost("/{bankAccountId:int}/import", async (int companyId, int bankAccountId, int periodId, HttpRequest request, ImportService importService, AuditService auditService, IOptions<ImportLimitConfig> importLimits, AccountsDbContext db) =>
        {
            var user = AuthContext.RequireUser(request.HttpContext);
            var bankBelongsToCompany = await db.BankAccounts
                .AnyAsync(b => b.Id == bankAccountId && b.CompanyId == companyId);
            var period = await db.AccountingPeriods
                .Where(p => p.Id == periodId && p.CompanyId == companyId)
                .Select(p => new { p.Status, p.LockedAt })
                .FirstOrDefaultAsync();
            if (!bankBelongsToCompany || period is null)
                return Results.NotFound(new { error = "Bank account or accounting period not found for this company." });
            if (period.Status is PeriodStatus.Finalised or PeriodStatus.Filed || period.LockedAt is not null)
                return Results.Conflict(new { error = "Accounting period is locked. Reopen the period before importing transactions." });

            if (!request.HasFormContentType) return Results.BadRequest(new { error = "Expected multipart form data" });
            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file == null) return Results.BadRequest(new { error = "No file uploaded" });
            var limits = importLimits.Value;
            var extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension) || !limits.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "Only CSV bank statement files are accepted." });
            if (file.Length <= 0)
                return Results.BadRequest(new { error = "Uploaded file is empty." });
            if (file.Length > limits.MaxCsvBytes)
                return Results.BadRequest(new { error = $"CSV file is too large. Maximum allowed size is {limits.MaxCsvBytes / 1024 / 1024} MB." });
            if (!string.IsNullOrWhiteSpace(file.ContentType) && !limits.AllowedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = $"Unsupported upload content type '{file.ContentType}'. Upload a CSV export from the bank." });

            using var stream = file.OpenReadStream();
            var result = await importService.ImportCsvAsync(bankAccountId, periodId, stream, file.FileName);
            await auditService.LogAsync(companyId, periodId, "ImportBatch", bankAccountId, "BankCsvImported", null, new
            {
                file.FileName,
                file.Length,
                result.TotalRows,
                result.ImportedRows,
                result.DuplicatesSkipped,
                result.AutoCategorised,
                WarningCount = result.Warnings.Count
            }, AuthenticatedIdentity.AuditUserId(user));
            return Results.Ok(result);
        }).DisableAntiforgery();

        // Transactions
        var transactions = app.MapGroup("/api/companies/{companyId:int}/periods/{periodId:int}/transactions").WithTags("Transactions");

        transactions.MapGet("/", async (int companyId, int periodId, AccountsDbContext db,
            int? page, int? pageSize, bool? uncategorised, int? categoryId, int? bankAccountId, string? search) =>
        {
            var query = db.ImportedTransactions
                .Include(t => t.Category)
                .Where(t => t.PeriodId == periodId && t.BankAccount.CompanyId == companyId && !t.IsDuplicate);

            if (uncategorised == true)
                query = query.Where(t => t.CategoryId == null);

            if (categoryId.HasValue)
                query = query.Where(t => t.CategoryId == categoryId.Value);

            if (bankAccountId.HasValue)
                query = query.Where(t => t.BankAccountId == bankAccountId.Value);

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(t => t.Description.Contains(search));

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(t => t.Date)
                .Skip(((page ?? 1) - 1) * (pageSize ?? 50))
                .Take(pageSize ?? 50)
                .ToListAsync();

            return Results.Ok(new { total, items });
        });

        transactions.MapPut("/{id:int}/categorise", async (int companyId, int periodId, int id, CategoriseInput input, AccountsDbContext db) =>
        {
            var txn = await db.ImportedTransactions
                .Include(t => t.BankAccount)
                .FirstOrDefaultAsync(t => t.Id == id && t.PeriodId == periodId && t.BankAccount.CompanyId == companyId);
            if (txn == null) return Results.NotFound();

            var categoryExists = await db.AccountCategories
                .AnyAsync(c => c.Id == input.CategoryId && (c.CompanyId == companyId || (c.IsSystem && c.CompanyId == null)));
            if (!categoryExists) return Results.BadRequest(new { error = "Category is not available for this company." });

            txn.CategoryId = input.CategoryId;
            txn.ManualOverride = true;
            txn.ConfidenceScore = 1.0m;
            await db.SaveChangesAsync();
            return Results.Ok(txn);
        });

        transactions.MapPost("/bulk-categorise", async (int companyId, int periodId, BulkCategoriseInput input, AccountsDbContext db) =>
        {
            if (input.TransactionIds.Count == 0)
                return Results.BadRequest(new { error = "No transactions selected." });

            var categoryExists = await db.AccountCategories
                .AnyAsync(c => c.Id == input.CategoryId && (c.CompanyId == companyId || (c.IsSystem && c.CompanyId == null)));
            if (!categoryExists) return Results.BadRequest(new { error = "Category is not available for this company." });

            var txns = await db.ImportedTransactions
                .Include(t => t.BankAccount)
                .Where(t => input.TransactionIds.Contains(t.Id) && t.PeriodId == periodId && t.BankAccount.CompanyId == companyId)
                .ToListAsync();
            foreach (var txn in txns)
            {
                txn.CategoryId = input.CategoryId;
                txn.ManualOverride = true;
                txn.ConfidenceScore = 1.0m;
            }
            await db.SaveChangesAsync();
            return Results.Ok(new { updated = txns.Count });
        });

        // Categories
        var categories = app.MapGroup("/api/companies/{companyId:int}/categories").WithTags("Categories");

        categories.MapGet("/", async (int companyId, AccountsDbContext db) =>
            await db.AccountCategories
                .Where(c => c.CompanyId == companyId || (c.IsSystem && c.CompanyId == null))
                .OrderBy(c => c.Code)
                .ToListAsync());

        categories.MapPost("/seed", async (int companyId, CategoryService service) =>
        {
            var cats = await service.SeedDefaultCategoriesAsync(companyId);
            return Results.Ok(cats);
        });

        categories.MapPost("/", async (int companyId, AccountCategory input, AccountsDbContext db) =>
        {
            input.CompanyId = companyId;
            db.AccountCategories.Add(input);
            await db.SaveChangesAsync();
            return Results.Created($"/api/companies/{companyId}/categories/{input.Id}", input);
        });

        // Transaction Rules
        var rules = app.MapGroup("/api/companies/{companyId:int}/transaction-rules").WithTags("Transaction Rules");

        rules.MapGet("/", async (int companyId, AccountsDbContext db) =>
            await db.TransactionRules.Include(r => r.Category).Where(r => r.CompanyId == companyId).OrderBy(r => r.Priority).ToListAsync());

        rules.MapPost("/", async (int companyId, TransactionRule input, AccountsDbContext db) =>
        {
            input.CompanyId = companyId;
            db.TransactionRules.Add(input);
            await db.SaveChangesAsync();
            return Results.Created($"/api/companies/{companyId}/transaction-rules/{input.Id}", input);
        });

        rules.MapDelete("/{id:int}", async (int companyId, int id, AccountsDbContext db) =>
        {
            var rule = await db.TransactionRules.FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == companyId);
            if (rule == null) return Results.NotFound();
            db.TransactionRules.Remove(rule);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}

public record CategoriseInput(int CategoryId);
public record BulkCategoriseInput(List<int> TransactionIds, int CategoryId);
