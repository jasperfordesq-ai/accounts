using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounts.Tests;

public sealed class DirectorsReportEvidenceTests
{
    [Fact]
    public async Task Generate_RequiresAnExplicitFilingRegime()
    {
        await using var db = CreateDb();
        var fixture = await SeedAsync(db, regime: null);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Service(db).GenerateAsync(fixture.Company.Id, fixture.Period.Id));

        Assert.Contains("filing regime", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Generate_UsesOnlyDatedOfficerServiceThatOverlapsTheReportingPeriod()
    {
        await using var db = CreateDb();
        var fixture = await SeedAsync(db);
        db.CompanyOfficers.AddRange(
            Officer(fixture.Company.Id, "Retired During Year", OfficerRole.Director, new DateOnly(2020, 1, 1), new DateOnly(2025, 6, 30)),
            Officer(fixture.Company.Id, "Future Director", OfficerRole.Director, new DateOnly(2026, 1, 1)),
            Officer(fixture.Company.Id, "Former Director", OfficerRole.Director, new DateOnly(2020, 1, 1), new DateOnly(2024, 12, 31)),
            Officer(fixture.Company.Id, "Undated Director", OfficerRole.Director, appointed: null));
        AddReview(db, fixture.Period.Id, DirectorsReportService.PrincipalActivitiesReviewKey,
            "The principal activity is the provision of professional services.");
        await db.SaveChangesAsync();

        var report = await Service(db).GenerateAsync(fixture.Company.Id, fixture.Period.Id);

        Assert.Equal(["Retired During Year", "Current Director"], report.DirectorNames);
        Assert.Equal("2025-06-30", report.DirectorServicePeriods[0].ResignedDate);
        Assert.Equal("2025-01-01", report.DirectorServicePeriods[1].AppointedDate);
        Assert.Equal("Company Secretary", report.SecretaryName);
        Assert.False(report.OfficerTimelineComplete);
        Assert.DoesNotContain(report.DirectorNames, name => name is "Future Director" or "Former Director" or "Undated Director");
    }

    [Fact]
    public async Task Generate_EmitsReviewedRepresentationsLossAndDistinctDividendStates()
    {
        await using var db = CreateDb();
        var fixture = await SeedAsync(db, auditExempt: false);
        var bank = new BankAccount
        {
            CompanyId = fixture.Company.Id,
            Name = "Current account",
            OpeningBalanceDate = fixture.Period.PeriodStart
        };
        var expense = new AccountCategory
        {
            CompanyId = fixture.Company.Id,
            Code = "6000",
            Name = "Administrative expenses",
            Type = AccountCategoryType.Expense
        };
        var bankControl = new AccountCategory
        {
            CompanyId = fixture.Company.Id,
            Code = "1400",
            Name = "Bank current account",
            Type = AccountCategoryType.Asset
        };
        db.AddRange(bank, bankControl, expense);
        await db.SaveChangesAsync();
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = fixture.Period.Id,
            Date = new DateOnly(2025, 4, 30),
            Description = "Professional fee paid",
            Amount = -125m,
            CategoryId = expense.Id
        });
        db.Dividends.AddRange(
            new Dividend { PeriodId = fixture.Period.Id, Amount = 10m, DatePaid = new DateOnly(2025, 8, 1) },
            new Dividend { PeriodId = fixture.Period.Id, Amount = 20m, DateDeclared = new DateOnly(2025, 9, 1) },
            new Dividend
            {
                PeriodId = fixture.Period.Id,
                Amount = 5m,
                DateDeclared = new DateOnly(2025, 10, 1),
                DatePaid = new DateOnly(2026, 1, 15)
            });
        AddReview(db, fixture.Period.Id, DirectorsReportService.PrincipalActivitiesReviewKey,
            "The principal activity is the provision of professional services.");
        AddReview(db, fixture.Period.Id, DirectorsReportService.AuditInformationReviewKey,
            "Director enquiries and signed confirmations are retained at reference WP-DR-330.");
        AddReview(db, fixture.Period.Id, "post-balance-sheet-events",
            "No significant post balance sheet events were identified by the directors.");
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var profitAndLoss = await statements.GetProfitAndLossAsync(fixture.Company.Id, fixture.Period.Id);
        Assert.Equal(-125m, profitAndLoss.ProfitAfterTax);
        var report = await new DirectorsReportService(db, statements).GenerateAsync(fixture.Company.Id, fixture.Period.Id);

        Assert.Equal(-125m, report.ProfitOrLossAfterTax);
        Assert.Equal(10m, report.DividendsPaid);
        Assert.Equal(25m, report.DividendsDeclaredNotPaid);
        Assert.Contains("loss", report.ResultsAndDividends, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("€125.00", report.ResultsAndDividends, StringComparison.Ordinal);
        Assert.Contains("remained unpaid", report.ResultsAndDividends, StringComparison.OrdinalIgnoreCase);
        Assert.True(report.PrincipalActivitiesReviewed);
        Assert.Equal("Reviewer Name", report.PrincipalActivitiesReviewedBy);
        Assert.True(report.PostBalanceSheetEventsReviewed);
        Assert.Contains("no significant events", report.PostBalanceSheetEvents, StringComparison.OrdinalIgnoreCase);
        Assert.True(report.AuditInformationEvidenceRequired);
        Assert.True(report.AuditInformationEvidenceRecorded);
        Assert.Contains("each director has taken all the steps", report.AuditInformationStatement, StringComparison.OrdinalIgnoreCase);
        Assert.True(report.OfficerTimelineComplete);
    }

