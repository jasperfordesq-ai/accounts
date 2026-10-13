using System.Security.Cryptography;
using System.Text;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Accounts.Tests;

/// <summary>
/// Real identity + connector services over an in-memory database, composed through DI so
/// middleware that opens fresh scopes behaves as it does in the API.
/// </summary>
internal sealed class ConnectorTestFixture : IAsyncDisposable
{
    public const string OwnerPassword = "Correct Horse Battery Staple 1!";
    public const string ClientVersion = "mcp-connector/0.1.0";

    private readonly ServiceProvider root;
    private readonly AsyncServiceScope scope;

    private ConnectorTestFixture(ServiceProvider root, MutableClock clock, ConnectorConfig connectorConfig)
    {
        this.root = root;
        Clock = clock;
        ConnectorConfig = connectorConfig;
        scope = root.CreateAsyncScope();
    }

    public MutableClock Clock { get; }
    public ConnectorConfig ConnectorConfig { get; }
    public IServiceProvider Services => scope.ServiceProvider;
    public IServiceScopeFactory ScopeFactory => root.GetRequiredService<IServiceScopeFactory>();
    public AccountsDbContext Db => Services.GetRequiredService<AccountsDbContext>();
    public AuthService Auth => Services.GetRequiredService<AuthService>();
    public MfaSecurityService Mfa => Services.GetRequiredService<MfaSecurityService>();
    public IdentityAccessService Identity => Services.GetRequiredService<IdentityAccessService>();
    public UserLifecycleService Lifecycle => Services.GetRequiredService<UserLifecycleService>();
    public ConnectorSessionService Connectors => Services.GetRequiredService<ConnectorSessionService>();
    public OAuthService OAuth => Services.GetRequiredService<OAuthService>();
    public Tenant Tenant { get; private set; } = null!;
    public Company Company { get; private set; } = null!;
    public UserAccount Owner { get; private set; } = null!;
    public string OwnerTotpSecret { get; private set; } = "";

