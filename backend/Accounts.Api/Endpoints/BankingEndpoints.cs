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

        banks.MapGet("/", async (int companyId, HttpContext context, AccountsDbContext db) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            return Results.Ok(await db.BankAccounts.Where(b => b.CompanyId == companyId).ToListAsync());
        });

        banks.MapPost("/", async (int companyId, BankAccount input, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, AccountingWriteGuard writeGuard) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            if (BankingEndpointInputs.ValidateBankAccount(input) is { } validationProblem)
                return validationProblem;

            var effectiveDate = input.OpeningBalance == 0 ? DateOnly.MaxValue : input.OpeningBalanceDate;
            if (await writeGuard.BlockIfCompanyAccountingLockedAsync(companyId, effectiveDate) is { } blocked)
                return blocked;

            input.CompanyId = companyId;
            db.BankAccounts.Add(input);
            await db.SaveChangesAsync();
            return Results.Created($"/api/companies/{companyId}/bank-accounts/{input.Id}", input);
        });

        banks.MapPut("/{id:int}", async (int companyId, int id, BankAccount input, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, AccountingWriteGuard writeGuard) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            var bank = await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == id && b.CompanyId == companyId);
            if (bank == null) return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            if (BankingEndpointInputs.ValidateBankAccount(input) is { } validationProblem)
                return validationProblem;

            var existingEffectiveDate = bank.OpeningBalance == 0 ? DateOnly.MaxValue : bank.OpeningBalanceDate;
            var inputEffectiveDate = input.OpeningBalance == 0 ? DateOnly.MaxValue : input.OpeningBalanceDate;
            var effectiveDate = BankingEndpointInputs.Earliest(existingEffectiveDate, inputEffectiveDate);
            if (await writeGuard.BlockIfCompanyAccountingLockedAsync(companyId, effectiveDate) is { } blocked)
                return blocked;

            bank.Name = input.Name;
            bank.Iban = input.Iban;
            bank.Currency = input.Currency;
            bank.OpeningBalance = input.OpeningBalance;
            bank.OpeningBalanceDate = input.OpeningBalanceDate;
            await db.SaveChangesAsync();
            return Results.Ok(bank);
        });

        banks.MapDelete("/{id:int}", async (int companyId, int id, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, AccountingWriteGuard writeGuard) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            var bank = await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == id && b.CompanyId == companyId);
            if (bank == null) return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            var effectiveDate = bank.OpeningBalance == 0 ? DateOnly.MaxValue : bank.OpeningBalanceDate;
            if (await writeGuard.BlockIfCompanyAccountingLockedAsync(companyId, effectiveDate) is { } blocked)
                return blocked;

            db.BankAccounts.Remove(bank);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // CSV Import
        banks.MapPost("/{bankAccountId:int}/import", ImportCsvEndpointAsync).DisableAntiforgery();

        // Transactions
        var transactions = app.MapGroup("/api/companies/{companyId:int}/periods/{periodId:int}/transactions").WithTags("Transactions");

        transactions.MapGet("/", ListTransactionsEndpointAsync);

        transactions.MapPut("/{id:int}/categorise", CategoriseTransactionEndpointAsync);

        transactions.MapPost("/bulk-categorise", BulkCategoriseTransactionsEndpointAsync);

        // Categories
        var categories = app.MapGroup("/api/companies/{companyId:int}/categories").WithTags("Categories");

        categories.MapGet("/", async (int companyId, HttpContext context, AccountsDbContext db) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            return Results.Ok(await db.AccountCategories
                .Where(c => c.CompanyId == companyId || (c.IsSystem && c.CompanyId == null))
                .OrderBy(c => c.Code)
                .ToListAsync());
        });

        categories.MapPost("/seed", async (int companyId, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, CategoryService service) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            var cats = await service.SeedDefaultCategoriesAsync(companyId);
            return Results.Ok(cats);
        });

        categories.MapPost("/", async (int companyId, AccountCategory input, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            input.CompanyId = companyId;
            db.AccountCategories.Add(input);
            await db.SaveChangesAsync();
            return Results.Created($"/api/companies/{companyId}/categories/{input.Id}", input);
        });

        // Transaction Rules
        var rules = app.MapGroup("/api/companies/{companyId:int}/transaction-rules").WithTags("Transaction Rules");

        rules.MapGet("/", async (int companyId, HttpContext context, AccountsDbContext db) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            return Results.Ok(await db.TransactionRules
                .Include(r => r.Category)
                .Where(r => r.CompanyId == companyId)
                .OrderBy(r => r.Priority)
                .ToListAsync());
        });

        rules.MapPost("/", async (int companyId, TransactionRuleInput input, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            if (await TransactionRuleInputs.ValidateAsync(db, companyId, input) is { } validationProblem)
                return validationProblem;

            var rule = TransactionRuleInputs.ToEntity(companyId, input);
            db.TransactionRules.Add(rule);
            await db.SaveChangesAsync();
            return Results.Created($"/api/companies/{companyId}/transaction-rules/{rule.Id}", rule);
        });

        rules.MapDelete("/{id:int}", async (int companyId, int id, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            var rule = await db.TransactionRules.FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == companyId);
            if (rule == null) return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            db.TransactionRules.Remove(rule);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    public static async Task<IResult> CategoriseTransactionEndpointAsync(
        int companyId,
        int periodId,
        int id,
        CategoriseInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
            return Results.NotFound();

        var period = await FindTransactionPeriodAsync(db, companyId, periodId);
        if (period is null)
            return Results.NotFound();

        var txn = await db.ImportedTransactions
            .Include(t => t.BankAccount)
            .FirstOrDefaultAsync(t => t.Id == id && t.PeriodId == periodId && t.BankAccount.CompanyId == companyId);
        if (txn == null) return Results.NotFound();

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        if (PeriodLocked(period.Status, period.LockedAt))
            return LockedCategorisationResult();

        if (!await CategoryAvailableToCompanyAsync(db, companyId, input.CategoryId))
            return Results.BadRequest(new { error = "Category is not available for this company." });

        var user = AuthContext.RequireUser(context);
        var oldValue = TransactionCategorisationSnapshot(txn);
        ApplyCategorisation(txn, input.CategoryId);
        await audit.LogAsync(
            companyId,
            periodId,
            "ImportedTransaction",
            txn.Id,
            AuditEventCodes.TransactionCategorised,
            oldValue,
            TransactionCategorisationSnapshot(txn),
            AuthenticatedIdentity.AuditUserId(user));
        return Results.Ok(txn);
    }

    public static async Task<IResult> ListTransactionsEndpointAsync(
        int companyId,
        int periodId,
        AccountsDbContext db,
        HttpContext context,
        int? page,
        int? pageSize,
        bool? uncategorised,
        int? categoryId,
        int? bankAccountId,
        string? search)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
            return Results.NotFound();

        if (await ValidateTransactionReadPeriodAsync(db, companyId, periodId) is { } blocked)
            return blocked;

        var query = db.ImportedTransactions
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

        await HydrateAvailableCategoriesAsync(db, companyId, items);

        return Results.Ok(new { total, items });
    }

    public static async Task<IResult> BulkCategoriseTransactionsEndpointAsync(
        int companyId,
        int periodId,
        BulkCategoriseInput input,
        AccountsDbContext db,
        AuditService audit,
        HttpContext context,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
            return Results.NotFound();

        var transactionIds = input.TransactionIds?.Distinct().ToArray() ?? [];
        var period = await FindTransactionPeriodAsync(db, companyId, periodId);
        if (period is null)
            return Results.NotFound();

        if (transactionIds.Length == 0)
        {
            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } emptySelectionDenied)
                return emptySelectionDenied;

            return Results.BadRequest(new { error = "No transactions selected." });
        }

        var txns = await db.ImportedTransactions
            .Include(t => t.BankAccount)
            .Where(t => transactionIds.Contains(t.Id) && t.PeriodId == periodId && t.BankAccount.CompanyId == companyId)
            .OrderBy(t => t.Id)
            .ToListAsync();
        if (txns.Count != transactionIds.Length)
            return Results.NotFound(new { error = "One or more transactions were not found for this company period." });

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } deniedBulkWrite)
            return deniedBulkWrite;

        if (PeriodLocked(period.Status, period.LockedAt))
            return LockedCategorisationResult();

        if (!await CategoryAvailableToCompanyAsync(db, companyId, input.CategoryId))
            return Results.BadRequest(new { error = "Category is not available for this company." });

        var user = AuthContext.RequireUser(context);
        var oldValue = txns.Select(TransactionCategorisationSnapshot).ToList();
        foreach (var txn in txns)
        {
            ApplyCategorisation(txn, input.CategoryId);
        }

        await audit.LogAsync(
            companyId,
            periodId,
            "ImportedTransactionBatch",
            periodId,
            AuditEventCodes.TransactionsBulkCategorised,
            oldValue,
            new
            {
                CategoryId = input.CategoryId,
                RequestedCount = transactionIds.Length,
                UpdatedCount = txns.Count,
                TransactionIds = txns.Select(t => t.Id).ToArray(),
                Transactions = txns.Select(TransactionCategorisationSnapshot).ToList()
            },
            AuthenticatedIdentity.AuditUserId(user));
        return Results.Ok(new { updated = txns.Count });
    }

    private static async Task<IResult?> ValidateTransactionWritePeriodAsync(
        AccountsDbContext db,
        int companyId,
        int periodId)
    {
        var period = await FindTransactionPeriodAsync(db, companyId, periodId);
        if (period is null)
            return Results.NotFound();

        return PeriodLocked(period.Status, period.LockedAt)
            ? LockedCategorisationResult()
            : null;
    }

    private static Task<TransactionPeriodState?> FindTransactionPeriodAsync(
        AccountsDbContext db,
        int companyId,
        int periodId) =>
        db.AccountingPeriods
            .AsNoTracking()
            .Where(p => p.Id == periodId && p.CompanyId == companyId)
            .Select(p => new TransactionPeriodState(p.Status, p.LockedAt))
            .FirstOrDefaultAsync();

    private static bool PeriodLocked(PeriodStatus status, DateTime? lockedAt) =>
        status is PeriodStatus.Finalised or PeriodStatus.Filed || lockedAt is not null;

    private static IResult LockedCategorisationResult() =>
        Results.Conflict(new { error = "Accounting period is locked. Reopen the period before categorising transactions." });

    public static async Task<IResult> ImportCsvEndpointAsync(
        int companyId,
        int bankAccountId,
        int periodId,
        HttpRequest request,
        ImportService importService,
        AuditService auditService,
        IOptions<ImportLimitConfig> importLimits,
        AccountsDbContext db,
        ApiAccessService apiAccess)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyAsync(request.HttpContext, db, companyId))
            return Results.NotFound(new { error = "Bank account or accounting period not found for this company." });

        var bankBelongsToCompany = await db.BankAccounts
            .AnyAsync(b => b.Id == bankAccountId && b.CompanyId == companyId);
        var period = await db.AccountingPeriods
            .Where(p => p.Id == periodId && p.CompanyId == companyId)
            .Select(p => new { p.Status, p.LockedAt })
            .FirstOrDefaultAsync();
        if (!bankBelongsToCompany || period is null)
            return Results.NotFound(new { error = "Bank account or accounting period not found for this company." });

        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(request.HttpContext, apiAccess) is { } denied)
            return denied;

        var user = AuthContext.RequireUser(request.HttpContext);
        if (period.Status is PeriodStatus.Finalised or PeriodStatus.Filed || period.LockedAt is not null)
            return Results.Conflict(new { error = "Accounting period is locked. Reopen the period before importing transactions." });

        if (!request.HasFormContentType) return Results.BadRequest(new { error = "Expected multipart form data" });
        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync();
        }
        catch (InvalidDataException)
        {
            return Results.BadRequest(new { error = "Upload a valid multipart CSV bank statement." });
        }
        catch (BadHttpRequestException)
        {
            return Results.BadRequest(new { error = "Upload a valid multipart CSV bank statement." });
        }
        catch (IOException)
        {
            return Results.BadRequest(new { error = "Upload a valid multipart CSV bank statement." });
        }

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

        ImportService.ImportResult result;
        try
        {
            using var stream = file.OpenReadStream();
            result = await importService.ImportCsvAsync(companyId, bankAccountId, periodId, stream, file.FileName);
        }
        catch (BusinessRuleException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

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
    }

    private static async Task<IResult?> ValidateTransactionReadPeriodAsync(
        AccountsDbContext db,
        int companyId,
        int periodId)
    {
        var periodExists = await db.AccountingPeriods
            .AsNoTracking()
            .AnyAsync(p => p.Id == periodId && p.CompanyId == companyId);
        return periodExists ? null : Results.NotFound();
    }

    private static Task<bool> CategoryAvailableToCompanyAsync(AccountsDbContext db, int companyId, int categoryId) =>
        db.AccountCategories.AnyAsync(c => c.Id == categoryId && (c.CompanyId == companyId || (c.IsSystem && c.CompanyId == null)));

    private static async Task HydrateAvailableCategoriesAsync(AccountsDbContext db, int companyId, IReadOnlyCollection<ImportedTransaction> transactions)
    {
        var categoryIds = transactions
            .Select(t => t.CategoryId)
            .OfType<int>()
            .Distinct()
            .ToArray();
        if (categoryIds.Length == 0)
            return;

        var availableCategories = await db.AccountCategories
            .Where(c => categoryIds.Contains(c.Id) && (c.CompanyId == companyId || (c.IsSystem && c.CompanyId == null)))
            .ToDictionaryAsync(c => c.Id);

        foreach (var transaction in transactions)
        {
            transaction.Category = transaction.CategoryId is { } transactionCategoryId
                && availableCategories.TryGetValue(transactionCategoryId, out var category)
                    ? category
                    : null;
        }
    }

    private static void ApplyCategorisation(ImportedTransaction txn, int categoryId)
    {
        txn.CategoryId = categoryId;
        txn.ManualOverride = true;
        txn.ConfidenceScore = 1.0m;
    }

    private static object TransactionCategorisationSnapshot(ImportedTransaction txn) => new
    {
        txn.Id,
        txn.Date,
        txn.Description,
        txn.Amount,
        txn.CategoryId,
        txn.ManualOverride,
        txn.ConfidenceScore
    };
}

