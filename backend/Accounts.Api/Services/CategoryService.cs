using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public class CategoryService(AccountsDbContext db)
{
    public async Task<List<AccountCategory>> SeedDefaultCategoriesAsync(int companyId)
    {
        var existing = await db.AccountCategories.AnyAsync(c => c.CompanyId == companyId);
        if (existing) return await db.AccountCategories.Where(c => c.CompanyId == companyId).OrderBy(c => c.Code).ToListAsync();

        // Seed the company's own chart variant.
        var variant = await db.Companies
            .Where(c => c.Id == companyId)
            .Select(c => (ChartOfAccountsVariant?)c.ChartOfAccountsVariant)
            .FirstOrDefaultAsync()
            ?? throw new ResourceNotFoundException($"Company {companyId} not found");
        var categories = ChartDefinitions.For(variant).CreateCategories(companyId);
        db.AccountCategories.AddRange(categories);
        await db.SaveChangesAsync();
        return categories;
    }

    public async Task<(int? categoryId, decimal confidence)> AutoCategoriseAsync(int companyId, string description)
    {
        var rules = await db.TransactionRules
            .Where(r => r.CompanyId == companyId)
            .Where(r => r.Category.CompanyId == companyId || (r.Category.IsSystem && r.Category.CompanyId == null))
            .OrderBy(r => r.Priority)
            .ToListAsync();

        foreach (var rule in rules)
        {
            if (description.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase))
                return (rule.CategoryId, 0.85m);
        }

        // Fuzzy matching — check category names
        var categories = await db.AccountCategories
            .Where(c => c.CompanyId == companyId || (c.IsSystem && c.CompanyId == null))
            .ToListAsync();

        var descLower = description.ToLower();
        foreach (var cat in categories)
        {
            var nameParts = cat.Name.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (nameParts.Any(p => p.Length > 3 && descLower.Contains(p)))
                return (cat.Id, 0.5m);
        }

        return (null, 0m);
    }
}
