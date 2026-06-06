# Firm Login and Tenant Access Control Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add authenticated firm-user sessions, tenant isolation, role-based authorization, and authenticated reviewer/audit identity for the Irish Accounts production-readiness Slice 1.

**Architecture:** Keep the existing API-key middleware as the service-to-service guard, then add signed cookie user sessions, authenticated principal middleware, tenant access middleware, and central role authorization middleware before endpoint handlers. Use the existing dirty-worktree `Tenant`, `UserAccount`, and `Company.TenantId` model as the foundation, then make targeted endpoint edits where the server must stamp tenant or authenticated identity.

**Tech Stack:** ASP.NET Core Minimal API on .NET 10, EF Core 9, PostgreSQL/InMemory EF tests, Next.js 16 App Router, HeroUI v3, Tailwind CSS 4.

---

## File Structure

- Create `backend/Accounts.Api/Rules/AuthSessionConfig.cs`: session cookie name, lifetime, signing key, and production cookie safety settings.
- Create `backend/Accounts.Api/Services/AuthService.cs`: password verification, session ticket signing/validation, login, logout cookie helpers, and current-user loading.
- Create `backend/Accounts.Api/Services/AuthContext.cs`: lightweight helpers for reading the authenticated principal from `HttpContext`.
- Create `backend/Accounts.Api/Services/AuthenticatedIdentity.cs`: one place to derive review display names and audit user identifiers from the authenticated principal.
- Create `backend/Accounts.Api/Services/RoleAuthorizationService.cs`: central role/permission rules.
- Create `backend/Accounts.Api/Middleware/UserSessionMiddleware.cs`: requires a valid user session for protected API routes.
- Create `backend/Accounts.Api/Middleware/TenantAccessMiddleware.cs`: ensures `/api/companies/{companyId}` belongs to the signed-in user's tenant.
- Create `backend/Accounts.Api/Middleware/RoleAuthorizationMiddleware.cs`: enforces role permissions before endpoint handlers.
- Create `backend/Accounts.Api/Endpoints/AuthEndpoints.cs`: `/api/auth/login`, `/api/auth/logout`, `/api/auth/me`.
- Modify `backend/Accounts.Api/Endpoints/EndpointRegistration.cs`: register auth endpoints.
- Modify `backend/Accounts.Api/Program.cs`: configure auth options/services/middleware; tenant-scope company list/detail/update/delete; stamp `TenantId` on company create; use authenticated identity for period status locks.
- Modify `backend/Accounts.Api/Services/ProductionSafetyService.cs`: reject unsafe session configuration in production.
- Modify `backend/Accounts.Api/Data/AccountsDbContext.cs`: keep tenant/user mappings and use the existing lower-case email convention for lookup.
- Modify `backend/Accounts.Api/Data/SeedData.cs`: keep seeded users and align roles with `Owner`, `Accountant`, `Reviewer`, `Client`.
- Modify `backend/Accounts.Tests/AccountsWorkflowTests.cs`: add service and middleware tests for login, session, tenant isolation, authorization, and identity stamping.
- Create `frontend/src/lib/auth.ts`: typed client functions for login/logout/me.
- Create `frontend/src/components/AuthProvider.tsx`: session state and route guard.
- Create `frontend/src/app/login/page.tsx`: login screen.
- Modify `frontend/src/app/providers.tsx`: wrap app in auth provider.
- Modify `frontend/src/components/AppNavbar.tsx`: show user/role/tenant and logout.
- Modify `frontend/src/lib/api.ts`: add credentials, auth types, remove caller-controlled reviewer identity payloads.
- Modify `frontend/src/lib/reviewer.ts`: replace localStorage reviewer lookup with session identity or remove this file after updating imports.
- Modify `frontend/src/app/companies/[companyId]/periods/[periodId]/page.tsx`: remove manual reviewer panel and use authenticated identity.
- Modify `frontend/src/app/companies/[companyId]/periods/[periodId]/year-end/page.tsx`: remove caller-supplied `confirmedBy`.

---

### Task 1: Add Auth Session Configuration and Production Safety

**Files:**
- Create: `backend/Accounts.Api/Rules/AuthSessionConfig.cs`
- Modify: `backend/Accounts.Api/Program.cs`
- Modify: `backend/Accounts.Api/Services/ProductionSafetyService.cs`
- Test: `backend/Accounts.Tests/AccountsWorkflowTests.cs`

- [ ] **Step 1: Write failing production safety tests**

Append these tests near the existing `ProductionSafety_*` tests in `backend/Accounts.Tests/AccountsWorkflowTests.cs`:

```csharp
[Fact]
public void ProductionSafety_BlocksWeakSessionSigningKeyInProduction()
{
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
            ["AllowedOrigins:0"] = "https://accounts.example.ie",
            ["AuthSession:SigningKey"] = "short"
        })
        .Build();
    var service = new ProductionSafetyService(
        new TestEnvironment("Production"),
        config,
        Options.Create(new DatabaseStartupConfig
        {
            AutoMigrateOnStartup = false,
            SeedDemoData = false
        }),
        new ApiAccessService(
            Options.Create(new ApiAccessConfig
            {
                Enabled = true,
                RequireInProduction = true,
                Keys =
                [
                    new ApiAccessKeyConfig
                    {
                        Name = "Production firm",
                        KeyHash = ApiAccessService.HashKey("real-secret")
                    }
                ]
            }),
            new TestEnvironment("Production")));

    var failures = service.Validate();

    Assert.Contains(failures, f => f.Contains("AuthSession:SigningKey"));
}

[Fact]
public void ProductionSafety_AllowsStrongSessionSigningConfiguration()
{
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
            ["AllowedOrigins:0"] = "https://accounts.example.ie",
            ["AuthSession:SigningKey"] = new string('a', 64)
        })
        .Build();
    var service = new ProductionSafetyService(
        new TestEnvironment("Production"),
        config,
        Options.Create(new DatabaseStartupConfig
        {
            AutoMigrateOnStartup = false,
            SeedDemoData = false
        }),
        new ApiAccessService(
            Options.Create(new ApiAccessConfig
            {
                Enabled = true,
                RequireInProduction = true,
                Keys =
                [
                    new ApiAccessKeyConfig
                    {
                        Name = "Production firm",
                        KeyHash = ApiAccessService.HashKey("real-secret")
                    }
                ]
            }),
            new TestEnvironment("Production")));

    Assert.Empty(service.Validate());
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test --filter "ProductionSafety_BlocksWeakSessionSigningKeyInProduction|ProductionSafety_AllowsStrongSessionSigningConfiguration"
```

Expected: compilation fails because `ProductionSafetyService` does not yet validate `AuthSession:SigningKey`, or the weak-key test fails because no failure is returned.

- [ ] **Step 3: Add auth session config**

Create `backend/Accounts.Api/Rules/AuthSessionConfig.cs`:

```csharp
namespace Accounts.Api.Rules;

public class AuthSessionConfig
{
    public string CookieName { get; set; } = "accounts_session";
    public string SigningKey { get; set; } = "";
    public int ExpiryMinutes { get; set; } = 480;
    public bool SecureCookiesInProduction { get; set; } = true;
}
```

Modify `backend/Accounts.Api/Program.cs` near the existing config registrations:

```csharp
builder.Services.Configure<AuthSessionConfig>(builder.Configuration.GetSection("AuthSession"));
```

- [ ] **Step 4: Validate config in production safety**

Modify `backend/Accounts.Api/Services/ProductionSafetyService.cs` constructor to accept auth options:

```csharp
public class ProductionSafetyService(
    IHostEnvironment environment,
    IConfiguration configuration,
    IOptions<DatabaseStartupConfig> databaseStartup,
    IOptions<AuthSessionConfig> authSession,
    ApiAccessService apiAccess)
```

