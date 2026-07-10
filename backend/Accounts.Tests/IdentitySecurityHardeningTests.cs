using System.Net;
using System.Security.Cryptography;
using System.Text;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounts.Tests;

public sealed class IdentitySecurityHardeningTests
{
    [Fact]
    public async Task PwnedPasswordRange_SendsOnlyFiveCharacterPrefixWithPaddingAndMatchesLocally()
    {
        const string password = "Synthetic-Breached-Passphrase-2026!";
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"00000000000000000000000000000000000:0\r\n{hash[5..]}:42\r\n")
        });
        var service = Service(handler);

        var result = await service.CheckAsync(password);

        Assert.Equal(PasswordSafetyStatus.Breached, result.Status);
        Assert.Equal(42, result.BreachCount);
        Assert.Equal(hash[..5], handler.RequestUri!.Segments[^1]);
        Assert.Equal("true", handler.PaddingHeader);
        Assert.DoesNotContain(password, handler.RequestUri.AbsoluteUri, StringComparison.Ordinal);
        Assert.DoesNotContain(hash, handler.RequestUri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PwnedPasswordRange_FailsClosedOnUnavailableOrOversizedProvider()
    {
        var unavailable = Service(new CapturingHandler(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        Assert.Equal(PasswordSafetyStatus.Unavailable, (await unavailable.CheckAsync("Strong-Unavailable-Passphrase-2026!")).Status);

        var oversized = Service(new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[2_000_001])
        }));
        Assert.Equal(PasswordSafetyStatus.Unavailable, (await oversized.CheckAsync("Strong-Oversized-Passphrase-2026!")).Status);
    }

    [Fact]
    public void MfaKeyRing_DecryptsLegacyAndPriorKeysThenRewrapsWithActiveKey()
    {
        var auth = Options.Create(AuthConfig());
        var legacy = new MfaSecurityService(auth);
        var secret = legacy.GenerateSecret();
        var legacyEnvelope = legacy.EncryptSecret(secret);

        var keyOneConfig = IdentityConfig("key-one", includeSecond: true);
        var keyOne = new MfaSecurityService(auth, Options.Create(keyOneConfig));
        var legacyResult = keyOne.DecryptSecretWithRotation(legacyEnvelope);
        Assert.Equal(secret, legacyResult.Secret);
        Assert.True(legacyResult.NeedsRewrap);
        var keyOneEnvelope = keyOne.EncryptSecret(secret);
        Assert.StartsWith("v2.key-one.", keyOneEnvelope, StringComparison.Ordinal);

        var keyTwoConfig = IdentityConfig("key-two", includeSecond: true);
        var keyTwo = new MfaSecurityService(auth, Options.Create(keyTwoConfig));
        var oldKeyResult = keyTwo.DecryptSecretWithRotation(keyOneEnvelope);
        Assert.Equal(secret, oldKeyResult.Secret);
        Assert.True(oldKeyResult.NeedsRewrap);
        var activeEnvelope = keyTwo.EncryptSecret(oldKeyResult.Secret);
        Assert.StartsWith("v2.key-two.", activeEnvelope, StringComparison.Ordinal);
        Assert.False(keyTwo.DecryptSecretWithRotation(activeEnvelope).NeedsRewrap);
    }

    [Fact]
    public void TotpCounter_RejectsReplayButAcceptsTheNextTimeStep()
    {
        var mfa = new MfaSecurityService(Options.Create(AuthConfig()));
        var secret = mfa.GenerateSecret();
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var code = mfa.ComputeTotpForTesting(secret, now);

        Assert.True(mfa.TryVerifyTotp(secret, code, now, -1, out var counter));
        Assert.False(mfa.TryVerifyTotp(secret, code, now, counter, out _));
        var next = now.AddSeconds(MfaSecurityService.TotpPeriodSeconds);
        Assert.True(mfa.TryVerifyTotp(secret, mfa.ComputeTotpForTesting(secret, next), next, counter, out var nextCounter));
        Assert.True(nextCounter > counter);
    }

    [Fact]
    public void ProductionIdentityConfiguration_RequiresBreachChecksAndIndependentKeyRing()
    {
        Assert.NotEmpty(ProductionSafetyService.ValidateIdentitySecurity(new IdentitySecurityConfig()));
        Assert.Empty(ProductionSafetyService.ValidateIdentitySecurity(IdentityConfig("key-two", includeSecond: true)));
    }

    private static PwnedPasswordSafetyService Service(HttpMessageHandler handler) => new(
        new HttpClient(handler),
        Options.Create(new IdentitySecurityConfig
        {
            PwnedPasswordsRangeBaseUrl = "https://api.pwnedpasswords.com/range/",
            PwnedPasswordsMaximumResponseBytes = 2_000_000
        }));

    private static AuthSessionConfig AuthConfig() => new()
    {
        SigningKey = Key(3),
        RequirePrivilegedMfa = true
    };

    private static IdentitySecurityConfig IdentityConfig(string activeKey, bool includeSecond) => new()
    {
        RequireInProduction = true,
        BreachedPasswordCheckEnabled = true,
        BreachedPasswordFailClosed = true,
        PwnedPasswordsRangeBaseUrl = "https://api.pwnedpasswords.com/range/",
        IdentityHmacKey = Key(5),
        ActiveMfaEncryptionKeyId = activeKey,
        MfaEncryptionKeys = includeSecond
            ?
            [
                new MfaEncryptionKeyConfig { KeyId = "key-one", EncryptionKey = Key(7) },
                new MfaEncryptionKeyConfig { KeyId = "key-two", EncryptionKey = Key(11) }
            ]
            : [new MfaEncryptionKeyConfig { KeyId = activeKey, EncryptionKey = Key(7) }]
    };

    private static string Key(int offset) => Convert.ToBase64String(
        Enumerable.Range(offset, 64).Select(value => (byte)value).ToArray());

    private sealed class CapturingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? PaddingHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            PaddingHeader = request.Headers.GetValues("Add-Padding").Single();
            return Task.FromResult(response);
        }
    }
}
