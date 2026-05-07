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
    public async Task Readiness_RequiresGoingConcernAssessmentAndReviewedNotes()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.GoingConcernConfirmed = false;
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            CalculatedClass = CompanySizeClass.Micro,
            Turnover = 0m,
            BalanceSheetTotal = 1m,
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
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var readiness = await service.GetReadinessScoreAsync(period.Id);

        Assert.Contains("Going concern assessment not completed", readiness.MissingItems);
        Assert.Contains("Notes to the financial statements not generated or reviewed", readiness.MissingItems);
    }

    [Fact]
    public async Task Readiness_RequiresExplicitReviewOfNilYearEndSections()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);

        var service = new FinancialStatementsService(db);

        var readiness = await service.GetReadinessScoreAsync(period.Id);

        Assert.Contains("Debtors and other receivables not reviewed", readiness.MissingItems);
        Assert.Contains("Creditors, accruals and payables not reviewed", readiness.MissingItems);
        Assert.Contains("Payroll and staff status not confirmed", readiness.MissingItems);
        Assert.Contains("Dividends not reviewed", readiness.MissingItems);
        Assert.Contains("Post balance sheet events, related parties, or contingencies not reviewed", readiness.MissingItems);
        Assert.Contains("Going concern assessment not completed", readiness.MissingItems);
    }

    [Fact]
    public async Task Readiness_AcceptsExplicitNilReviewConfirmations()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.YearEndReviewConfirmations.AddRange(
            NilReview(period.Id, "debtors"),
            NilReview(period.Id, "creditors"),
            NilReview(period.Id, "payroll"),
            NilReview(period.Id, "tax"),
            NilReview(period.Id, "dividends"),
            NilReview(period.Id, "post-balance-sheet-events"),
            NilReview(period.Id, "related-parties"),
            NilReview(period.Id, "contingent-liabilities"),
            NilReview(period.Id, "going-concern"));
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var readiness = await service.GetReadinessScoreAsync(period.Id);

        Assert.DoesNotContain("Debtors and other receivables not reviewed", readiness.MissingItems);
        Assert.DoesNotContain("Creditors, accruals and payables not reviewed", readiness.MissingItems);
        Assert.DoesNotContain("Payroll and staff status not confirmed", readiness.MissingItems);
        Assert.DoesNotContain("Tax balances not reviewed", readiness.MissingItems);
        Assert.DoesNotContain("Dividends not reviewed", readiness.MissingItems);
        Assert.DoesNotContain("Post balance sheet events, related parties, or contingencies not reviewed", readiness.MissingItems);
        Assert.DoesNotContain("Going concern assessment not completed", readiness.MissingItems);
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
        await MakePeriodReadyForCroDocumentsAsync(db, period);

        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);

        var approvalPack = await documents.GenerateAccountsPackageAsync(period.Id);
        var croPack = await documents.GenerateCroFilingPackAsync(period.Id);

        Assert.NotEmpty(approvalPack);
        Assert.NotEmpty(croPack);
        Assert.NotEqual(approvalPack.Length, croPack.Length);
    }

    [Fact]
    public async Task CroFilingPack_RequiresConfirmedFilingRegime()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => documents.GenerateCroFilingPackAsync(period.Id));

        Assert.Contains("Confirm the filing regime", error.Message);
    }

    [Fact]
    public async Task CroFilingPack_BlocksWhenReadinessItemsRemainOpen()
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
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => documents.GenerateCroFilingPackAsync(period.Id));

        Assert.Contains("Cannot generate final CRO filing pack", error.Message);
        Assert.Contains("balance sheet does not balance", error.Message);
    }

    [Fact]
    public async Task TrialBalance_PostsImportedTransactionsWithImplicitBankSide()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankCategory = AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var salesCategory = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bankAccount.Id,
            PeriodId = period.Id,
            Date = new DateOnly(2025, 3, 1),
            Description = "Customer receipt",
            Amount = 100m,
            CategoryId = salesCategory.Id
        });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var trialBalance = await service.GetTrialBalanceAsync(period.Id);

        Assert.Equal(100m, trialBalance.Single(l => l.Code == bankCategory.Code).Debit);
        Assert.Equal(100m, trialBalance.Single(l => l.Code == salesCategory.Code).Credit);
        Assert.Equal(trialBalance.Sum(l => l.Debit), trialBalance.Sum(l => l.Credit));
    }

    [Fact]
    public async Task TrialBalance_IncludesReviewedOpeningBalancesAndBankOpeningSide()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankCategory = AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var retainedCategory = AddCategory(db, period.CompanyId, "3100", "Retained Earnings", AccountCategoryType.Equity);
        db.BankAccounts.Add(new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 500m
        });
        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = retainedCategory.Id,
            Credit = 500m,
            SourceNote = "Prior-year signed accounts",
            EnteredBy = "Accounts reviewer",
            Reviewed = true
        });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var trialBalance = await service.GetTrialBalanceAsync(period.Id);

        Assert.Equal(500m, trialBalance.Single(l => l.Code == bankCategory.Code).Debit);
        Assert.Equal(500m, trialBalance.Single(l => l.Code == retainedCategory.Code).Credit);
        Assert.Equal(trialBalance.Sum(l => l.Debit), trialBalance.Sum(l => l.Credit));
    }

    [Fact]
    public async Task StatementSources_ExposeOpeningTransactionsAndAdjustments()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var salesCategory = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var retainedCategory = AddCategory(db, period.CompanyId, "3100", "Retained Earnings", AccountCategoryType.Equity);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 500m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();
        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = retainedCategory.Id,
            Credit = 500m,
            SourceNote = "Prior accounts",
            EnteredBy = "Accounts reviewer",
            Reviewed = true
        });
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bankAccount.Id,
            PeriodId = period.Id,
            Date = new DateOnly(2025, 4, 1),
            Description = "Customer receipt",
            Amount = 250m,
            CategoryId = salesCategory.Id
        });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var sources = await service.GetStatementSourcesAsync(period.Id);
        var bank = sources.Single(s => s.Code == "1400");
        var sales = sources.Single(s => s.Code == "4000");
        var retained = sources.Single(s => s.Code == "3100");

        Assert.Equal(500m, bank.OpeningDebit);
        Assert.Equal(250m, bank.TransactionDebit);
        Assert.Equal(250m, sales.TransactionCredit);
        Assert.Equal(500m, retained.OpeningCredit);
        Assert.Contains(bank.SourceNotes, n => n.Contains("Bank opening balance"));
    }

    [Fact]
    public async Task ProfitAndLoss_IncludesUnpostedYearEndAdjustmentsButNotTaxProvisionTwice()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var salesCategory = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var sundryCategory = AddCategory(db, period.CompanyId, "7900", "Sundry Expenses", AccountCategoryType.Expense);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        db.ImportedTransactions.AddRange(
            new ImportedTransaction
            {
                BankAccountId = bankAccount.Id,
                PeriodId = period.Id,
                Date = new DateOnly(2025, 3, 1),
                Description = "Customer receipt",
                Amount = 1_000m,
                CategoryId = salesCategory.Id
            },
            new ImportedTransaction
            {
                BankAccountId = bankAccount.Id,
                PeriodId = period.Id,
                Date = new DateOnly(2025, 3, 2),
                Description = "Sundry expense",
                Amount = -200m,
                CategoryId = sundryCategory.Id
            });
        db.Adjustments.AddRange(
            new Adjustment
            {
                PeriodId = period.Id,
                Description = "Manual year-end correction",
                Amount = 50m,
                ImpactOnProfit = -50m,
                Source = AdjustmentSource.Manual,
                IsAuto = false
            },
            new Adjustment
            {
                PeriodId = period.Id,
                Description = "Corporation tax provision",
                Amount = 100m,
                ImpactOnProfit = -100m,
                Source = AdjustmentSource.Auto,
                IsAuto = true
            });
        db.TaxBalances.Add(new TaxBalance
        {
            PeriodId = period.Id,
            TaxType = TaxType.CorporationTax,
            Liability = 100m,
            Paid = 0m,
            Balance = 100m
        });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var profitAndLoss = await service.GetProfitAndLossAsync(period.Id);

        Assert.Equal(1_000m, profitAndLoss.Turnover);
        Assert.Equal(200m, profitAndLoss.TotalOverheads);
        Assert.Equal(-50m, profitAndLoss.TotalYearEndAdjustments);
        Assert.Equal(750m, profitAndLoss.ProfitBeforeTax);
        Assert.Equal(100m, profitAndLoss.TaxCharge);
        Assert.Equal(650m, profitAndLoss.ProfitAfterTax);
    }

    [Fact]
    public async Task AutoAdjustments_PostToDebitAndCreditCategories()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FixedAssets.Add(new FixedAsset
        {
            CompanyId = period.CompanyId,
            Name = "Laptop",
            Category = "Computer Equipment",
            Cost = 1_200m,
            AcquisitionDate = new DateOnly(2025, 1, 1),
            UsefulLifeYears = 3,
            DepreciationMethod = DepreciationMethod.StraightLine
        });
        db.Creditors.Add(new Creditor
        {
            PeriodId = period.Id,
            Name = "Accountancy fee",
            Amount = 500m,
            Type = CreditorType.Accrual,
            DueWithinYear = true
        });
        await db.SaveChangesAsync();

        var service = new AdjustmentService(db);

        await service.GenerateAutoAdjustmentsAsync(period.Id);

        var postedAdjustments = await db.Adjustments
            .Where(a => a.PeriodId == period.Id && a.Amount > 0)
            .ToListAsync();
        Assert.NotEmpty(postedAdjustments);
        Assert.All(postedAdjustments, adjustment =>
        {
            Assert.True(adjustment.DebitCategoryId.HasValue, $"{adjustment.Description} missing debit category");
            Assert.True(adjustment.CreditCategoryId.HasValue, $"{adjustment.Description} missing credit category");
        });
    }

    [Fact]
    public async Task FilingWorkflow_BlocksWhenCroCertificationSignatoriesAreMissing()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.CompanyOfficers.RemoveRange(db.CompanyOfficers.Where(o => o.CompanyId == period.CompanyId));
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);

        var status = await workflow.GetStatusAsync(period.CompanyId, period.Id);

        Assert.Contains("No active director recorded for CRO accounts certification.", status.BlockingIssues);
        Assert.Contains("No active company secretary recorded for CRO accounts certification.", status.BlockingIssues);
    }

    [Fact]
    public async Task FilingWorkflow_DoesNotApproveCroFilingWhileBlockersRemain()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflow.UpdateCroStatusAsync(period.CompanyId, period.Id, FilingStatus.Approved, "Reviewer"));

        Assert.Contains("Cannot approve CRO filing while blockers remain", error.Message);
    }

    [Fact]
    public async Task FilingWorkflow_TreatsOverdueDeadlinesAsWarningsNotReadinessBlockers()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingDeadlines.Add(new FilingDeadline
        {
            CompanyId = period.CompanyId,
            PeriodId = period.Id,
            DeadlineType = DeadlineType.CRO,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)
        });
        await db.SaveChangesAsync();

        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);

        var status = await workflow.GetStatusAsync(period.CompanyId, period.Id);

        Assert.Contains(status.WarningIssues, w => w.Contains("CRO deadline passed"));
        Assert.DoesNotContain(status.BlockingIssues, b => b.Contains("CRO deadline passed"));
    }

    [Fact]
    public async Task FilingWorkflow_RequiresPaymentBeforeCroAcceptance()
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
        await MakePeriodReadyForCroDocumentsAsync(db, period);

        var statements = new FinancialStatementsService(db);
        var ixbrl = new IxbrlService(db, statements);
        var workflow = new FilingWorkflowService(db, statements, ixbrl);
        await workflow.MarkDocumentGeneratedAsync(period.CompanyId, period.Id, "accounts");
        await workflow.MarkDocumentGeneratedAsync(period.CompanyId, period.Id, "signature");
        await workflow.UpdateCroStatusAsync(period.CompanyId, period.Id, FilingStatus.Approved, "Reviewer");
        await workflow.UpdateCroStatusAsync(period.CompanyId, period.Id, FilingStatus.Submitted, "Reviewer");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflow.UpdateCroStatusAsync(period.CompanyId, period.Id, FilingStatus.Accepted, "Reviewer"));
        Assert.Contains("Confirm CORE payment", error.Message);

        await workflow.ConfirmCroPaymentAsync(period.CompanyId, period.Id, "Reviewer");
        var accepted = await workflow.UpdateCroStatusAsync(period.CompanyId, period.Id, FilingStatus.Accepted, "Reviewer");

        Assert.Equal(FilingStatus.Accepted, accepted.FilingStatus);
        Assert.True(accepted.PaymentCompleted);
    }

    [Fact]
    public void Deadline_MoveToNextWorkingDay_SkipsIrishPublicHolidays()
    {
        Assert.Equal(new DateOnly(2026, 3, 18), DeadlineService.MoveToNextWorkingDay(new DateOnly(2026, 3, 17)));
        Assert.Equal(new DateOnly(2026, 4, 7), DeadlineService.MoveToNextWorkingDay(new DateOnly(2026, 4, 6)));
        Assert.Equal(new DateOnly(2026, 12, 29), DeadlineService.MoveToNextWorkingDay(new DateOnly(2026, 12, 25)));
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

    private static AccountCategory AddCategory(
        AccountsDbContext db,
        int companyId,
        string code,
        string name,
        AccountCategoryType type)
    {
        var category = new AccountCategory
        {
            CompanyId = companyId,
            Code = code,
            Name = name,
            Type = type,
            IsSystem = true
        };
        db.AccountCategories.Add(category);
        db.SaveChanges();
        return category;
    }

    private static async Task MakePeriodReadyForCroDocumentsAsync(AccountsDbContext db, AccountingPeriod period)
    {
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            CalculatedClass = CompanySizeClass.Micro,
            Turnover = 100m,
            BalanceSheetTotal = 101m,
            AvgEmployees = 1
        });

        var bankCategory = AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var salesCategory = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var shareCapitalCategory = AddCategory(db, period.CompanyId, "3000", "Share Capital", AccountCategoryType.Equity);

        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 1m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = shareCapitalCategory.Id,
            Credit = 1m,
            SourceNote = "Opening share capital per register",
            EnteredBy = "Accounts reviewer",
            Reviewed = true,
            ReviewedBy = "Accounts reviewer",
            ReviewedAt = DateTime.UtcNow
        });
        db.ShareCapitals.Add(new ShareCapital
        {
            CompanyId = period.CompanyId,
            ShareClass = "Ordinary",
            NumberIssued = 1,
            NominalValue = 1m,
            TotalValue = 1m
        });
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bankAccount.Id,
            PeriodId = period.Id,
            Date = new DateOnly(2025, 3, 1),
            Description = "Customer receipt",
            Amount = 100m,
            CategoryId = salesCategory.Id
        });
        db.Adjustments.Add(new Adjustment
        {
            PeriodId = period.Id,
            Description = "No year-end adjustment required",
            Amount = 0m,
            ImpactOnProfit = 0m,
            Source = AdjustmentSource.Manual,
            CreatedBy = "Accounts reviewer",
            ApprovedBy = "Accounts reviewer",
            ApprovedAt = DateTime.UtcNow
        });
        db.YearEndReviewConfirmations.AddRange(
            NilReview(period.Id, "debtors"),
            NilReview(period.Id, "creditors"),
            NilReview(period.Id, "payroll"),
            NilReview(period.Id, "tax"),
            NilReview(period.Id, "dividends"),
            NilReview(period.Id, "post-balance-sheet-events"),
            NilReview(period.Id, "related-parties"),
            NilReview(period.Id, "contingent-liabilities"),
            NilReview(period.Id, "going-concern"));
        await db.SaveChangesAsync();

        _ = bankCategory;
    }

    private static YearEndReviewConfirmation NilReview(int periodId, string sectionKey) => new()
    {
        PeriodId = periodId,
        SectionKey = sectionKey,
        Confirmed = true,
        ConfirmedBy = "Accounts reviewer",
        Note = "Nil position reviewed."
    };
}
