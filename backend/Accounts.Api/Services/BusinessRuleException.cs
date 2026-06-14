namespace Accounts.Api.Services;

public class BusinessRuleException(string message) : InvalidOperationException(message);
