using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Middleware;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using QuestPDF.Infrastructure;
using System.Security.Cryptography;
using System.Text;
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
    public async Task FilingRegime_RecentRepeatedLateCroFilings_RemoveAuditExemption()
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
        db.FilingHistories.AddRange(
            new FilingHistory
            {
                CompanyId = period.CompanyId,
                PeriodId = period.Id,
                DeadlineType = DeadlineType.CRO,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)),
                FiledDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1).AddDays(4)),
                DaysLate = 4,
                PenaltyAmount = 112m
            },
            new FilingHistory
            {
                CompanyId = period.CompanyId,
                PeriodId = period.Id,
                DeadlineType = DeadlineType.CRO,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-2)),
                FiledDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-2).AddDays(1)),
                DaysLate = 1,
                PenaltyAmount = 103m
            });
        await db.SaveChangesAsync();

        var service = new FilingRegimeService(db);

        var result = await service.DetermineAsync(period.Id);

        Assert.False(result.AuditExempt);
        Assert.Contains("late CRO filings", result.Summary);
        var saved = await db.FilingRegimes.SingleAsync(f => f.PeriodId == period.Id);
        Assert.False(saved.AuditExempt);
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
    public async Task BalanceSheet_GroupsFixedAssetDepreciationPerAssetNotPerCategoryTotal()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var laptop = new FixedAsset
        {
            CompanyId = period.CompanyId,
            Name = "Laptop",
            Category = "Computer Equipment",
            Cost = 100m,
            AcquisitionDate = period.PeriodStart,
            UsefulLifeYears = 3
        };
        var server = new FixedAsset
        {
            CompanyId = period.CompanyId,
            Name = "Server",
            Category = "Computer Equipment",
            Cost = 200m,
            AcquisitionDate = period.PeriodStart,
            UsefulLifeYears = 4
        };
        db.FixedAssets.AddRange(laptop, server);
        await db.SaveChangesAsync();

        db.DepreciationEntries.AddRange(
            new DepreciationEntry
            {
                AssetId = laptop.Id,
                PeriodId = period.Id,
                OpeningNbv = 100m,
                Charge = 30m,
                ClosingNbv = 70m
            },
            new DepreciationEntry
            {
                AssetId = server.Id,
                PeriodId = period.Id,
                OpeningNbv = 200m,
                Charge = 50m,
                ClosingNbv = 150m
            });
        await db.SaveChangesAsync();

        var service = new FinancialStatementsService(db);

        var balanceSheet = await service.GetBalanceSheetAsync(period.Id);
        var computerEquipment = balanceSheet.FixedAssets.Categories.Single(c => c.Category == "Computer Equipment");

        Assert.Equal(300m, computerEquipment.Cost);
        Assert.Equal(80m, computerEquipment.Depreciation);
        Assert.Equal(220m, computerEquipment.Nbv);
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
    public void AuditExemptionStatement_IsPrintedOnlyWhenConfirmedAvailable()
    {
        Assert.True(DocumentGeneratorService.ShouldIncludeAuditExemptionStatement(ElectedRegime.Micro, auditExempt: true));
        Assert.True(DocumentGeneratorService.ShouldIncludeAuditExemptionStatement(ElectedRegime.SmallAbridged, auditExempt: true));
        Assert.False(DocumentGeneratorService.ShouldIncludeAuditExemptionStatement(ElectedRegime.Micro, auditExempt: false));
        Assert.False(DocumentGeneratorService.ShouldIncludeAuditExemptionStatement(ElectedRegime.Medium, auditExempt: true));
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

    [Fact]
    public async Task ImportCsv_RejectsBankAccountFromAnotherCompanyPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        using var csv = new MemoryStream(Encoding.UTF8.GetBytes("Date,Description,Amount\n01/01/2026,Receipt,100\n"));
        var service = new ImportService(db, Options.Create(new ImportLimitConfig()));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ImportCsvAsync(bankAccount.Id, otherPeriod.Id, csv, "bank.csv"));

        Assert.Contains("does not belong to the company", error.Message);
    }

    [Fact]
    public async Task ImportCsv_EnforcesConfiguredRowLimit()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 0m
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        using var csv = new MemoryStream(Encoding.UTF8.GetBytes("Date,Description,Amount\n01/01/2026,Receipt,100\n02/01/2026,Receipt,200\n"));
        var service = new ImportService(db, Options.Create(new ImportLimitConfig { MaxRows = 1 }));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ImportCsvAsync(bankAccount.Id, period.Id, csv, "bank.csv"));

        Assert.Contains("too many rows", error.Message);
    }

    [Fact]
    public void ProductionSafety_BlocksDemoDatabaseStartupInProduction()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=accounts_dev",
                ["AllowedOrigins:0"] = "http://localhost:3000"
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Production"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = true,
                SeedDemoData = true
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig { Enabled = false, RequireInProduction = true }),
                new TestEnvironment("Production")));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("AutoMigrateOnStartup"));
        Assert.Contains(failures, f => f.Contains("SeedDemoData"));
        Assert.Contains(failures, f => f.Contains("development database password"));
        Assert.Contains(failures, f => f.Contains("localhost"));
        Assert.Contains(failures, f => f.Contains("AuthSession:SigningKey"));
        Assert.Contains(failures, f => f.Contains("ApiAccess:Enabled"));
    }

    [Fact]
    public void ProductionSafety_AllowsDeliberateProductionConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = StrongSessionSigningKey()
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Production"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = false,
                SeedDemoData = false
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig
                {
                    Enabled = true,
                    RequireInProduction = true,
                    Keys =
                    [
                        new ApiAccessKeyConfig
                        {
                            Name = "Production firm",
                            KeyHash = ApiAccessService.HashKey("real-secret")
                        }
                    ]
                }),
                new TestEnvironment("Production")));

        Assert.Empty(service.Validate());
    }

    [Fact]
    public void ProductionSafety_BlocksWeakSessionSigningKeyInProduction()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = new string('a', 64)
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Production"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = false,
                SeedDemoData = false
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig
                {
                    Enabled = true,
                    RequireInProduction = true,
                    Keys =
                    [
                        new ApiAccessKeyConfig
                        {
                            Name = "Production firm",
                            KeyHash = ApiAccessService.HashKey("real-secret")
                        }
                    ]
                }),
                new TestEnvironment("Production")));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("AuthSession:SigningKey"));
    }

    [Fact]
    public void ProductionSafety_BlocksInvalidEncodedSessionSigningKeyInProduction()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = "not-a-base64-session-secret-value!!!!!"
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Production"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = false,
                SeedDemoData = false
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig
                {
                    Enabled = true,
                    RequireInProduction = true,
                    Keys =
                    [
                        new ApiAccessKeyConfig
                        {
                            Name = "Production firm",
                            KeyHash = ApiAccessService.HashKey("real-secret")
                        }
                    ]
                }),
                new TestEnvironment("Production")));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("AuthSession:SigningKey"));
    }

    [Fact]
    public void ProductionSafety_AllowsStrongSessionSigningConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = StrongSessionSigningKey()
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Production"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = false,
                SeedDemoData = false
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig
                {
                    Enabled = true,
                    RequireInProduction = true,
                    Keys =
                    [
                        new ApiAccessKeyConfig
                        {
                            Name = "Production firm",
                            KeyHash = ApiAccessService.HashKey("real-secret")
                        }
                    ]
                }),
                new TestEnvironment("Production")));

        Assert.Empty(service.Validate());
    }

    [Fact]
    public void ProductionSafety_AllowsStrongBase64UrlSessionSigningConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = StrongSessionSigningKeyBase64Url()
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Production"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = false,
                SeedDemoData = false
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig
                {
                    Enabled = true,
                    RequireInProduction = true,
                    Keys =
                    [
                        new ApiAccessKeyConfig
                        {
                            Name = "Production firm",
                            KeyHash = ApiAccessService.HashKey("real-secret")
                        }
                    ]
                }),
                new TestEnvironment("Production")));

        Assert.Empty(service.Validate());
    }

    [Theory]
    [InlineData(14)]
    [InlineData(1441)]
    public void ProductionSafety_BlocksSessionExpiryOutsideProductionRange(int expiryMinutes)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = StrongSessionSigningKey(),
                ["AuthSession:ExpiryMinutes"] = expiryMinutes.ToString()
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Production"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = false,
                SeedDemoData = false
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig
                {
                    Enabled = true,
                    RequireInProduction = true,
                    Keys =
                    [
                        new ApiAccessKeyConfig
                        {
                            Name = "Production firm",
                            KeyHash = ApiAccessService.HashKey("real-secret")
                        }
                    ]
                }),
                new TestEnvironment("Production")));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("AuthSession:ExpiryMinutes"));
    }

    [Fact]
    public void ProductionSafety_BlocksInsecureSessionCookiesInProduction()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Password=not-the-dev-password",
                ["AllowedOrigins:0"] = "https://accounts.example.ie",
                ["AuthSession:SigningKey"] = StrongSessionSigningKey(),
                ["AuthSession:SecureCookiesInProduction"] = "false"
            })
            .Build();
        var service = new ProductionSafetyService(
            new TestEnvironment("Production"),
            config,
            Options.Create(new DatabaseStartupConfig
            {
                AutoMigrateOnStartup = false,
                SeedDemoData = false
            }),
            AuthSessionOptions(config),
            new ApiAccessService(
                Options.Create(new ApiAccessConfig
                {
                    Enabled = true,
                    RequireInProduction = true,
                    Keys =
                    [
                        new ApiAccessKeyConfig
                        {
                            Name = "Production firm",
                            KeyHash = ApiAccessService.HashKey("real-secret")
                        }
                    ]
                }),
                new TestEnvironment("Production")));

        var failures = service.Validate();

        Assert.Contains(failures, f => f.Contains("AuthSession:SecureCookiesInProduction"));
    }

    [Fact]
    public async Task SeedData_DoesNotRewriteNonDemoUserCredentials()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db, "External Firm", "external-firm");
        var user = new UserAccount
        {
            TenantId = tenant.Id,
            Email = "real-user@example.ie",
            DisplayName = "Real User",
            Role = "Owner",
            PasswordHash = "legacy-hash",
            PasswordSalt = "legacy-salt",
            PasswordAlgorithm = "Legacy-SHA1",
            PasswordStrengthScore = 1,
            MustChangePassword = false,
            IsActive = true
        };
        db.UserAccounts.Add(user);
        await db.SaveChangesAsync();
        var userId = user.Id;

        await SeedData.SeedAsync(db);

        db.ChangeTracker.Clear();
        var reloaded = await db.UserAccounts.SingleAsync(u => u.Id == userId);
        Assert.Equal("legacy-hash", reloaded.PasswordHash);
        Assert.Equal("legacy-salt", reloaded.PasswordSalt);
        Assert.Equal("Legacy-SHA1", reloaded.PasswordAlgorithm);
        Assert.Equal(1, reloaded.PasswordStrengthScore);
        Assert.False(reloaded.MustChangePassword);
    }

    [Fact]
    public async Task AuthService_LoginAcceptsValidPasswordAndReturnsTenantPrincipal()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        var user = await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!");
        var service = CreateAuthService(db);

        var result = await service.LoginAsync(" OWNER@EXAMPLE.IE ", "Correct Horse Battery Staple 1!");

        Assert.True(result.Succeeded);
        Assert.Null(result.FailureReason);
        Assert.NotNull(result.User);
        Assert.Equal(user.Id, result.User.UserId);
        Assert.Equal(tenant.Id, result.User.TenantId);
        Assert.Equal("Example Firm", result.User.TenantName);
        Assert.Equal("owner@example.ie", result.User.Email);
        Assert.Equal("Owner User", result.User.DisplayName);
        Assert.Equal("Admin", result.User.Role);
        var saved = await db.UserAccounts.FindAsync(user.Id);
        Assert.NotNull(saved?.LastLoginAt);
    }

    [Fact]
    public async Task AuthService_LoginRejectsWrongPassword()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!");
        var service = CreateAuthService(db);

        var result = await service.LoginAsync("owner@example.ie", "wrong password");

        Assert.False(result.Succeeded);
        Assert.Null(result.User);
        Assert.Contains("Invalid email or password", result.FailureReason);
    }

    [Fact]
    public async Task AuthService_LoginRejectsInactiveUser()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!", isActive: false);
        var service = CreateAuthService(db);

        var result = await service.LoginAsync("owner@example.ie", "Correct Horse Battery Staple 1!");

        Assert.False(result.Succeeded);
        Assert.Null(result.User);
        Assert.Contains("inactive", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthService_LoginRejectsInactiveUserWithWrongPasswordAsInvalidCredentials()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!", isActive: false);
        var service = CreateAuthService(db);

        var result = await service.LoginAsync("owner@example.ie", "wrong password");

        Assert.False(result.Succeeded);
        Assert.Null(result.User);
        Assert.Equal("Invalid email or password.", result.FailureReason);
    }

    [Fact]
    public async Task AuthService_LoginRejectsMissingCredentialsWithFailureReason()
    {
        await using var db = CreateDbContext();
        var service = CreateAuthService(db);

        var result = await service.LoginAsync(" ", null);

        Assert.False(result.Succeeded);
        Assert.Null(result.User);
        Assert.Equal("Email and password are required.", result.FailureReason);
    }

    [Fact]
    public async Task AuthService_LoginRejectsUnsupportedPasswordAlgorithmAsInvalidCredentials()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        await SeedUserAsync(
            db,
            tenant,
            "owner@example.ie",
            "Correct Horse Battery Staple 1!",
            passwordAlgorithm: "PBKDF2-SHA1-1000");
        var service = CreateAuthService(db);

        var result = await service.LoginAsync("owner@example.ie", "Correct Horse Battery Staple 1!");

        Assert.False(result.Succeeded);
        Assert.Null(result.User);
        Assert.Equal("Invalid email or password.", result.FailureReason);
    }

    [Fact]
    public async Task AuthService_SessionRoundTripReturnsActiveUser()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        var user = await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!");
        var service = CreateAuthService(db);
        var login = await service.LoginAsync("owner@example.ie", "Correct Horse Battery Staple 1!");
        var now = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

        var cookieValue = service.CreateSessionCookieValue(login.User!, now);
        var roundTripped = await service.ReadSessionAsync(cookieValue, now.AddMinutes(10));

        Assert.NotNull(roundTripped);
        Assert.Equal(user.Id, roundTripped.UserId);
        Assert.Equal(tenant.Id, roundTripped.TenantId);
        Assert.Equal("Example Firm", roundTripped.TenantName);
        Assert.Equal("owner@example.ie", roundTripped.Email);
        Assert.Equal("Owner User", roundTripped.DisplayName);
        Assert.Equal("Admin", roundTripped.Role);
    }

    [Fact]
    public async Task AuthService_Base64UrlSigningKeyRoundTripsSessionCookie()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        var user = await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!");
        var service = CreateAuthService(db, StrongSessionSigningKeyBase64Url());
        var login = await service.LoginAsync("owner@example.ie", "Correct Horse Battery Staple 1!");
        var now = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

        var cookieValue = service.CreateSessionCookieValue(login.User!, now);
        var roundTripped = await service.ReadSessionAsync(cookieValue, now.AddMinutes(10));

        Assert.NotNull(roundTripped);
        Assert.Equal(user.Id, roundTripped.UserId);
        Assert.Equal(tenant.Id, roundTripped.TenantId);
    }

    [Fact]
    public void AuthService_CreateCookieOptionsClampsExpiryAndSecuresProductionCookies()
    {
        using var db = CreateDbContext();
        var now = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var lowExpiry = CreateAuthService(db, expiryMinutes: 5);
        var highExpiry = CreateAuthService(db, expiryMinutes: 2_000, environmentName: "Production", secureCookiesInProduction: false);

        var lowOptions = lowExpiry.CreateCookieOptions(now);
        var highOptions = highExpiry.CreateCookieOptions(now);
        var clearOptions = highExpiry.ClearCookieOptions();

        Assert.True(lowOptions.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, lowOptions.SameSite);
        Assert.Equal("/", lowOptions.Path);
        Assert.Equal(now.AddMinutes(15), lowOptions.Expires);
        Assert.Equal(now.AddMinutes(1_440), highOptions.Expires);
        Assert.True(highOptions.Secure);
        Assert.True(clearOptions.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, clearOptions.SameSite);
        Assert.Equal("/", clearOptions.Path);
        Assert.True(clearOptions.Secure);
        Assert.True(clearOptions.Expires < DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task AuthService_SessionExpiryUsesClampedExpiryMinutes()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!");
        var service = CreateAuthService(db, expiryMinutes: 5);
        var login = await service.LoginAsync("owner@example.ie", "Correct Horse Battery Staple 1!");
        var now = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

        var cookieValue = service.CreateSessionCookieValue(login.User!, now);
        var stillValidAtClampedExpiry = await service.ReadSessionAsync(cookieValue, now.AddMinutes(14));
        var expiredAfterClampedExpiry = await service.ReadSessionAsync(cookieValue, now.AddMinutes(16));

        Assert.NotNull(stillValidAtClampedExpiry);
        Assert.Null(expiredAfterClampedExpiry);
    }

    [Fact]
    public async Task UserSessionMiddleware_BlocksProtectedApiWithoutSession()
    {
        await using var db = CreateDbContext();
        var auth = CreateAuthService(db);
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Path = "/api/companies";
        var middleware = new UserSessionMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, auth);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task UserSessionMiddleware_AllowsLoginWithoutSession()
    {
        await using var db = CreateDbContext();
        var auth = CreateAuthService(db);
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/auth/login";
        var middleware = new UserSessionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, auth);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task UserSessionMiddleware_LoadsPrincipalFromValidSession()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db);
        var user = await SeedUserAsync(db, tenant, "owner@example.ie", "Correct Horse Battery Staple 1!");
        var auth = CreateAuthService(db);
        var login = await auth.LoginAsync("owner@example.ie", "Correct Horse Battery Staple 1!");
        var cookieValue = auth.CreateSessionCookieValue(login.User!, DateTimeOffset.UtcNow);
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Path = "/api/companies";
        context.Request.Headers.Cookie = $"{auth.CookieName}={cookieValue}";
        var middleware = new UserSessionMiddleware(innerContext =>
        {
            nextCalled = true;
            Assert.Equal(user.Id, AuthContext.RequireUser(innerContext).UserId);
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, auth);

        Assert.True(nextCalled);
    }

    [Fact]
    public void ApiAccess_AllowsAuthEndpointsWithoutServiceKey()
    {
        var service = new ApiAccessService(
            Options.Create(new ApiAccessConfig
            {
                Enabled = true,
                Keys =
                [
                    new ApiAccessKeyConfig
                    {
                        Name = "Firm A",
                        KeyHash = ApiAccessService.HashKey("secret-a")
                    }
                ]
            }),
            new TestEnvironment("Production"));

        var authEndpoint = service.Authorize(null, new PathString("/api/auth/login"), HttpMethods.Post);
        var ordinaryEndpoint = service.Authorize(null, new PathString("/api/companies"), HttpMethods.Get);

        Assert.True(authEndpoint.IsAllowed);
        Assert.False(ordinaryEndpoint.IsAllowed);
    }

    [Fact]
    public void ApiAccess_AllowsKeyForConfiguredCompanyOnly()
    {
        var service = new ApiAccessService(
            Options.Create(new ApiAccessConfig
            {
                Enabled = true,
                Keys =
                [
                    new ApiAccessKeyConfig
                    {
                        Name = "Firm A",
                        KeyHash = ApiAccessService.HashKey("secret-a"),
                        AllowedCompanyIds = [10]
                    }
                ]
            }),
            new TestEnvironment("Production"));

        var allowed = service.Authorize("secret-a", new PathString("/api/companies/10/periods"));
        var denied = service.Authorize("secret-a", new PathString("/api/companies/11/periods"));
        var invalid = service.Authorize("wrong", new PathString("/api/companies/10/periods"));

        Assert.True(allowed.IsAllowed);
        Assert.False(denied.IsAllowed);
        Assert.False(invalid.IsAllowed);
        Assert.Contains("not authorised", denied.DenialReason);
    }

    [Fact]
    public void ApiAccess_EnforcesRolesForWritesAndAdminActions()
    {
        var service = new ApiAccessService(
            Options.Create(new ApiAccessConfig
            {
                Enabled = true,
                Keys =
                [
                    new ApiAccessKeyConfig
                    {
                        Name = "Read only",
                        Role = "Reader",
                        DevelopmentKey = "reader",
                        AllowedCompanyIds = [10]
                    },
                    new ApiAccessKeyConfig
                    {
                        Name = "Writer",
                        Role = "Writer",
                        DevelopmentKey = "writer",
                        AllowedCompanyIds = [10]
                    },
                    new ApiAccessKeyConfig
                    {
                        Name = "Admin",
                        Role = "Admin",
                        DevelopmentKey = "admin"
                    }
                ]
            }),
            new TestEnvironment("Development"));

        var readerWrite = service.Authorize("reader", new PathString("/api/companies/10/periods"), HttpMethods.Post);
        var writerWrite = service.Authorize("writer", new PathString("/api/companies/10/periods"), HttpMethods.Post);
        var writerDelete = service.Authorize("writer", new PathString("/api/companies/10"), HttpMethods.Delete);
        var scopedCompanyCreate = service.Authorize("writer", new PathString("/api/companies"), HttpMethods.Post);
        var adminCompanyCreate = service.Authorize("admin", new PathString("/api/companies"), HttpMethods.Post);

        Assert.False(readerWrite.IsAllowed);
        Assert.Contains("read-only", readerWrite.DenialReason);
        Assert.True(writerWrite.IsAllowed);
        Assert.False(writerDelete.IsAllowed);
        Assert.Contains("administrative", writerDelete.DenialReason);
        Assert.False(scopedCompanyCreate.IsAllowed);
        Assert.Contains("administrative", scopedCompanyCreate.DenialReason);
        Assert.True(adminCompanyCreate.IsAllowed);
    }

    [Fact]
    public void ApiAccess_RejectsDevelopmentKeysInProduction()
    {
        var service = new ApiAccessService(
            Options.Create(new ApiAccessConfig
            {
                Enabled = true,
                Keys = [new ApiAccessKeyConfig { Name = "Dev key", DevelopmentKey = "plain-text" }]
            }),
            new TestEnvironment("Production"));

        var failures = service.ValidateConfiguration();

        Assert.Contains(failures, f => f.Contains("DevelopmentKey"));
    }

    [Fact]
    public void ApiAccess_RejectsInvalidRoleConfiguration()
    {
        var service = new ApiAccessService(
            Options.Create(new ApiAccessConfig
            {
                Enabled = true,
                Keys = [new ApiAccessKeyConfig { Name = "Odd key", Role = "SuperUser", DevelopmentKey = "plain-text" }]
            }),
            new TestEnvironment("Development"));

        var failures = service.ValidateConfiguration();

        Assert.Contains(failures, f => f.Contains("invalid Role"));
    }

    [Fact]
    public async Task PeriodOwnershipMiddleware_BlocksPeriodFromDifferentCompany()
    {
        await using var db = CreateDbContext();
        var allowedPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Path = $"/api/companies/{allowedPeriod.CompanyId}/periods/{otherPeriod.Id}/debtors";
        var middleware = new PeriodOwnershipMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task PeriodOwnershipMiddleware_AllowsPeriodFromRouteCompany()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/debtors";
        var middleware = new PeriodOwnershipMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task PeriodLockMiddleware_BlocksAccountingWritesToFinalisedPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/debtors";
        var middleware = new PeriodLockMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }

    [Fact]
    public async Task PeriodLockMiddleware_AllowsReadsToFinalisedPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/debtors";
        var middleware = new PeriodLockMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task PeriodLockMiddleware_AllowsFilingWorkflowWritesToFinalisedPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/filing/cro-status";
        var middleware = new PeriodLockMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        Assert.True(nextCalled);
    }

    [Fact]
    public void EndpointInputs_RejectsInvalidCompanyAndPeriodInputs()
    {
        var badCompany = new CompanyInput
        {
            LegalName = "",
            IncorporationDate = default,
            FinancialYearStartMonth = 13,
            ArdMonth = 0
        };
        var badPeriod = new AccountingPeriodInput
        {
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2026, 8, 1),
            MemberAuditNoticeReceived = true
        };

        Assert.NotNull(EndpointInputs.ValidateCompany(badCompany));
        Assert.NotNull(EndpointInputs.ValidatePeriod(badPeriod));
    }

    [Fact]
    public void EndpointInputs_RequiresReasonWhenReopeningLockedPeriod()
    {
        var period = new AccountingPeriod
        {
            CompanyId = 1,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = true,
            Status = PeriodStatus.Finalised,
            LockedAt = DateTime.UtcNow,
            LockedBy = "Reviewer"
        };
        var invalid = new PeriodStatusUpdate(PeriodStatus.Review, null, "too short");
        var valid = new PeriodStatusUpdate(PeriodStatus.Review, null, "Material correction required");

        Assert.NotNull(EndpointInputs.ValidatePeriodStatusUpdate(period, invalid));
        Assert.Null(EndpointInputs.ValidatePeriodStatusUpdate(period, valid));
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

    private static AuthService CreateAuthService(
        AccountsDbContext db,
        string? signingKey = null,
        string environmentName = "Development",
        int expiryMinutes = 60,
        bool secureCookiesInProduction = true) =>
        new(
            db,
            Options.Create(new AuthSessionConfig
            {
                SigningKey = signingKey ?? StrongSessionSigningKey(),
                ExpiryMinutes = expiryMinutes,
                SecureCookiesInProduction = secureCookiesInProduction
            }),
            new TestEnvironment(environmentName));

    private static async Task<Tenant> SeedTenantAsync(
        AccountsDbContext db,
        string name = "Example Firm",
        string slug = "example-firm")
    {
        var tenant = new Tenant
        {
            Name = name,
            Slug = slug
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static async Task<UserAccount> SeedUserAsync(
        AccountsDbContext db,
        Tenant tenant,
        string email,
        string password,
        bool isActive = true,
        string role = "Admin",
        string passwordAlgorithm = AuthService.PasswordAlgorithm)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            210_000,
            HashAlgorithmName.SHA256,
            32);
        var user = new UserAccount
        {
            TenantId = tenant.Id,
            Tenant = tenant,
            Email = email.Trim().ToLowerInvariant(),
            DisplayName = "Owner User",
            Role = role,
            PasswordHash = Convert.ToBase64String(hash),
            PasswordSalt = Convert.ToBase64String(salt),
            PasswordAlgorithm = passwordAlgorithm,
            PasswordStrengthScore = 5,
            IsActive = isActive
        };
        db.UserAccounts.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static IOptions<AuthSessionConfig> AuthSessionOptions(IConfiguration config) =>
        Options.Create(config.GetSection("AuthSession").Get<AuthSessionConfig>() ?? new AuthSessionConfig());

    private static string StrongSessionSigningKey() =>
        Convert.ToBase64String(Enumerable.Range(0, 64).Select(i => (byte)i).ToArray());

    private static string StrongSessionSigningKeyBase64Url() =>
        StrongSessionSigningKey().TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Accounts.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
