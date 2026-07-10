using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

/// <summary>
/// Builds the accounting ledger used by every primary statement. Imported bank transactions and
/// persisted adjustments are always expanded into equal debit/credit postings. Balance-sheet
/// accounts are carried from the exact adjacent period and prior-period income, expense and
/// contra-equity balances are closed into retained earnings.
/// </summary>
internal sealed class AccountingLedgerService(AccountsDbContext db)
{
    internal sealed class LedgerLine(AccountCategory category)
    {
        public AccountCategory Category { get; } = category;
        public decimal OpeningDebit { get; private set; }
        public decimal OpeningCredit { get; private set; }
        public decimal TransactionDebit { get; private set; }
        public decimal TransactionCredit { get; private set; }
        public int TransactionCount { get; private set; }
        public decimal AdjustmentDebit { get; private set; }
        public decimal AdjustmentCredit { get; private set; }
        public int AdjustmentCount { get; private set; }
        public List<string> SourceNotes { get; } = [];

        public decimal Debit => OpeningDebit + TransactionDebit + AdjustmentDebit;
        public decimal Credit => OpeningCredit + TransactionCredit + AdjustmentCredit;
        public decimal NetDebit => Debit - Credit;
        public decimal OpeningNetDebit => OpeningDebit - OpeningCredit;
        public decimal CurrentNetDebit => TransactionDebit + AdjustmentDebit - TransactionCredit - AdjustmentCredit;

        public void AddOpeningDebit(decimal amount, string source)
        {
            OpeningDebit += Positive(amount);
            SourceNotes.Add(source);
        }

        public void AddOpeningCredit(decimal amount, string source)
        {
            OpeningCredit += Positive(amount);
            SourceNotes.Add(source);
        }

        public void AddTransactionDebit(decimal amount, string source)
        {
            TransactionDebit += Positive(amount);
            TransactionCount++;
            SourceNotes.Add(source);
        }

        public void AddTransactionCredit(decimal amount, string source)
        {
            TransactionCredit += Positive(amount);
            TransactionCount++;
            SourceNotes.Add(source);
        }

        public void AddAdjustmentDebit(decimal amount, string source)
        {
            AdjustmentDebit += Positive(amount);
            AdjustmentCount++;
            SourceNotes.Add(source);
        }

        public void AddAdjustmentCredit(decimal amount, string source)
        {
            AdjustmentCredit += Positive(amount);
            AdjustmentCount++;
            SourceNotes.Add(source);
        }

        private static decimal Positive(decimal amount) => Math.Abs(amount);
    }

    internal sealed record CashMovement(
        decimal Amount,
        AccountCategory ContraCategory,
        string SourceReference);

    internal sealed record LedgerSnapshot(
        AccountingPeriod Period,
        IReadOnlyList<AccountCategory> Categories,
        IReadOnlyDictionary<int, LedgerLine> Lines,
        IReadOnlyList<CashMovement> CashMovements)
    {
        public decimal TotalDebits => Lines.Values.Sum(line => line.Debit);
        public decimal TotalCredits => Lines.Values.Sum(line => line.Credit);
        public decimal Difference => TotalDebits - TotalCredits;

        public LedgerLine? ByCode(string code) => Lines.Values.FirstOrDefault(line => line.Category.Code == code);

        public decimal NetDebit(Func<AccountCategory, bool> predicate) =>
            Lines.Values.Where(line => predicate(line.Category)).Sum(line => line.NetDebit);

        public decimal OpeningNetDebit(Func<AccountCategory, bool> predicate) =>
            Lines.Values.Where(line => predicate(line.Category)).Sum(line => line.OpeningNetDebit);

        public decimal CurrentNetDebit(Func<AccountCategory, bool> predicate) =>
            Lines.Values.Where(line => predicate(line.Category)).Sum(line => line.CurrentNetDebit);
    }

    public async Task<LedgerSnapshot> BuildAsync(int companyId, int periodId) =>
        await BuildAsync(companyId, periodId, []);

