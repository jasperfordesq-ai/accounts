using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Services;

public class BootstrapOwnerService(
    AccountsDbContext db,
    IOptions<BootstrapOwnerConfig> options,
    IPasswordSafetyService? passwordSafety = null)
{
    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        var config = options.Value;
        if (!config.Enabled)
            return;

        var tenantName = Required(config.TenantName, "BootstrapOwner:TenantName").Trim();
        var tenantSlug = Required(config.TenantSlug, "BootstrapOwner:TenantSlug").Trim().ToLowerInvariant();
        var ownerEmail = Required(config.OwnerEmail, "BootstrapOwner:OwnerEmail").Trim().ToLowerInvariant();
        var ownerDisplayName = Required(config.OwnerDisplayName, "BootstrapOwner:OwnerDisplayName").Trim();
        var ownerPassword = Required(config.OwnerInitialPassword, "BootstrapOwner:OwnerInitialPassword");
        if (BootstrapOwnerPasswordPolicy.Validate(ownerPassword) is { } passwordFailure)
            throw new InvalidOperationException($"{passwordFailure}.");
        if (passwordSafety is not null)
        {
            var safety = await passwordSafety.CheckAsync(ownerPassword, cancellationToken);
            if (safety.Status == PasswordSafetyStatus.Breached)
                throw new InvalidOperationException("BootstrapOwner:OwnerInitialPassword appears in a known breach.");
            if (safety.Status == PasswordSafetyStatus.Unavailable)
                throw new InvalidOperationException("Bootstrap owner password safety validation is unavailable.");
        }

        var tenant = await db.Tenants.SingleOrDefaultAsync(t => t.Slug == tenantSlug, cancellationToken);
        var existingOwnerEmailUser = tenant is null
            ? null
            : await db.UserAccounts.SingleOrDefaultAsync(
                u => u.TenantId == tenant.Id && u.Email.ToLower() == ownerEmail,
                cancellationToken);
        if (existingOwnerEmailUser is not null && !IsUsableConfiguredOwner(existingOwnerEmailUser, tenant))
        {
            throw new InvalidOperationException(
                "BootstrapOwner:OwnerEmail already exists but is not an active Owner in the configured tenant.");
        }

        if (tenant is null)
        {
            tenant = new Tenant
            {
                Name = tenantName,
                Slug = tenantSlug,
                IsMainDemoTenant = false
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (existingOwnerEmailUser is null)
        {
            var (hash, salt) = PasswordHasher.HashPassword(ownerPassword);
            db.UserAccounts.Add(new UserAccount
            {
                TenantId = tenant.Id,
                Email = ownerEmail,
                DisplayName = ownerDisplayName,
                Role = "Owner",
                PasswordHash = hash,
                PasswordSalt = salt,
                PasswordAlgorithm = AuthService.PasswordAlgorithm,
                PasswordStrengthScore = 5,
                IsActive = true,
                MustChangePassword = config.OwnerMustChangePassword,
                PasswordLastChangedAt = DateTime.UtcNow
            });
        }
        else if (!config.OwnerMustChangePassword && existingOwnerEmailUser.MustChangePassword)
        {
            existingOwnerEmailUser.MustChangePassword = false;
            existingOwnerEmailUser.UpdatedAt = DateTime.UtcNow;
        }

        var orphanCompanies = await db.Companies
            .Where(c => c.TenantId == null)
            .ToListAsync(cancellationToken);
        foreach (var company in orphanCompanies)
            company.TenantId = tenant.Id;

        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool IsUsableConfiguredOwner(UserAccount user, Tenant? tenant) =>
        tenant is not null
        && user.TenantId == tenant.Id
        && user.IsActive
        && user.Role.Trim().Equals("Owner", StringComparison.OrdinalIgnoreCase);

    private static string Required(string value, string key) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{key} is required when BootstrapOwner:Enabled is true.")
            : value;
}
