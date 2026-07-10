namespace Accounts.Api.Services;

/// <summary>
/// A tracked write would create an invalid tenant/company/period ownership relationship.
/// The message deliberately omits foreign identifiers so a rejected request cannot disclose
/// another tenant's data.
/// </summary>
public sealed class PersistenceOwnershipException(string entityName)
    : BusinessRuleException($"{entityName} ownership relationship is invalid.");
