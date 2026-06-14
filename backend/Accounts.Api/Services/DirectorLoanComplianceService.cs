using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public class DirectorLoanComplianceService(AccountsDbContext db, FinancialStatementsService statementsService)
{
    private const decimal StatutoryInterestRate = 5.0m; // s.243 Companies Act 2014
    private const decimal Section239Threshold = 0.10m; // 10% of net relevant assets

    public record DirectorLoanComplianceResult(
        decimal TotalDirectorLoans,
        decimal NetAssets,
        decimal ThresholdAmount,
        bool ExceedsThreshold,
        bool SapRequired,
        decimal StatutoryInterestDue,
        List<DirectorLoanDetail> Loans,
        string? Warning
    );

    public record DirectorLoanDetail(
        int Id,
        string DirectorName,
        decimal OpeningBalance,
        decimal MaxDuringYear,
        decimal ClosingBalance,
        decimal InterestCharged,
        bool IsDocumented,
        bool ExceedsThreshold
    );

    /// <summary>
    /// Run s.239 compliance check: aggregate director loans vs 10% of net relevant assets.
    /// Calculate 5% statutory interest on undocumented loans.
    /// </summary>
    public async Task<DirectorLoanComplianceResult> GetComplianceStatusAsync(int companyId, int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new InvalidOperationException($"Period {periodId} not found");

        var periodDirectorLoans = await db.DirectorLoans
            .Include(dl => dl.Director)
            .Where(dl => dl.PeriodId == periodId && dl.Director.CompanyId == companyId)
            .ToListAsync();
        var directorLoans = periodDirectorLoans.Where(dl => dl.ClosingBalance > 0).ToList();

        // Get net assets from balance sheet
        decimal netAssets;
        try
        {
            var bs = await statementsService.GetBalanceSheetAsync(companyId, periodId);
            netAssets = bs.NetAssets;
        }
        catch
        {
            netAssets = 0; // Balance sheet not yet available
        }

        var thresholdAmount = netAssets * Section239Threshold;
        var totalLoans = directorLoans.Sum(dl => dl.ClosingBalance);
        var exceedsThreshold = totalLoans > thresholdAmount && thresholdAmount > 0;

        // Calculate statutory interest on undocumented loans
        decimal totalInterestDue = 0;
        var details = new List<DirectorLoanDetail>();

        foreach (var dl in periodDirectorLoans)
        {
            var interestDue = 0m;
            if (!dl.IsDocumented && dl.ClosingBalance > 0)
            {
                // 5% statutory interest on average balance
                var avgBalance = (dl.OpeningBalance + dl.ClosingBalance) / 2;
                interestDue = avgBalance * (StatutoryInterestRate / 100m);
                totalInterestDue += interestDue;
            }

            details.Add(new DirectorLoanDetail(
                dl.Id,
                dl.Director?.Name ?? $"Director #{dl.DirectorId}",
                dl.OpeningBalance,
                dl.MaxBalanceDuringYear > 0 ? dl.MaxBalanceDuringYear : Math.Max(dl.OpeningBalance, dl.ClosingBalance),
                dl.ClosingBalance,
                dl.InterestCharged > 0 ? dl.InterestCharged : interestDue,
                dl.IsDocumented,
                dl.ClosingBalance > thresholdAmount && thresholdAmount > 0
            ));
        }

        string? warning = null;
        if (exceedsThreshold)
        {
            warning = $"COMPLIANCE ALERT: Aggregate director loans (€{totalLoans:N2}) exceed 10% of net assets (€{thresholdAmount:N2}). "
                    + "A Summary Approval Procedure (SAP) under s.239 Companies Act 2014 is required. "
                    + "Directors must make a statutory declaration of solvency and file with CRO within 21 days.";
        }
        else if (directorLoans.Any(dl => !dl.IsDocumented))
        {
            warning = "Undocumented director loans detected. Under the Companies Act, undocumented loans are deemed repayable on demand "
                    + $"and bear interest at the statutory rate of {StatutoryInterestRate}%.";
        }

        return new DirectorLoanComplianceResult(
            totalLoans,
            netAssets,
            thresholdAmount,
            exceedsThreshold,
            exceedsThreshold,
            totalInterestDue,
            details,
            warning
        );
    }

    /// <summary>
    /// Generate s.307 disclosure note text for all director loans.
    /// </summary>
    public async Task<string> GenerateSection307NoteAsync(int companyId, int periodId)
    {
        var compliance = await GetComplianceStatusAsync(companyId, periodId);
        if (compliance.Loans.Count == 0)
            return "No loans, quasi-loans, or credit transactions with directors existed during the financial year.";

        var lines = new List<string>
        {
            "In accordance with Section 307 of the Companies Act 2014, the following transactions with directors are disclosed:",
            ""
        };

        foreach (var loan in compliance.Loans)
        {
            lines.Add($"Director: {loan.DirectorName}");
            lines.Add($"  Opening balance at start of year: €{loan.OpeningBalance:N2}");
            lines.Add($"  Maximum amount outstanding during year: €{loan.MaxDuringYear:N2}");
            lines.Add($"  Interest charged: €{loan.InterestCharged:N2}");
            lines.Add($"  Closing balance at year end: €{loan.ClosingBalance:N2}");
            lines.Add($"  Loan documented: {(loan.IsDocumented ? "Yes" : "No — deemed repayable on demand at statutory rate")}");
            lines.Add("");
        }

        if (compliance.ExceedsThreshold)
        {
            lines.Add($"Note: Aggregate director loans exceed 10% of the company's net relevant assets. "
                     + "The loans were made in accordance with the provisions of Section 239 of the Companies Act 2014.");
        }

        return string.Join("\n", lines);
    }
}
