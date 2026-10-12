using Accounts.Api.Entities;

namespace Accounts.Api.Services;

// Accounts that may not be chosen as the target of a categorisation, a transaction rule or an opening
// balance, and that an import never applies a rule to: an archived account (kept for history and its
// balance, but no longer in use) and the bank control account (every bank transaction already posts to
// it, and bank opening balances are entered on the bank account). The server refuses them whatever the
// picker offered.
public static class AccountCategoryTargets
{
    public static string? RefusalReason(AccountCategory category, bool openingBalance = false)
    {
        if (category.IsArchived)
            return $"Account {category.Code} {category.Name} is archived and cannot be used. Choose an active account.";

        if (CategoryClassification.Role(category) == AccountSystemRole.BankControl)
            return openingBalance
                ? $"Account {category.Code} {category.Name} is the bank control account. Enter the bank's opening balance on the bank account instead."
                : $"Account {category.Code} {category.Name} is the bank control account; bank transactions already post to it. Choose the account for the other side of the entry.";

        return null;
    }
}