Add this block in `Validate()` before `failures.AddRange(apiAccess.ValidateConfiguration());`:

```csharp
var session = authSession.Value;
if (string.IsNullOrWhiteSpace(session.SigningKey) || session.SigningKey.Trim().Length < 32)
    failures.Add("AuthSession:SigningKey must be configured to at least 32 characters in production.");

if (session.ExpiryMinutes is < 15 or > 1440)
    failures.Add("AuthSession:ExpiryMinutes must be between 15 and 1440 minutes in production.");

if (!session.SecureCookiesInProduction)
    failures.Add("AuthSession:SecureCookiesInProduction must be true in production.");
```

Update existing `ProductionSafetyService` test constructors by adding:

```csharp
Options.Create(new AuthSessionConfig { SigningKey = new string('a', 64) }),
```

after each `Options.Create(new DatabaseStartupConfig` argument and before the `new ApiAccessService` argument. In `ProductionSafety_BlocksDemoDatabaseStartupInProduction`, pass `Options.Create(new AuthSessionConfig())` so the test also observes the missing signing key.

- [ ] **Step 5: Run tests to verify green**

Run:

```powershell
dotnet test --filter "ProductionSafety"
```

Expected: all production safety tests pass.

- [ ] **Step 6: Commit**

```powershell
git add backend/Accounts.Api/Rules/AuthSessionConfig.cs backend/Accounts.Api/Program.cs backend/Accounts.Api/Services/ProductionSafetyService.cs backend/Accounts.Tests/AccountsWorkflowTests.cs
git commit -m "feat: add auth session production guardrails"
```

---

### Task 2: Add Auth Service with Password Verification and Signed Sessions

**Files:**
- Create: `backend/Accounts.Api/Services/AuthService.cs`
- Create: `backend/Accounts.Api/Services/AuthContext.cs`
- Modify: `backend/Accounts.Api/Program.cs`
- Test: `backend/Accounts.Tests/AccountsWorkflowTests.cs`

- [ ] **Step 1: Write failing AuthService tests**

Append these helpers and tests to `backend/Accounts.Tests/AccountsWorkflowTests.cs`. Put the helper methods near the existing private helpers.

```csharp
[Fact]
public async Task AuthService_LoginAcceptsValidPasswordAndReturnsTenantPrincipal()
{
    await using var db = CreateDbContext();
    var tenant = await SeedTenantAsync(db, "firm-a", "Firm A");
    await SeedUserAsync(db, tenant.Id, "owner@example.ie", "Firm Owner", "Owner", "Correct!Password-2026-abcdef");
    var service = CreateAuthService(db);

    var result = await service.LoginAsync("OWNER@example.ie", "Correct!Password-2026-abcdef");

    Assert.True(result.Succeeded);
    Assert.NotNull(result.User);
    Assert.Equal(tenant.Id, result.User.TenantId);
    Assert.Equal("owner@example.ie", result.User.Email);
    Assert.Equal("Owner", result.User.Role);
}

[Fact]
public async Task AuthService_LoginRejectsWrongPassword()
{
    await using var db = CreateDbContext();
    var tenant = await SeedTenantAsync(db, "firm-a", "Firm A");
    await SeedUserAsync(db, tenant.Id, "owner@example.ie", "Firm Owner", "Owner", "Correct!Password-2026-abcdef");
    var service = CreateAuthService(db);

    var result = await service.LoginAsync("owner@example.ie", "Wrong!Password-2026-abcdef");

    Assert.False(result.Succeeded);
    Assert.Null(result.User);
}

[Fact]
public async Task AuthService_LoginRejectsInactiveUser()
{
    await using var db = CreateDbContext();
    var tenant = await SeedTenantAsync(db, "firm-a", "Firm A");
    var user = await SeedUserAsync(db, tenant.Id, "owner@example.ie", "Firm Owner", "Owner", "Correct!Password-2026-abcdef");
    user.IsActive = false;
    await db.SaveChangesAsync();
    var service = CreateAuthService(db);

    var result = await service.LoginAsync("owner@example.ie", "Correct!Password-2026-abcdef");

    Assert.False(result.Succeeded);
    Assert.Contains("inactive", result.FailureReason, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task AuthService_SessionRoundTripReturnsActiveUser()
{
    await using var db = CreateDbContext();
    var tenant = await SeedTenantAsync(db, "firm-a", "Firm A");
    var seeded = await SeedUserAsync(db, tenant.Id, "owner@example.ie", "Firm Owner", "Owner", "Correct!Password-2026-abcdef");
    var service = CreateAuthService(db);
    var principal = new AuthenticatedUser(seeded.Id, tenant.Id, tenant.Name, seeded.Email, seeded.DisplayName, seeded.Role);

    var cookie = service.CreateSessionCookieValue(principal, DateTimeOffset.UtcNow);
    var restored = await service.ReadSessionAsync(cookie, DateTimeOffset.UtcNow.AddMinutes(1));

    Assert.NotNull(restored);
    Assert.Equal(seeded.Id, restored.UserId);
    Assert.Equal(tenant.Id, restored.TenantId);
}

private static AuthService CreateAuthService(AccountsDbContext db) =>
    new(
        db,
        Options.Create(new AuthSessionConfig
        {
            SigningKey = new string('b', 64),
            ExpiryMinutes = 480
        }),
        new TestEnvironment("Development"));

private static async Task<Tenant> SeedTenantAsync(AccountsDbContext db, string slug, string name)
{
    var tenant = new Tenant { Slug = slug, Name = name };
    db.Tenants.Add(tenant);
    await db.SaveChangesAsync();
    return tenant;
}

private static async Task<UserAccount> SeedUserAsync(
    AccountsDbContext db,
    int tenantId,
    string email,
    string displayName,
    string role,
    string password)
{
    var salt = RandomNumberGenerator.GetBytes(32);
    var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
    var user = new UserAccount
    {
        TenantId = tenantId,
        Email = email.Trim().ToLowerInvariant(),
        DisplayName = displayName,
        Role = role,
        PasswordSalt = Convert.ToBase64String(salt),
        PasswordHash = Convert.ToBase64String(hash),
        PasswordAlgorithm = "PBKDF2-SHA256-210000",
        PasswordStrengthScore = 5,
        IsActive = true
    };
    db.UserAccounts.Add(user);
    await db.SaveChangesAsync();
    return user;
}
```

Add these `using` statements at the top of the test file:

```csharp
using System.Security.Cryptography;
using Accounts.Api.Rules;
```

- [ ] **Step 2: Run tests to verify RED**

Run:

```powershell
dotnet test --filter "AuthService_"
```

Expected: compile fails because `AuthService`, `AuthenticatedUser`, and `AuthSessionConfig` are not yet available to the tests, or the tests fail because behavior is missing.

- [ ] **Step 3: Add AuthContext helpers**

Create `backend/Accounts.Api/Services/AuthContext.cs`:

```csharp
namespace Accounts.Api.Services;

public static class AuthContext
{
    public const string ItemKey = "AuthenticatedUser";

    public static AuthenticatedUser? GetUser(HttpContext context) =>
        context.Items[ItemKey] as AuthenticatedUser;

    public static AuthenticatedUser RequireUser(HttpContext context) =>
        GetUser(context) ?? throw new InvalidOperationException("Authenticated user is not available on this request.");
}

public record AuthenticatedUser(
    int UserId,
    int TenantId,
    string TenantName,
    string Email,
    string DisplayName,
    string Role);
```

- [ ] **Step 4: Add AuthService**