    public static async Task<ConnectorTestFixture> CreateAsync(Action<ConnectorConfig>? configure = null)
    {
        var databaseName = Guid.NewGuid().ToString();
        var clock = new MutableClock(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero));
        var authConfig = new AuthSessionConfig
        {
            SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            ExpiryMinutes = 60,
            IdleTimeoutMinutes = 30,
            AbsoluteLifetimeMinutes = 480,
            RecentMfaMinutes = 10,
            MfaChallengeMinutes = 5,
            RequirePrivilegedMfa = true
        };
        var connectorConfig = new ConnectorConfig
        {
            Enabled = true,
            SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            PublicBaseUrl = "https://filingbridge.example"
        };
        configure?.Invoke(connectorConfig);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddSingleton(Options.Create(authConfig));
        services.AddSingleton(Options.Create(connectorConfig));
        services.AddSingleton(Options.Create(new IdentitySecurityConfig()));
        services.AddScoped(_ => new AccountsDbContext(
            new DbContextOptionsBuilder<AccountsDbContext>().UseInMemoryDatabase(databaseName).Options,
            null));
        services.AddScoped<IPasswordVerifier, Pbkdf2PasswordVerifier>();
        services.AddScoped(provider => new AuthService(
            provider.GetRequiredService<AccountsDbContext>(),
            provider.GetRequiredService<IOptions<AuthSessionConfig>>(),
            provider.GetRequiredService<IHostEnvironment>(),
            provider.GetRequiredService<IPasswordVerifier>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped(provider => new MfaSecurityService(provider.GetRequiredService<IOptions<AuthSessionConfig>>()));
        services.AddScoped(provider => new AuditService(provider.GetRequiredService<AccountsDbContext>()));
        services.AddScoped(provider => new IdentityAccessService(
            provider.GetRequiredService<AccountsDbContext>(),
            provider.GetRequiredService<AuthService>(),
            provider.GetRequiredService<MfaSecurityService>(),
            provider.GetRequiredService<AuditService>(),
            provider.GetRequiredService<IOptions<AuthSessionConfig>>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped(provider => new UserLifecycleService(
            provider.GetRequiredService<AccountsDbContext>(),
            provider.GetRequiredService<MfaSecurityService>(),
            provider.GetRequiredService<AuditService>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped(provider => new ConnectorSessionService(
            provider.GetRequiredService<AccountsDbContext>(),
            provider.GetRequiredService<AuthService>(),
            provider.GetRequiredService<IdentityAccessService>(),
            provider.GetRequiredService<MfaSecurityService>(),
            provider.GetRequiredService<AuditService>(),
            provider.GetRequiredService<IOptions<ConnectorConfig>>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped(provider => new OAuthService(
            provider.GetRequiredService<AccountsDbContext>(),
            provider.GetRequiredService<ConnectorSessionService>(),
            provider.GetRequiredService<MfaSecurityService>(),
            provider.GetRequiredService<IOptions<ConnectorConfig>>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<ConnectorActionSummaryService>();
        services.AddSingleton<ConnectorWriteBudget>();

        var fixture = new ConnectorTestFixture(services.BuildServiceProvider(), clock, connectorConfig);
        var db = fixture.Db;
        fixture.Tenant = new Tenant { Name = "Example Firm", Slug = "example-" + Guid.NewGuid().ToString("N")[..8] };
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
        fixture.Owner = await fixture.AddUserAsync("owner@example.ie", "Owner");
        fixture.OwnerTotpSecret = await fixture.EnrolAsync(fixture.Owner);
        return fixture;
    }

    public async Task<UserAccount> AddUserAsync(string email, string role, string password = OwnerPassword)
    {
        var hash = PasswordHasher.HashPassword(password);
        var user = new UserAccount
        {
            TenantId = Tenant.Id,
            Tenant = Tenant,
            Email = email,
            DisplayName = role + " Person",
            Role = role,
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            PasswordAlgorithm = AuthService.PasswordAlgorithm,
            PasswordLastChangedAt = Clock.GetUtcNow().UtcDateTime,
            IsActive = true
        };
        Db.UserAccounts.Add(user);
        await Db.SaveChangesAsync();
        if (role == "Client")
        {
            Db.UserCompanyAccesses.Add(new UserCompanyAccess { UserId = user.Id, CompanyId = Company.Id });
            await Db.SaveChangesAsync();
        }
        return user;
    }

    /// <summary>Enrols TOTP through the real browser flow and returns the secret.</summary>
    public async Task<string> EnrolAsync(UserAccount user, string password = OwnerPassword)
    {
        var login = await Identity.BeginLoginAsync(Tenant.Slug, user.Email, password);
        var challenge = login.MfaChallenge ?? throw new InvalidOperationException("Expected an enrolment challenge.");
        var secret = challenge.EnrollmentSecret ?? throw new InvalidOperationException("Expected an enrolment secret.");
        var completed = await Identity.CompleteChallengeAsync(challenge.ChallengeToken, NextTotp(secret), null);
        if (!completed.Succeeded) throw new InvalidOperationException("Enrolment failed.");
        return secret;
    }

    /// <summary>A TOTP for the current time; advances the clock so the next call gets a fresh window.</summary>
    public string NextTotp(string? secret = null)
    {
        var code = Mfa.ComputeTotpForTesting(secret ?? OwnerTotpSecret, Clock.GetUtcNow());
        Clock.Advance(TimeSpan.FromSeconds(31));
        return code;
    }

    public Task<ConnectorLoginResult> ConnectOwnerAsync(string accessLevel = "Admin", string dataScope = "Withheld") =>
        Connectors.LoginAsync(
            new ConnectorLoginInput(Tenant.Slug, Owner.Email, OwnerPassword, accessLevel, dataScope, NextTotp(), null, "test device"),
            "0.1.0");

    public DefaultHttpContext NewHttpContext(string method, string path, string? body = null, string? bearer = null)
    {
        var context = new DefaultHttpContext { RequestServices = Services };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Headers["X-FilingBridge-Client"] = ClientVersion;
        if (bearer is not null) context.Request.Headers.Authorization = "Bearer " + bearer;
        if (body is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;
            context.Request.ContentType = "application/json";
        }
        context.Response.Body = new MemoryStream();
        return context;
    }

    public static async Task<string> ReadResponseAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await new StreamReader(context.Response.Body).ReadToEndAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
        await root.DisposeAsync();
    }

    internal sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan by) => current = current.Add(by);
    }

    private sealed class TestHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Accounts.Tests";
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