    private async Task<LedgerSnapshot> BuildAsync(int companyId, int periodId, HashSet<int> path)
    {
        if (!path.Add(periodId))
            throw new BusinessRuleException("Accounting-period chronology contains a cycle; the ledger cannot be built.");

        try
        {
            var period = await db.AccountingPeriods
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
                ?? throw new ResourceNotFoundException($"Period {periodId} not found");

            var categories = await db.AccountCategories
                .AsNoTracking()
                .Where(c => c.CompanyId == companyId || (c.IsSystem && c.CompanyId == null))
                .OrderByDescending(c => c.CompanyId == companyId)
                .ThenBy(c => c.Code)
                .ThenBy(c => c.Id)
                .ToListAsync();
            var lines = categories.ToDictionary(c => c.Id, c => new LedgerLine(c));
            var categoryById = categories.ToDictionary(c => c.Id);
            var bankCategory = PreferredCategory(categories, companyId, "1400");
            var retainedCategory = PreferredCategory(categories, companyId, "3100");

            var explicitOpenings = await db.OpeningBalances
                .AsNoTracking()
                .Where(o => o.PeriodId == periodId)
                .OrderBy(o => o.Id)
                .ToListAsync();
            var priorPeriod = await PeriodChronologyService
                .PriorPeriodQuery(db, companyId, period.PeriodStart)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            var carriedPriorLedger = priorPeriod is not null && explicitOpenings.Count == 0;
            if (carriedPriorLedger)
            {
                var prior = await BuildAsync(companyId, priorPeriod!.Id, path);
                CarryForward(prior, lines, categories, companyId, retainedCategory);
            }
            else
            {
                foreach (var opening in explicitOpenings)
                {
                    if (!lines.TryGetValue(opening.AccountCategoryId, out var line))
                        throw new BusinessRuleException($"Opening balance #{opening.Id} refers to an unavailable account category.");

                    var source = $"Opening balance #{opening.Id}: {opening.SourceNote ?? "reviewer-entered take-on"}";
                    if (opening.Debit != 0) line.AddOpeningDebit(opening.Debit, source);
                    if (opening.Credit != 0) line.AddOpeningCredit(opening.Credit, source);
                }

                if (bankCategory is not null)
                {
                    var bankOpenings = await db.BankAccounts
                        .AsNoTracking()
                        .Where(b => b.CompanyId == companyId
                            && b.OpeningBalance != 0
                            && b.OpeningBalanceDate != null
                            && b.OpeningBalanceDate <= period.PeriodStart)
                        .OrderBy(b => b.Id)
                        .ToListAsync();
                    foreach (var bank in bankOpenings)
                    {
                        var source = $"Bank opening balance #{bank.Id}: {bank.Name}";
                        if (bank.OpeningBalance > 0) lines[bankCategory.Id].AddOpeningDebit(bank.OpeningBalance, source);
                        else lines[bankCategory.Id].AddOpeningCredit(bank.OpeningBalance, source);
                    }
                }
            }

            var cashMovements = new List<CashMovement>();
            var transactions = await db.ImportedTransactions
                .AsNoTracking()
                .Where(t => t.PeriodId == periodId && t.CategoryId != null && !t.IsDuplicate)
                .OrderBy(t => t.Date)
                .ThenBy(t => t.Id)
                .ToListAsync();
            foreach (var transaction in transactions)
            {
                if (bankCategory is null)
                    throw new BusinessRuleException("The bank control account (1400) is required before transactions can be posted.");
                if (!categoryById.TryGetValue(transaction.CategoryId!.Value, out var contra)
                    || !lines.TryGetValue(contra.Id, out var contraLine))
                    throw new BusinessRuleException($"Imported transaction #{transaction.Id} refers to an unavailable account category.");
                if (contra.Id == bankCategory.Id)
                    throw new BusinessRuleException($"Imported transaction #{transaction.Id} cannot use the bank control account as its own contra account.");

                var source = $"Imported transaction #{transaction.Id}: {transaction.Description}";
                var amount = Math.Abs(transaction.Amount);
                if (amount == 0)
                    continue;

                if (transaction.Amount > 0)
                {
                    lines[bankCategory.Id].AddTransactionDebit(amount, source);
                    contraLine.AddTransactionCredit(amount, source);
                }
                else
                {
                    lines[bankCategory.Id].AddTransactionCredit(amount, source);
                    contraLine.AddTransactionDebit(amount, source);
                }
                cashMovements.Add(new CashMovement(transaction.Amount, contra, source));
            }

            var adjustments = await db.Adjustments
                .AsNoTracking()
                .Where(a => a.PeriodId == periodId)
                .OrderBy(a => a.CreatedAt)
                .ThenBy(a => a.Id)
                .ToListAsync();
            foreach (var adjustment in adjustments)
            {
                AdjustmentPostingRules.EnsureValidPosting(adjustment);
                if (!categoryById.TryGetValue(adjustment.DebitCategoryId!.Value, out var debitCategory)
                    || !categoryById.TryGetValue(adjustment.CreditCategoryId!.Value, out var creditCategory))
                    throw new BusinessRuleException($"Journal #{adjustment.Id} refers to an unavailable account category.");

                var source = $"Journal #{adjustment.Id}: {adjustment.Description}";
                lines[debitCategory.Id].AddAdjustmentDebit(adjustment.Amount, source);
                lines[creditCategory.Id].AddAdjustmentCredit(adjustment.Amount, source);

                var debitIsCash = IsCash(debitCategory);
                var creditIsCash = IsCash(creditCategory);
                if (debitIsCash ^ creditIsCash)
                {
                    cashMovements.Add(debitIsCash
                        ? new CashMovement(adjustment.Amount, creditCategory, source)
                        : new CashMovement(-adjustment.Amount, debitCategory, source));
                }
            }

            return new LedgerSnapshot(period, categories, lines, cashMovements);
        }
        finally
        {
            path.Remove(periodId);
        }
    }