Create `backend/Accounts.Api/Services/AuthService.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Services;

public class AuthService(AccountsDbContext db, IOptions<AuthSessionConfig> options, IHostEnvironment environment)
{
    public const string PasswordAlgorithm = "PBKDF2-SHA256-210000";
    private const int PasswordIterations = 210_000;
    private readonly AuthSessionConfig _config = options.Value;

    public string CookieName => string.IsNullOrWhiteSpace(_config.CookieName) ? "accounts_session" : _config.CookieName;

    public async Task<LoginResult> LoginAsync(string? email, string? password)
    {
        var normalizedEmail = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail) || string.IsNullOrEmpty(password))
            return LoginResult.Failed("Email and password are required.");

        var user = await db.UserAccounts
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

        if (user is null)
            return LoginResult.Failed("Invalid email or password.");
        if (!user.IsActive)
            return LoginResult.Failed("User account is inactive.");
        if (user.PasswordAlgorithm != PasswordAlgorithm)
            return LoginResult.Failed("User account password algorithm is not supported.");
        if (!VerifyPassword(user, password))
            return LoginResult.Failed("Invalid email or password.");

        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return LoginResult.Success(ToPrincipal(user));
    }

    public string CreateSessionCookieValue(AuthenticatedUser user, DateTimeOffset now)
    {
        var ticket = new AuthSessionTicket(
            user.UserId,
            user.TenantId,
            now.AddMinutes(Math.Clamp(_config.ExpiryMinutes, 15, 1440)));
        var payloadJson = JsonSerializer.Serialize(ticket);
        var payload = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signature = Sign(payload);
        return $"{payload}.{signature}";
    }

    public async Task<AuthenticatedUser?> ReadSessionAsync(string? cookieValue, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(cookieValue))
            return null;

        var parts = cookieValue.Split('.', 2);
        if (parts.Length != 2 || !FixedTimeEquals(Sign(parts[0]), parts[1]))
            return null;

        AuthSessionTicket? ticket;
        try
        {
            var json = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(parts[0]));
            ticket = JsonSerializer.Deserialize<AuthSessionTicket>(json);
        }
        catch
        {
            return null;
        }

        if (ticket is null || ticket.ExpiresAtUtc <= now)
            return null;

        var user = await db.UserAccounts
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Id == ticket.UserId && u.TenantId == ticket.TenantId);

        if (user is null || !user.IsActive || user.PasswordAlgorithm != PasswordAlgorithm)
            return null;

        return ToPrincipal(user);
    }

    public CookieOptions CreateCookieOptions(DateTimeOffset now) => new()
    {
        HttpOnly = true,
        Secure = environment.IsProduction(),
        SameSite = SameSiteMode.Lax,
        Expires = now.AddMinutes(Math.Clamp(_config.ExpiryMinutes, 15, 1440)),
        Path = "/"
    };

    public CookieOptions ClearCookieOptions() => new()
    {
        HttpOnly = true,
        Secure = environment.IsProduction(),
        SameSite = SameSiteMode.Lax,
        Expires = DateTimeOffset.UnixEpoch,
        Path = "/"
    };

    private bool VerifyPassword(UserAccount user, string password)
    {
        try
        {
            var salt = Convert.FromBase64String(user.PasswordSalt);
            var expected = Convert.FromBase64String(user.PasswordHash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordIterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    private AuthenticatedUser ToPrincipal(UserAccount user) =>
        new(user.Id, user.TenantId, user.Tenant.Name, user.Email.Trim().ToLowerInvariant(), user.DisplayName, user.Role);

    private string Sign(string payload)
    {
        if (string.IsNullOrWhiteSpace(_config.SigningKey))
            throw new InvalidOperationException("AuthSession:SigningKey is not configured.");

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_config.SigningKey));
        return WebEncoders.Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private sealed record AuthSessionTicket(int UserId, int TenantId, DateTimeOffset ExpiresAtUtc);
}

public record LoginResult(bool Succeeded, AuthenticatedUser? User, string? FailureReason)
{
    public static LoginResult Success(AuthenticatedUser user) => new(true, user, null);
    public static LoginResult Failed(string reason) => new(false, null, reason);
}
```

- [ ] **Step 5: Register AuthService**

Modify `backend/Accounts.Api/Program.cs` service registrations:

```csharp
builder.Services.AddScoped<AuthService>();
```

- [ ] **Step 6: Run tests to verify GREEN**

Run:

```powershell
dotnet test --filter "AuthService_"
```

Expected: all `AuthService_` tests pass.

- [ ] **Step 7: Commit**

```powershell
git add backend/Accounts.Api/Services/AuthService.cs backend/Accounts.Api/Services/AuthContext.cs backend/Accounts.Api/Program.cs backend/Accounts.Tests/AccountsWorkflowTests.cs
git commit -m "feat: add firm user auth service"
```

---

### Task 3: Add Auth Endpoints and User Session Middleware

**Files:**
- Create: `backend/Accounts.Api/Endpoints/AuthEndpoints.cs`
- Create: `backend/Accounts.Api/Middleware/UserSessionMiddleware.cs`
- Modify: `backend/Accounts.Api/Endpoints/EndpointRegistration.cs`
- Modify: `backend/Accounts.Api/Program.cs`
- Test: `backend/Accounts.Tests/AccountsWorkflowTests.cs`

- [ ] **Step 1: Write failing middleware tests**

Append these tests:

```csharp
[Fact]
public async Task UserSessionMiddleware_BlocksProtectedApiWithoutSession()
{
    await using var db = CreateDbContext();
    var middleware = new UserSessionMiddleware(_ => Task.CompletedTask);
    var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
    context.Request.Path = "/api/companies";

    await middleware.InvokeAsync(context, CreateAuthService(db));

    Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
}

[Fact]
public async Task UserSessionMiddleware_AllowsLoginWithoutSession()
{
    await using var db = CreateDbContext();
    var nextCalled = false;
    var middleware = new UserSessionMiddleware(_ =>
    {
        nextCalled = true;
        return Task.CompletedTask;
    });
    var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
    context.Request.Method = HttpMethods.Post;
    context.Request.Path = "/api/auth/login";

    await middleware.InvokeAsync(context, CreateAuthService(db));

    Assert.True(nextCalled);
}

[Fact]
public async Task UserSessionMiddleware_LoadsPrincipalFromValidSession()
{
    await using var db = CreateDbContext();
    var tenant = await SeedTenantAsync(db, "firm-a", "Firm A");
    var user = await SeedUserAsync(db, tenant.Id, "owner@example.ie", "Firm Owner", "Owner", "Correct!Password-2026-abcdef");
    var auth = CreateAuthService(db);
    var principal = new AuthenticatedUser(user.Id, tenant.Id, tenant.Name, user.Email, user.DisplayName, user.Role);
    var cookie = auth.CreateSessionCookieValue(principal, DateTimeOffset.UtcNow);
    var nextCalled = false;
    var middleware = new UserSessionMiddleware(_ =>
    {
        nextCalled = true;
        return Task.CompletedTask;
    });
    var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
    context.Request.Path = "/api/companies";
    context.Request.Cookies = new RequestCookieCollection(new Dictionary<string, string>
    {
        [auth.CookieName] = cookie
    });

    await middleware.InvokeAsync(context, auth);

    Assert.True(nextCalled);
    Assert.Equal(user.Id, AuthContext.RequireUser(context).UserId);
}
```

- [ ] **Step 2: Run tests to verify RED**

Run:

```powershell
dotnet test --filter "UserSessionMiddleware_"
```

Expected: compile fails because `UserSessionMiddleware` does not exist.

- [ ] **Step 3: Add UserSessionMiddleware**

Create `backend/Accounts.Api/Middleware/UserSessionMiddleware.cs`:

```csharp
using Accounts.Api.Services;

namespace Accounts.Api.Middleware;

public class UserSessionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AuthService auth)
    {
        if (!context.Request.Path.StartsWithSegments("/api") || AllowsAnonymous(context.Request))
        {
            await next(context);
            return;
        }

        var user = await auth.ReadSessionAsync(context.Request.Cookies[auth.CookieName], DateTimeOffset.UtcNow);
        if (user is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "Authentication required." });
            return;
        }

        context.Items[AuthContext.ItemKey] = user;
        await next(context);
    }

    private static bool AllowsAnonymous(HttpRequest request) =>
        request.Path.StartsWithSegments("/api/auth/login")
        || request.Path.StartsWithSegments("/health");
}
```