public record CategoriseInput(int CategoryId);
public record BulkCategoriseInput(List<int> TransactionIds, int CategoryId);
public record TransactionRuleInput(string Pattern, int CategoryId, int Priority);
public record TransactionPeriodState(PeriodStatus Status, DateTime? LockedAt);

public static class BankingEndpointInputs
{
    public static IResult? ValidateBankAccount(BankAccount input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return Results.BadRequest(new { error = "Bank account name is required." });
        if (input.OpeningBalance != 0 && input.OpeningBalanceDate is null)
            return Results.BadRequest(new { error = "Opening balance date is required when an opening balance is entered." });

        return null;
    }

    public static DateOnly? Earliest(DateOnly? left, DateOnly? right) =>
        (left, right) switch
        {
            (null, null) => null,
            ({ } value, null) => value,
            (null, { } value) => value,
            ({ } a, { } b) => a <= b ? a : b
        };
}

public static class TransactionRuleInputs
{
    public static async Task<IResult?> ValidateAsync(AccountsDbContext db, int companyId, TransactionRuleInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Pattern))
            return Results.BadRequest(new { error = "Transaction rule pattern is required." });


        var categoryBelongsToCompany = await db.AccountCategories
            .AnyAsync(c => c.Id == input.CategoryId && (c.CompanyId == companyId || (c.IsSystem && c.CompanyId == null)));
        return categoryBelongsToCompany
            ? null
            : Results.BadRequest(new { error = "Category is not available for this company." });
    }

    public static TransactionRule ToEntity(int companyId, TransactionRuleInput input) => new()
    {
        CompanyId = companyId,
        Pattern = input.Pattern.Trim(),
        CategoryId = input.CategoryId,
        Priority = input.Priority
    };
}
