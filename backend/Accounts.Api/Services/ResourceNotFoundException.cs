namespace Accounts.Api.Services;

public class ResourceNotFoundException(string message) : InvalidOperationException(message);