- [ ] **Step 4: Add auth endpoints**

Create `backend/Accounts.Api/Endpoints/AuthEndpoints.cs`:

```csharp
using Accounts.Api.Services;

namespace Accounts.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Authentication");

        group.MapPost("/login", async (LoginInput input, HttpContext context, AuthService auth) =>
        {
            var result = await auth.LoginAsync(input.Email, input.Password);
            if (!result.Succeeded || result.User is null)
                return Results.Unauthorized();

            var now = DateTimeOffset.UtcNow;
            context.Response.Cookies.Append(
                auth.CookieName,
                auth.CreateSessionCookieValue(result.User, now),
                auth.CreateCookieOptions(now));

            return Results.Ok(AuthResponse.From(result.User));
        });

        group.MapPost("/logout", (HttpContext context, AuthService auth) =>
        {
            context.Response.Cookies.Delete(auth.CookieName, auth.ClearCookieOptions());
            return Results.NoContent();
        });

        group.MapGet("/me", (HttpContext context) =>
            Results.Ok(AuthResponse.From(AuthContext.RequireUser(context))));
    }
}

public record LoginInput(string? Email, string? Password);

public record AuthResponse(
    int userId,
    int tenantId,
    string tenantName,
    string email,
    string displayName,
    string role)
{
    public static AuthResponse From(AuthenticatedUser user) =>
        new(user.UserId, user.TenantId, user.TenantName, user.Email, user.DisplayName, user.Role);
}
```

Modify `backend/Accounts.Api/Endpoints/EndpointRegistration.cs`:

```csharp
public static void MapAllEndpoints(this WebApplication app)
{
    app.MapAuthEndpoints();
    app.MapClassificationEndpoints();
    app.MapBankingEndpoints();
    app.MapYearEndEndpoints();
    app.MapAdjustmentEndpoints();
    app.MapStatementsEndpoints();
    app.MapDocumentEndpoints();
    app.MapRevenueEndpoints();
    app.MapDeadlineEndpoints();
    app.MapCharityEndpoints();
    app.MapFilingWorkflowEndpoints();
}
```

- [ ] **Step 5: Wire middleware in Program**

Modify middleware order in `backend/Accounts.Api/Program.cs`:

```csharp
app.UseMiddleware<Accounts.Api.Middleware.SecurityHeadersMiddleware>();
app.UseRateLimiter();
app.UseCors();
app.UseMiddleware<Accounts.Api.Middleware.ApiAccessMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.UserSessionMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.ExceptionMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.PeriodOwnershipMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.PeriodLockMiddleware>();
```

- [ ] **Step 6: Run tests to verify GREEN**

Run:

```powershell
dotnet test --filter "UserSessionMiddleware_"
```

Expected: all user-session middleware tests pass.

- [ ] **Step 7: Commit**

```powershell
git add backend/Accounts.Api/Endpoints/AuthEndpoints.cs backend/Accounts.Api/Middleware/UserSessionMiddleware.cs backend/Accounts.Api/Endpoints/EndpointRegistration.cs backend/Accounts.Api/Program.cs backend/Accounts.Tests/AccountsWorkflowTests.cs
git commit -m "feat: require signed user sessions"
```

---

### Task 4: Enforce Tenant Isolation for Company Routes

**Files:**
- Create: `backend/Accounts.Api/Middleware/TenantAccessMiddleware.cs`
- Modify: `backend/Accounts.Api/Program.cs`
- Test: `backend/Accounts.Tests/AccountsWorkflowTests.cs`

- [ ] **Step 1: Write failing tenant middleware and query tests**

Append:

```csharp
[Fact]
public async Task TenantAccessMiddleware_HidesCrossTenantCompany()
{
    await using var db = CreateDbContext();
    var tenantA = await SeedTenantAsync(db, "firm-a", "Firm A");
    var tenantB = await SeedTenantAsync(db, "firm-b", "Firm B");
    var companyB = await SeedTenantCompanyAsync(db, tenantB.Id, "Tenant B Limited");
    var nextCalled = false;
    var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
    context.Request.Path = $"/api/companies/{companyB.Id}";
    context.Items[AuthContext.ItemKey] = new AuthenticatedUser(1, tenantA.Id, tenantA.Name, "owner@a.ie", "Owner A", "Owner");
    var middleware = new TenantAccessMiddleware(_ =>
    {
        nextCalled = true;
        return Task.CompletedTask;
    });

    await middleware.InvokeAsync(context, db);

    Assert.False(nextCalled);
    Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
}

[Fact]
public async Task TenantAccessMiddleware_AllowsOwnTenantCompany()
{
    await using var db = CreateDbContext();
    var tenantA = await SeedTenantAsync(db, "firm-a", "Firm A");
    var companyA = await SeedTenantCompanyAsync(db, tenantA.Id, "Tenant A Limited");
    var nextCalled = false;
    var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
    context.Request.Path = $"/api/companies/{companyA.Id}";
    context.Items[AuthContext.ItemKey] = new AuthenticatedUser(1, tenantA.Id, tenantA.Name, "owner@a.ie", "Owner A", "Owner");
    var middleware = new TenantAccessMiddleware(_ =>
    {
        nextCalled = true;
        return Task.CompletedTask;
    });

    await middleware.InvokeAsync(context, db);

    Assert.True(nextCalled);
}

private static async Task<Company> SeedTenantCompanyAsync(AccountsDbContext db, int tenantId, string legalName)
{
    var company = new Company
    {
        TenantId = tenantId,
        LegalName = legalName,
        CroNumber = $"{tenantId}{legalName.Length}",
        CompanyType = CompanyType.Private,
        IncorporationDate = new DateOnly(2025, 1, 1),
        ArdMonth = 9,
        IsTrading = true
    };
    db.Companies.Add(company);
    await db.SaveChangesAsync();
    return company;
}
```

- [ ] **Step 2: Run tests to verify RED**

Run:

```powershell
dotnet test --filter "TenantAccessMiddleware_"
```

Expected: compile fails because `TenantAccessMiddleware` does not exist.

- [ ] **Step 3: Add tenant access middleware**

Create `backend/Accounts.Api/Middleware/TenantAccessMiddleware.cs`:

```csharp
using Accounts.Api.Data;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Middleware;

public class TenantAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AccountsDbContext db)
    {
        if (!TryGetCompanyId(context.Request.Path, out var companyId))
        {
            await next(context);
            return;
        }

        var user = AuthContext.RequireUser(context);
        var allowed = await db.Companies
            .AsNoTracking()
            .AnyAsync(c => c.Id == companyId && c.TenantId == user.TenantId);

        if (!allowed)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "Company not found." });
            return;
        }

        await next(context);
    }

    public static bool TryGetCompanyId(PathString path, out int companyId)
    {
        companyId = 0;
        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments is not { Length: >= 3 })
            return false;

        return segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("companies", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(segments[2], out companyId);
    }
}
```

- [ ] **Step 4: Wire tenant middleware**

Modify `backend/Accounts.Api/Program.cs` middleware order:

```csharp
app.UseMiddleware<Accounts.Api.Middleware.ApiAccessMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.UserSessionMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.TenantAccessMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.ExceptionMiddleware>();
```

- [ ] **Step 5: Tenant-scope company endpoints**

Modify company endpoints in `backend/Accounts.Api/Program.cs`:

