using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public class NotesDisclosureService(AccountsDbContext db)
{
    public async Task<List<NotesDisclosure>> GenerateNotesAsync(int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company)
            .Include(p => p.FilingRegime)
            .FirstAsync(p => p.Id == periodId);

        var company = period.Company;
        var regime = period.FilingRegime?.ElectedRegime ?? ElectedRegime.Small;

        // Remove existing auto-generated notes
        var existing = await db.NotesDisclosures.Where(n => n.PeriodId == periodId).ToListAsync();
        db.NotesDisclosures.RemoveRange(existing);

        var notes = new List<NotesDisclosure>();
        int num = 1;

        // Note 1: Accounting Policies (always required)
        var basisText = regime == ElectedRegime.Micro
            ? "The financial statements have been prepared on the going concern basis and in accordance with FRS 105 \"The Financial Reporting Standard applicable to the Micro-entities Regime\" and the Companies Act 2014."
            : "The financial statements have been prepared on the going concern basis and in accordance with FRS 102 \"The Financial Reporting Standard applicable in the UK and Republic of Ireland\" issued by the Financial Reporting Council, and the Companies Act 2014.";

        var policiesContent = $"Basis of Preparation\n{basisText}\n\n";
        policiesContent += "Currency\nThe financial statements are presented in Euro (\u20ac), which is also the functional currency of the company.\n\n";

        // Check if company has fixed assets for depreciation policy
        var hasAssets = await db.FixedAssets.AnyAsync(a => a.CompanyId == company.Id && a.DisposalDate == null);
        if (hasAssets)
        {
            policiesContent += "Tangible Fixed Assets and Depreciation\n";
            policiesContent += "Tangible fixed assets are stated at cost less accumulated depreciation. Depreciation is provided at rates calculated to write off the cost of each asset over its expected useful life, as follows:\n";

            var categories = await db.FixedAssets
                .Where(a => a.CompanyId == company.Id && a.DisposalDate == null)
                .Select(a => new { a.Category, a.DepreciationMethod, a.UsefulLifeYears })
                .Distinct()
                .ToListAsync();

            foreach (var cat in categories)
            {
                var method = cat.DepreciationMethod == DepreciationMethod.StraightLine ? "Straight line" : "Reducing balance";
                policiesContent += $"  {cat.Category}: {method} over {cat.UsefulLifeYears} years\n";
            }
            policiesContent += "\n";
        }

        var hasStock = await db.Inventories.AnyAsync(i => i.PeriodId == periodId);
        if (hasStock)
        {
            policiesContent += "Stock\nStock is valued at the lower of cost and estimated net realisable value.\n\n";
        }

        policiesContent += "Revenue Recognition\nRevenue is recognised to the extent that it is probable that the economic benefits will flow to the company and the revenue can be reliably measured.\n\n";
        policiesContent += "Taxation\nCorporation tax is provided on taxable profits at the current rate. Deferred tax is recognised in respect of all timing differences that have originated but not reversed at the balance sheet date.";

        notes.Add(new NotesDisclosure { PeriodId = periodId, NoteNumber = num++, Title = "Accounting Policies", Content = policiesContent, IsRequired = true, IsIncluded = true });

        // For micro regime, only minimal additional notes required
        if (regime == ElectedRegime.Micro)
        {
            // Director advances/guarantees (micro requirement)
            var dirLoans = await db.DirectorLoans.Where(d => d.PeriodId == periodId).Include(d => d.Director).ToListAsync();
            if (dirLoans.Count > 0)
            {
                var dlContent = "The following advances, credits and guarantees existed between the company and its directors during the financial year:\n\n";
                foreach (var dl in dirLoans)
                    dlContent += $"{dl.Director.Name}: Opening \u20ac{dl.OpeningBalance:N0}, Advances \u20ac{dl.Advances:N0}, Repayments \u20ac{dl.Repayments:N0}, Closing \u20ac{dl.ClosingBalance:N0}\n";
                notes.Add(new NotesDisclosure { PeriodId = periodId, NoteNumber = num++, Title = "Advances, Credits and Guarantees to Directors", Content = dlContent, IsRequired = true, IsIncluded = true });
            }

            notes.Add(new NotesDisclosure { PeriodId = periodId, NoteNumber = num++, Title = "Approval of Financial Statements", Content = $"The financial statements were approved and authorised for issue by the Board of Directors on {DateTime.Now:dd MMMM yyyy}.", IsRequired = true, IsIncluded = true });

            db.NotesDisclosures.AddRange(notes);
            await db.SaveChangesAsync();
            return notes;
        }

        // Note 2: Tangible Fixed Assets (if any)
        if (hasAssets)
        {
            var assets = await db.FixedAssets.Include(a => a.DepreciationEntries).Where(a => a.CompanyId == company.Id).ToListAsync();
            var depEntries = assets.SelectMany(a => a.DepreciationEntries.Where(d => d.PeriodId == periodId)).ToList();

            var totalCost = assets.Sum(a => a.Cost);
            var totalDep = depEntries.Sum(d => d.Charge);
            var totalNbv = assets.Sum(a =>
            {
                var entry = a.DepreciationEntries.FirstOrDefault(d => d.PeriodId == periodId);
                return entry?.ClosingNbv ?? (a.Cost - a.DepreciationEntries.Sum(d => d.Charge));
            });

            var assetContent = $"Cost at period end: \u20ac{totalCost:N0}\n";
            assetContent += $"Depreciation charge for the year: \u20ac{totalDep:N0}\n";
            assetContent += $"Net book value at period end: \u20ac{totalNbv:N0}";

            notes.Add(new NotesDisclosure { PeriodId = periodId, NoteNumber = num++, Title = "Tangible Fixed Assets", Content = assetContent, IsRequired = true, IsIncluded = true });
        }

        // Note: Debtors
        var debtors = await db.Debtors.Where(d => d.PeriodId == periodId).ToListAsync();
        if (debtors.Count > 0)
        {
            var dContent = "";
            var trade = debtors.Where(d => d.Type == DebtorType.Trade).Sum(d => d.Amount);
            var prepay = debtors.Where(d => d.Type == DebtorType.Prepayment).Sum(d => d.Amount);
            var other = debtors.Where(d => d.Type == DebtorType.Other).Sum(d => d.Amount);
            if (trade > 0) dContent += $"Trade debtors: \u20ac{trade:N0}\n";
            if (prepay > 0) dContent += $"Prepayments and accrued income: \u20ac{prepay:N0}\n";
            if (other > 0) dContent += $"Other debtors: \u20ac{other:N0}\n";
            dContent += $"Total: \u20ac{debtors.Sum(d => d.Amount):N0}";
            notes.Add(new NotesDisclosure { PeriodId = periodId, NoteNumber = num++, Title = "Debtors", Content = dContent, IsRequired = true, IsIncluded = true });
        }

        // Note: Creditors falling due within one year
        var creditorsWithin = await db.Creditors.Where(c => c.PeriodId == periodId && c.DueWithinYear).ToListAsync();
        var taxBals = await db.TaxBalances.Where(t => t.PeriodId == periodId).ToListAsync();
        if (creditorsWithin.Count > 0 || taxBals.Count > 0)
        {
            var cContent = "";
            var tradeCred = creditorsWithin.Where(c => c.Type == CreditorType.Trade).Sum(c => c.Amount);
            var accruals = creditorsWithin.Where(c => c.Type == CreditorType.Accrual).Sum(c => c.Amount);
            var taxCred = creditorsWithin.Where(c => c.Type == CreditorType.Tax).Sum(c => c.Amount) + taxBals.Sum(t => t.Balance);
            var otherCred = creditorsWithin.Where(c => c.Type == CreditorType.Other).Sum(c => c.Amount);
            if (tradeCred > 0) cContent += $"Trade creditors: \u20ac{tradeCred:N0}\n";
            if (accruals > 0) cContent += $"Accruals: \u20ac{accruals:N0}\n";
            if (taxCred > 0) cContent += $"Taxation and social insurance: \u20ac{taxCred:N0}\n";
            if (otherCred > 0) cContent += $"Other creditors: \u20ac{otherCred:N0}\n";
            var total = tradeCred + accruals + taxCred + otherCred;
            cContent += $"Total: \u20ac{total:N0}";
            notes.Add(new NotesDisclosure { PeriodId = periodId, NoteNumber = num++, Title = "Creditors: Amounts Falling Due Within One Year", Content = cContent, IsRequired = true, IsIncluded = true });
        }

        // Note: Creditors after one year
        var loansAfter = await db.Loans.Where(l => l.CompanyId == company.Id && l.DueAfterYear > 0).ToListAsync();
        if (loansAfter.Count > 0)
        {
            var lContent = "";
            foreach (var loan in loansAfter)
                lContent += $"{loan.Lender}: \u20ac{loan.DueAfterYear:N0}\n";
            lContent += $"Total: \u20ac{loansAfter.Sum(l => l.DueAfterYear):N0}";
            notes.Add(new NotesDisclosure { PeriodId = periodId, NoteNumber = num++, Title = "Creditors: Amounts Falling Due After More Than One Year", Content = lContent, IsRequired = true, IsIncluded = true });
        }

        // Note: Share Capital
        var shareCapitals = await db.ShareCapitals.Where(s => s.CompanyId == company.Id).ToListAsync();
        var scContent = shareCapitals.Count > 0
            ? string.Join("\n", shareCapitals.Select(s => $"Authorised and issued: {s.NumberIssued} {s.ShareClass} shares of \u20ac{s.NominalValue:N2} each, fully paid: \u20ac{s.TotalValue:N0}"))
            : "Authorised and issued: 1 Ordinary share of \u20ac1.00, fully paid: \u20ac1";
        notes.Add(new NotesDisclosure { PeriodId = periodId, NoteNumber = num++, Title = "Share Capital", Content = scContent, IsRequired = true, IsIncluded = true });

        // Note: Staff numbers (if employer)
        if (company.IsEmployer)
        {
            var payroll = await db.PayrollSummaries.FirstOrDefaultAsync(p => p.PeriodId == periodId);
            if (payroll != null)
            {
                var staffContent = $"The average number of employees during the financial year was {payroll.StaffCount}.\n\n";
                staffContent += $"Staff costs:\n";
                staffContent += $"  Wages and salaries: \u20ac{payroll.GrossWages:N0}\n";
                staffContent += $"  Social insurance costs (Employer PRSI): \u20ac{payroll.EmployerPrsi:N0}\n";
                staffContent += $"  Pension costs: \u20ac{payroll.PensionContributions:N0}\n";
                staffContent += $"  Total: \u20ac{payroll.GrossWages + payroll.EmployerPrsi + payroll.PensionContributions:N0}";
                notes.Add(new NotesDisclosure { PeriodId = periodId, NoteNumber = num++, Title = "Employees and Remuneration", Content = staffContent, IsRequired = true, IsIncluded = true });
            }
        }

        // Note: Director loans
        var directorLoans = await db.DirectorLoans.Where(d => d.PeriodId == periodId).Include(d => d.Director).ToListAsync();
        if (directorLoans.Count > 0)
        {
            var dlContent = "";
            foreach (var dl in directorLoans)
                dlContent += $"{dl.Director.Name}: Opening \u20ac{dl.OpeningBalance:N0}, Advances \u20ac{dl.Advances:N0}, Repayments \u20ac{dl.Repayments:N0}, Closing \u20ac{dl.ClosingBalance:N0}\n";
            notes.Add(new NotesDisclosure { PeriodId = periodId, NoteNumber = num++, Title = "Directors' Loans and Transactions", Content = dlContent, IsRequired = true, IsIncluded = true });
        }

        // Note: Approval
        notes.Add(new NotesDisclosure { PeriodId = periodId, NoteNumber = num++, Title = "Approval of Financial Statements", Content = $"The financial statements were approved and authorised for issue by the Board of Directors on {DateTime.Now:dd MMMM yyyy}.", IsRequired = true, IsIncluded = true });

        db.NotesDisclosures.AddRange(notes);
        await db.SaveChangesAsync();
        return notes;
    }
}