    [Fact]
    public async Task DraftAndFinalReadiness_WithholdUnreviewedStatutoryAssertions()
    {
        await using var db = CreateDb();
        var fixture = await SeedAsync(db, auditExempt: false);
        AddReview(db, fixture.Period.Id, DirectorsReportService.PrincipalActivitiesReviewKey, "Too short");
        AddReview(db, fixture.Period.Id, DirectorsReportService.AuditInformationReviewKey, "Also short");
        db.Dividends.Add(new Dividend { PeriodId = fixture.Period.Id, Amount = 25m });
        await db.SaveChangesAsync();

        var service = Service(db);
        var report = await service.GenerateAsync(fixture.Company.Id, fixture.Period.Id);
        var blockers = await new FinancialStatementsService(db)
            .GetFinalOutputReadinessBlockersAsync(fixture.Company.Id, fixture.Period.Id);

        Assert.StartsWith("UNREVIEWED", report.PrincipalActivities, StringComparison.Ordinal);
        Assert.False(report.PrincipalActivitiesReviewed);
        Assert.Null(report.PostBalanceSheetEvents);
        Assert.False(report.PostBalanceSheetEventsReviewed);
        Assert.Null(report.AuditInformationStatement);
        Assert.False(report.AuditInformationEvidenceRecorded);
        Assert.Contains(blockers, blocker => blocker.Contains("principal-activities narrative", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(blockers, blocker => blocker.Contains("relevant-audit-information", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(blockers, blocker => blocker.Contains("declaration date or payment date", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReviewEndpoint_RejectsTokenDirectorsReportEvidence()
    {
        await using var db = CreateDb();
        var fixture = await SeedAsync(db);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = $"/api/companies/{fixture.Company.Id}/periods/{fixture.Period.Id}/year-end-reviews/{DirectorsReportService.PrincipalActivitiesReviewKey}";
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 7,
            TenantId: 1,
            TenantName: "Practice",
            Email: "reviewer@example.ie",
            DisplayName: "Reviewer Name",
            Role: "Reviewer");

        var result = await YearEndEndpoints.UpdateYearEndReviewEndpointAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            DirectorsReportService.PrincipalActivitiesReviewKey,
            new YearEndReviewInput(true, null, "Too short"),
            db,
            new AuditService(db),
            context);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.False(await db.YearEndReviewConfirmations.AnyAsync());
    }

    private static DirectorsReportService Service(AccountsDbContext db) =>
        new(db, new FinancialStatementsService(db));

    private static AccountsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AccountsDbContext(options);
    }

    private static async Task<(Company Company, AccountingPeriod Period)> SeedAsync(
        AccountsDbContext db,
        ElectedRegime? regime = ElectedRegime.Small,
        bool auditExempt = true)
    {
        var company = new Company
        {
            TenantId = 1,
            LegalName = "Evidence Limited",
            CroNumber = "123456",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2025, 1, 1),
            AnnualReturnDate = new DateOnly(2024, 9, 15),
            IsTrading = true,
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Dublin"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = true
        };
        db.AccountingPeriods.Add(period);
        db.CompanyOfficers.AddRange(
            Officer(company.Id, "Current Director", OfficerRole.Director, new DateOnly(2025, 1, 1)),
            Officer(company.Id, "Company Secretary", OfficerRole.Secretary, new DateOnly(2025, 1, 1)));
        await db.SaveChangesAsync();

        if (regime is { } electedRegime)
        {
            db.FilingRegimes.Add(new FilingRegime
            {
                PeriodId = period.Id,
                ElectedRegime = electedRegime,
                AuditExempt = auditExempt,
                CanUseMicro = electedRegime == ElectedRegime.Micro,
                CanFileAbridged = electedRegime <= ElectedRegime.SmallAbridged
            });
            await db.SaveChangesAsync();
        }

        return (company, period);
    }

    private static CompanyOfficer Officer(
        int companyId,
        string name,
        OfficerRole role,
        DateOnly? appointed,
        DateOnly? resigned = null) => new()
    {
        CompanyId = companyId,
        Name = name,
        Role = role,
        AppointedDate = appointed,
        ResignedDate = resigned
    };

    private static void AddReview(AccountsDbContext db, int periodId, string sectionKey, string note) =>
        db.YearEndReviewConfirmations.Add(new YearEndReviewConfirmation
        {
            PeriodId = periodId,
            SectionKey = sectionKey,
            Confirmed = true,
            ConfirmedBy = "Reviewer Name",
            ConfirmedAt = new DateTime(2026, 1, 20, 12, 0, 0, DateTimeKind.Utc),
            Note = note
        });
}
