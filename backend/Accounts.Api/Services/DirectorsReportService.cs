using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Accounts.Api.Services;

public class DirectorsReportService(AccountsDbContext db, FinancialStatementsService statementsService)
{
    public const string PrincipalActivitiesReviewKey = "directors-report-principal-activities";
    public const string AuditInformationReviewKey = "directors-report-audit-information";

    public sealed record OfficerServicePeriod(
        string Name,
        string Role,
        string AppointedDate,
        string? ResignedDate);

    public record DirectorsReportData(
        string CompanyName,
        string PeriodStart,
        string PeriodEnd,
        List<string> DirectorNames,
        List<OfficerServicePeriod> DirectorServicePeriods,
        string? SecretaryName,
        OfficerServicePeriod? SecretaryServicePeriod,
        string PrincipalActivities,
        bool PrincipalActivitiesReviewed,
        string? PrincipalActivitiesReviewedBy,
        DateTime? PrincipalActivitiesReviewedAt,
        string ResultsAndDividends,
        decimal ProfitOrLossAfterTax,
        decimal DividendsPaid,
        decimal DividendsDeclaredNotPaid,
        string AccountingRecordsStatement,
        string? PostBalanceSheetEvents,
        bool PostBalanceSheetEventsReviewed,
        string? GoingConcernStatement,
        string? AuditInformationStatement,
        bool AuditInformationEvidenceRequired,
        bool AuditInformationEvidenceRecorded,
        string? AuditInformationConfirmedBy,
        DateTime? AuditInformationConfirmedAt,
        bool OfficerTimelineComplete,
        bool IsMicroExempt,
        bool IsSmallExemptFromBusinessReview,
        string ElectedRegime);

