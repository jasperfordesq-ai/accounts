using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Accounts.Api.Rules;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Services;

public enum PasswordSafetyStatus
{
    Accepted,
    Breached,
    Unavailable
}

public sealed record PasswordSafetyDecision(PasswordSafetyStatus Status, long BreachCount = 0);

public interface IPasswordSafetyService
{
    Task<PasswordSafetyDecision> CheckAsync(string password, CancellationToken cancellationToken = default);
}

/// <summary>
/// Uses the HIBP Pwned Passwords range protocol: only five SHA-1 prefix characters leave the
/// process, padded responses are requested, unrelated suffixes are discarded immediately, and no
/// password/hash/result is logged or persisted.
/// </summary>
public sealed class PwnedPasswordSafetyService(
    HttpClient httpClient,
    IOptions<IdentitySecurityConfig> configuredOptions) : IPasswordSafetyService
{
    private static readonly HashSet<string> LocalDenyList = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password123", "password123!", "qwerty", "qwerty123!",
        "letmein", "letmein123!", "admin", "administrator", "welcome123!",
        "changeme", "changeme123!", "irishaccounts", "companypassword123!"
    };
    private readonly IdentitySecurityConfig options = configuredOptions.Value;

    public async Task<PasswordSafetyDecision> CheckAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (LocalDenyList.Contains(password)) return new(PasswordSafetyStatus.Breached, 1);
        if (!options.BreachedPasswordCheckEnabled) return new(PasswordSafetyStatus.Accepted);
        if (!Uri.TryCreate(options.PwnedPasswordsRangeBaseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme != Uri.UriSchemeHttps)
            return RemoteCheckUnavailable();

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var digest = SHA1.HashData(passwordBytes);
        CryptographicOperations.ZeroMemory(passwordBytes);
        var hash = Convert.ToHexString(digest);
        CryptographicOperations.ZeroMemory(digest);
        var prefix = hash[..5];
        var suffix = hash[5..];
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/" + prefix, UriKind.Absolute));
        request.Headers.Add("Add-Padding", "true");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Irish-Accounts-Platform", "1.0"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.PwnedPasswordsTimeoutSeconds, 1, 15)));
        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            if (!response.IsSuccessStatusCode
                || response.Content.Headers.ContentLength > options.PwnedPasswordsMaximumResponseBytes)
                return RemoteCheckUnavailable();

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: false);
            var charactersRead = 0;
            while (await reader.ReadLineAsync(timeout.Token) is { } line)
            {
                charactersRead += line.Length + 2;
                if (charactersRead > options.PwnedPasswordsMaximumResponseBytes)
                    return RemoteCheckUnavailable();
                var separator = line.IndexOf(':');
                if (separator != 35) continue;
                var candidate = line[..separator];
                if (!candidate.Equals(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                if (!long.TryParse(
                        line[(separator + 1)..],
                        System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var count)
                    || count <= 0)
                    return new(PasswordSafetyStatus.Accepted);
                return new(PasswordSafetyStatus.Breached, count);
            }
            return new(PasswordSafetyStatus.Accepted);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RemoteCheckUnavailable();
        }
        catch (HttpRequestException)
        {
            return RemoteCheckUnavailable();
        }
        catch (IOException)
        {
            return RemoteCheckUnavailable();
        }
    }

    private PasswordSafetyDecision RemoteCheckUnavailable() =>
        options.BreachedPasswordFailClosed
            ? new(PasswordSafetyStatus.Unavailable)
            : new(PasswordSafetyStatus.Accepted);
}
