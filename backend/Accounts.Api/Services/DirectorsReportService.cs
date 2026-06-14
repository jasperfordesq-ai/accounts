using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public class DirectorsReportService(AccountsDbContext db, FinancialStatementsService statementsService)
{
    public record DirectorsReportData(
        string CompanyName,
        string PeriodStart,
        string PeriodEnd,
        List<string> DirectorNames,
        string? SecretaryName,
        string PrincipalActivities,
        string ResultsAndDividends,
        string AccountingRecordsStatement,
        string? PostBalanceSheetEvents,
        string? GoingConcernStatement,
        string? AuditInformationStatement,
        bool IsMicroExempt,
        bool IsSmallExemptFromBusinessReview,
        string ElectedRegime
    );

    public async Task<DirectorsReportData> GenerateAsync(int companyId, int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company).ThenInclude(c => c.Officers)
            .Include(p => p.FilingRegime)
            .Include(p => p.Dividends)
            .Include(p => p.PostBalanceSheetEvents)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        var company = period.Company;
        var regime = period.FilingRegime?.ElectedRegime ?? Entities.ElectedRegime.Small;
        var isMicro = regime == Entities.ElectedRegime.Micro;
        var isSmall = regime <= Entities.ElectedRegime.SmallAbridged;

        var directors = company.Officers
            .Where(o => o.Role == OfficerRole.Director)
            .Select(o => o.Name).ToList();
        var secretary = company.Officers
            .FirstOrDefault(o => o.Role == OfficerRole.Secretary || o.Role == OfficerRole.CompanySecretary)?.Name;

        // Results and dividends
        decimal profitAfterTax;
        try
        {
            var pl = await statementsService.GetProfitAndLossAsync(companyId, periodId);
            profitAfterTax = pl.ProfitAfterTax;
        }
        catch
        {
            throw new BusinessRuleException("Cannot generate directors' report data until the profit and loss account can be generated.");
        }
        var totalDividends = period.Dividends.Sum(d => d.Amount);
        var resultsText = totalDividends > 0
            ? $"The profit for the financial year after providing for corporation tax amounted to \u20ac{profitAfterTax:N0}. The directors paid/proposed dividends of \u20ac{totalDividends:N0} during the year and recommend that the remaining profit of \u20ac{profitAfterTax - totalDividends:N0} be transferred to retained earnings."
            : $"The profit for the financial year after providing for corporation tax amounted to \u20ac{profitAfterTax:N0}. The directors recommend that the profit be transferred to retained earnings.";

        // Accounting records statement (s.281-285)
        var accountingRecords = $"The directors acknowledge their responsibilities under Sections 281 to 285 of the Companies Act 2014 to keep adequate accounting records for the company. The accounting records of the company are maintained at the registered office at {company.RegisteredOfficeAddress1 ?? "the company's principal place of business"}, {company.RegisteredOfficeCity ?? ""}, {(company.RegisteredOfficeCounty != null ? $"Co. {company.RegisteredOfficeCounty}" : "")}.";

        // Post-balance-sheet events
        string? pbseText = null;
        if (period.PostBalanceSheetEvents.Count > 0)
        {
            var events = period.PostBalanceSheetEvents.Select(e => $"- {e.EventDate:dd MMMM yyyy}: {e.Description}" + (e.IsAdjusting ? " (adjusting event)" : " (non-adjusting event)"));
            pbseText = "The following significant events have occurred since the balance sheet date:\n" + string.Join("\n", events);
        }
        else
        {
            pbseText = "There have been no significant events affecting the company since the financial year end.";
        }

        // Going concern
        string? goingConcernText = null;
        if (!period.GoingConcernConfirmed)
        {
            goingConcernText = period.GoingConcernNote ?? "The directors have identified material uncertainties related to events or conditions that may cast significant doubt on the company's ability to continue as a going concern.";
        }

        // Audit information (only if audited)
        string? auditInfoText = null;
        if (period.FilingRegime?.AuditExempt == false)
        {
            auditInfoText = "So far as each of the directors is aware, there is no relevant audit information of which the company's statutory auditors are unaware, and each director has taken all the steps that he or she ought to have taken as a director in order to make himself or herself aware of any relevant audit information and to establish that the company's statutory auditors are aware of that information.";
        }

        // Principal activities
        var activities = company.IsTrading
            ? $"The principal activity of the company during the year was {(company.TradingName != null ? $"trading as {company.TradingName}" : "its main business operations")}."
            : "The company was dormant during the financial year.";

        return new DirectorsReportData(
            company.LegalName,
            period.PeriodStart.ToString("dd MMMM yyyy"),
            period.PeriodEnd.ToString("dd MMMM yyyy"),
            directors,
            secretary,
            activities,
            resultsText,
            accountingRecords,
            pbseText,
            goingConcernText,
            auditInfoText,
            isMicro,
            isSmall,
            regime.ToString()
        );
    }
}
