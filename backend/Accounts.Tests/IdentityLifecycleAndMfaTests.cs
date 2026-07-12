using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Middleware;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Accounts.Tests;

public sealed class IdentityLifecycleAndMfaTests
{
    private const string OwnerPassword = "Correct Horse Battery Staple 1!";
    private const string NewPassword = "Different Strong Password Value 2!";

    [Fact]
    public void MfaLoginChallenge_EmitsExplicitNullEnrollmentFieldsUnderProductionJsonPolicy()
    {
        var challenge = new MfaChallengeResponse(
            "challenge-token-that-is-long-enough-for-the-wire-contract",
            false,
            new DateTime(2026, 7, 11, 2, 30, 0, DateTimeKind.Utc),
            null,
            null);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(challenge, options));

        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("enrollmentSecret").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("otpAuthUri").ValueKind);
    }

    [Fact]
    public async Task PrivilegedLogin_RequiresTotpEnrollment_AndCreatesNoPrincipalBeforeSecondFactor()
    {
        await using var fixture = await IdentityFixture.CreateAsync();

        var firstFactor = await fixture.Identity.BeginLoginAsync(fixture.Tenant.Slug, fixture.Owner.Email, OwnerPassword);

        Assert.True(firstFactor.Succeeded);
        Assert.Null(firstFactor.User);
        var challenge = Assert.IsType<MfaChallengeResponse>(firstFactor.MfaChallenge);
        Assert.True(challenge.RequiresEnrollment);
        Assert.NotNull(challenge.EnrollmentSecret);
        var stored = await fixture.Db.UserMfaCredentials.SingleAsync();
        Assert.DoesNotContain(challenge.EnrollmentSecret!, stored.EncryptedSecret, StringComparison.Ordinal);

        var code = fixture.Mfa.ComputeTotpForTesting(challenge.EnrollmentSecret!, fixture.Clock.GetUtcNow());
        var completion = await fixture.Identity.CompleteChallengeAsync(challenge.ChallengeToken, code, null);

        Assert.True(completion.Succeeded);
        Assert.Equal("totp", completion.User!.MfaMethod);
        Assert.Equal(10, completion.RecoveryCodes.Count);
        var storedHashes = await fixture.Db.UserMfaRecoveryCodes.Select(item => item.CodeHash).ToArrayAsync();
        Assert.Equal(10, storedHashes.Length);
        Assert.DoesNotContain(completion.RecoveryCodes[0], storedHashes);

        var cookie = fixture.Auth.CreateSessionCookieValue(completion.User with { CsrfToken = "csrf-test" }, fixture.Clock.GetUtcNow());
        var session = await fixture.Auth.ReadSessionAsync(cookie, fixture.Clock.GetUtcNow().AddMinutes(1));
        Assert.NotNull(session);
        Assert.Equal("totp", session!.MfaMethod);

        var replay = await fixture.Identity.CompleteChallengeAsync(challenge.ChallengeToken, code, null);
        Assert.False(replay.Succeeded);
    }

    [Fact]
    public async Task RecoveryCode_IsOneTime_AndCannotSatisfyRecentTotpGate()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var recoveryCodes = await EnrollOwnerAsync(fixture);
        var login = await fixture.Identity.BeginLoginAsync(fixture.Tenant.Slug, fixture.Owner.Email, OwnerPassword);
        var challenge = Assert.IsType<MfaChallengeResponse>(login.MfaChallenge);

        var recovered = await fixture.Identity.CompleteChallengeAsync(challenge.ChallengeToken, null, recoveryCodes[0]);

        Assert.True(recovered.Succeeded);
        Assert.Equal("recovery", recovered.User!.MfaMethod);
        Assert.False(RecentAuthenticationMiddleware.HasRecentTotp(recovered.User, fixture.Clock.GetUtcNow(), 10));

        var secondLogin = await fixture.Identity.BeginLoginAsync(fixture.Tenant.Slug, fixture.Owner.Email, OwnerPassword);
        var secondChallenge = Assert.IsType<MfaChallengeResponse>(secondLogin.MfaChallenge);
        var replay = await fixture.Identity.CompleteChallengeAsync(secondChallenge.ChallengeToken, null, recoveryCodes[0]);
        Assert.False(replay.Succeeded);
    }

    [Theory]
    [InlineData("Owner", true)]
    [InlineData("Accountant", true)]
    [InlineData("Reviewer", true)]
    [InlineData("Client", false)]
    public async Task OwnerCanInviteAndAcceptEveryRole_WithoutDirectSql(string role, bool requiresMfa)
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var invited = await fixture.Lifecycle.InviteAsync(
            fixture.OwnerPrincipal,
            new InviteUserInput($"{role.ToLowerInvariant()}-invited@example.ie", $"{role} User", role, [fixture.Company.Id]));

        Assert.False(invited.User.IsActive);
        await fixture.Lifecycle.AcceptInvitationAsync(new AcceptActionTokenInput(invited.ActionToken, NewPassword));
        var accepted = await fixture.Db.UserAccounts.SingleAsync(user => user.Id == invited.User.UserId);
        Assert.True(accepted.IsActive);
        Assert.Equal(role, accepted.Role);

        var login = await fixture.Identity.BeginLoginAsync(fixture.Tenant.Slug, accepted.Email, NewPassword);
        Assert.Equal(requiresMfa, login.MfaChallenge is not null);
        Assert.Equal(!requiresMfa, login.User is not null);
    }

    [Fact]
    public async Task NonOwnerCannotEscalateRole_AndPermissionChangesInvalidateExistingSession()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var client = await fixture.AddUserAsync("client@example.ie", "Client", NewPassword);
        var clientPrincipal = await fixture.Auth.GetPrincipalAsync(client.Id) ?? throw new InvalidOperationException();
        var oldCookie = fixture.Auth.CreateSessionCookieValue(clientPrincipal with { CsrfToken = "csrf" }, fixture.Clock.GetUtcNow());
        var reviewer = await fixture.AddUserAsync("reviewer@example.ie", "Reviewer", NewPassword);
        var reviewerPrincipal = await fixture.Auth.GetPrincipalAsync(reviewer.Id) ?? throw new InvalidOperationException();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Lifecycle.ChangeRoleAsync(reviewerPrincipal, client.Id, "Owner"));

        var changed = await fixture.Lifecycle.ChangeRoleAsync(fixture.OwnerPrincipal, client.Id, "Reviewer");
        Assert.Equal("Reviewer", changed.Role);
        Assert.Null(await fixture.Auth.ReadSessionAsync(oldCookie, fixture.Clock.GetUtcNow().AddMinutes(1)));
    }

    [Fact]
    public async Task CompanyRemovalAndDeactivationImmediatelyRevokeExistingSessions()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var client = await fixture.AddUserAsync("client@example.ie", "Client", NewPassword, [fixture.Company.Id]);
        var principal = await fixture.Auth.GetPrincipalAsync(client.Id) ?? throw new InvalidOperationException();
        var cookie = fixture.Auth.CreateSessionCookieValue(principal with { CsrfToken = "csrf" }, fixture.Clock.GetUtcNow());

        await fixture.Lifecycle.SetCompanyAssignmentsAsync(fixture.OwnerPrincipal, client.Id, []);
        Assert.Null(await fixture.Auth.ReadSessionAsync(cookie, fixture.Clock.GetUtcNow().AddMinutes(1)));

        principal = await fixture.Auth.GetPrincipalAsync(client.Id) ?? throw new InvalidOperationException();
        cookie = fixture.Auth.CreateSessionCookieValue(principal with { CsrfToken = "csrf2" }, fixture.Clock.GetUtcNow());
        await fixture.Lifecycle.SetActiveAsync(fixture.OwnerPrincipal, client.Id, false);
        Assert.Null(await fixture.Auth.ReadSessionAsync(cookie, fixture.Clock.GetUtcNow().AddMinutes(1)));
    }

    [Fact]
    public async Task OwnerCanUnlockRevokeSessionsAndPermanentlyOffboardAUser()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var client = await fixture.AddUserAsync("departing@example.ie", "Client", NewPassword, [fixture.Company.Id]);
        client.FailedLoginCount = 5;
        client.LastFailedLoginAt = fixture.Clock.GetUtcNow().UtcDateTime;
        client.LockedUntilUtc = fixture.Clock.GetUtcNow().AddMinutes(15).UtcDateTime;
        await fixture.Db.SaveChangesAsync();

        var unlocked = await fixture.Lifecycle.UnlockAsync(fixture.OwnerPrincipal, client.Id);

        Assert.False(unlocked.IsLocked);
        Assert.Equal(0, client.FailedLoginCount);
        Assert.Null(client.LastFailedLoginAt);
        Assert.Null(client.LockedUntilUtc);

        var principal = await fixture.Auth.GetPrincipalAsync(client.Id) ?? throw new InvalidOperationException();
        var cookieBeforeRevocation = fixture.Auth.CreateSessionCookieValue(
            principal with { CsrfToken = "before-revocation" },
            fixture.Clock.GetUtcNow());

        await fixture.Lifecycle.RevokeSessionsAsync(fixture.OwnerPrincipal, client.Id);

        Assert.Null(await fixture.Auth.ReadSessionAsync(cookieBeforeRevocation, fixture.Clock.GetUtcNow().AddMinutes(1)));

        principal = await fixture.Auth.GetPrincipalAsync(client.Id) ?? throw new InvalidOperationException();
        var cookieBeforeOffboarding = fixture.Auth.CreateSessionCookieValue(
            principal with { CsrfToken = "before-offboarding" },
            fixture.Clock.GetUtcNow());
        var reset = await fixture.Lifecycle.BeginPasswordResetAsync(fixture.OwnerPrincipal, client.Id);

        var offboarded = await fixture.Lifecycle.OffboardAsync(fixture.OwnerPrincipal, client.Id);

        Assert.False(offboarded.IsActive);
        Assert.Empty(offboarded.CompanyIds);
        Assert.NotNull(offboarded.OffboardedAtUtc);
        Assert.Null(await fixture.Auth.ReadSessionAsync(cookieBeforeOffboarding, fixture.Clock.GetUtcNow().AddMinutes(1)));
        Assert.Null(await fixture.Auth.GetPrincipalAsync(client.Id));
        Assert.False((await fixture.Identity.BeginLoginAsync(fixture.Tenant.Slug, client.Email, NewPassword)).Succeeded);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            fixture.Lifecycle.CompletePasswordResetAsync(new AcceptActionTokenInput(reset.ActionToken, OwnerPassword)));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            fixture.Lifecycle.SetActiveAsync(fixture.OwnerPrincipal, client.Id, true));

        var events = await fixture.Db.UserLifecycleEvents
            .Where(entry => entry.TargetUserId == client.Id)
            .OrderBy(entry => entry.Id)
            .ToListAsync();
        Assert.Contains(events, entry => entry.EventType == UserLifecycleEventTypes.Unlocked);
        Assert.Contains(events, entry => entry.EventType == UserLifecycleEventTypes.SessionsRevoked);
        Assert.Contains(events, entry => entry.EventType == UserLifecycleEventTypes.Offboarded);
        Assert.All(events, entry => Assert.DoesNotContain(NewPassword, entry.DetailsJson, StringComparison.Ordinal));
        Assert.All(events, entry => Assert.DoesNotContain(reset.ActionToken, entry.DetailsJson, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AccountLockAndUnlockRevokeSessionsAndPersistedMfaChallenges()
    {
        await using var fixture = await IdentityFixture.CreateAsync(requireMfa: false);
        var client = await fixture.AddUserAsync("lock-cycle@example.ie", "Client", NewPassword);
        var principal = await fixture.Auth.GetPrincipalAsync(client.Id) ?? throw new InvalidOperationException();
        var cookieBeforeLock = fixture.Auth.CreateSessionCookieValue(
            principal with { CsrfToken = "before-lock" },
            fixture.Clock.GetUtcNow());
        fixture.Db.UserMfaChallenges.Add(new UserMfaChallenge
        {
            TenantId = fixture.Tenant.Id,
            UserId = client.Id,
            Purpose = MfaChallengePurposes.Login,
            TokenHash = new string('a', 64),
            SessionVersion = client.SessionVersion,
            ExpiresAtUtc = fixture.Clock.GetUtcNow().AddMinutes(5).UtcDateTime
        });
        await fixture.Db.SaveChangesAsync();

        LoginResult failed = null!;
        for (var attempt = 0; attempt < 5; attempt++)
            failed = await fixture.Auth.LoginAsync(fixture.Tenant.Slug, client.Email, "Wrong Password Value 9!");

        Assert.True(failed.AccountLocked);
        Assert.Null(await fixture.Auth.ReadSessionAsync(cookieBeforeLock, fixture.Clock.GetUtcNow().AddMinutes(1)));
        Assert.All(await fixture.Db.UserMfaChallenges.ToListAsync(), challenge => Assert.NotNull(challenge.RevokedAtUtc));

        var lockedPrincipal = await fixture.Auth.GetPrincipalAsync(client.Id) ?? throw new InvalidOperationException();
        var cookieBeforeUnlock = fixture.Auth.CreateSessionCookieValue(
            lockedPrincipal with { CsrfToken = "before-unlock" },
            fixture.Clock.GetUtcNow());
        fixture.Db.UserMfaChallenges.Add(new UserMfaChallenge
        {
            TenantId = fixture.Tenant.Id,
            UserId = client.Id,
            Purpose = MfaChallengePurposes.Login,
            TokenHash = new string('b', 64),
            SessionVersion = client.SessionVersion,
            ExpiresAtUtc = fixture.Clock.GetUtcNow().AddMinutes(5).UtcDateTime
        });
        await fixture.Db.SaveChangesAsync();

        await fixture.Lifecycle.UnlockAsync(fixture.OwnerPrincipal, client.Id);

        Assert.Null(await fixture.Auth.ReadSessionAsync(cookieBeforeUnlock, fixture.Clock.GetUtcNow().AddMinutes(1)));
        Assert.All(await fixture.Db.UserMfaChallenges.ToListAsync(), challenge => Assert.NotNull(challenge.RevokedAtUtc));
    }

    [Fact]
    public async Task SameEmailCanBeProvisionedAndAuthenticatedIndependentlyAcrossTenants()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var otherTenant = new Tenant { Name = "Other Firm", Slug = "other-firm" };
        fixture.Db.Tenants.Add(otherTenant);
        await fixture.Db.SaveChangesAsync();
        var password = PasswordHasher.HashPassword(OwnerPassword);
        fixture.Db.UserAccounts.Add(new UserAccount
        {
            TenantId = otherTenant.Id,
            Tenant = otherTenant,
            Email = "private-other-tenant@example.ie",
            DisplayName = "Other User",
            Role = "Client",
            PasswordHash = password.Hash,
            PasswordSalt = password.Salt,
            PasswordAlgorithm = AuthService.PasswordAlgorithm,
            PasswordLastChangedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        await fixture.Db.SaveChangesAsync();

        var invitation = await fixture.Lifecycle.InviteAsync(
            fixture.OwnerPrincipal,
            new InviteUserInput("private-other-tenant@example.ie", "New User", "Client", []));
        await fixture.Lifecycle.AcceptInvitationAsync(new AcceptActionTokenInput(invitation.ActionToken, NewPassword));

        var provisioned = await fixture.Db.UserAccounts.SingleAsync(user => user.Id == invitation.User.UserId);
        Assert.Equal(fixture.Tenant.Id, provisioned.TenantId);
        Assert.True((await fixture.Auth.LoginAsync(
            fixture.Tenant.Slug,
            "private-other-tenant@example.ie",
            NewPassword)).Succeeded);
        Assert.True((await fixture.Auth.LoginAsync(
            otherTenant.Slug,
            "private-other-tenant@example.ie",
            OwnerPassword)).Succeeded);
        Assert.False((await fixture.Auth.LoginAsync(
            fixture.Tenant.Slug,
            "private-other-tenant@example.ie",
            OwnerPassword)).Succeeded);
    }

    [Fact]
    public async Task SameTenantDuplicateEmailUsesGenericProvisioningFailure()
    {
        await using var fixture = await IdentityFixture.CreateAsync();

        var failure = await Assert.ThrowsAsync<BusinessRuleException>(() => fixture.Lifecycle.InviteAsync(
            fixture.OwnerPrincipal,
            new InviteUserInput(fixture.Owner.Email, "Duplicate User", "Client", [])));
        Assert.DoesNotContain("exists", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fixture.Owner.Email, failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PasswordRecoveryRetainsLockoutRoleTenantAndMfaControls()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        await EnrollOwnerAsync(fixture);
        fixture.Owner.LockedUntilUtc = fixture.Clock.GetUtcNow().AddMinutes(15).UtcDateTime;
        fixture.Owner.FailedLoginCount = 5;
        await fixture.Db.SaveChangesAsync();

        var reset = await fixture.Lifecycle.BeginPasswordResetAsync(fixture.OwnerPrincipal, fixture.Owner.Id);
        await fixture.Lifecycle.CompletePasswordResetAsync(new AcceptActionTokenInput(reset.ActionToken, NewPassword));

        var user = await fixture.Db.UserAccounts.Include(candidate => candidate.MfaCredential).SingleAsync(candidate => candidate.Id == fixture.Owner.Id);
        Assert.Equal("Owner", user.Role);
        Assert.Equal(fixture.Tenant.Id, user.TenantId);
        Assert.True(user.LockedUntilUtc > fixture.Clock.GetUtcNow().UtcDateTime);
        Assert.NotNull(user.MfaCredential?.EnabledAtUtc);
        var blocked = await fixture.Identity.BeginLoginAsync(fixture.Tenant.Slug, user.Email, NewPassword);
        Assert.False(blocked.Succeeded);
        Assert.True(blocked.FirstFactor.AccountLocked);
    }

    [Fact]
    public async Task SessionUsesSlidingIdleLimitWithoutExtendingAbsoluteLifetime()
    {
        await using var fixture = await IdentityFixture.CreateAsync(idleMinutes: 10, absoluteMinutes: 30, requireMfa: false);
        var login = await fixture.Auth.LoginAsync(fixture.Tenant.Slug, fixture.Owner.Email, OwnerPassword);
        var issued = fixture.Clock.GetUtcNow();
        var cookie = fixture.Auth.CreateSessionCookieValue(login.User! with { CsrfToken = "csrf" }, issued);
        var activeAtNine = await fixture.Auth.ReadSessionAsync(cookie, issued.AddMinutes(9));
        Assert.NotNull(activeAtNine);
        Assert.Null(await fixture.Auth.ReadSessionAsync(cookie, issued.AddMinutes(11)));

        var refreshed = fixture.Auth.CreateRefreshedSessionCookieValue(activeAtNine!, issued.AddMinutes(9));
        Assert.NotNull(await fixture.Auth.ReadSessionAsync(refreshed, issued.AddMinutes(18)));
        Assert.Null(await fixture.Auth.ReadSessionAsync(refreshed, issued.AddMinutes(30)));
    }

    [Fact]
    public void SensitiveOperationMatrixIncludesAdministrationPrivacyFinalisationAndDeletion()
    {
        Assert.True(RecentAuthenticationMiddleware.RequiresRecentMfa("/api/admin/users/4/role", HttpMethods.Put));
        Assert.True(RecentAuthenticationMiddleware.RequiresRecentMfa("/api/privacy/export", HttpMethods.Post));
        Assert.True(RecentAuthenticationMiddleware.RequiresRecentMfa("/api/companies/1/periods/2/status", HttpMethods.Put));
        Assert.True(RecentAuthenticationMiddleware.RequiresRecentMfa("/api/companies/1", HttpMethods.Delete));
        Assert.True(RecentAuthenticationMiddleware.RequiresRecentMfa("/api/companies/1/periods/2/filing/revenue-external-validation", HttpMethods.Post));
        Assert.True(RecentAuthenticationMiddleware.RequiresRecentMfa("/api/companies/1/periods/2/external-filing-handoff/snapshots/123/outcomes", HttpMethods.Post));
        Assert.True(RecentAuthenticationMiddleware.RequiresRecentMfa("/api/companies/1/periods/2/external-filing-handoff/authorities", HttpMethods.Post));
        Assert.False(RecentAuthenticationMiddleware.RequiresRecentMfa("/api/companies", HttpMethods.Get));
    }

    [Theory]
    [InlineData("PUT", "/api/admin/users/4/role")]
    [InlineData("PUT", "/api/companies/1/periods/2/status")]
    [InlineData("DELETE", "/api/companies/1")]
    [InlineData("POST", "/api/companies/1/periods/2/filing/revenue-external-validation")]
    [InlineData("POST", "/api/companies/1/periods/2/external-filing-handoff/snapshots/123/outcomes")]
    public async Task SensitiveHttpOperationsRejectStaleOrRecoveryAuthenticationAndAcceptRecentTotp(string method, string path)
    {
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(now);
        var options = Options.Create(new AuthSessionConfig { RequirePrivilegedMfa = true, RecentMfaMinutes = 10 });
        var downstreamCalls = 0;
        var middleware = new RecentAuthenticationMiddleware(_ =>
        {
            downstreamCalls++;
            return Task.CompletedTask;
        });
        var principal = new AuthenticatedUser(1, 1, "Test", "owner@example.invalid", "Owner", "Owner");

        foreach (var authentication in new[]
        {
            principal with { MfaMethod = "totp", MfaVerifiedAtUtc = now.AddMinutes(-11) },
            principal with { MfaMethod = "recovery", MfaVerifiedAtUtc = now }
        })
        {
            var rejected = new DefaultHttpContext();
            rejected.Request.Method = method;
            rejected.Request.Path = path;
            rejected.Response.Body = new MemoryStream();
            rejected.Items[AuthContext.ItemKey] = authentication;

            await middleware.InvokeAsync(rejected, options, clock);

            Assert.Equal(StatusCodes.Status428PreconditionRequired, rejected.Response.StatusCode);
            rejected.Response.Body.Position = 0;
            Assert.Contains(RecentAuthenticationMiddleware.ErrorCode, await new StreamReader(rejected.Response.Body).ReadToEndAsync());
        }

        var accepted = new DefaultHttpContext();
        accepted.Request.Method = method;
        accepted.Request.Path = path;
        accepted.Items[AuthContext.ItemKey] = principal with { MfaMethod = "totp", MfaVerifiedAtUtc = now.AddMinutes(-9) };
        await middleware.InvokeAsync(accepted, options, clock);

        Assert.Equal(1, downstreamCalls);
    }

    [Fact]
    public async Task LifecycleLedgerIsAppendOnlyAndContainsNoCredentialMaterial()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        var invited = await fixture.Lifecycle.InviteAsync(
            fixture.OwnerPrincipal,
            new InviteUserInput("new@example.ie", "New User", "Client", []));
        var evidence = await fixture.Db.UserLifecycleEvents.SingleAsync(entry => entry.TargetUserId == invited.User.UserId);
        Assert.DoesNotContain(invited.ActionToken, evidence.DetailsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("new@example.ie", evidence.DetailsJson, StringComparison.OrdinalIgnoreCase);

        evidence.EventType = "Tampered";
        await Assert.ThrowsAsync<BusinessRuleException>(() => fixture.Db.SaveChangesAsync());
    }

    private static async Task<IReadOnlyList<string>> EnrollOwnerAsync(IdentityFixture fixture)
    {
        var login = await fixture.Identity.BeginLoginAsync(fixture.Tenant.Slug, fixture.Owner.Email, OwnerPassword);
        var challenge = Assert.IsType<MfaChallengeResponse>(login.MfaChallenge);
        var code = fixture.Mfa.ComputeTotpForTesting(challenge.EnrollmentSecret!, fixture.Clock.GetUtcNow());
        var completion = await fixture.Identity.CompleteChallengeAsync(challenge.ChallengeToken, code, null);
        Assert.True(completion.Succeeded);
        return completion.RecoveryCodes;
    }

    private sealed class IdentityFixture : IAsyncDisposable
    {
        private IdentityFixture(AccountsDbContext db, MutableTimeProvider clock, AuthSessionConfig config)
        {
            Db = db;
            Clock = clock;
            var options = Options.Create(config);
            Auth = new AuthService(db, options, new TestEnvironment(), new Pbkdf2PasswordVerifier(), clock);
            Mfa = new MfaSecurityService(options);
            var audit = new AuditService(db);
            Identity = new IdentityAccessService(db, Auth, Mfa, audit, options, clock);
            Lifecycle = new UserLifecycleService(db, Mfa, audit, clock);
        }

        public AccountsDbContext Db { get; }
        public MutableTimeProvider Clock { get; }
        public AuthService Auth { get; }
        public MfaSecurityService Mfa { get; }
        public IdentityAccessService Identity { get; }
        public UserLifecycleService Lifecycle { get; }
        public Tenant Tenant { get; private set; } = null!;
        public Company Company { get; private set; } = null!;
        public UserAccount Owner { get; private set; } = null!;
        public AuthenticatedUser OwnerPrincipal { get; private set; } = null!;

        public static async Task<IdentityFixture> CreateAsync(int idleMinutes = 10, int absoluteMinutes = 60, bool requireMfa = true)
        {
            var options = new DbContextOptionsBuilder<AccountsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var db = new AccountsDbContext(options);
            var clock = new MutableTimeProvider(new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero));
            var fixture = new IdentityFixture(db, clock, new AuthSessionConfig
            {
                SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
                ExpiryMinutes = 60,
                IdleTimeoutMinutes = idleMinutes,
                AbsoluteLifetimeMinutes = absoluteMinutes,
                RecentMfaMinutes = 10,
                MfaChallengeMinutes = 5,
                RequirePrivilegedMfa = requireMfa
            });
            fixture.Tenant = new Tenant { Name = "Example Firm", Slug = Guid.NewGuid().ToString("N") };
            db.Tenants.Add(fixture.Tenant);
            await db.SaveChangesAsync();
            fixture.Company = new Company
            {
                TenantId = fixture.Tenant.Id,
                LegalName = "Example Limited",
                CroNumber = "123456",
                CompanyType = CompanyType.Private,
                IncorporationDate = new DateOnly(2020, 1, 1),
                RegisteredOfficeAddress1 = "1 Main Street",
                RegisteredOfficeCity = "Dublin",
                RegisteredOfficeCounty = "Dublin",
                IsTrading = true
            };
            db.Companies.Add(fixture.Company);
            await db.SaveChangesAsync();
            fixture.Owner = await fixture.AddUserAsync("owner@example.ie", "Owner", OwnerPassword);
            fixture.Owner.DisplayName = "Named Owner";
            await db.SaveChangesAsync();
            fixture.OwnerPrincipal = await fixture.Auth.GetPrincipalAsync(fixture.Owner.Id) ?? throw new InvalidOperationException();
            return fixture;
        }

        public async Task<UserAccount> AddUserAsync(string email, string role, string password, IReadOnlyList<int>? companyIds = null)
        {
            var hash = PasswordHasher.HashPassword(password);
            var user = new UserAccount
            {
                TenantId = Tenant.Id,
                Tenant = Tenant,
                Email = email,
                DisplayName = role + " User",
                Role = role,
                PasswordHash = hash.Hash,
                PasswordSalt = hash.Salt,
                PasswordAlgorithm = AuthService.PasswordAlgorithm,
                PasswordLastChangedAt = Clock.GetUtcNow().UtcDateTime,
                IsActive = true
            };
            Db.UserAccounts.Add(user);
            await Db.SaveChangesAsync();
            foreach (var companyId in companyIds ?? [])
                Db.UserCompanyAccesses.Add(new UserCompanyAccess { UserId = user.Id, CompanyId = companyId });
            await Db.SaveChangesAsync();
            return user;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Accounts.Tests";
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
