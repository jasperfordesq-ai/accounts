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

        var categories = new List<AccountCategory>
        {
            // Income
            new() { CompanyId = companyId, Code = "4000", Name = "Sales / Revenue", Type = AccountCategoryType.Income, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "4100", Name = "Other Income", Type = AccountCategoryType.Income, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "4200", Name = "Interest Received", Type = AccountCategoryType.Income, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "4300", Name = "Grants Received", Type = AccountCategoryType.Income, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },

            // Cost of Sales
            new() { CompanyId = companyId, Code = "5000", Name = "Cost of Sales", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "5100", Name = "Direct Materials", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "5200", Name = "Direct Labour", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },

            // Overheads
            new() { CompanyId = companyId, Code = "6000", Name = "Wages & Salaries", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "6010", Name = "Employer PRSI", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "6020", Name = "Pension Contributions", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "6100", Name = "Rent", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "6110", Name = "Rates", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "6200", Name = "Insurance", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "6300", Name = "Light & Heat", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "6400", Name = "Telephone & Internet", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "6500", Name = "Office Supplies & Stationery", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "6600", Name = "Motor Expenses", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "6700", Name = "Travel & Subsistence", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "6800", Name = "Professional Fees", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "6810", Name = "Accountancy Fees", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "6820", Name = "Legal Fees", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "6900", Name = "Bank Charges & Interest", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "7000", Name = "Depreciation", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.NonDeductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "7100", Name = "Repairs & Maintenance", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "7200", Name = "Advertising & Marketing", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "7300", Name = "Software & Subscriptions", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "7400", Name = "Training & Development", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "7500", Name = "Entertainment", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.NonDeductible, IsSystem = true },
            new() { CompanyId = companyId, Code = "7900", Name = "Sundry Expenses", Type = AccountCategoryType.Expense, TaxTreatment = TaxTreatment.Deductible, IsSystem = true },

            // Fixed Assets
            new() { CompanyId = companyId, Code = "0010", Name = "Land & Buildings", Type = AccountCategoryType.Asset, TaxTreatment = TaxTreatment.CapitalAllowance, IsSystem = true },
            new() { CompanyId = companyId, Code = "0020", Name = "Plant & Machinery", Type = AccountCategoryType.Asset, TaxTreatment = TaxTreatment.CapitalAllowance, IsSystem = true },
            new() { CompanyId = companyId, Code = "0030", Name = "Motor Vehicles", Type = AccountCategoryType.Asset, TaxTreatment = TaxTreatment.CapitalAllowance, IsSystem = true },
            new() { CompanyId = companyId, Code = "0040", Name = "Office Equipment", Type = AccountCategoryType.Asset, TaxTreatment = TaxTreatment.CapitalAllowance, IsSystem = true },
            new() { CompanyId = companyId, Code = "0050", Name = "Computer Equipment", Type = AccountCategoryType.Asset, TaxTreatment = TaxTreatment.CapitalAllowance, IsSystem = true },

            // Current Assets
            new() { CompanyId = companyId, Code = "1000", Name = "Stock / Inventory", Type = AccountCategoryType.Asset, TaxTreatment = TaxTreatment.Other, IsSystem = true },
            new() { CompanyId = companyId, Code = "1100", Name = "Trade Debtors", Type = AccountCategoryType.Asset, TaxTreatment = TaxTreatment.Other, IsSystem = true },
            new() { CompanyId = companyId, Code = "1200", Name = "Prepayments", Type = AccountCategoryType.Asset, TaxTreatment = TaxTreatment.Other, IsSystem = true },
            new() { CompanyId = companyId, Code = "1300", Name = "VAT Receivable", Type = AccountCategoryType.Asset, TaxTreatment = TaxTreatment.Other, IsSystem = true },
            new() { CompanyId = companyId, Code = "1400", Name = "Bank Current Account", Type = AccountCategoryType.Asset, TaxTreatment = TaxTreatment.Other, IsSystem = true },
            new() { CompanyId = companyId, Code = "1410", Name = "Petty Cash", Type = AccountCategoryType.Asset, TaxTreatment = TaxTreatment.Other, IsSystem = true },

            // Liabilities
            new() { CompanyId = companyId, Code = "2000", Name = "Trade Creditors", Type = AccountCategoryType.Liability, TaxTreatment = TaxTreatment.Other, IsSystem = true },
            new() { CompanyId = companyId, Code = "2100", Name = "Accruals", Type = AccountCategoryType.Liability, TaxTreatment = TaxTreatment.Other, IsSystem = true },
            new() { CompanyId = companyId, Code = "2200", Name = "VAT Payable", Type = AccountCategoryType.Liability, TaxTreatment = TaxTreatment.Other, IsSystem = true },
            new() { CompanyId = companyId, Code = "2300", Name = "PAYE / PRSI Payable", Type = AccountCategoryType.Liability, TaxTreatment = TaxTreatment.Other, IsSystem = true },
            new() { CompanyId = companyId, Code = "2400", Name = "Corporation Tax Payable", Type = AccountCategoryType.Liability, TaxTreatment = TaxTreatment.Other, IsSystem = true },
            new() { CompanyId = companyId, Code = "2500", Name = "Director Loan Account", Type = AccountCategoryType.Liability, TaxTreatment = TaxTreatment.Other, IsSystem = true },
            new() { CompanyId = companyId, Code = "2600", Name = "Bank Loan (< 1 year)", Type = AccountCategoryType.Liability, TaxTreatment = TaxTreatment.Other, IsSystem = true },
            new() { CompanyId = companyId, Code = "2700", Name = "Bank Loan (> 1 year)", Type = AccountCategoryType.Liability, TaxTreatment = TaxTreatment.Other, IsSystem = true },

            // Equity
            new() { CompanyId = companyId, Code = "3000", Name = "Share Capital", Type = AccountCategoryType.Equity, TaxTreatment = TaxTreatment.Other, IsSystem = true },
            new() { CompanyId = companyId, Code = "3100", Name = "Retained Earnings", Type = AccountCategoryType.Equity, TaxTreatment = TaxTreatment.Other, IsSystem = true },
            new() { CompanyId = companyId, Code = "3200", Name = "Dividends Paid", Type = AccountCategoryType.Equity, TaxTreatment = TaxTreatment.Other, IsSystem = true },
        };

        db.AccountCategories.AddRange(categories);
        await db.SaveChangesAsync();
        return categories;
    }

    public async Task<(int? categoryId, decimal confidence)> AutoCategoriseAsync(int companyId, string description)
    {
        var rules = await db.TransactionRules
            .Where(r => r.CompanyId == companyId)
            .OrderBy(r => r.Priority)
            .ToListAsync();

        foreach (var rule in rules)
        {
            if (description.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase))
                return (rule.CategoryId, 0.85m);
        }

        // Fuzzy matching — check category names
        var categories = await db.AccountCategories
            .Where(c => c.CompanyId == companyId || c.IsSystem)
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