    public async Task<DirectorsReportData> GenerateAsync(int companyId, int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(item => item.Company).ThenInclude(company => company.Officers)
            .Include(item => item.Company).ThenInclude(company => company.CharityInfo)
            .Include(item => item.FilingRegime)
            .Include(item => item.Dividends)
            .Include(item => item.PostBalanceSheetEvents)
            .Include(item => item.YearEndReviewConfirmations)
            .FirstOrDefaultAsync(item => item.Id == periodId && item.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        var company = period.Company;
        var regime = period.FilingRegime?.ElectedRegime
            ?? throw new BusinessRuleException("Cannot generate directors' report data until the filing regime has been determined.");
        var isMicro = regime == ElectedRegime.Micro;
        var isSmall = regime <= ElectedRegime.SmallAbridged;

        var reportingOfficers = company.Officers
            .Where(officer => ServedDuring(officer, period.PeriodStart, period.PeriodEnd))
            .OrderBy(officer => officer.AppointedDate)
            .ThenBy(officer => officer.Name, StringComparer.Ordinal)
            .ToList();
        var directors = reportingOfficers
            .Where(officer => officer.Role == OfficerRole.Director)
            .ToList();
        var secretary = reportingOfficers
            .FirstOrDefault(officer => officer.Role is OfficerRole.Secretary or OfficerRole.CompanySecretary);
        var officerTimelineComplete = company.Officers
            .Where(officer => officer.Role is OfficerRole.Director or OfficerRole.Secretary or OfficerRole.CompanySecretary)
            .All(officer => officer.AppointedDate is not null)
            && directors.Count > 0;

        var principalActivitiesReview = CompleteReview(period, PrincipalActivitiesReviewKey, requireNote: true);
        var auditInformationReview = CompleteReview(period, AuditInformationReviewKey, requireNote: true);
        var postBalanceSheetReview = CompleteReview(period, "post-balance-sheet-events", requireNote: true);

        decimal profitAfterTax;
        try
        {
            var profitAndLoss = await statementsService.GetProfitAndLossAsync(companyId, periodId);
            profitAfterTax = profitAndLoss.ProfitAfterTax;
        }
        catch
        {
            throw new BusinessRuleException("Cannot generate directors' report data until the profit and loss account can be generated.");
        }

        var paidDividends = period.Dividends
            .Where(dividend => dividend.DatePaid >= period.PeriodStart && dividend.DatePaid <= period.PeriodEnd)
            .Sum(dividend => dividend.Amount);
        var declaredNotPaid = period.Dividends
            .Where(dividend => dividend.DateDeclared >= period.PeriodStart
                && dividend.DateDeclared <= period.PeriodEnd
                && (dividend.DatePaid is null || dividend.DatePaid > period.PeriodEnd))
            .Sum(dividend => dividend.Amount);
        var resultsText = ResultsAndDividendsText(profitAfterTax, paidDividends, declaredNotPaid);

        var accountingRecords =
            $"The directors acknowledge their responsibilities under Sections 281 to 285 of the Companies Act 2014 "
            + "to keep adequate accounting records for the company. The accounting records of the company are maintained at "
            + RegisteredOffice(company) + ".";

        string? postBalanceSheetText = null;
        if (period.PostBalanceSheetEvents.Count > 0)
        {
            var events = period.PostBalanceSheetEvents
                .OrderBy(item => item.EventDate)
                .Select(item => $"- {item.EventDate:dd MMMM yyyy}: {item.Description}"
                    + (item.IsAdjusting ? " (adjusting event)" : " (non-adjusting event)"));
            postBalanceSheetText = "The following significant events have occurred since the balance sheet date:\n"
                + string.Join("\n", events);
        }
        else if (postBalanceSheetReview is not null)
        {
            postBalanceSheetText = "There have been no significant events affecting the company since the financial year end.";
        }

        string? goingConcernText = null;
        if (!period.GoingConcernConfirmed)
        {
            goingConcernText = period.GoingConcernNote
                ?? "The directors have identified material uncertainties related to events or conditions that may cast significant doubt on the company's ability to continue as a going concern.";
        }

        var auditInformationRequired = period.FilingRegime.AuditExempt == false;
        var auditInformationText = auditInformationRequired && auditInformationReview is not null
            ? "So far as each of the directors is aware, there is no relevant audit information of which the company's statutory auditors are unaware, and each director has taken all the steps that he or she ought to have taken as a director in order to make himself or herself aware of any relevant audit information and to establish that the company's statutory auditors are aware of that information."
            : null;

        // Principal activities are a directors' representation, not something the platform can
        // derive safely from IsTrading or a trading name. Draft output names the missing evidence;
        // final-output readiness below blocks until a named review supplies the exact narrative.
        var activities = principalActivitiesReview?.Note?.Trim()
            ?? "UNREVIEWED - record the directors' approved principal-activities narrative before finalising this report.";

        return new DirectorsReportData(
            company.LegalName,
            period.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            period.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            directors.Select(officer => officer.Name).ToList(),
            directors.Select(ServicePeriod).ToList(),
            secretary?.Name,
            secretary is null ? null : ServicePeriod(secretary),
            activities,
            principalActivitiesReview is not null,
            principalActivitiesReview?.ConfirmedBy,
            principalActivitiesReview?.ConfirmedAt,
            resultsText,
            profitAfterTax,
            paidDividends,
            declaredNotPaid,
            accountingRecords,
            postBalanceSheetText,
            period.PostBalanceSheetEvents.Count > 0 || postBalanceSheetReview is not null,
            goingConcernText,
            auditInformationText,
            auditInformationRequired,
            !auditInformationRequired || auditInformationReview is not null,
            auditInformationReview?.ConfirmedBy,
            auditInformationReview?.ConfirmedAt,
            officerTimelineComplete,
            isMicro,
            isSmall,
            regime.ToString());
    }

    public static bool ServedDuring(CompanyOfficer officer, DateOnly periodStart, DateOnly periodEnd) =>
        officer.AppointedDate is { } appointed
        && appointed <= periodEnd
        && (officer.ResignedDate is null || officer.ResignedDate >= periodStart);

    public static bool IsCompleteReview(
        YearEndReviewConfirmation review,
        string sectionKey,
        bool requireNote = true) =>
        string.Equals(review.SectionKey, sectionKey, StringComparison.OrdinalIgnoreCase)
        && review.Confirmed
        && !string.IsNullOrWhiteSpace(review.ConfirmedBy)
        && review.ConfirmedAt.Kind == DateTimeKind.Utc
        && review.ConfirmedAt <= DateTime.UtcNow.AddMinutes(5)
        && (!requireNote || (review.Note?.Trim().Length ?? 0) >= 20);

    private static YearEndReviewConfirmation? CompleteReview(
        AccountingPeriod period,
        string sectionKey,
        bool requireNote) =>
        period.YearEndReviewConfirmations.FirstOrDefault(review => IsCompleteReview(review, sectionKey, requireNote));

    private static OfficerServicePeriod ServicePeriod(CompanyOfficer officer) => new(
        officer.Name,
        officer.Role.ToString(),
        officer.AppointedDate!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        officer.ResignedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    private static string ResultsAndDividendsText(
        decimal profitOrLossAfterTax,
        decimal dividendsPaid,
        decimal dividendsDeclaredNotPaid)
    {
        var result = profitOrLossAfterTax switch
        {
            > 0 => $"The profit for the financial year after corporation tax was {Euro(profitOrLossAfterTax)}.",
            < 0 => $"The loss for the financial year after corporation tax was {Euro(Math.Abs(profitOrLossAfterTax))}.",
            _ => "The company recorded neither a profit nor a loss for the financial year after corporation tax."
        };
        var paid = dividendsPaid > 0
            ? $" Dividends of {Euro(dividendsPaid)} were paid during the financial year."
            : " No dividends were paid during the financial year.";
        var declared = dividendsDeclaredNotPaid > 0
            ? $" Dividends of {Euro(dividendsDeclaredNotPaid)} were declared during the financial year but remained unpaid at the reporting date."
            : string.Empty;

        return result + paid + declared;
    }

    private static string RegisteredOffice(Company company)
    {
        var parts = new[]
        {
            company.RegisteredOfficeAddress1,
            company.RegisteredOfficeAddress2,
            company.RegisteredOfficeCity,
            company.RegisteredOfficeCounty is null ? null : $"Co. {company.RegisteredOfficeCounty}",
            company.RegisteredOfficeEircode
        }.Where(part => !string.IsNullOrWhiteSpace(part));
        var address = string.Join(", ", parts);
        return string.IsNullOrWhiteSpace(address) ? "the registered office recorded for the company" : address;
    }

    private static string Euro(decimal value) =>
        string.Create(CultureInfo.GetCultureInfo("en-IE"), $"€{value:N2}");
}
