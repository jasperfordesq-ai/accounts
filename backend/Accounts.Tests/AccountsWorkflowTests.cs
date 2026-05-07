using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuestPDF.Infrastructure;
using Xunit;

namespace Accounts.Tests;

public class AccountsWorkflowTests
{
    static AccountsWorkflowTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Fact]
    public async Task SizeClassification_FirstYearMicro_AllowsMicroAndAuditExemption()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            Turnover = 120_000m,
            BalanceSheetTotal = 30_000m,
            AvgEmployees = 2
        });
        await db.SaveChangesAsync();

        var service = new SizeClassificationService(db, Options.Create(new SizeThresholdConfig()));

        var result = await service.ClassifyAsync(period.Id);

        Assert.Equal(CompanySizeClass.Micro, result.CalculatedClass);
        Assert.True(result.CanUseMicro);
        Assert.True(result.AuditExempt);
        Assert.Contains("Micro", result.AvailableRegimes[0]);
    }

    [Fact]
    public async Task FilingRegime_MicroClassification_DefaultsToMicroRequirements()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            Turnover = 100_000m,
            BalanceSheetTotal = 20_000m,
            AvgEmployees = 1,
            CalculatedClass = CompanySizeClass.Micro
        });
        await db.SaveChangesAsync();

        var service = new FilingRegimeService(db);

        var result = await service.DetermineAsync(period.Id);

        Assert.Equal(ElectedRegime.Micro, result.Regime);
        Assert.True(result.CanUseMicro);
        Assert.Contains(result.RequiredStatements, s => s.Contains("s.280D"));
    }

    [Fact]
    public async Task BalanceSheet_ExposesUnexplainedDifferenceInsteadOfPluggingReserves()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.BankAccounts.Add(new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 100m,
            Currency = "EUR"
        });
        db.ShareCapitals.Add(new ShareCapital
        {
            CompanyId = period.CompanyId,
            ShareClass = "Ordinary",
            NumberIssued = 1,
            NominalValue = 1m,
            TotalValue = 1m
        });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var balanceSheet = await service.GetBalanceSheetAsync(period.Id);

        Assert.False(balanceSheet.Balances);
        Assert.Equal(99m, balanceSheet.CapitalAndReserves.UnexplainedDifference);
        Assert.Equal(0m, balanceSheet.CapitalAndReserves.RetainedEarnings);
        Assert.Equal(1m, balanceSheet.CapitalAndReserves.Total);
    }

    [Fact]
    public async Task Readiness_UsesActualBalanceSheetEquation()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            CalculatedClass = CompanySizeClass.Micro,
            Turnover = 0m,
            BalanceSheetTotal = 100m,
            AvgEmployees = 0
        });
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        db.BankAccounts.Add(new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 100m
        });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var readiness = await service.GetReadinessScoreAsync(period.Id);

        Assert.False(readiness.BalanceSheetBalances);
        Assert.Contains(readiness.Warnings, w => w.Contains("Balance sheet does not balance"));
    }

    [Fact]
    public async Task MicroCroPack_IsDistinctFromApprovalPack()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = ElectedRegime.Micro,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = true
        });
        db.NotesDisclosures.Add(new NotesDisclosure
        {
            PeriodId = period.Id,
            NoteNumber = 1,
            Title = "Approval of Financial Statements",
            Content = "Approved by the directors.",
            IsRequired = true,
            IsIncluded = true
        });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);

        var approvalPack = await documents.GenerateAccountsPackageAsync(period.Id);
        var croPack = await documents.GenerateCroFilingPackAsync(period.Id);

        Assert.NotEmpty(approvalPack);
        Assert.NotEmpty(croPack);
        Assert.NotEqual(approvalPack.Length, croPack.Length);
    }

    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AccountsDbContext(options);
    }

    private static async Task<AccountingPeriod> SeedCompanyPeriodAsync(AccountsDbContext db, bool isFirstYear)
    {
        var company = new Company
        {
            LegalName = "Example Micro Limited",
            CroNumber = "123456",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2025, 1, 1),
            ArdMonth = 9,
            IsTrading = true,
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        db.CompanyOfficers.AddRange(
            new CompanyOfficer { CompanyId = company.Id, Name = "A Director", Role = OfficerRole.Director },
            new CompanyOfficer { CompanyId = company.Id, Name = "B Secretary", Role = OfficerRole.Secretary }
        );

        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = isFirstYear
        };
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();

        return period;
    }
}
