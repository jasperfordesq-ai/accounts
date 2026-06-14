using System.Net;

namespace Accounts.Api.Services;

public static class RateLimitClientKey
{
    public static string FromHttpContext(HttpContext context, bool trustForwardedFor = false)
    {
        if (trustForwardedFor)
        {
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwardedFor))
            {
                var firstForwardedIp = forwardedFor.Split(',')[0].Trim();
                if (IPAddress.TryParse(firstForwardedIp, out var parsedForwardedIp))
                    return parsedForwardedIp.ToString();
            }
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