    private static void CarryForward(
        LedgerSnapshot prior,
        Dictionary<int, LedgerLine> currentLines,
        IReadOnlyList<AccountCategory> currentCategories,
        int companyId,
        AccountCategory? retainedCategory)
    {
        decimal retainedNetDebit = 0m;
        var priorReference = $"Carried from exact prior period #{prior.Period.Id} ending {prior.Period.PeriodEnd:yyyy-MM-dd}";

        foreach (var priorLine in prior.Lines.Values)
        {
            if (priorLine.NetDebit == 0)
                continue;

            var isPersistentBalance = priorLine.Category.Type is AccountCategoryType.Asset or AccountCategoryType.Liability
                || (priorLine.Category.Type == AccountCategoryType.Equity && priorLine.Category.Code == "3000");
            if (!isPersistentBalance)
            {
                retainedNetDebit += priorLine.NetDebit;
                continue;
            }

            var category = currentLines.ContainsKey(priorLine.Category.Id)
                ? priorLine.Category
                : PreferredCategory(currentCategories, companyId, priorLine.Category.Code);
            if (category is null || !currentLines.TryGetValue(category.Id, out var line))
                throw new BusinessRuleException($"Prior-period balance for account {priorLine.Category.Code} cannot be carried forward.");

            if (priorLine.NetDebit > 0) line.AddOpeningDebit(priorLine.NetDebit, priorReference);
            else line.AddOpeningCredit(priorLine.NetDebit, priorReference);
        }

        if (retainedNetDebit == 0)
            return;
        if (retainedCategory is null || !currentLines.TryGetValue(retainedCategory.Id, out var retainedLine))
            throw new BusinessRuleException("The retained earnings account (3100) is required to close the prior-period ledger.");

        if (retainedNetDebit > 0) retainedLine.AddOpeningDebit(retainedNetDebit, priorReference + " (closed to retained earnings)");
        else retainedLine.AddOpeningCredit(retainedNetDebit, priorReference + " (closed to retained earnings)");
    }

    private static AccountCategory? PreferredCategory(
        IEnumerable<AccountCategory> categories,
        int companyId,
        string code) =>
        categories
            .Where(c => c.Code == code)
            .OrderByDescending(c => c.CompanyId == companyId)
            .ThenBy(c => c.Id)
            .FirstOrDefault();

    internal static bool IsCash(AccountCategory category) =>
        category.Type == AccountCategoryType.Asset && category.Code.StartsWith("14", StringComparison.Ordinal);
}