```csharp
companies.MapGet("/", async (HttpContext context, AccountsDbContext db) =>
{
    var user = AuthContext.RequireUser(context);
    var query = db.Companies.Where(c => c.TenantId == user.TenantId);
    return await query.Select(c => new
    {
        c.Id, c.LegalName, c.TradingName, c.CroNumber, c.CompanyType,
        c.IsTrading, c.IsDormant, c.CreatedAt,
        PeriodCount = c.Periods.Count
    }).ToListAsync();
});

companies.MapGet("/{id:int}", async (int id, HttpContext context, AccountsDbContext db) =>
{
    var user = AuthContext.RequireUser(context);
    return await db.Companies
            .Include(c => c.Officers)
            .Include(c => c.Periods)
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == user.TenantId)
        is { } company ? Results.Ok(company) : Results.NotFound();
});

companies.MapPost("/", async (CompanyInput input, HttpContext context, AccountsDbContext db) =>
{
    if (EndpointInputs.ValidateCompany(input) is { } validationProblem)
        return validationProblem;

    var user = AuthContext.RequireUser(context);
    var company = EndpointInputs.ToCompany(input);
    company.TenantId = user.TenantId;
    db.Companies.Add(company);
    await db.SaveChangesAsync();
    return Results.Created($"/api/companies/{company.Id}", company);
});
```

Also update company PUT and DELETE queries so both include `TenantId == user.TenantId`.

- [ ] **Step 6: Run tenant tests**

Run:

```powershell
dotnet test --filter "TenantAccessMiddleware_"
```

Expected: both tenant middleware tests pass.

- [ ] **Step 7: Run full backend tests**

Run:

```powershell
dotnet test
```

Expected: all backend tests pass.

- [ ] **Step 8: Commit**

```powershell
git add backend/Accounts.Api/Middleware/TenantAccessMiddleware.cs backend/Accounts.Api/Program.cs backend/Accounts.Tests/AccountsWorkflowTests.cs
git commit -m "feat: enforce tenant-scoped company access"
```

---

### Task 5: Add Central Role Authorization

**Files:**
- Create: `backend/Accounts.Api/Services/RoleAuthorizationService.cs`
- Create: `backend/Accounts.Api/Middleware/RoleAuthorizationMiddleware.cs`
- Modify: `backend/Accounts.Api/Program.cs`
- Test: `backend/Accounts.Tests/AccountsWorkflowTests.cs`

- [ ] **Step 1: Write failing role tests**

Append:

```csharp
[Theory]
[InlineData("Client", "POST", "/api/companies/1/periods", false)]
[InlineData("Accountant", "POST", "/api/companies/1/periods", true)]
[InlineData("Reviewer", "POST", "/api/companies/1/periods/2/adjustments/3/approve", true)]
[InlineData("Reviewer", "POST", "/api/companies", false)]
[InlineData("Owner", "DELETE", "/api/companies/1", true)]
public void RoleAuthorization_EnforcesExpectedWritePermissions(string role, string method, string path, bool expected)
{
    var user = new AuthenticatedUser(1, 1, "Firm A", "user@example.ie", "User", role);

    var decision = RoleAuthorizationService.Authorize(user, new PathString(path), method);

    Assert.Equal(expected, decision.IsAllowed);
}

[Fact]
public async Task RoleAuthorizationMiddleware_ReturnsForbiddenForDeniedWrite()
{
    var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
    context.Request.Method = HttpMethods.Post;
    context.Request.Path = "/api/companies/1/periods";
    context.Items[AuthContext.ItemKey] = new AuthenticatedUser(1, 1, "Firm A", "client@example.ie", "Client", "Client");
    var nextCalled = false;
    var middleware = new RoleAuthorizationMiddleware(_ =>
    {
        nextCalled = true;
        return Task.CompletedTask;
    });

    await middleware.InvokeAsync(context);

    Assert.False(nextCalled);
    Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
}
```

- [ ] **Step 2: Run tests to verify RED**

Run:

```powershell
dotnet test --filter "RoleAuthorization"
```

Expected: compile fails because `RoleAuthorizationService` and middleware do not exist.

- [ ] **Step 3: Add role authorization service**

Create `backend/Accounts.Api/Services/RoleAuthorizationService.cs`:

```csharp
namespace Accounts.Api.Services;

public static class RoleAuthorizationService
{
    public static RoleAuthorizationDecision Authorize(AuthenticatedUser user, PathString path, string method)
    {
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
            return RoleAuthorizationDecision.Allowed();

        var role = user.Role.Trim();
        if (role.Equals("Owner", StringComparison.OrdinalIgnoreCase))
            return RoleAuthorizationDecision.Allowed();

        if (role.Equals("Client", StringComparison.OrdinalIgnoreCase))
            return RoleAuthorizationDecision.Denied("Client users are read-only.");

        if (role.Equals("Reviewer", StringComparison.OrdinalIgnoreCase))
            return IsReviewerWrite(path)
                ? RoleAuthorizationDecision.Allowed()
                : RoleAuthorizationDecision.Denied("Reviewer users can approve, review, finalise, and update filing workflow only.");

        if (role.Equals("Accountant", StringComparison.OrdinalIgnoreCase))
        {
            if (IsReviewerWrite(path) || IsCompanyDelete(path, method))
                return RoleAuthorizationDecision.Denied("Accountant users cannot perform reviewer or owner-only actions.");

            return RoleAuthorizationDecision.Allowed();
        }

        return RoleAuthorizationDecision.Denied("User role is not authorised.");
    }

    private static bool IsReviewerWrite(PathString path)
    {
        var value = path.Value ?? "";
        return value.Contains("/approve", StringComparison.OrdinalIgnoreCase)
            || value.Contains("/year-end-reviews", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/status", StringComparison.OrdinalIgnoreCase)
            || value.Contains("/filing/cro-status", StringComparison.OrdinalIgnoreCase)
            || value.Contains("/filing/cro-payment", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompanyDelete(PathString path, string method) =>
        HttpMethods.IsDelete(method)
        && System.Text.RegularExpressions.Regex.IsMatch(path.Value ?? "", @"^/api/companies/\d+/?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}

public record RoleAuthorizationDecision(bool IsAllowed, string? DenialReason)
{
    public static RoleAuthorizationDecision Allowed() => new(true, null);
    public static RoleAuthorizationDecision Denied(string reason) => new(false, reason);
}
```

- [ ] **Step 4: Add role middleware**

Create `backend/Accounts.Api/Middleware/RoleAuthorizationMiddleware.cs`:

```csharp
using Accounts.Api.Services;

namespace Accounts.Api.Middleware;

public class RoleAuthorizationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/api/auth/login"))
        {
            await next(context);
            return;
        }

        var user = AuthContext.GetUser(context);
        if (user is null)
        {
            await next(context);
            return;
        }

        var decision = RoleAuthorizationService.Authorize(user, context.Request.Path, context.Request.Method);
        if (!decision.IsAllowed)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = decision.DenialReason });
            return;
        }

        await next(context);
    }
}
```

- [ ] **Step 5: Wire role middleware**

Modify `backend/Accounts.Api/Program.cs`:

```csharp
app.UseMiddleware<Accounts.Api.Middleware.UserSessionMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.TenantAccessMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.RoleAuthorizationMiddleware>();
app.UseMiddleware<Accounts.Api.Middleware.ExceptionMiddleware>();
```

- [ ] **Step 6: Run tests to verify GREEN**

Run:

```powershell
dotnet test --filter "RoleAuthorization"
```

Expected: role authorization tests pass.

- [ ] **Step 7: Commit**

```powershell
git add backend/Accounts.Api/Services/RoleAuthorizationService.cs backend/Accounts.Api/Middleware/RoleAuthorizationMiddleware.cs backend/Accounts.Api/Program.cs backend/Accounts.Tests/AccountsWorkflowTests.cs
git commit -m "feat: enforce firm user roles"
```

---

### Task 6: Use Authenticated Identity for Review, Approval, Filing, and Audit

