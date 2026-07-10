using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Mvc;
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

        banks.MapPost("/", async (int companyId, BankAccountInput input, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, AccountingWriteGuard writeGuard, AuditService audit) =>
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

            var bank = input.ToEntity(companyId);
            db.BankAccounts.Add(bank);
            await db.SaveChangesAsync();
            await DomainAuditCoverage.LogAsync(
                audit, context, companyId, null, nameof(BankAccount), bank.Id,
                AuditEventCodes.BankAccountCreated, null, DomainAuditCoverage.BankSnapshot(bank),
                context.RequestAborted);
            return Results.Created($"/api/companies/{companyId}/bank-accounts/{bank.Id}", bank);
        });

        banks.MapPut("/{id:int}", async (int companyId, int id, BankAccountInput input, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, AccountingWriteGuard writeGuard, AuditService audit) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            var bank = await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == id && b.CompanyId == companyId);
            if (bank == null) return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            if (BankingEndpointInputs.ValidateBankAccount(input) is { } validationProblem)
                return validationProblem;

            var normalizedCurrency = BankingEndpointInputs.NormalizeCurrency(input.Currency);
            var identityChanged = !string.Equals(bank.Name, input.Name!.Trim(), StringComparison.Ordinal)
                || !string.Equals(bank.Iban, input.Iban, StringComparison.Ordinal)
                || !string.Equals(bank.Currency, normalizedCurrency, StringComparison.Ordinal);
            if (identityChanged)
            {
                var hasRetainedImportEvidence = await db.ImportBatches.AnyAsync(batch => batch.BankAccountId == id)
                    || await db.ImportedTransactions.AnyAsync(transaction => transaction.BankAccountId == id);
                if (hasRetainedImportEvidence)
                {
                    return Results.Conflict(new
                    {
                        error = "Bank account name, IBAN and currency cannot change after import evidence is retained. Create a new bank account for a changed identity or currency."
                    });
                }
            }

            var existingEffectiveDate = bank.OpeningBalance == 0 ? DateOnly.MaxValue : bank.OpeningBalanceDate;
            var inputEffectiveDate = input.OpeningBalance == 0 ? DateOnly.MaxValue : input.OpeningBalanceDate;
            var effectiveDate = BankingEndpointInputs.Earliest(existingEffectiveDate, inputEffectiveDate);
            if (await writeGuard.BlockIfCompanyAccountingLockedAsync(companyId, effectiveDate) is { } blocked)
                return blocked;

            var oldValue = DomainAuditCoverage.BankSnapshot(bank);
            bank.Name = input.Name!.Trim();
            bank.Iban = string.IsNullOrWhiteSpace(input.Iban) ? null : input.Iban.Trim();
            bank.Currency = normalizedCurrency;
            bank.OpeningBalance = input.OpeningBalance;
            bank.OpeningBalanceDate = input.OpeningBalanceDate;
            await db.SaveChangesAsync();
            await DomainAuditCoverage.LogAsync(
                audit, context, companyId, null, nameof(BankAccount), bank.Id,
                AuditEventCodes.BankAccountUpdated, oldValue, DomainAuditCoverage.BankSnapshot(bank),
                context.RequestAborted);
            return Results.Ok(bank);
        });

        banks.MapDelete("/{id:int}", async (int companyId, int id, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, AccountingWriteGuard writeGuard, AuditService audit) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            var bank = await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == id && b.CompanyId == companyId);
            if (bank == null) return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            var hasRetainedImportEvidence = await db.ImportBatches.AnyAsync(batch => batch.BankAccountId == id)
                || await db.ImportedTransactions.AnyAsync(transaction => transaction.BankAccountId == id);
            if (hasRetainedImportEvidence)
            {
                return Results.Conflict(new
                {
                    error = "A bank account with retained import evidence cannot be deleted. Keep the account so its source rows and review decisions remain auditable."
                });
            }

            var effectiveDate = bank.OpeningBalance == 0 ? DateOnly.MaxValue : bank.OpeningBalanceDate;
            if (await writeGuard.BlockIfCompanyAccountingLockedAsync(companyId, effectiveDate) is { } blocked)
                return blocked;

            var oldValue = DomainAuditCoverage.BankSnapshot(bank);
            db.BankAccounts.Remove(bank);
            await db.SaveChangesAsync();
            await DomainAuditCoverage.LogAsync(
                audit, context, companyId, null, nameof(BankAccount), id,
                AuditEventCodes.BankAccountDeleted, oldValue, null,
                context.RequestAborted);
            return Results.NoContent();
        });

        // CSV Import
        banks.MapPost("/{bankAccountId:int}/import", ImportCsvEndpointAsync).DisableAntiforgery();

        // Transactions
        var transactions = app.MapGroup("/api/companies/{companyId:int}/periods/{periodId:int}/transactions").WithTags("Transactions");

        transactions.MapGet("/", ListTransactionsEndpointAsync);

        transactions.MapPut("/{id:int}/categorise", CategoriseTransactionEndpointAsync);

        transactions.MapPost("/bulk-categorise", BulkCategoriseTransactionsEndpointAsync);

        transactions.MapGet("/duplicate-review", GetDuplicateReviewQueueEndpointAsync);

        transactions.MapPost("/{id:int}/duplicate-review", DecideDuplicateReviewEndpointAsync);

        transactions.MapPost("/duplicate-review/batches/{importBatchId:int}", DecideDuplicateReviewBatchEndpointAsync);

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

        categories.MapPost("/seed", async (int companyId, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, CategoryService service, AuditService audit) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            var existingIds = await db.AccountCategories
                .Where(category => category.CompanyId == companyId)
                .Select(category => category.Id)
                .ToArrayAsync(context.RequestAborted);
            var cats = await service.SeedDefaultCategoriesAsync(companyId);
            foreach (var category in cats.Where(category => category.CompanyId == companyId && !existingIds.Contains(category.Id)))
            {
                await DomainAuditCoverage.LogAsync(
                    audit, context, companyId, null, nameof(AccountCategory), category.Id,
                    AuditEventCodes.AccountCategoriesSeeded, null, DomainAuditCoverage.CategorySnapshot(category),
                    context.RequestAborted);
            }
            return Results.Ok(cats);
        });

        categories.MapPost("/", async (int companyId, AccountCategoryInput input, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, AuditService audit) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            if (await CategoryInputs.ValidateAsync(db, companyId, input) is { } validationProblem)
                return validationProblem;

            var category = input.ToEntity(companyId);
            db.AccountCategories.Add(category);
            await db.SaveChangesAsync();
            await DomainAuditCoverage.LogAsync(
                audit, context, companyId, null, nameof(AccountCategory), category.Id,
                AuditEventCodes.AccountCategoryCreated, null, DomainAuditCoverage.CategorySnapshot(category),
                context.RequestAborted);
            return Results.Created($"/api/companies/{companyId}/categories/{category.Id}", category);
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

        rules.MapPost("/", async (int companyId, TransactionRuleInput input, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, AuditService audit) =>
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
            await DomainAuditCoverage.LogAsync(
                audit, context, companyId, null, nameof(TransactionRule), rule.Id,
                AuditEventCodes.TransactionRuleCreated, null, DomainAuditCoverage.RuleSnapshot(rule),
                context.RequestAborted);
            return Results.Created($"/api/companies/{companyId}/transaction-rules/{rule.Id}", rule);
        });

        rules.MapDelete("/{id:int}", async (int companyId, int id, HttpContext context, ApiAccessService apiAccess, AccountsDbContext db, AuditService audit) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
                return Results.NotFound();

            var rule = await db.TransactionRules.FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == companyId);
            if (rule == null) return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            var oldValue = DomainAuditCoverage.RuleSnapshot(rule);
            db.TransactionRules.Remove(rule);
            await db.SaveChangesAsync();
            await DomainAuditCoverage.LogAsync(
                audit, context, companyId, null, nameof(TransactionRule), id,
                AuditEventCodes.TransactionRuleDeleted, oldValue, null,
                context.RequestAborted);
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
        if (txn.IsDuplicate)
            return Results.Conflict(new { error = "A discarded duplicate row cannot be categorised. Reopen its duplicate decision first." });

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
        await HydrateAvailableCategoriesAsync(db, companyId, [txn]);
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
        string? search,
        string? sortBy = null,
        string? sortDirection = null)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
            return Results.NotFound();

        if (await ValidateTransactionReadPeriodAsync(db, companyId, periodId) is { } blocked)
            return blocked;

        var periodQuery = db.ImportedTransactions
            .Where(t => t.PeriodId == periodId && t.BankAccount.CompanyId == companyId && !t.IsDuplicate);

        var periodTotal = await periodQuery.CountAsync();
        var periodUncategorised = await periodQuery.CountAsync(t => t.CategoryId == null);
        var periodCategorised = periodTotal - periodUncategorised;

        var query = periodQuery;

        if (uncategorised == true)
            query = query.Where(t => t.CategoryId == null);

        if (categoryId.HasValue)
            query = query.Where(t => t.CategoryId == categoryId.Value);

        if (bankAccountId.HasValue)
            query = query.Where(t => t.BankAccountId == bankAccountId.Value);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(t => t.Description.Contains(search));

        // data-list-transactions-pagesize-cap: clamp the page size (and page) so an unbounded
        // pageSize cannot pull every row into memory (a memory/DoS vector). Cap at 200 per page.
        const int maxPageSize = 200;
        var safePageSize = Math.Clamp(pageSize ?? 50, 1, maxPageSize);

        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)safePageSize));
        var safePage = Math.Min(Math.Max(page ?? 1, 1), totalPages);
        var normalisedSortBy = sortBy?.Trim().ToLowerInvariant() switch
        {
            "description" => "description",
            "amount" => "amount",
            "confidence" => "confidence",
            _ => "date"
        };
        var normalisedSortDirection = string.Equals(sortDirection, "asc", StringComparison.OrdinalIgnoreCase)
            ? "asc"
            : "desc";

        var orderedQuery = (normalisedSortBy, normalisedSortDirection) switch
        {
            ("date", "asc") => query.OrderBy(t => t.Date).ThenBy(t => t.Id),
            ("description", "asc") => query.OrderBy(t => t.Description).ThenBy(t => t.Id),
            ("description", "desc") => query.OrderByDescending(t => t.Description).ThenByDescending(t => t.Id),
            ("amount", "asc") => query.OrderBy(t => t.Amount).ThenBy(t => t.Id),
            ("amount", "desc") => query.OrderByDescending(t => t.Amount).ThenByDescending(t => t.Id),
            ("confidence", "asc") => query.OrderBy(t => t.ConfidenceScore).ThenBy(t => t.Id),
            ("confidence", "desc") => query.OrderByDescending(t => t.ConfidenceScore).ThenByDescending(t => t.Id),
            _ => query.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
        };

        var items = await orderedQuery
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync();

        await HydrateAvailableCategoriesAsync(db, companyId, items);
        return Results.Ok(new
        {
            total,
            items,
            page = safePage,
            pageSize = safePageSize,
            totalPages,
            hasPreviousPage = safePage > 1,
            hasNextPage = safePage < totalPages,
            sortBy = normalisedSortBy,
            sortDirection = normalisedSortDirection,
            aggregates = new
            {
                total = periodTotal,
                categorised = periodCategorised,
                uncategorised = periodUncategorised
            }
        });
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
        if (txns.Any(transaction => transaction.IsDuplicate))
            return Results.Conflict(new { error = "Discarded duplicate rows cannot be bulk-categorised. Reopen their duplicate decisions first." });

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
        ApiAccessService apiAccess,
        [FromServices] AccountingConcurrencyCoordinator? concurrency = null,
        [FromServices] IdempotencyService? idempotency = null)
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

        try
        {
            byte[] sourceBytes;
            await using (var source = file.OpenReadStream())
            {
                using var buffer = new MemoryStream();
                await source.CopyToAsync(buffer, request.HttpContext.RequestAborted);
                sourceBytes = buffer.ToArray();
            }

            var normalizedFilename = Path.GetFileName(file.FileName).Trim();
            var sourceFileSha256 = DuplicateCandidateDetector.ComputeSourceFileSha256(sourceBytes);
            concurrency ??= new AccountingConcurrencyCoordinator(db);
            idempotency ??= new IdempotencyService(db);
            var command = await IdempotencyHttpContract.ExecuteAsync(
                request.HttpContext,
                idempotency,
                user,
                IdempotencyOperations.BankImport,
                new
                {
                    companyId,
                    bankAccountId,
                    periodId,
                    SourceFilename = normalizedFilename,
                    SourceFileSha256 = sourceFileSha256,
                    SourceFileBytes = sourceBytes.LongLength
                },
                async cancellationToken =>
                {
                    await using var concurrencyLease = await concurrency.AcquirePeriodAsync(
                        companyId,
                        periodId,
                        cancellationToken);
                    var lockedPeriod = await db.AccountingPeriods
                        .AsNoTracking()
                        .Where(item => item.Id == periodId && item.CompanyId == companyId)
                        .Select(item => new { item.Status, item.LockedAt })
                        .SingleAsync(cancellationToken);
                    if (lockedPeriod.Status is PeriodStatus.Finalised or PeriodStatus.Filed || lockedPeriod.LockedAt is not null)
                        throw new PeriodLockedForImportException();

                    using var stream = new MemoryStream(sourceBytes, writable: false);
                    var result = await importService.ImportCsvAsync(
                        companyId,
                        bankAccountId,
                        periodId,
                        stream,
                        normalizedFilename,
                        cancellationToken: cancellationToken);
                    var auditEntityType = result.ImportBatchId is null ? "BankAccount" : "ImportBatch";
                    var auditEntityId = result.ImportBatchId ?? bankAccountId;
                    var auditAction = result.ImportBatchId is null
                        ? AuditEventCodes.BankCsvImportAttemptRejected
                        : AuditEventCodes.BankCsvImported;
                    await auditService.LogAsync(companyId, periodId, auditEntityType, auditEntityId, auditAction, null, new
                    {
                        result.ImportBatchId,
                        result.SourceFilename,
                        result.SourceFileSha256,
                        result.SourceFileBytes,
                        result.TotalRows,
                        result.ImportedRows,
                        result.DuplicateCandidates,
                        result.AutoCategorised,
                        WarningCount = result.Warnings.Count,
                        result.Warnings
                    },
                        AuthenticatedIdentity.AuditUserId(user),
                        requestId: request.HttpContext.TraceIdentifier,
                        actorDisplayName: AuthenticatedIdentity.ReviewerDisplayName(user),
                        cancellationToken: cancellationToken);
                    await concurrencyLease.CommitIfOwnedAsync(cancellationToken);
                    return new IdempotencyOperationOutcome<ImportService.ImportResult>(
                        result,
                        auditEntityType,
                        auditEntityId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                });
            if (command.Error is not null)
                return command.Error;
            return IdempotencyHttpContract.JsonResult(command.Execution!);
        }
        catch (PeriodLockedForImportException)
        {
            return Results.Conflict(new { error = "Accounting period is locked. Reopen the period before importing transactions." });
        }
        catch (BusinessRuleException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private sealed class PeriodLockedForImportException()
        : BusinessRuleException("Accounting period is locked. Reopen the period before importing transactions.");

    private static async Task<IResult> GetDuplicateReviewQueueEndpointAsync(
        int companyId,
        int periodId,
        int? page,
        int? pageSize,
        int? batchPage,
        int? batchPageSize,
        HttpContext context,
        AccountsDbContext db,
        [FromServices] DuplicateReviewService? service = null)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
            return Results.NotFound();

        try
        {
            service ??= new DuplicateReviewService(
                db,
                new AuditService(db),
                new AccountingConcurrencyCoordinator(db));
            return Results.Ok(await service.GetQueueAsync(
                companyId,
                periodId,
                page ?? 1,
                pageSize ?? 50,
                batchPage ?? 1,
                batchPageSize ?? 10,
                context.RequestAborted));
        }
        catch (ResourceNotFoundException)
        {
            return Results.NotFound();
        }
        catch (BusinessRuleException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> DecideDuplicateReviewEndpointAsync(
        int companyId,
        int periodId,
        int id,
        DuplicateReviewDecisionInput input,
        HttpContext context,
        ApiAccessService apiAccess,
        AccountsDbContext db,
        [FromServices] DuplicateReviewService? service = null)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
            return Results.NotFound();
        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        try
        {
            service ??= new DuplicateReviewService(
                db,
                new AuditService(db),
                new AccountingConcurrencyCoordinator(db));
            var result = await service.DecideAsync(
                companyId,
                periodId,
                id,
                input.Decision,
                input.Reason ?? string.Empty,
                input.ExpectedStatus,
                input.ExpectedDecisionVersion,
                AuthContext.RequireUser(context),
                context.TraceIdentifier,
                context.RequestAborted);
            return Results.Ok(result);
        }
        catch (ResourceNotFoundException)
        {
            return Results.NotFound();
        }
        catch (BusinessRuleException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (AccountingConcurrencyException exception)
        {
            return Results.Conflict(new
            {
                error = exception.Message,
                code = exception.Code,
                reloadRequired = true,
                reconcileRequired = true
            });
        }
    }

    private static async Task<IResult> DecideDuplicateReviewBatchEndpointAsync(
        int companyId,
        int periodId,
        int importBatchId,
        DuplicateReviewBatchDecisionInput input,
        HttpContext context,
        ApiAccessService apiAccess,
        AccountsDbContext db,
        [FromServices] DuplicateReviewService? service = null)
    {
        if (!await CompanyEndpointAccess.CanAccessCompanyAsync(context, db, companyId))
            return Results.NotFound();
        if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
            return denied;

        try
        {
            service ??= new DuplicateReviewService(
                db,
                new AuditService(db),
                new AccountingConcurrencyCoordinator(db));
            var result = await service.DecideExactReimportBatchAsync(
                companyId,
                periodId,
                importBatchId,
                input.Decision,
                input.Reason ?? string.Empty,
                input.ExpectedStatus,
                input.ExpectedCandidateCount,
                input.ExpectedDecisionToken ?? string.Empty,
                AuthContext.RequireUser(context),
                context.TraceIdentifier,
                context.RequestAborted);
            return Results.Ok(result);
        }
        catch (ResourceNotFoundException)
        {
            return Results.NotFound();
        }
        catch (BusinessRuleException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (AccountingConcurrencyException exception)
        {
            return Results.Conflict(new
            {
                error = exception.Message,
                code = exception.Code,
                reloadRequired = true,
                reconcileRequired = true
            });
        }
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
    public static IResult? ValidateBankAccount(BankAccountInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return Results.BadRequest(new { error = "Bank account name is required." });
        if (input.Name.Trim().Length > 200)
            return Results.BadRequest(new { error = "Bank account name must be 200 characters or fewer." });
        if (input.Iban?.Trim().Length > 34)
            return Results.BadRequest(new { error = "IBAN must be 34 characters or fewer." });
        var currency = NormalizeCurrency(input.Currency);
        if (currency.Length != 3 || currency.Any(character => character is < 'A' or > 'Z'))
            return Results.BadRequest(new { error = "Currency must be a three-letter ISO currency code." });
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

    public static string NormalizeCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency) ? "EUR" : currency.Trim().ToUpperInvariant();
}

public static class CategoryInputs
{
    public static async Task<IResult?> ValidateAsync(AccountsDbContext db, int companyId, AccountCategoryInput input)
    {
        var errors = new Dictionary<string, string[]>();
        var code = input.Code?.Trim() ?? "";
        var name = input.Name?.Trim() ?? "";
        if (code.Length == 0) errors["code"] = ["Code is required."];
        else if (code.Length > 20) errors["code"] = ["Code must be 20 characters or fewer."];
        if (name.Length == 0) errors["name"] = ["Name is required."];
        else if (name.Length > 200) errors["name"] = ["Name must be 200 characters or fewer."];

        if (input.ParentId is { } parentId)
        {
            var parentAvailable = await db.AccountCategories
                .AnyAsync(c => c.Id == parentId && (c.CompanyId == companyId || (c.IsSystem && c.CompanyId == null)));
            if (!parentAvailable)
                errors["parentId"] = ["Parent category must belong to the same company."];
        }

        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        return null;
    }

    // Kept for direct domain-test compatibility. HTTP endpoints bind AccountCategoryInput and never
    // expose this entity-normalisation overload as a request contract.
    public static async Task<IResult?> ValidateAndNormalizeAsync(AccountsDbContext db, int companyId, AccountCategory input)
    {
        AccountCategoryInput request = input;
        if (await ValidateAsync(db, companyId, request) is { } invalid)
            return invalid;

        input.Id = 0;
        input.IsSystem = false;
        input.CompanyId = companyId;
        input.Code = request.Code!.Trim();
        input.Name = request.Name!.Trim();
        input.Children = [];
        input.Transactions = [];
        input.Rules = [];
        return null;
    }
}

public sealed record DuplicateReviewDecisionInput(
    DuplicateReviewStatus Decision,
    string? Reason,
    DuplicateReviewStatus ExpectedStatus,
    int ExpectedDecisionVersion);

public sealed record DuplicateReviewBatchDecisionInput(
    DuplicateReviewStatus Decision,
    string? Reason,
    DuplicateReviewStatus ExpectedStatus,
    int ExpectedCandidateCount,
    string? ExpectedDecisionToken);

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
