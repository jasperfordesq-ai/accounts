using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Accounts.Tests;

// Batch B fix round: a company holding a charitable tax exemption has no Revenue (CT1) obligations,
// except for a period whose corporation-tax liability is confirmed as due (for the current amount).
// Every Revenue suppression site goes through CorporationTaxObligation so the sites cannot drift.
public partial class AccountsWorkflowTests
{
    [Fact]
    public async Task ExemptCompany_WithCorporationTaxConfirmedDue_GetsRevenueObligationsLikeANonExemptCompany()
    {
        await using var db = CreateDbContext();
        var period = await SeedExemptCorporationTaxPeriodAsync(db, confirmed: true);
        var service = new DeadlineService(db);

        var calculated = await service.CalculateDeadlinesAsync(period.CompanyId, period.Id);

        var revenue = Assert.Single(calculated, deadline => deadline.DeadlineType == DeadlineType.Revenue);
        Assert.Equal(CorporationTaxFilingSupportCalculator.ReturnAndBalanceDueDate(period.PeriodEnd), revenue.DueDate);
        Assert.Contains(await service.GetDeadlinesAsync(period.CompanyId), deadline => deadline.DeadlineType == DeadlineType.Revenue);
        // The reminder and dashboard queries use the same predicate.
        Assert.Contains(
            await db.FilingDeadlines.Where(deadline => deadline.CompanyId == period.CompanyId).Where(CorporationTaxObligation.DeadlineApplies).ToListAsync(),
            deadline => deadline.DeadlineType == DeadlineType.Revenue);
        Assert.True(await CorporationTaxObligation.RevenueAppliesAsync(db, period.Id));
        // The deadline has passed (period ended 31 Dec 2025), so the filing workflow warns about it.
        var statements = new FinancialStatementsService(db);
        var workflow = new FilingWorkflowService(db, statements, new IxbrlService(db, statements));
        Assert.Contains((await workflow.GetStatusAsync(period.CompanyId, period.Id)).WarningIssues,
            warning => warning.StartsWith("Revenue deadline passed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExemptCompany_WithoutAValidConfirmation_KeepsRevenueSuppressed()
    {
        await using var db = CreateDbContext();
        var period = await SeedExemptCorporationTaxPeriodAsync(db, confirmed: true);
        var service = new DeadlineService(db);
        await service.CalculateDeadlinesAsync(period.CompanyId, period.Id);

        // The liability no longer matches the confirmed amount: the confirmation does not count.
        var balance = await db.TaxBalances.SingleAsync(t => t.PeriodId == period.Id);
        balance.Liability = 175m;
        balance.Balance = 175m;
        await db.SaveChangesAsync();

        Assert.False(await CorporationTaxObligation.RevenueAppliesAsync(db, period.Id));
        Assert.DoesNotContain(await service.GetDeadlinesAsync(period.CompanyId), deadline => deadline.DeadlineType == DeadlineType.Revenue);
        Assert.DoesNotContain(
            await db.FilingDeadlines.Where(deadline => deadline.CompanyId == period.CompanyId).Where(CorporationTaxObligation.DeadlineApplies).ToListAsync(),
            deadline => deadline.DeadlineType == DeadlineType.Revenue);
        var statements = new FinancialStatementsService(db);
        var workflow = new FilingWorkflowService(db, statements, new IxbrlService(db, statements));
        Assert.DoesNotContain((await workflow.GetStatusAsync(period.CompanyId, period.Id)).WarningIssues,
            warning => warning.StartsWith("Revenue deadline", StringComparison.Ordinal));

        // Recalculation removes the unfiled Revenue deadline, as for any exempt company.
        var recalculated = await service.CalculateDeadlinesAsync(period.CompanyId, period.Id);
        Assert.DoesNotContain(recalculated, deadline => deadline.DeadlineType == DeadlineType.Revenue);
        Assert.DoesNotContain(await db.FilingDeadlines.Where(d => d.PeriodId == period.Id).ToListAsync(), d => d.DeadlineType == DeadlineType.Revenue);
    }

    [Fact]
    public async Task ExemptCompany_WithNothingConfirmed_HasNoRevenueDeadline()
    {
        await using var db = CreateDbContext();
        var period = await SeedExemptCorporationTaxPeriodAsync(db, confirmed: false);

        var calculated = await new DeadlineService(db).CalculateDeadlinesAsync(period.CompanyId, period.Id);

        Assert.Equal([DeadlineType.CRO, DeadlineType.Charity], calculated.Select(d => d.DeadlineType).OrderBy(type => type).ToArray());
        Assert.False(await CorporationTaxObligation.RevenueAppliesAsync(db, period.Id));
    }

    [Fact]
    public void RevenueSuppressionSites_AllUseTheSharedCorporationTaxObligationRule()
    {
        var root = Path.Combine(RepositoryRoot(), "backend", "Accounts.Api");
        // Every suppression site, counted in code (comments stripped), so removing any one call fails.
        var expected = new (string File, string Call, int Count)[]
        {
            (Path.Combine("Services", "DeadlineService.cs"), ".Where(CorporationTaxObligation.DeadlineApplies)", 4),
            (Path.Combine("Services", "DeadlineService.cs"), "CorporationTaxObligation.RevenueAppliesAsync(", 1),
            (Path.Combine("Services", "DashboardDeadlineService.cs"), ".Where(CorporationTaxObligation.RevenueAppliesToPeriod)", 1),
            (Path.Combine("Services", "DeadlineReminderService.cs"), ".Where(CorporationTaxObligation.DeadlineApplies)", 1),
            (Path.Combine("Services", "FilingWorkflowService.cs"), ".Where(CorporationTaxObligation.DeadlineApplies)", 1),
            (Path.Combine("Endpoints", "PeriodStatusEndpoint.cs"), "CorporationTaxObligation.RevenueAppliesAsync(", 1),
            (Path.Combine("Services", "AdjustmentService.cs"), "CorporationTaxObligation.IsConfirmedDueAsync(", 1),
            (Path.Combine("Services", "FinancialStatementsService.cs"), "CorporationTaxObligation.IsConfirmedDueAsync(", 1)
        };
        foreach (var (file, call, count) in expected)
        {
            var code = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(Path.Combine(root, file)), @"//[^\r\n]*", "");
            Assert.True(code.Split(call).Length - 1 == count, $"{file} should call {call} {count} time(s).");
            // The provision and readiness readers test the exemption itself; only deadline sites must not.
            if (!file.EndsWith("AdjustmentService.cs", StringComparison.Ordinal) && !file.EndsWith("FinancialStatementsService.cs", StringComparison.Ordinal))
                Assert.DoesNotContain("HoldsCharitableTaxExemption", code, StringComparison.Ordinal);
            Assert.DoesNotContain("ConfirmedDueAt != null", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ExemptCompany_WithAStaleUnconfirmedRevenueDeadline_NeitherShowsItAsUpcomingNorAcceptsAnOverride()
    {
        await using var db = CreateDbContext();
        var period = await SeedExemptCorporationTaxPeriodAsync(db, confirmed: true);
        var service = new DeadlineService(db);
        await service.CalculateDeadlinesAsync(period.CompanyId, period.Id);
        // The confirmation stops counting, leaving a stale unfiled Revenue row due before everything else.
        var balance = await db.TaxBalances.SingleAsync(t => t.PeriodId == period.Id);
        balance.Liability = 175m;
        balance.Balance = 175m;
        var stale = await db.FilingDeadlines.SingleAsync(d => d.PeriodId == period.Id && d.DeadlineType == DeadlineType.Revenue);
        stale.DueDate = new DateOnly(2026, 1, 1);
        await db.SaveChangesAsync();

        var upcoming = await service.GetUpcomingDeadlineAsync(period.CompanyId);
        Assert.NotNull(upcoming);
        Assert.NotEqual(DeadlineType.Revenue, upcoming!.DeadlineType);
        var overrideError = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RecordManualOverrideAsync(
            period.CompanyId,
            period.Id,
            DeadlineType.Revenue,
            new DateOnly(2026, 12, 1),
            "Fictitious override reason for a stale Revenue deadline",
            "Fictitious evidence reference",
            new string('a', 64),
            AuthenticatedRole("Owner")));
        Assert.Equal("Deadline not found. Calculate deadlines first.", overrideError.Message);
    }

    [Fact]
    public async Task ConfirmingCorporationTaxDue_RecalculatesThePeriodsDeadlines_AndClearingItRemovesTheRevenueDeadline()
    {
        await using var db = CreateDbContext();
        var period = await SeedExemptCorporationTaxPeriodAsync(db, confirmed: false);
        await new DeadlineService(db).CalculateDeadlinesAsync(period.CompanyId, period.Id);
        Assert.False(await db.FilingDeadlines.AnyAsync(d => d.PeriodId == period.Id && d.DeadlineType == DeadlineType.Revenue));
        var basePath = $"/api/companies/{period.CompanyId}/periods/{period.Id}/tax-balances";

        var confirmed = await YearEndEndpoints.ConfirmCorporationTaxDueEndpointAsync(
            period.CompanyId,
            period.Id,
            new CorporationTaxDueConfirmationInput("Income outside the exemption", 1_200m, 0m),
            db,
            new AuditService(db),
            AuthenticatedRequest("Owner", HttpMethods.Post, $"{basePath}/CorporationTax/confirm-due"));

        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(confirmed));
        var revenue = await db.FilingDeadlines.SingleAsync(d => d.PeriodId == period.Id && d.DeadlineType == DeadlineType.Revenue);
        Assert.Equal(CorporationTaxFilingSupportCalculator.ReturnAndBalanceDueDate(period.PeriodEnd), revenue.DueDate);

        var changed = await YearEndEndpoints.UpsertTaxBalanceEndpointAsync(
            period.CompanyId,
            period.Id,
            TaxType.CorporationTax,
            new TaxBalanceInput { Liability = 175m, Paid = 0m, Balance = 175m },
            db,
            new AuditService(db),
            AuthenticatedRequest("Owner", HttpMethods.Put, $"{basePath}/CorporationTax"));

        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(changed));
        Assert.False(await db.FilingDeadlines.AnyAsync(d => d.PeriodId == period.Id && d.DeadlineType == DeadlineType.Revenue));
    }

    // Fix round 3: a refused recalculation keeps the confirmation and logs a warning naming only the
    // company, the period and the exception type (no message text, no personal data).
    [Fact]
    public async Task ConfirmingCorporationTaxDue_WhenTheDeadlineRecalculationIsRefused_KeepsTheConfirmationAndLogsAWarning()
    {
        await using var db = CreateDbContext();
        // An exempt CLG without a confirmed Annual Return Date: CalculateDeadlinesAsync refuses.
        var company = new Company
        {
            TenantId = 1,
            LegalName = "Example Exempt CLG",
            CroNumber = "C80000",
            CompanyType = CompanyType.CompanyLimitedByGuarantee,
            IncorporationDate = new DateOnly(2025, 1, 1),
            IsCharitableOrganisation = true,
            HoldsCharitableTaxExemption = true,
            CharitableTaxExemptionReference = "CHY-00000"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var period = new AccountingPeriod { CompanyId = company.Id, PeriodStart = new DateOnly(2025, 1, 1), PeriodEnd = new DateOnly(2025, 12, 31), IsFirstYear = true };
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();
        db.TaxBalances.Add(new TaxBalance { PeriodId = period.Id, TaxType = TaxType.CorporationTax, Liability = 150m, Balance = 150m });
        await db.SaveChangesAsync();
        var logger = new CapturingLogger<DeadlineService>();

        var result = await YearEndEndpoints.ConfirmCorporationTaxDueEndpointAsync(
            period.CompanyId,
            period.Id,
            new CorporationTaxDueConfirmationInput("Income outside the exemption", 1_200m, 0m),
            db,
            new AuditService(db),
            AuthenticatedRequest("Owner", HttpMethods.Post, $"/api/companies/{period.CompanyId}/periods/{period.Id}/tax-balances/CorporationTax/confirm-due"),
            deadlineLogger: logger);

        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(result));
        Assert.True(await CorporationTaxObligation.IsConfirmedDueAsync(db, period.Id));
        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Null(warning.Exception);
        Assert.Contains($"company {period.CompanyId}", warning.Message, StringComparison.Ordinal);
        Assert.Contains($"period {period.Id}", warning.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(BusinessRuleException), warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Annual Return Date", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Income outside the exemption", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Example User", warning.Message, StringComparison.Ordinal);
    }

    private static async Task<AccountingPeriod> SeedExemptCorporationTaxPeriodAsync(AccountsDbContext db, bool confirmed)
    {
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var company = await db.Companies.SingleAsync(candidate => candidate.Id == period.CompanyId);
        company.IsCharitableOrganisation = true;
        company.HoldsCharitableTaxExemption = true;
        company.CharitableTaxExemptionReference = "CHY-00000";
        company.CharitableTaxExemptionConfirmedDate = new DateOnly(2022, 12, 16);
        db.TaxBalances.Add(new TaxBalance
        {
            PeriodId = period.Id,
            TaxType = TaxType.CorporationTax,
            Liability = 150m,
            Balance = 150m,
            ConfirmedDueBy = confirmed ? "Example Reviewer" : null,
            ConfirmedDueAt = confirmed ? DateTime.UtcNow : null,
            ConfirmedDueReason = confirmed ? "Income outside the exemption" : null,
            ConfirmedDueLiability = confirmed ? 150m : null,
            ConfirmedTaxableProfitOutsideExemption = confirmed ? 1_200m : null,
            ConfirmedNonTradingProfitOutsideExemption = confirmed ? 0m : null
        });
        await db.SaveChangesAsync();
        return period;
    }
}