**Files:**
- Create: `backend/Accounts.Api/Services/AuthenticatedIdentity.cs`
- Modify: `backend/Accounts.Api/Program.cs`
- Modify: `backend/Accounts.Api/Endpoints/AdjustmentEndpoints.cs`
- Modify: `backend/Accounts.Api/Endpoints/FilingWorkflowEndpoints.cs`
- Modify: `backend/Accounts.Api/Endpoints/YearEndEndpoints.cs`
- Modify: `backend/Accounts.Api/Endpoints/BankingEndpoints.cs`
- Test: `backend/Accounts.Tests/AccountsWorkflowTests.cs`

- [ ] **Step 1: Write failing identity helper tests**

Append:

```csharp
[Fact]
public void AuthenticatedIdentity_UsesPrincipalForReviewerDisplayName()
{
    var reviewer = new AuthenticatedUser(7, 1, "Firm A", "reviewer@example.ie", "Maeve Reviewer", "Reviewer");

    var displayName = AuthenticatedIdentity.ReviewerDisplayName(reviewer);

    Assert.Equal("Maeve Reviewer", displayName);
}

[Fact]
public void AuthenticatedIdentity_UsesPrincipalEmailForAuditUserId()
{
    var reviewer = new AuthenticatedUser(7, 1, "Firm A", "Reviewer@Example.IE", "Maeve Reviewer", "Reviewer");

    var auditUserId = AuthenticatedIdentity.AuditUserId(reviewer);

    Assert.Equal("reviewer@example.ie", auditUserId);
}
```

- [ ] **Step 2: Run tests to verify RED**

Run:

```powershell
dotnet test --filter "AuthenticatedIdentity_"
```

Expected: compile fails because `AuthenticatedIdentity` does not exist.

- [ ] **Step 3: Add authenticated identity helper**

Create `backend/Accounts.Api/Services/AuthenticatedIdentity.cs`:

```csharp
namespace Accounts.Api.Services;

public static class AuthenticatedIdentity
{
    public static string ReviewerDisplayName(AuthenticatedUser user) =>
        string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName.Trim();

    public static string AuditUserId(AuthenticatedUser user) =>
        user.Email.Trim().ToLowerInvariant();
}
```

- [ ] **Step 4: Run helper tests to verify GREEN**

Run:

```powershell
dotnet test --filter "AuthenticatedIdentity_"
```

Expected: both authenticated identity helper tests pass.

- [ ] **Step 5: Update period status identity**

Modify `backend/Accounts.Api/Program.cs` period status handler:

```csharp
periods.MapPut("/{id:int}/status", async (int companyId, int id, PeriodStatusUpdate update, HttpContext context, AccountsDbContext db) =>
{
    var user = AuthContext.RequireUser(context);
    var period = await db.AccountingPeriods.FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId);
    if (period is null) return Results.NotFound();

    if (EndpointInputs.ValidatePeriodStatusUpdate(period, update) is { } validationProblem)
        return validationProblem;

    var wasLocked = period.Status is Accounts.Api.Entities.PeriodStatus.Finalised or Accounts.Api.Entities.PeriodStatus.Filed || period.LockedAt is not null;
    period.Status = update.Status;
    if (update.Status is Accounts.Api.Entities.PeriodStatus.Finalised or Accounts.Api.Entities.PeriodStatus.Filed)
    {
        period.LockedAt ??= DateTime.UtcNow;
        period.LockedBy = AuthenticatedIdentity.ReviewerDisplayName(user);
    }
    else if (wasLocked)
    {
        period.LockedAt = null;
        period.LockedBy = null;
    }
    await db.SaveChangesAsync();
    return Results.Ok(period);
});
```

Adjust `ValidatePeriodStatusUpdate` so it no longer requires `update.LockedBy` for locking. Keep the reopen reason requirement.

- [ ] **Step 6: Update adjustment identity**

Modify `backend/Accounts.Api/Endpoints/AdjustmentEndpoints.cs` approval endpoint:

```csharp
group.MapPost("/{id:int}/approve", async (int companyId, int periodId, int id, ApprovalInput input, HttpContext context, AccountsDbContext db, AuditService audit) =>
{
    var user = AuthContext.RequireUser(context);
    var item = await db.Adjustments.FirstOrDefaultAsync(a => a.Id == id && a.PeriodId == periodId);
    if (item == null) return Results.NotFound();
    item.ApprovedBy = AuthenticatedIdentity.ReviewerDisplayName(user);
    item.ApprovedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    await audit.LogAsync(companyId, periodId, "Adjustment", id, "Approved", newValue: new { item.Description, item.ApprovedBy }, userId: AuthenticatedIdentity.AuditUserId(user));
    return Results.Ok(item);
});
```

Also update create manual adjustment audit log:

```csharp
var user = AuthContext.RequireUser(context);
await audit.LogAsync(companyId, periodId, "Adjustment", input.Id, "Created", newValue: new { input.Description, input.Amount }, userId: AuthenticatedIdentity.AuditUserId(user));
```

- [ ] **Step 7: Update year-end review identity**

Modify `backend/Accounts.Api/Endpoints/YearEndEndpoints.cs` review confirmation endpoint:

```csharp
reviews.MapPut("/{sectionKey}", async (int companyId, int periodId, string sectionKey, YearEndReviewInput input, HttpContext context, AccountsDbContext db) =>
{
    var user = AuthContext.RequireUser(context);
    if (!ReviewSectionKeys.Contains(sectionKey))
        return Results.BadRequest(new { error = "Unknown year-end review section." });

    var periodExists = await db.AccountingPeriods.AnyAsync(p => p.Id == periodId && p.CompanyId == companyId);
    if (!periodExists) return Results.NotFound();

    var confirmation = await db.YearEndReviewConfirmations
        .FirstOrDefaultAsync(r => r.PeriodId == periodId && r.SectionKey == sectionKey);

    if (confirmation == null)
    {
        confirmation = new YearEndReviewConfirmation
        {
            PeriodId = periodId,
            SectionKey = sectionKey
        };
        db.YearEndReviewConfirmations.Add(confirmation);
    }

    confirmation.Confirmed = input.Confirmed;
    confirmation.ConfirmedBy = AuthenticatedIdentity.ReviewerDisplayName(user);
    confirmation.ConfirmedAt = DateTime.UtcNow;
    confirmation.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();

    await db.SaveChangesAsync();
    return Results.Ok(confirmation);
});
```

Keep `YearEndReviewInput` shape for compatibility, but ignore `ConfirmedBy`.

- [ ] **Step 8: Update filing workflow identity**

Modify `backend/Accounts.Api/Endpoints/FilingWorkflowEndpoints.cs`:

```csharp
group.MapPut("/cro-status", async (int companyId, int periodId, FilingStatusInput input, HttpContext context, FilingWorkflowService service) =>
{
    try
    {
        var user = AuthContext.RequireUser(context);
        var result = await service.UpdateCroStatusAsync(companyId, periodId, input.Status, AuthenticatedIdentity.ReviewerDisplayName(user), input.Reason, input.SubmissionReference);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

group.MapPost("/cro-payment", async (int companyId, int periodId, CroPaymentInput input, HttpContext context, FilingWorkflowService service) =>
{
    try
    {
        var user = AuthContext.RequireUser(context);
        var result = await service.ConfirmCroPaymentAsync(companyId, periodId, AuthenticatedIdentity.ReviewerDisplayName(user));
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});
```

- [ ] **Step 9: Update import audit identity**

Modify `backend/Accounts.Api/Endpoints/BankingEndpoints.cs` import endpoint to accept `HttpContext context` and use:

```csharp
var user = AuthContext.RequireUser(context.HttpContext);
```

If using `HttpRequest request`, access the context through `request.HttpContext`:

```csharp
var user = AuthContext.RequireUser(request.HttpContext);
await auditService.LogAsync(companyId, periodId, "ImportBatch", bankAccountId, "BankCsvImported", null, new
{
    file.FileName,
    file.Length,
    result.TotalRows,
    result.ImportedRows,
    result.DuplicatesSkipped,
    result.AutoCategorised,
    WarningCount = result.Warnings.Count
}, AuthenticatedIdentity.AuditUserId(user));
```

- [ ] **Step 10: Run backend tests**

Run:

```powershell
dotnet test
```

Expected: all backend tests pass.

- [ ] **Step 11: Commit**

```powershell
git add backend/Accounts.Api/Services/AuthenticatedIdentity.cs backend/Accounts.Api/Program.cs backend/Accounts.Api/Endpoints/AdjustmentEndpoints.cs backend/Accounts.Api/Endpoints/FilingWorkflowEndpoints.cs backend/Accounts.Api/Endpoints/YearEndEndpoints.cs backend/Accounts.Api/Endpoints/BankingEndpoints.cs backend/Accounts.Tests/AccountsWorkflowTests.cs
git commit -m "feat: stamp authenticated reviewer identity"
```

---

### Task 7: Add Frontend Auth Client, Provider, Login Page, and Navbar Session UI

**Files:**
- Create: `frontend/src/lib/auth.ts`
- Create: `frontend/src/components/AuthProvider.tsx`
- Create: `frontend/src/app/login/page.tsx`
- Modify: `frontend/src/app/providers.tsx`
- Modify: `frontend/src/components/AppNavbar.tsx`
- Modify: `frontend/src/lib/api.ts`

- [ ] **Step 1: Add API credentials support first**

Modify `frontend/src/lib/api.ts` inside `apiFetch` fetch call:

```typescript
const res = await fetch(`${API_BASE}${path}`, {
  headers: {
    "Content-Type": "application/json",
    ...fetchOptions?.headers,
  },
  credentials: "include",
  signal: controller.signal,
  ...fetchOptions,
});
```

Make sure `credentials: "include"` appears after spreading `fetchOptions` only if `fetchOptions` cannot override it. The final code should keep credentials enabled for all API calls.

- [ ] **Step 2: Create auth client**

Create `frontend/src/lib/auth.ts`:

```typescript
import { ApiError } from "@/lib/api";

export interface AuthUser {
  userId: number;
  tenantId: number;
  tenantName: string;
  email: string;
  displayName: string;
  role: "Owner" | "Accountant" | "Reviewer" | "Client" | string;
}

async function authFetch<T>(path: string, options?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
      ...options?.headers,
    },
    ...options,
  });

  if (!response.ok) {
    const body = await response.text().catch(() => "");
    throw new ApiError(response.status, response.statusText, body);
  }

  if (response.status === 204) return undefined as T;
  return response.json();
}

export function login(email: string, password: string) {
  return authFetch<AuthUser>("/api/auth/login", {
    method: "POST",
    body: JSON.stringify({ email, password }),
  });
}

export function logout() {
  return authFetch<void>("/api/auth/logout", { method: "POST" });
}

export function getCurrentUser() {
  return authFetch<AuthUser>("/api/auth/me");
}
```

- [ ] **Step 3: Create auth provider**

Create `frontend/src/components/AuthProvider.tsx`:

```tsx
"use client";

import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { usePathname, useRouter } from "next/navigation";
import { Spinner } from "@heroui/react";
import { getCurrentUser, login as loginRequest, logout as logoutRequest, type AuthUser } from "@/lib/auth";

interface AuthContextValue {
  user: AuthUser | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  canWriteWorkingPapers: boolean;
  canReview: boolean;
  isOwner: boolean;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const [user, setUser] = useState<AuthUser | null>(null);
  const [loading, setLoading] = useState(true);
  const isLogin = pathname === "/login";

  useEffect(() => {
    let mounted = true;
    getCurrentUser()
      .then((current) => {
        if (mounted) setUser(current);
      })
      .catch(() => {
        if (mounted) setUser(null);
      })
      .finally(() => {
        if (mounted) setLoading(false);
      });
    return () => {
      mounted = false;
    };
  }, []);

  useEffect(() => {
    if (!loading && !user && !isLogin) router.replace("/login");
    if (!loading && user && isLogin) router.replace("/");
  }, [loading, user, isLogin, router]);

  const value = useMemo<AuthContextValue>(() => {
    const role = user?.role ?? "";
    const isOwner = role === "Owner";
    const canWriteWorkingPapers = isOwner || role === "Accountant";
    const canReview = isOwner || role === "Reviewer";
    return {
      user,
      loading,
      login: async (email, password) => {
        const loggedIn = await loginRequest(email, password);
        setUser(loggedIn);
        router.replace("/");
      },
      logout: async () => {
        await logoutRequest();
        setUser(null);
        router.replace("/login");
      },
      canWriteWorkingPapers,
      canReview,
      isOwner,
    };
  }, [loading, router, user]);

  if (loading && !isLogin) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-[var(--background)]">
        <Spinner size="sm" />
      </div>
    );
  }

  if (!user && !isLogin) return null;

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const value = useContext(AuthContext);
  if (!value) throw new Error("useAuth must be used inside AuthProvider");
  return value;
}
```

- [ ] **Step 4: Wrap providers**

Modify `frontend/src/app/providers.tsx`:

```tsx
import { AuthProvider } from "@/components/AuthProvider";
```

Wrap existing content:

```tsx
<RouterProvider navigate={(path) => router.push(path)}>
  <AuthProvider>
    <ErrorBoundary>
      {children}
    </ErrorBoundary>
    <Toaster
      position="bottom-right"
      richColors
      closeButton
      toastOptions={{
        duration: 4000,
        className: "text-sm",
      }}
    />
  </AuthProvider>
</RouterProvider>
```

- [ ] **Step 5: Add login page**

Create `frontend/src/app/login/page.tsx`:

```tsx
"use client";

import { FormEvent, useState } from "react";
import { Button, Card, CardContent, TextField } from "@heroui/react";
import { Building2, LogIn } from "lucide-react";
import { useAuth } from "@/components/AuthProvider";

export default function LoginPage() {
  const { login } = useAuth();
  const [email, setEmail] = useState("owner@accounts-demo.ie");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      await login(email, password);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Sign in failed.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="mx-auto flex min-h-[70vh] max-w-md flex-col justify-center">
      <div className="mb-6 flex items-center gap-3">
        <div className="rounded-md bg-emerald-50 p-2 dark:bg-emerald-900/30">
          <Building2 className="h-6 w-6 text-emerald-600 dark:text-emerald-400" />
        </div>
        <div>
          <h1 className="text-xl font-semibold text-gray-950 dark:text-gray-50">Irish Accounts</h1>
          <p className="text-sm text-gray-500 dark:text-gray-400">Sign in to your firm workspace</p>
        </div>
      </div>
      <Card className="border border-gray-200 bg-white shadow-sm dark:border-neutral-800 dark:bg-neutral-950">
        <CardContent className="p-5">
          <form className="space-y-4" onSubmit={handleSubmit}>
            <TextField
              label="Email"
              type="email"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              isRequired
            />
            <TextField
              label="Password"
              type="password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              isRequired
            />
            {error && (
              <div className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700 dark:border-red-900/60 dark:bg-red-950/30 dark:text-red-300">
                {error}
              </div>
            )}
            <Button type="submit" variant="primary" isLoading={submitting} className="w-full">
              <LogIn className="h-4 w-4" />
              Sign in
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
```

- [ ] **Step 6: Update navbar**

Modify `frontend/src/components/AppNavbar.tsx`:

```tsx
import { Building2, LayoutDashboard, Plus, Menu, X, LogOut, UserCircle } from "lucide-react";
import { useAuth } from "./AuthProvider";
```

Inside component:

```tsx
const { user, logout, isOwner } = useAuth();
```

Hide nav on login:

```tsx
if (pathname === "/login") return null;
```

Replace the New Company link condition with:

```tsx
{isOwner && (
  <Link href="/companies/new">
    <Button
      variant={isActive("/companies/new") ? "secondary" : "primary"}
      size="sm"
      aria-current={isActive("/companies/new") ? "page" : undefined}
    >
      <Plus className="w-4 h-4" />
      New Company
    </Button>
  </Link>
)}
```

Add user display before theme toggle:

```tsx
{user && (
  <div className="hidden lg:flex items-center gap-2 rounded-md border border-gray-200 px-2 py-1 text-xs text-gray-600 dark:border-neutral-700 dark:text-gray-300">
    <UserCircle className="h-4 w-4" />
    <span>{user.displayName}</span>
    <span className="text-gray-400">({user.role})</span>
  </div>
)}
<Button variant="ghost" size="sm" isIconOnly aria-label="Sign out" onPress={logout}>
  <LogOut className="w-4 h-4" />
</Button>
```

- [ ] **Step 7: Run frontend checks**

Run:

```powershell
npm run lint
npm run build
```

Expected: lint and production build pass.

- [ ] **Step 8: Commit**

```powershell
git add frontend/src/lib/auth.ts frontend/src/components/AuthProvider.tsx frontend/src/app/login/page.tsx frontend/src/app/providers.tsx frontend/src/components/AppNavbar.tsx frontend/src/lib/api.ts
git commit -m "feat: add firm login frontend shell"
```

---

### Task 8: Remove Manual Reviewer Identity from Frontend Workflows

**Files:**
- Modify: `frontend/src/lib/api.ts`
- Delete or modify: `frontend/src/lib/reviewer.ts`
- Modify: `frontend/src/app/companies/[companyId]/periods/[periodId]/page.tsx`
- Modify: `frontend/src/app/companies/[companyId]/periods/[periodId]/year-end/page.tsx`

- [ ] **Step 1: Update API client reviewer-sensitive calls**

Modify `frontend/src/lib/api.ts`:

```typescript
export const saveYearEndReviewConfirmation = (
  companyId: number,
  periodId: number,
  sectionKey: string,
  data: { confirmed: boolean; note?: string },
) =>
  apiFetch<YearEndReviewConfirmation>(
    `/api/companies/${companyId}/periods/${periodId}/year-end-reviews/${sectionKey}`,
    { method: "PUT", body: JSON.stringify(data) },
  );
```

Change `approveAdjustment`:

```typescript
export const approveAdjustment = (
  companyId: number,
  periodId: number,
  id: number,
) =>
  apiFetch<Adjustment>(
    `/api/companies/${companyId}/periods/${periodId}/adjustments/${id}/approve`,
    {
      method: "POST",
      body: JSON.stringify({}),
    }
  );
```

Change filing functions:

```typescript
export const updateCroFilingStatus = (
  companyId: number,
  periodId: number,
  data: { status: string; reason?: string; submissionReference?: string }
) =>
  apiFetch<unknown>(`/api/companies/${companyId}/periods/${periodId}/filing/cro-status`, { method: "PUT", body: JSON.stringify(data) });

export const confirmCroPayment = (companyId: number, periodId: number) =>
  apiFetch<unknown>(`/api/companies/${companyId}/periods/${periodId}/filing/cro-payment`, { method: "POST", body: JSON.stringify({}) });
```

For file import, remove the `X-Reviewer` header argument and always rely on the authenticated session.

- [ ] **Step 2: Update period workspace imports and state**

Modify `frontend/src/app/companies/[companyId]/periods/[periodId]/page.tsx`:

Remove:

```tsx
import { getReviewerName } from "@/lib/reviewer";
```

Remove state:

```tsx
const [reviewerName, setReviewerName] = useState("Accounts reviewer");
```

Remove `handleSaveReviewerName`.

Remove the full period workspace `ReviewPanel` block whose `title` prop is `Reviewer Identity`.

Update approve call:

```tsx
await approveAdjustment(cId, pId, id);
```

Update filing calls:

```tsx
await updateCroFilingStatus(cId, pId, { status: "Approved" });
await updateCroFilingStatus(cId, pId, { status: "Submitted" });
await confirmCroPayment(cId, pId);
await updateCroFilingStatus(cId, pId, { status: "Accepted" });
await updateCroFilingStatus(cId, pId, {
  status: "CorrectionRequired",
  reason: correctionReason,
});
```

- [ ] **Step 3: Update year-end page**

Modify `frontend/src/app/companies/[companyId]/periods/[periodId]/year-end/page.tsx`:

Remove:

```tsx
import { getReviewerName } from "@/lib/reviewer";
```

Update review save:

```tsx
const updated = await saveYearEndReviewConfirmation(cId, pId, sectionKey, {
  confirmed: true,
  note: reviewNote,
});
```

- [ ] **Step 4: Remove reviewer helper if unused**

Run:

```powershell
rg -n "getReviewerName|accounts.reviewerName|X-Reviewer" frontend/src
```

Expected: no matches. If no matches remain, delete `frontend/src/lib/reviewer.ts`.

- [ ] **Step 5: Run frontend checks**

Run:

```powershell
npm run lint
npm run build
```

Expected: lint and build pass.

- [ ] **Step 6: Commit**

```powershell
git add frontend/src/lib/api.ts frontend/src/app/companies/[companyId]/periods/[periodId]/page.tsx frontend/src/app/companies/[companyId]/periods/[periodId]/year-end/page.tsx
git rm frontend/src/lib/reviewer.ts
git commit -m "feat: use signed-in reviewer identity in frontend"
```

---

### Task 9: Final Verification and Production-Readiness Evidence

**Files:**
- Modify: `Docs/superpowers/plans/2026-06-06-firm-login-tenant-access.md` checkbox statuses during execution if using inline execution.
- No production source changes unless verification reveals a bug.

- [ ] **Step 1: Run backend tests**

Run:

```powershell
dotnet test
```

Expected: all backend tests pass with zero failures.

- [ ] **Step 2: Run frontend lint**

Run:

```powershell
npm run lint
```

Expected: ESLint exits 0.

- [ ] **Step 3: Run frontend production build**

Run:

```powershell
npm run build
```

Expected: Next.js production build exits 0 and includes `/login` in the route list.

- [ ] **Step 4: Inspect final diff**

Run:

```powershell
git status --short
git diff --stat HEAD
```

Expected: no uncommitted files remain except intentionally preserved user work outside Slice 1. If the pre-existing tenant/user work has been incorporated, it should be staged and committed as part of the relevant feature commits.

- [ ] **Step 5: Completion audit against acceptance criteria**

Verify each item from the spec:

```text
- Seeded demo user can sign in through /login.
- Dashboard and company pages require authentication.
- Company list and company detail routes are tenant-scoped.
- Cross-tenant company IDs return 404.
- Role-denied writes fail on the backend.
- Review, approval, finalise, and filing workflow actions record signed-in identity.
- Manual reviewer identity warning is removed.
- dotnet test, npm run lint, and npm run build pass.
```

Document the exact command outputs and any residual risks in the final response.

- [ ] **Step 6: Commit final verification fixes if any**

If verification required code fixes:

```powershell
git add backend frontend Docs
git commit -m "fix: complete firm login verification"
```

If no fixes were required, do not create an empty commit.

---

## Plan Self-Review

Spec coverage:

- Authentication endpoints and signed sessions: Tasks 2 and 3.
- Tenant-scoped company access: Task 4.
- Role permissions: Task 5.
- Authenticated audit/reviewer identity: Task 6.
- Production safety: Task 1.
- Frontend login/session guard/navbar: Task 7.
- Removal of manual reviewer identity: Task 8.
- Verification gates: Task 9.

The plan intentionally keeps the existing API-key guard and does not add external identity providers, MFA, SSO, or user administration screens.
