using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public static class AdjustmentPostingRules
{
    public readonly record struct DerivedImpact(decimal Profit, decimal Assets);

    public static void EnsureValidPosting(Adjustment adjustment)
    {
        if (adjustment.Amount <= 0)
            throw new BusinessRuleException("Journal amount must be greater than zero.");
        if (adjustment.DebitCategoryId is null || adjustment.CreditCategoryId is null)
            throw new BusinessRuleException("Every journal requires both a debit account and a credit account.");
        if (adjustment.DebitCategoryId == adjustment.CreditCategoryId)
            throw new BusinessRuleException("A journal debit and credit must use different accounts.");
    }

    public static DerivedImpact DeriveImpact(
        AccountCategory debit,
        AccountCategory credit,
        decimal amount)
    {
        if (amount <= 0)
            throw new BusinessRuleException("Journal amount must be greater than zero.");
        if (debit.Id == credit.Id)
            throw new BusinessRuleException("A journal debit and credit must use different accounts.");

        var profit = ProfitDebitEffect(debit, amount) + ProfitCreditEffect(credit, amount);
        var assets = AssetDebitEffect(debit, amount) + AssetCreditEffect(credit, amount);
        return new DerivedImpact(profit, assets);
    }

    public static async Task ApplyDerivedImpactAsync(
        AccountsDbContext db,
        Adjustment adjustment,
        CancellationToken cancellationToken = default)
    {
        EnsureValidPosting(adjustment);
        var ids = new[] { adjustment.DebitCategoryId!.Value, adjustment.CreditCategoryId!.Value };
        var categories = await db.AccountCategories
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);
        if (!categories.TryGetValue(ids[0], out var debit) || !categories.TryGetValue(ids[1], out var credit))
            throw new BusinessRuleException("Journal accounts are not available.");

        var impact = DeriveImpact(debit, credit, adjustment.Amount);
        adjustment.ImpactOnProfit = impact.Profit;
        adjustment.ImpactOnAssets = impact.Assets;
    }

    public static async Task ApplyDerivedImpactsAsync(
        AccountsDbContext db,
        IEnumerable<Adjustment> adjustments,
        CancellationToken cancellationToken = default)
    {
        foreach (var adjustment in adjustments)
            await ApplyDerivedImpactAsync(db, adjustment, cancellationToken);
    }

    private static decimal ProfitDebitEffect(AccountCategory category, decimal amount) =>
        category.Type is AccountCategoryType.Income or AccountCategoryType.Expense ? -amount : 0m;

    private static decimal ProfitCreditEffect(AccountCategory category, decimal amount) =>
        category.Type is AccountCategoryType.Income or AccountCategoryType.Expense ? amount : 0m;

    private static decimal AssetDebitEffect(AccountCategory category, decimal amount) =>
        category.Type == AccountCategoryType.Asset ? amount : 0m;

    private static decimal AssetCreditEffect(AccountCategory category, decimal amount) =>
        category.Type == AccountCategoryType.Asset ? -amount : 0m;
}
