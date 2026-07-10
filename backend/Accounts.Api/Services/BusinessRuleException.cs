namespace Accounts.Api.Services;

public class BusinessRuleException(string message) : InvalidOperationException(message);

/// <summary>
/// A final filing transition or artifact release was denied because its exact release evidence is
/// incomplete or stale. This is a conflict with current workflow state, not an invalid JSON request.
/// </summary>
public sealed class FilingReleaseBlockedException(string message) : BusinessRuleException(message);
