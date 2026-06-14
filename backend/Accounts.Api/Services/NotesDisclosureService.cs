using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public class NotesDisclosureService(AccountsDbContext db)
{
    public async Task<List<NotesDisclosure>> GenerateNotesAsync(int companyId, int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company)
            .Include(p => p.FilingRegime)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        var company = period.Company;
        var regime = period.FilingRegime?.ElectedRegime ?? ElectedRegime.Small;
        var periodsToDate = (await db.AccountingPeriods
            .Where(p => p.CompanyId == companyId && p.PeriodEnd <= period.PeriodEnd)
            .Select(p => p.Id)
            .ToListAsync())
            .ToHashSet();
        var activeAssets = await db.FixedAssets
            .Include(a => a.DepreciationEntries)
            .Where(a => a.CompanyId == company.Id
                && a.AcquisitionDate <= period.PeriodEnd
                && (a.DisposalDate == null || a.DisposalDate > period.PeriodEnd))
            .ToListAsync();

        // Remove generated notes while preserving custom user disclosures.
        var existing = await db.NotesDisclosures.Where(n => n.PeriodId == periodId).ToListAsync();
        var generatedExisting = existing.Where(n => n.IsRequired).ToList();
        var customExisting = existing
            .Where(n => !n.IsRequired)
            .OrderBy(n => n.NoteNumber)
            .ThenBy(n => n.Id)
            .ToList();
        db.NotesDisclosures.RemoveRange(generatedExisting);

        var notes = new List<NotesDisclosure>();
        int num = 1;

        // Note 1: Accounting Policies (always required)
        var basisText = regime == ElectedRegime.Micro
            ? "The financial statements have been prepared on the going concern basis and in accordance with FRS 105 \"The Financial Reporting Standard applicable to the Micro-entities Regime\" and the Companies Act 2014."
            : "The financial statements have been prepared on the going concern basis and in accordance with FRS 102 \"The Financial Reporting Standard applicable in the UK and Republic of Ireland\" issued by the Financial Reporting Council, and the Companies Act 2014.";

        var policiesContent = $"Basis of Preparation\n{basisText}\n\n";
        policiesContent += "Currency\nThe financial statements are presented in Euro (\u20ac), which is also the functional currency of the company.\n\n";

        // Check if company has fixed assets for depreciation policy
        var hasAssets = activeAssets.Count > 0;
        if (hasAssets)
        {
            policiesContent += "Tangible Fixed Assets and Depreciation\n";
            policiesContent += "Tangible fixed assets are stated at cost less accumulated depreciation. Depreciation is provided at rates calculated to write off the cost of each asset over its expected useful life, as follows:\n";

            var categories = activeAssets
                .Select(a => new { a.Category, a.DepreciationMethod, a.UsefulLifeYears })
                .Distinct()
                .ToList();

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
            var dirLoans = await db.DirectorLoans
                .Where(d => d.PeriodId == periodId && d.Director.CompanyId == companyId)
                .Include(d => d.Director)
                .ToListAsync();
            if (dirLoans.Count > 0)
            {
                var dlContent = "The following advances, credits and guarantees existed between the company and its directors during the financial year:\n\n";
                foreach (var dl in dirLoans)
                    dlContent += $"{dl.Director.Name}: Opening \u20ac{dl.OpeningBalance:N0}, Advances \u20ac{dl.Advances:N0}, Repayments \u20ac{dl.Repayments:N0}, Closing \u20ac{dl.ClosingBalance:N0}\n";
                notes.Add(new NotesDisclosure { PeriodId = periodId, NoteNumber = num++, Title = "Advances, Credits and Guarantees to Directors", Content = dlContent, IsRequired = true, IsIncluded = true });
            }

            notes.Add(new NotesDisclosure { PeriodId = periodId, NoteNumber = num++, Title = "Approval of Financial Statements", Content = $"The financial statements were approved and authorised for issue by the Board of Directors on {DateTime.Now:dd MMMM yyyy}.", IsRequired = true, IsIncluded = true });

            RenumberCustomNotesAfterGenerated(customExisting, notes.Count);
            db.NotesDisclosures.AddRange(notes);
            await db.SaveChangesAsync();
            return await db.NotesDisclosures
                .Where(n => n.PeriodId == periodId)
                .OrderBy(n => n.NoteNumber)
                .ToListAsync();
        }

        // Note 2: Tangible Fixed Assets (if any)
        if (hasAssets)
        {
            var depEntries = activeAssets.SelectMany(a => a.DepreciationEntries.Where(d => d.PeriodId == periodId)).ToList();

            var totalCost = activeAssets.Sum(a => a.Cost);
            var totalDep = depEntries.Sum(d => d.Charge);
            var totalNbv = activeAssets.Sum(a =>
            {
                var entry = a.DepreciationEntries.FirstOrDefault(d => d.PeriodId == periodId);
                return entry?.ClosingNbv ?? (a.Cost - a.DepreciationEntries.Where(d => periodsToDate.Contains(d.PeriodId)).Sum(d => d.Charge));
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
        var loanSnapshots = await db.LoanBalanceSnapshots
            .Include(s => s.Loan)
            .Where(s => s.PeriodId == periodId && s.Loan.CompanyId == company.Id && s.DueAfterYear > 0)
            .ToListAsync();
        loanSnapshots = loanSnapshots
            .Where(s => LoanAppliesAtPeriodEnd(s.Loan, period.PeriodStart, period.PeriodEnd))
            .ToList();
        var snapshotLoanIds = loanSnapshots.Select(s => s.LoanId).ToHashSet();
        var loansAfter = (await db.Loans
            .Where(l => l.CompanyId == company.Id
                && l.DueAfterYear > 0
                && l.DrawdownDate != null
                && l.BalanceAsOfDate != null
                && l.DrawdownDate <= period.PeriodEnd
                && l.BalanceAsOfDate >= period.PeriodStart
                && l.BalanceAsOfDate <= period.PeriodEnd)
            .ToListAsync())
            .Where(l => LoanAppliesAtPeriodEnd(l, period.PeriodStart, period.PeriodEnd))
            .Where(l => !snapshotLoanIds.Contains(l.Id))
            .Select(l => new LoanDisclosureLine(l.Lender, l.DueAfterYear))
            .Concat(loanSnapshots.Select(s => new LoanDisclosureLine(s.Loan.Lender, s.DueAfterYear)))
            .ToList();
        if (loansAfter.Count > 0)
        {
            var lContent = "";
            foreach (var loan in loansAfter)
                lContent += $"{loan.Lender}: \u20ac{loan.Amount:N0}\n";
            lContent += $"Total: \u20ac{loansAfter.Sum(l => l.Amount):N0}";
            notes.Add(new NotesDisclosure { PeriodId = periodId, NoteNumber = num++, Title = "Creditors: Amounts Falling Due After More Than One Year", Content = lContent, IsRequired = true, IsIncluded = true });
        }

        // Note: Share Capital
        var shareCapitals = await db.ShareCapitals
            .Where(s => s.CompanyId == company.Id
                && s.IssueDate != null
                && s.IssueDate <= period.PeriodEnd
                && (s.CancelledDate == null || s.CancelledDate > period.PeriodEnd))
            .ToListAsync();
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
        var directorLoans = await db.DirectorLoans
            .Where(d => d.PeriodId == periodId && d.Director.CompanyId == companyId)
            .Include(d => d.Director)
            .ToListAsync();
        if (directorLoans.Count > 0)
        {
            var dlContent = "";
            foreach (var dl in directorLoans)
                dlContent += $"{dl.Director.Name}: Opening \u20ac{dl.OpeningBalance:N0}, Advances \u20ac{dl.Advances:N0}, Repayments \u20ac{dl.Repayments:N0}, Closing \u20ac{dl.ClosingBalance:N0}\n";
            notes.Add(new NotesDisclosure { PeriodId = periodId, NoteNumber = num++, Title = "Directors' Loans and Transactions", Content = dlContent, IsRequired = true, IsIncluded = true });
        }

        // Note: Post Balance Sheet Events
        var pbsEvents = await db.PostBalanceSheetEvents.Where(e => e.PeriodId == periodId).OrderBy(e => e.EventDate).ToListAsync();
        if (pbsEvents.Count > 0)
        {
            var pbsContent = "The following events have occurred after the balance sheet date:\n\n";
            foreach (var evt in pbsEvents)
            {
                var eventType = evt.IsAdjusting ? "Adjusting" : "Non-adjusting";
                pbsContent += $"{evt.EventDate:dd MMMM yyyy} ({eventType}): {evt.Description}";
                if (evt.FinancialImpact.HasValue)
                    pbsContent += $" — Estimated financial impact: \u20ac{evt.FinancialImpact.Value:N0}";
                pbsContent += "\n";
            }

            var adjustingEvents = pbsEvents.Where(e => e.IsAdjusting).ToList();
            if (adjustingEvents.Count > 0)
                pbsContent += "\nAdjusting events have been reflected in the financial statements.";

            notes.Add(new NotesDisclosure { PeriodId = periodId, NoteNumber = num++, Title = "Post Balance Sheet Events", Content = pbsContent.TrimEnd(), IsRequired = true, IsIncluded = true });
        }

        // Note: Related Party Transactions
        var rptItems = await db.RelatedPartyTransactions.Where(r => r.PeriodId == periodId).OrderBy(r => r.PartyName).ToListAsync();
        if (rptItems.Count > 0)
        {
            var rptContent = "The following transactions with related parties took place during the financial year:\n\n";
            foreach (var rp in rptItems)
            {
                rptContent += $"{rp.PartyName} ({rp.Relationship}) — {rp.TransactionType}: \u20ac{rp.Amount:N0}";
                if (rp.BalanceOwed.HasValue && rp.BalanceOwed.Value != 0)
                    rptContent += $", Balance owed at year end: \u20ac{rp.BalanceOwed.Value:N0}";
                if (!string.IsNullOrWhiteSpace(rp.Terms))
                    rptContent += $" ({rp.Terms})";
                rptContent += "\n";
            }

            notes.Add(new NotesDisclosure { PeriodId = periodId, NoteNumber = num++, Title = "Related Party Transactions", Content = rptContent.TrimEnd(), IsRequired = true, IsIncluded = true });
        }

        // Note: Contingent Liabilities
        var clItems = await db.ContingentLiabilities.Where(c => c.PeriodId == periodId).OrderBy(c => c.Description).ToListAsync();
        if (clItems.Count > 0)
        {
            var clContent = "The following contingent liabilities existed at the balance sheet date:\n\n";
            foreach (var item in clItems)
            {
                clContent += $"{item.Description} ({item.Nature}) — Likelihood: {item.Likelihood}";
                if (item.EstimatedAmount.HasValue)
                    clContent += $", Estimated amount: \u20ac{item.EstimatedAmount.Value:N0}";
                clContent += "\n";
            }

            notes.Add(new NotesDisclosure { PeriodId = periodId, NoteNumber = num++, Title = "Contingent Liabilities", Content = clContent.TrimEnd(), IsRequired = true, IsIncluded = true });
        }

        // Note: Going Concern
        if (!period.GoingConcernConfirmed)
        {
            var gcContent = "Material uncertainty relating to going concern\n\n";
            gcContent += !string.IsNullOrWhiteSpace(period.GoingConcernNote)
                ? period.GoingConcernNote
                : "The directors have identified material uncertainties related to events or conditions that may cast significant doubt on the company's ability to continue as a going concern.";
            notes.Add(new NotesDisclosure { PeriodId = periodId, NoteNumber = num++, Title = "Going Concern", Content = gcContent, IsRequired = true, IsIncluded = true });
        }
        else
        {
            notes.Add(new NotesDisclosure { PeriodId = periodId, NoteNumber = num++, Title = "Going Concern", Content = "The directors have a reasonable expectation that the company has adequate resources to continue in operational existence for the foreseeable future. The financial statements have been prepared on the going concern basis.", IsRequired = true, IsIncluded = true });
        }

        // Note: Approval
        notes.Add(new NotesDisclosure { PeriodId = periodId, NoteNumber = num++, Title = "Approval of Financial Statements", Content = $"The financial statements were approved and authorised for issue by the Board of Directors on {DateTime.Now:dd MMMM yyyy}.", IsRequired = true, IsIncluded = true });

        RenumberCustomNotesAfterGenerated(customExisting, notes.Count);
        db.NotesDisclosures.AddRange(notes);
        await db.SaveChangesAsync();
        return await db.NotesDisclosures
            .Where(n => n.PeriodId == periodId)
            .OrderBy(n => n.NoteNumber)
            .ToListAsync();
    }

    private static void RenumberCustomNotesAfterGenerated(List<NotesDisclosure> customNotes, int generatedCount)
    {
        var noteNumber = generatedCount + 1;
        foreach (var note in customNotes)
            note.NoteNumber = noteNumber++;
    }

    private static bool LoanAppliesAtPeriodEnd(Loan loan, DateOnly periodStart, DateOnly periodEnd) =>
        loan.DrawdownDate is { } drawdownDate
        && loan.BalanceAsOfDate is { } balanceAsOfDate
        && drawdownDate <= periodEnd
        && balanceAsOfDate >= periodStart
        && balanceAsOfDate <= periodEnd;

    private record LoanDisclosureLine(string Lender, decimal Amount);
}
