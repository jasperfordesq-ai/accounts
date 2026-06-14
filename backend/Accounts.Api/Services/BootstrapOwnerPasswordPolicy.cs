namespace Accounts.Api.Services;

public static class BootstrapOwnerPasswordPolicy
{
    public const string RequirementMessage =
        "BootstrapOwner:OwnerInitialPassword must be at least 20 characters and include upper case, lower case, number, and symbol characters";

    public static string? Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password)
            || password.Length < 20
            || !password.Any(char.IsUpper)
            || !password.Any(char.IsLower)
            || !password.Any(char.IsDigit)
            || !password.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            return RequirementMessage;
        }

        return null;
    }
}
