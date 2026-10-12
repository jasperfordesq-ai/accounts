using System.Security.Cryptography;
using System.Text.Json;
using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounts.Tests;

public sealed class CorporationTaxSupportTests
{
    private const string FixtureSha256 = "3294c8b1d989e0af161e59c901de3a2906814504c7c0a609c2b0c74dc1ec3d95";

    [Fact]
    public void IndependentFixture_IsBytePinnedAndExplicitlyPendingQualifiedReview()
    {
        var bytes = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "corporation-tax-independent-v1.json"));
        using var document = JsonDocument.Parse(bytes);

        Assert.Equal(FixtureSha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("irish-corporation-tax-support-v1", document.RootElement.GetProperty("fixtureId").GetString());
        Assert.Equal("pending-qualified-accountant", document.RootElement.GetProperty("independentReviewStatus").GetString());
        Assert.Equal(
            ["signed-reversals-passive-directors", "two-period-loss-carry-forward", "wear-and-tear-and-disposal-balancing"],
            document.RootElement.GetProperty("scenarios")
                .EnumerateArray()
                .Select(item => item.GetProperty("code").GetString())
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task SignedRefundCreditAndManualJournalReversal_ReconcileTaxStreamsAndKeepDirectorsFeesSeparate()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedPeriodAsync(db, "Signed adjustment fixture");
        var sales = AddCategory(db, fixture.Company.Id, "4000", "Sales", AccountCategoryType.Income);
        var passive = AddCategory(db, fixture.Company.Id, "8000", "Rental income", AccountCategoryType.Income);
        passive.IsNonTradingIncome = true;
        var wages = AddCategory(db, fixture.Company.Id, "6100", "Employee wages", AccountCategoryType.Expense);
        var directorsFees = AddCategory(db, fixture.Company.Id, "6110", "Directors fees", AccountCategoryType.Expense);
        var entertainment = AddCategory(
            db,
            fixture.Company.Id,
            "6200",
            "Business entertainment",
            AccountCategoryType.Expense,
            TaxTreatment.NonDeductible);
        var bankLedger = AddCategory(db, fixture.Company.Id, "1400", "Bank control", AccountCategoryType.Asset);
        await db.SaveChangesAsync();

        db.ImportedTransactions.AddRange(
            Tx(fixture, sales.Id, 100_000m, "Trading sales", 1),
            Tx(fixture, passive.Id, 4_000m, "Rent received", 2),
            Tx(fixture, wages.Id, -40_000m, "Employee payroll", 3),
            Tx(fixture, directorsFees.Id, -8_000m, "Directors fees payroll", 4),
            Tx(fixture, entertainment.Id, -1_000m, "Entertainment expense", 5),
            Tx(fixture, entertainment.Id, 200m, "Supplier refund", 6));
        db.Adjustments.AddRange(
            new Adjustment
            {
                PeriodId = fixture.Period.Id,
                Description = "Manual non-deductible accrual",
                DebitCategoryId = entertainment.Id,
                CreditCategoryId = bankLedger.Id,
                Amount = 300m,
                ImpactOnProfit = -300m,
                Source = AdjustmentSource.Manual,
                CreatedBy = "Automated fixture"
            },
            new Adjustment
            {
                PeriodId = fixture.Period.Id,
                Description = "Manual non-deductible reversal",
                DebitCategoryId = bankLedger.Id,
                CreditCategoryId = entertainment.Id,
                Amount = 100m,
                ImpactOnProfit = 100m,
                Source = AdjustmentSource.Manual,
                CreatedBy = "Automated fixture"
            });
        db.PayrollSummaries.Add(new PayrollSummary
        {
            PeriodId = fixture.Period.Id,
            GrossWages = 40_000m,
            DirectorsFees = 8_000m,
            EmployerPrsi = 4_400m,
            PensionContributions = 2_000m,
            StaffCount = 5
        });
        await db.SaveChangesAsync();

        var service = new TaxComputationService(db, new FinancialStatementsService(db));
        var context = AuthenticatedContext(fixture.Tenant.Id, fixture.Company.Id, fixture.Period.Id);
        var result = await RevenueEndpoints.UpsertCorporationTaxScopeReviewEndpointAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            SupportedScope(passiveIncomePresent: true),
            service,
            db,
            new AuditService(db),
            context);
        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);

        var computation = await service.ComputeAsync(fixture.Company.Id, fixture.Period.Id);
        var support = await service.GetCt1SupportDataAsync(fixture.Company.Id, fixture.Period.Id);
        var expected = FixtureScenario("signed-reversals-passive-directors");

        Assert.Equal(expected.GetProperty("accountingProfit").GetDecimal(), computation.AccountingProfit);
        Assert.Equal(expected.GetProperty("passiveNonTradingIncome").GetDecimal(), computation.PassiveNonTradingIncome);
        Assert.Equal(
            expected.GetProperty("signedNonDeductibleAdjustment").GetDecimal(),
            Assert.Single(computation.Adjustments, item => item.Description.Contains("non-deductible", StringComparison.OrdinalIgnoreCase)).Amount);
        Assert.Equal(expected.GetProperty("tradingProfitBeforeLossRelief").GetDecimal(), computation.TradingProfitBeforeLossRelief);
        Assert.Equal(expected.GetProperty("corporationTaxAt125").GetDecimal(), computation.CorporationTaxAt125);
        Assert.Equal(expected.GetProperty("corporationTaxAt25").GetDecimal(), computation.CorporationTaxAt25);
        Assert.Equal(expected.GetProperty("totalCorporationTax").GetDecimal(), computation.TotalCorporationTax);
        Assert.True(computation.FinalTaxChargeSupported, string.Join(" | ", computation.BlockingReasons));
        Assert.False(computation.IsCompleteCt1Return);
        Assert.True(computation.ManualReviewRequired);
        Assert.Equal(TaxComputationService.OutputKind, computation.OutputKind);
        Assert.Equal(8_000m, support.TotalDirectorsFees);
        Assert.NotEqual(support.TotalEmployeeCosts, support.TotalDirectorsFees);
        Assert.Equal(54_400m, support.TotalEmployeeCosts);
        var review = await db.CorporationTaxScopeReviews.SingleAsync();
        Assert.Equal("Automated corporation-tax preparer (not qualified acceptance)", review.PreparedBy);
        Assert.Single(await db.AuditLogs.Where(item => item.Action == AuditEventCodes.CorporationTaxScopeReviewUpserted).ToListAsync());

        var readResult = await RevenueEndpoints.GetCorporationTaxScopeReviewEndpointAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            service,
            db,
            context);
        var readPayload = Assert.IsAssignableFrom<IValueHttpResult>(readResult).Value;
        using var readJson = JsonDocument.Parse(JsonSerializer.Serialize(readPayload));
        Assert.True(readJson.RootElement.GetProperty("Computation").GetProperty("FinalTaxChargeSupported").GetBoolean());
    }

    [Fact]
    public async Task QualifyingAssetReviewAndDisposals_ProduceWearAndTearAndBoundedBalancingAdjustments()
    {
        await using var db = CreateDbContext();
        var tenant = new Tenant { Name = "Tax Fixture Firm", Slug = Guid.NewGuid().ToString("N") };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var company = Company(tenant.Id, "Capital allowance fixture");
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var prior = Period(company.Id, 2024);
        var current = Period(company.Id, 2025);
        db.AccountingPeriods.AddRange(prior, current);
        var bank = new BankAccount { CompanyId = company.Id, Name = "Current", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        AddCategory(db, company.Id, "1400", "Bank control", AccountCategoryType.Asset);
        AddCategory(db, company.Id, "3100", "Retained earnings", AccountCategoryType.Equity);
        var sales = AddCategory(db, company.Id, "4000", "Sales", AccountCategoryType.Income);
        await db.SaveChangesAsync();
        db.ImportedTransactions.AddRange(
            new ImportedTransaction
            {
                BankAccountId = bank.Id,
                PeriodId = prior.Id,
                Date = prior.PeriodStart.AddMonths(1),
                Description = "Prior trading sales",
                Amount = 2_000m,
                CategoryId = sales.Id
            },
            new ImportedTransaction
            {
                BankAccountId = bank.Id,
                PeriodId = current.Id,
                Date = current.PeriodStart.AddMonths(1),
                Description = "Trading sales",
                Amount = 50_000m,
                CategoryId = sales.Id
            });

        var active = ReviewedPlant(company.Id, "Active machine", 8_000m, new DateOnly(2025, 1, 1));
        var chargeAsset = ReviewedPlant(company.Id, "Machine sold above TWDV", 8_000m, new DateOnly(2020, 1, 1));
        chargeAsset.DisposalDate = new DateOnly(2025, 6, 1);
        chargeAsset.DisposalProceeds = 6_500m;
        var allowanceAsset = ReviewedPlant(company.Id, "Machine sold below TWDV", 8_000m, new DateOnly(2020, 1, 1));
        allowanceAsset.DisposalDate = new DateOnly(2025, 7, 1);
        allowanceAsset.DisposalProceeds = 4_000m;
        db.FixedAssets.AddRange(active, chargeAsset, allowanceAsset);
        await db.SaveChangesAsync();
        db.CapitalAllowanceClaims.AddRange(
            new CapitalAllowanceClaim { AssetId = chargeAsset.Id, PeriodId = prior.Id, Cost = 8_000m, Claim = 3_000m },
            new CapitalAllowanceClaim { AssetId = allowanceAsset.Id, PeriodId = prior.Id, Cost = 8_000m, Claim = 3_000m });
        await db.SaveChangesAsync();

        var service = new TaxComputationService(db, new FinancialStatementsService(db));
        await service.SaveScopeReviewAsync(company.Id, prior.Id, SupportedScope(), "Automated fixture actor");
        await service.SaveScopeReviewAsync(company.Id, current.Id, SupportedScope(), "Automated fixture actor");
        var computation = await service.ComputeAsync(company.Id, current.Id);
        var expected = FixtureScenario("wear-and-tear-and-disposal-balancing");

        Assert.Equal(expected.GetProperty("wearAndTearAllowance").GetDecimal(), computation.CapitalAllowances);
        Assert.Equal(expected.GetProperty("balancingAllowance").GetDecimal(), computation.BalancingAllowances);
        Assert.Equal(expected.GetProperty("balancingCharge").GetDecimal(), computation.BalancingCharges);
        Assert.Equal(expected.GetProperty("tradingProfitBeforeLossRelief").GetDecimal(), computation.TradingProfitBeforeLossRelief);
        Assert.Equal(expected.GetProperty("totalCorporationTax").GetDecimal(), computation.TotalCorporationTax);
        Assert.True(computation.FinalTaxChargeSupported, string.Join(" | ", computation.BlockingReasons));

        active.CapitalAllowanceTreatment = CapitalAllowanceTreatment.Unreviewed;
        active.CapitalAllowanceReviewedBy = null;
        active.CapitalAllowanceReviewedAtUtc = null;
        active.CapitalAllowanceEvidence = null;
        await db.SaveChangesAsync();
        var blocked = await service.ComputeAsync(company.Id, current.Id);
        Assert.False(blocked.FinalTaxChargeSupported);
        Assert.Contains(blocked.BlockingReasons, reason => reason.Contains("Active machine", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TradingLossLedger_PersistsAndUsesFirstAvailableSameTradeProfitAcrossPeriods()
    {
        await using var db = CreateDbContext();
        var tenant = new Tenant { Name = "Tax Fixture Firm", Slug = Guid.NewGuid().ToString("N") };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var company = Company(tenant.Id, "Loss ledger fixture");
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var first = Period(company.Id, 2024);
        var second = Period(company.Id, 2025);
        db.AccountingPeriods.AddRange(first, second);
        var bank = new BankAccount { CompanyId = company.Id, Name = "Current", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        AddCategory(db, company.Id, "1400", "Bank control", AccountCategoryType.Asset);
        AddCategory(db, company.Id, "3100", "Retained earnings", AccountCategoryType.Equity);
        var sales = AddCategory(db, company.Id, "4000", "Sales", AccountCategoryType.Income);
        var costs = AddCategory(db, company.Id, "6000", "Office costs", AccountCategoryType.Expense);
        await db.SaveChangesAsync();
        db.ImportedTransactions.AddRange(
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = first.Id, Date = first.PeriodStart.AddMonths(1), Description = "Sales", Amount = 1_000m, CategoryId = sales.Id },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = first.Id, Date = first.PeriodStart.AddMonths(2), Description = "Costs", Amount = -11_000m, CategoryId = costs.Id },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = second.Id, Date = second.PeriodStart.AddMonths(1), Description = "Sales", Amount = 6_000m, CategoryId = sales.Id });
        await db.SaveChangesAsync();

        var service = new TaxComputationService(db, new FinancialStatementsService(db));
        await service.SaveScopeReviewAsync(company.Id, first.Id, SupportedScope(lossTreatment: CorporationTaxLossTreatment.CarryForwardSameTrade), "Automated fixture actor");
        var firstComputation = await service.ComputeAsync(company.Id, first.Id);
        await service.SaveScopeReviewAsync(
            company.Id,
            second.Id,
            SupportedScope(
                lossTreatment: CorporationTaxLossTreatment.CarryForwardSameTrade,
                broughtForwardLoss: firstComputation.TradingLossCarriedForward,
                broughtForwardEvidence: "Prior retained period loss ledger hash and signed take-on."),
            "Automated fixture actor");
        var secondComputation = await service.ComputeAsync(company.Id, second.Id);
        var expected = FixtureScenario("two-period-loss-carry-forward");

        Assert.Equal(expected.GetProperty("lossArisingFirstPeriod").GetDecimal(), firstComputation.TradingLossAvailable);
        Assert.Equal(expected.GetProperty("lossUsedSecondPeriod").GetDecimal(), secondComputation.TradingLossUsed);
        Assert.Equal(expected.GetProperty("closingLossSecondPeriod").GetDecimal(), secondComputation.TradingLossCarriedForward);
        Assert.Equal(expected.GetProperty("secondPeriodCorporationTax").GetDecimal(), secondComputation.TotalCorporationTax);
        Assert.True(firstComputation.FinalTaxChargeSupported);
        Assert.True(secondComputation.FinalTaxChargeSupported);
        Assert.Equal(2, await db.CorporationTaxLossRecords.CountAsync());
    }

    [Fact]
    public async Task CloseCompanyAndGroupCases_FailClosedForFinalChargeAndRevenueHandoff()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedPeriodAsync(db, "Unsupported tax fixture");
        fixture.Company.IsGroupMember = true;
        var sales = AddCategory(db, fixture.Company.Id, "4000", "Sales", AccountCategoryType.Income);
        db.ImportedTransactions.Add(Tx(fixture, sales.Id, 10_000m, "Trading sales", 1));
        db.TaxBalances.Add(new TaxBalance
        {
            PeriodId = fixture.Period.Id,
            TaxType = TaxType.CorporationTax,
            Liability = 1_250m,
            Paid = 0m,
            Balance = 1_250m
        });
        await db.SaveChangesAsync();
        var service = new TaxComputationService(db, new FinancialStatementsService(db));
        await service.SaveScopeReviewAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            SupportedScope(isCloseCompany: true, hasGroupRelief: true),
            "Automated fixture actor");

        var computation = await service.ComputeAsync(fixture.Company.Id, fixture.Period.Id);
        Assert.False(computation.FinalTaxChargeSupported);
        Assert.Contains(computation.BlockingReasons, reason => reason.Contains("Close-company", StringComparison.Ordinal));
        Assert.Contains(computation.BlockingReasons, reason => reason.Contains("Group", StringComparison.Ordinal));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.AssertFinalTaxChargeSupportedAsync(fixture.Company.Id, fixture.Period.Id));

        var statements = new FinancialStatementsService(db);
        var workflow = new FilingWorkflowService(db, statements, new IxbrlService(db, statements));
        var revenue = await workflow.ValidateIxbrlAsync(fixture.Company.Id, fixture.Period.Id);
        Assert.Contains("Corporation-tax scope is not supported", revenue.IxbrlValidationErrors, StringComparison.Ordinal);
        Assert.False(revenue.IxbrlValidated);
        Assert.False(revenue.IxbrlGenerated);
    }

    [Fact]
    public async Task PassiveLosses_FailClosedInsteadOfBeingSilentlyClampedToZero()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedPeriodAsync(db, "Passive-loss fixture");
        var rental = AddCategory(db, fixture.Company.Id, "8000", "Rental result", AccountCategoryType.Income);
        rental.IsNonTradingIncome = true;
        db.ImportedTransactions.Add(Tx(fixture, rental.Id, -1_000m, "Rental deficit", 1));
        await db.SaveChangesAsync();

        var service = new TaxComputationService(db, new FinancialStatementsService(db));
        await service.SaveScopeReviewAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            SupportedScope(passiveIncomePresent: true),
            "Automated fixture actor");

        var computation = await service.ComputeAsync(fixture.Company.Id, fixture.Period.Id);

        Assert.Equal(-1_000m, computation.PassiveNonTradingIncome);
        Assert.Equal(0m, computation.CorporationTaxAt25);
        Assert.False(computation.FinalTaxChargeSupported);
        Assert.Contains(
            computation.BlockingReasons,
            reason => reason.Contains("Passive/non-trading losses", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DisposedNonQualifyingAsset_RequiresChargeableGainOrLossReview()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedPeriodAsync(db, "Non-qualifying disposal fixture");
        var sales = AddCategory(db, fixture.Company.Id, "4000", "Sales", AccountCategoryType.Income);
        db.ImportedTransactions.Add(Tx(fixture, sales.Id, 10_000m, "Trading sales", 1));
        db.FixedAssets.Add(new FixedAsset
        {
            CompanyId = fixture.Company.Id,
            Name = "Disposed land",
            Category = "Land",
            Cost = 5_000m,
            AcquisitionDate = fixture.Period.PeriodStart.AddYears(-2),
            DisposalDate = fixture.Period.PeriodStart.AddMonths(6),
            DisposalProceeds = 7_500m,
            UsefulLifeYears = 20,
            DepreciationMethod = DepreciationMethod.StraightLine,
            CapitalAllowanceTreatment = CapitalAllowanceTreatment.NonQualifying,
            CapitalAllowanceEvidence = "Retained evidence confirms this is outside plant and machinery.",
            CapitalAllowanceReviewedBy = "Automated fixture actor",
            CapitalAllowanceReviewedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new TaxComputationService(db, new FinancialStatementsService(db));
        await service.SaveScopeReviewAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            SupportedScope(),
            "Automated fixture actor");
        var computation = await service.ComputeAsync(fixture.Company.Id, fixture.Period.Id);

        Assert.False(computation.FinalTaxChargeSupported);
        Assert.Contains(
            computation.BlockingReasons,
            reason => reason.Contains("chargeable-gain/loss review", StringComparison.Ordinal));
    }

    // The auto depreciation journal debits the NonDeductible depreciation account (generic 7000). The
    // computation already adds back depreciation from the depreciation entries, so the journal must not
    // be added back a second time through the non-deductible stream.
    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    public async Task AutoDepreciationJournal_IsAddedBackOnce_AndGenuineNonDeductibleExpensesStillAre(int entertainmentSpend)
    {
        await using var db = CreateDbContext();
        var fixture = await SeedPeriodAsync(db, "Depreciation add-back fixture");
        var sales = AddCategory(db, fixture.Company.Id, "4000", "Sales", AccountCategoryType.Income);
        var entertainment = AddCategory(
            db,
            fixture.Company.Id,
            "6200",
            "Business entertainment",
            AccountCategoryType.Expense,
            TaxTreatment.NonDeductible);
        db.ImportedTransactions.Add(Tx(fixture, sales.Id, 50_000m, "Trading sales", 1));
        if (entertainmentSpend > 0)
            db.ImportedTransactions.Add(Tx(fixture, entertainment.Id, -entertainmentSpend, "Entertainment expense", 2));
        var machine = ReviewedPlant(fixture.Company.Id, "Workshop machine", 8_000m, fixture.Period.PeriodStart);
        machine.UsefulLifeYears = 5; // 1,600 straight-line depreciation; 1,000 wear and tear (12.5%)
        db.FixedAssets.Add(machine);
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(fixture.Company.Id, fixture.Period.Id);
        var service = new TaxComputationService(db, new FinancialStatementsService(db));
        await service.SaveScopeReviewAsync(fixture.Company.Id, fixture.Period.Id, SupportedScope(), "Automated fixture actor");
        var computation = await service.ComputeAsync(fixture.Company.Id, fixture.Period.Id);

        Assert.Single(await db.Adjustments.Where(item => item.Description.StartsWith("Depreciation —")).ToListAsync());
        var accountingProfit = 50_000m - entertainmentSpend - 1_600m;
        Assert.Equal(accountingProfit, computation.AccountingProfit);
        Assert.Equal(1_600m, Assert.Single(computation.Adjustments, item => item.Description == "Add back: depreciation").Amount);
        Assert.Equal(1_000m, computation.CapitalAllowances);
        if (entertainmentSpend > 0)
            Assert.Equal(entertainmentSpend, Assert.Single(computation.Adjustments, item => item.Description == "Add back: non-deductible expenses").Amount);
        else
            Assert.DoesNotContain(computation.Adjustments, item => item.Description.Contains("non-deductible", StringComparison.OrdinalIgnoreCase));
        // Taxable = accounting profit + depreciation (once) + genuine non-deductibles - capital allowances.
        Assert.Equal(accountingProfit + 1_600m + entertainmentSpend - 1_000m, computation.TradingProfitBeforeLossRelief);
        Assert.Equal(49_000m, computation.TaxableProfit);
        Assert.True(computation.FinalTaxChargeSupported, string.Join(" | ", computation.BlockingReasons));
    }

    public enum DepreciationJournalState { Generated, NotGeneratedYet, EditedUpwards, EditedDownwards, Deleted }

    // The depreciation add-back equals the depreciation actually charged in accounting profit: the
    // posted auto depreciation journal, whatever a reviewer did to it after generation.
    [Theory]
    [InlineData(DepreciationJournalState.Generated, 1_600)]
    [InlineData(DepreciationJournalState.NotGeneratedYet, 0)]
    [InlineData(DepreciationJournalState.EditedUpwards, 2_000)]
    [InlineData(DepreciationJournalState.EditedDownwards, 1_200)]
    [InlineData(DepreciationJournalState.Deleted, 0)]
    public async Task DepreciationAddBack_EqualsTheDepreciationChargedInProfit(DepreciationJournalState state, int charged)
    {
        await using var db = CreateDbContext();
        var fixture = await SeedPeriodAsync(db, "Depreciation journal state fixture");
        var sales = AddCategory(db, fixture.Company.Id, "4000", "Sales", AccountCategoryType.Income);
        db.ImportedTransactions.Add(Tx(fixture, sales.Id, 50_000m, "Trading sales", 1));
        var machine = ReviewedPlant(fixture.Company.Id, "Workshop machine", 8_000m, fixture.Period.PeriodStart);
        machine.UsefulLifeYears = 5; // generated charge 1,600; wear and tear 1,000
        db.FixedAssets.Add(machine);
        await db.SaveChangesAsync();

        if (state != DepreciationJournalState.NotGeneratedYet)
        {
            await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(fixture.Company.Id, fixture.Period.Id);
            var journal = await db.Adjustments.SingleAsync(item => item.Description.StartsWith("Depreciation —"));
            Assert.True(journal.IsAuto);
            if (state == DepreciationJournalState.Deleted)
                db.Adjustments.Remove(journal);
            else if (state != DepreciationJournalState.Generated)
                journal.Amount = charged; // a reviewer's edit keeps IsAuto (UpdateAdjustmentEndpointAsync)
            await db.SaveChangesAsync();
        }

        var statements = new FinancialStatementsService(db);
        var service = new TaxComputationService(db, statements);
        await service.SaveScopeReviewAsync(fixture.Company.Id, fixture.Period.Id, SupportedScope(), "Automated fixture actor");
        var computation = await service.ComputeAsync(fixture.Company.Id, fixture.Period.Id);

        Assert.Equal(50_000m - charged, computation.AccountingProfit);
        if (charged == 0)
            Assert.DoesNotContain(computation.Adjustments, item => item.Description == "Add back: depreciation");
        else
            Assert.Equal(charged, Assert.Single(computation.Adjustments, item => item.Description == "Add back: depreciation").Amount);
        Assert.DoesNotContain(computation.Adjustments, item => item.Description.Contains("non-deductible", StringComparison.OrdinalIgnoreCase));
        // Profit + depreciation charged - wear and tear 1,000 is the same taxable profit in every state.
        Assert.Equal(49_000m, computation.TaxableProfit);
        // The CT1 support pack reports the same depreciation figure as the add-back.
        Assert.Equal(charged, (await service.GetCt1SupportDataAsync(fixture.Company.Id, fixture.Period.Id)).DepreciationCharged);
    }

    // The charity chart's owned (8000) and leased (8010) depreciation accounts are NonDeductible on the
    // Depreciation line: each auto journal is added back once, through the depreciation line only.
    [Fact]
    public async Task CharityChart_OwnedAndLeasedDepreciation_IsAddedBackOnce()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedCharityPeriodAsync(db, "Charity depreciation fixture");
        db.ImportedTransactions.Add(Tx(fixture, fixture.Ids["4000"], 30_000m, "Unrestricted donations", 1));
        var owned = ReviewedPlant(fixture.Company.Id, "Office equipment", 5_000m, fixture.Period.PeriodStart);
        owned.Category = "Computer Equipment";
        owned.UsefulLifeYears = 5; // 1,000 to 8000; wear and tear 625
        var leased = ReviewedPlant(fixture.Company.Id, "Leased printer", 4_000m, fixture.Period.PeriodStart);
        leased.Category = ChartDefinitions.LeasedEquipmentClass;
        leased.UsefulLifeYears = 4; // 1,000 to 8010; wear and tear 500
        db.FixedAssets.AddRange(owned, leased);
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(fixture.Company.Id, fixture.Period.Id);
        var service = new TaxComputationService(db, new FinancialStatementsService(db));
        await service.SaveScopeReviewAsync(fixture.Company.Id, fixture.Period.Id, SupportedScope(), "Automated fixture actor");
        var computation = await service.ComputeAsync(fixture.Company.Id, fixture.Period.Id);

        var journals = await db.Adjustments.Where(item => item.Description.StartsWith("Depreciation —")).ToListAsync();
        Assert.Equal(
            [fixture.Ids["8000"], fixture.Ids["8010"]],
            journals.Select(item => item.DebitCategoryId!.Value).Order());
        Assert.Equal(28_000m, computation.AccountingProfit);
        Assert.Equal(2_000m, Assert.Single(computation.Adjustments, item => item.Description == "Add back: depreciation").Amount);
        Assert.DoesNotContain(computation.Adjustments, item => item.Description.Contains("non-deductible", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1_125m, computation.CapitalAllowances);
        Assert.Equal(28_000m + 2_000m - 1_125m, computation.TaxableProfit);
    }

    // A loss on disposal (7050) is a genuine non-deductible charge in profit and stays on the flag-driven
    // non-deductible add-back. A manual (non-auto) journal to the depreciation account is depreciation
    // charged in profit, so it joins the auto depreciation in the depreciation add-back (once).
    [Fact]
    public async Task DisposalLossAndManualDepreciationJournal_AreStillAddedBack()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedPeriodAsync(db, "Disposal loss fixture");
        var sales = AddCategory(db, fixture.Company.Id, "4000", "Sales", AccountCategoryType.Income);
        db.ImportedTransactions.Add(Tx(fixture, sales.Id, 50_000m, "Trading sales", 1));
        var machine = ReviewedPlant(fixture.Company.Id, "Sold machine", 8_000m, fixture.Period.PeriodStart);
        machine.DisposalDate = new DateOnly(2025, 7, 1);
        machine.DisposalProceeds = 5_000m;
        db.FixedAssets.Add(machine);
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(fixture.Company.Id, fixture.Period.Id);
        var depreciationAccount = await db.AccountCategories.SingleAsync(item => item.CompanyId == fixture.Company.Id && item.Code == "7000");
        db.Adjustments.Add(new Adjustment
        {
            PeriodId = fixture.Period.Id,
            Description = "Manual impairment top-up",
            DebitCategoryId = depreciationAccount.Id,
            CreditCategoryId = (await db.Adjustments.SingleAsync(item => item.Description.StartsWith("Depreciation —"))).CreditCategoryId,
            Amount = 100m,
            ImpactOnProfit = -100m,
            Source = AdjustmentSource.Manual,
            CreatedBy = "Automated fixture"
        });
        await db.SaveChangesAsync();
        var service = new TaxComputationService(db, new FinancialStatementsService(db));
        await service.SaveScopeReviewAsync(fixture.Company.Id, fixture.Period.Id, SupportedScope(), "Automated fixture actor");
        var computation = await service.ComputeAsync(fixture.Company.Id, fixture.Period.Id);

        // 8,000 over 8 years for 182/365 of a year = 498.63; loss = 8,000 - 498.63 - 5,000 = 2,501.37.
        Assert.Equal(2_501.37m, (await db.Adjustments.SingleAsync(item => item.Description.StartsWith("Fixed asset disposal loss"))).Amount);
        Assert.Equal(498.63m + 100m, Assert.Single(computation.Adjustments, item => item.Description == "Add back: depreciation").Amount);
        Assert.Equal(2_501.37m, Assert.Single(computation.Adjustments, item => item.Description == "Add back: non-deductible expenses").Amount);
        Assert.Equal(50_000m - 498.63m - 2_501.37m - 100m, computation.AccountingProfit);
    }

    // Accounting depreciation is never deductible (capital allowances replace it), so every movement on
    // the Depreciation line is added back whatever the account's tax flag: here a custom charity 8020
    // account created with the default Deductible treatment, charged by a manual journal and a bank item.
    [Fact]
    public async Task CustomDeductibleDepreciationAccount_ManualJournalAndBankItem_AreAddedBack()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedCharityPeriodAsync(db, "Charity custom depreciation fixture");
        db.ImportedTransactions.Add(Tx(fixture, fixture.Ids["4000"], 30_000m, "Donations", 1));
        var vehicles = new AccountCategory
        {
            CompanyId = fixture.Company.Id,
            Code = "8020",
            Name = "Depreciation — vehicles",
            Type = AccountCategoryType.Expense,
            TaxTreatment = TaxTreatment.Deductible,
            StatementLine = ChartDefinitions.CharityCustomLine("8020"),
            CashFlowClass = AccountCashFlowClass.Operating
        };
        db.AccountCategories.Add(vehicles);
        await db.SaveChangesAsync();
        Assert.Equal(AccountStatementLine.Depreciation, vehicles.StatementLine);
        db.Adjustments.Add(new Adjustment
        {
            PeriodId = fixture.Period.Id,
            Description = "Vehicle depreciation",
            DebitCategoryId = vehicles.Id,
            CreditCategoryId = fixture.Ids["0020"],
            Amount = 500m,
            ImpactOnProfit = -500m,
            Source = AdjustmentSource.Manual,
            CreatedBy = "Automated fixture"
        });
        db.ImportedTransactions.Add(Tx(fixture, vehicles.Id, -300m, "Depreciation booked through the bank", 2));
        await db.SaveChangesAsync();
        var service = new TaxComputationService(db, new FinancialStatementsService(db));
        await service.SaveScopeReviewAsync(fixture.Company.Id, fixture.Period.Id, SupportedScope(), "Automated fixture actor");
        var computation = await service.ComputeAsync(fixture.Company.Id, fixture.Period.Id);

        Assert.Equal(29_200m, computation.AccountingProfit);
        Assert.Equal(800m, Assert.Single(computation.Adjustments, item => item.Description == "Add back: depreciation").Amount);
        Assert.DoesNotContain(computation.Adjustments, item => item.Description.Contains("non-deductible", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(30_000m, computation.TaxableProfit);
        Assert.Equal(800m, (await service.GetCt1SupportDataAsync(fixture.Company.Id, fixture.Period.Id)).DepreciationCharged);
    }

    // Review probe (fix round 1): a CT payment from the bank categorised straight to the TaxOnProfit
    // charge account is below profit before tax, so the bank stream must not add it back either.
    [Fact]
    public async Task Probe_CtPaidFromBankToChargeAccount_NotAddedBack()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedPeriodAsync(db, "CT paid to 8000 fixture");
        var sales = AddCategory(db, fixture.Company.Id, "4000", "Sales", AccountCategoryType.Income);
        var charge = AddCategory(db, fixture.Company.Id, "8000", "Corporation Tax Charge", AccountCategoryType.Expense, TaxTreatment.NonDeductible);
        db.ImportedTransactions.AddRange(
            Tx(fixture, sales.Id, 50_000m, "Trading sales", 1),
            Tx(fixture, charge.Id, -6_250m, "Revenue CT payment", 5));
        await db.SaveChangesAsync();
        var service = new TaxComputationService(db, new FinancialStatementsService(db));
        await service.SaveScopeReviewAsync(fixture.Company.Id, fixture.Period.Id, SupportedScope(), "Automated fixture actor");
        var computation = await service.ComputeAsync(fixture.Company.Id, fixture.Period.Id);

        Assert.Equal(50_000m, computation.AccountingProfit);
        Assert.DoesNotContain(computation.Adjustments, item => item.Description.Contains("non-deductible", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(50_000m, computation.TaxableProfit);
    }

    // Accounting profit is profit before tax, which never includes the TaxOnProfit line. The auto CT
    // provision (generic 8000, charity 8900; both NonDeductible) must not be added back to it, and a
    // provision equal to the computed tax must not raise the pre-filing "does not match" warning.
    [Theory]
    [InlineData(ChartOfAccountsVariant.GenericTrading)]
    [InlineData(ChartOfAccountsVariant.CharityClg)]
    public async Task CorporationTaxProvision_IsNotAddedBackAndMatchesTheComputation(ChartOfAccountsVariant variant)
    {
        await using var db = CreateDbContext();
        Fixture fixture;
        int salesId;
        if (variant == ChartOfAccountsVariant.CharityClg)
        {
            var charity = await SeedCharityPeriodAsync(db, "Charity CT provision fixture");
            fixture = charity;
            salesId = charity.Ids["4000"];
        }
        else
        {
            fixture = await SeedPeriodAsync(db, "Generic CT provision fixture");
            salesId = AddCategory(db, fixture.Company.Id, "4000", "Sales", AccountCategoryType.Income).Id;
        }
        db.ImportedTransactions.Add(Tx(fixture, salesId, 10_000m, "Trading sales", 1));
        db.TaxBalances.Add(new TaxBalance
        {
            PeriodId = fixture.Period.Id,
            TaxType = TaxType.CorporationTax,
            Liability = 1_250m,
            Balance = 1_250m
        });
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(fixture.Company.Id, fixture.Period.Id);
        var provision = await db.Adjustments.SingleAsync(item => item.Description == "Corporation tax provision");
        var chargeAccount = await db.AccountCategories.SingleAsync(item => item.Id == provision.DebitCategoryId);
        Assert.Equal(variant == ChartOfAccountsVariant.CharityClg ? "8900" : "8000", chargeAccount.Code);
        Assert.Equal(TaxTreatment.NonDeductible, chargeAccount.TaxTreatment);

        var statements = new FinancialStatementsService(db);
        var service = new TaxComputationService(db, statements);
        await service.SaveScopeReviewAsync(fixture.Company.Id, fixture.Period.Id, SupportedScope(), "Automated fixture actor");
        var computation = await service.ComputeAsync(fixture.Company.Id, fixture.Period.Id);

        Assert.Equal(1_250m, (await statements.GetProfitAndLossAsync(fixture.Company.Id, fixture.Period.Id)).TaxCharge);
        Assert.Equal(10_000m, computation.AccountingProfit);
        Assert.DoesNotContain(computation.Adjustments, item => item.Description.Contains("non-deductible", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(10_000m, computation.TaxableProfit);
        Assert.Equal(1_250m, computation.TotalCorporationTax);
        Assert.True(computation.FinalTaxChargeSupported, string.Join(" | ", computation.BlockingReasons));

        var issues = await statements.GetPreFilingConsistencyIssuesAsync(fixture.Company.Id, fixture.Period.Id);
        Assert.DoesNotContain(issues, issue => issue.Contains("does not match", StringComparison.Ordinal));
        var readiness = await statements.GetReadinessScoreAsync(fixture.Company.Id, fixture.Period.Id);
        Assert.DoesNotContain(readiness.Warnings, warning => warning.Contains("does not match", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LossContinuity_CannotSkipAnInterveningAccountingPeriod()
    {
        await using var db = CreateDbContext();
        var tenant = new Tenant { Name = "Tax Fixture Firm", Slug = Guid.NewGuid().ToString("N") };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var company = Company(tenant.Id, "Loss continuity fixture");
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var first = Period(company.Id, 2024);
        var omitted = Period(company.Id, 2025);
        var current = Period(company.Id, 2026);
        db.AccountingPeriods.AddRange(first, omitted, current);
        var bank = new BankAccount { CompanyId = company.Id, Name = "Current", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        AddCategory(db, company.Id, "1400", "Bank control", AccountCategoryType.Asset);
        AddCategory(db, company.Id, "3100", "Retained earnings", AccountCategoryType.Equity);
        var sales = AddCategory(db, company.Id, "4000", "Sales", AccountCategoryType.Income);
        var costs = AddCategory(db, company.Id, "6000", "Office costs", AccountCategoryType.Expense);
        await db.SaveChangesAsync();
        db.ImportedTransactions.AddRange(
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = first.Id, Date = first.PeriodStart.AddMonths(1), Description = "Costs", Amount = -5_000m, CategoryId = costs.Id },
            new ImportedTransaction { BankAccountId = bank.Id, PeriodId = current.Id, Date = current.PeriodStart.AddMonths(1), Description = "Sales", Amount = 2_000m, CategoryId = sales.Id });
        await db.SaveChangesAsync();

        var service = new TaxComputationService(db, new FinancialStatementsService(db));
        await service.SaveScopeReviewAsync(
            company.Id,
            first.Id,
            SupportedScope(lossTreatment: CorporationTaxLossTreatment.CarryForwardSameTrade),
            "Automated fixture actor");
        await service.SaveScopeReviewAsync(
            company.Id,
            current.Id,
            SupportedScope(
                lossTreatment: CorporationTaxLossTreatment.CarryForwardSameTrade,
                broughtForwardLoss: 5_000m,
                broughtForwardEvidence: "First-period retained loss record, with the intervening period omitted."),
            "Automated fixture actor");

        var computation = await service.ComputeAsync(company.Id, current.Id);
        Assert.False(computation.FinalTaxChargeSupported);
        Assert.Contains(
            computation.BlockingReasons,
            reason => reason.Contains("Immediately preceding period", StringComparison.Ordinal));
    }

    private static TaxComputationService.CorporationTaxScopeReviewInput SupportedScope(
        bool passiveIncomePresent = false,
        CorporationTaxLossTreatment lossTreatment = CorporationTaxLossTreatment.NotApplicable,
        decimal broughtForwardLoss = 0m,
        string? broughtForwardEvidence = null,
        bool isCloseCompany = false,
        bool hasGroupRelief = false) => new(
            isCloseCompany,
            isCloseCompany ? false : null,
            hasGroupRelief,
            HasChargeableGains: false,
            HasForeignIncomeOrTaxCredits: false,
            HasExceptedTrade: false,
            HasOtherReliefsOrSpecialRegimes: false,
            DeclaredPassiveIncomePresent: passiveIncomePresent,
            PassiveIncomeClassificationReviewed: passiveIncomePresent,
            lossTreatment,
            broughtForwardLoss,
            broughtForwardEvidence,
            "Automated fixture scope evidence; not qualified-accountant acceptance.");

    private static FixedAsset ReviewedPlant(int companyId, string name, decimal cost, DateOnly acquired) => new()
    {
        CompanyId = companyId,
        Name = name,
        Category = "Plant & Machinery",
        Cost = cost,
        AcquisitionDate = acquired,
        UsefulLifeYears = 8,
        DepreciationMethod = DepreciationMethod.StraightLine,
        CapitalAllowanceTreatment = CapitalAllowanceTreatment.PlantAndMachinery12Point5,
        CapitalAllowanceEvidence = "Invoice and exclusive trade-use evidence retained for fixture.",
        CapitalAllowanceReviewedBy = "Automated fixture actor (not human acceptance)",
        CapitalAllowanceReviewedAtUtc = DateTime.UtcNow
    };

    private static ImportedTransaction Tx(
        Fixture fixture,
        int categoryId,
        decimal amount,
        string description,
        int month) => new()
        {
            BankAccountId = fixture.Bank.Id,
            PeriodId = fixture.Period.Id,
            Date = fixture.Period.PeriodStart.AddMonths(month),
            Description = description,
            Amount = amount,
            CategoryId = categoryId
        };

    private static async Task<Fixture> SeedPeriodAsync(AccountsDbContext db, string name)
    {
        var tenant = new Tenant { Name = "Tax Fixture Firm", Slug = Guid.NewGuid().ToString("N") };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var company = Company(tenant.Id, name);
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var period = Period(company.Id, 2025);
        db.AccountingPeriods.Add(period);
        AddCategory(db, company.Id, "1400", "Bank control", AccountCategoryType.Asset);
        var bank = new BankAccount { CompanyId = company.Id, Name = "Current", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        return new Fixture(tenant, company, period, bank);
    }

    // A charity-chart CLG without a charitable tax exemption (so it posts corporation tax).
    private static async Task<CharityFixture> SeedCharityPeriodAsync(AccountsDbContext db, string name)
    {
        var tenant = new Tenant { Name = "Tax Fixture Firm", Slug = Guid.NewGuid().ToString("N") };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var company = Company(tenant.Id, name);
        company.CompanyType = CompanyType.CompanyLimitedByGuarantee;
        company.ChartOfAccountsVariant = ChartOfAccountsVariant.CharityClg;
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var period = Period(company.Id, 2025);
        db.AccountingPeriods.Add(period);
        db.AccountCategories.AddRange(ChartDefinitions.CharityClg.CreateCategories(company.Id));
        var bank = new BankAccount { CompanyId = company.Id, Name = "Current", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        var ids = await db.AccountCategories
            .Where(category => category.CompanyId == company.Id)
            .ToDictionaryAsync(category => category.Code, category => category.Id);
        return new CharityFixture(tenant, company, period, bank, ids);
    }

    private static Company Company(int tenantId, string name) => new()
    {
        TenantId = tenantId,
        LegalName = name,
        CroNumber = Guid.NewGuid().ToString("N")[..8],
        TaxReference = "1234567A",
        CompanyType = CompanyType.Private,
        IncorporationDate = new DateOnly(2020, 1, 1),
        AnnualReturnDate = new DateOnly(2024, 9, 15),
        IsTrading = true,
        RegisteredOfficeAddress1 = "1 Tax Street",
        RegisteredOfficeCity = "Dublin",
        RegisteredOfficeCounty = "Dublin"
    };

    private static AccountingPeriod Period(int companyId, int year) => new()
    {
        CompanyId = companyId,
        PeriodStart = new DateOnly(year, 1, 1),
        PeriodEnd = new DateOnly(year, 12, 31),
        IsFirstYear = year == 2024
    };

    private static AccountCategory AddCategory(
        AccountsDbContext db,
        int companyId,
        string code,
        string name,
        AccountCategoryType type,
        TaxTreatment treatment = TaxTreatment.Deductible)
    {
        var category = new AccountCategory
        {
            CompanyId = companyId,
            Code = code,
            Name = name,
            Type = type,
            TaxTreatment = treatment
        };
        db.AccountCategories.Add(category);
        db.SaveChanges();
        return category;
    }

    private static DefaultHttpContext AuthenticatedContext(int tenantId, int companyId, int periodId)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = $"/api/companies/{companyId}/periods/{periodId}/revenue/scope-review";
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            900,
            tenantId,
            "Tax Fixture Firm",
            "fixture-tax@example.invalid",
            "Automated corporation-tax preparer (not qualified acceptance)",
            "Accountant");
        return context;
    }

    private static JsonElement FixtureScenario(string code)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "corporation-tax-independent-v1.json")));
        return document.RootElement.GetProperty("scenarios")
            .EnumerateArray()
            .Single(item => item.GetProperty("code").GetString() == code)
            .Clone();
    }

    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AccountsDbContext(options);
    }

    private record Fixture(Tenant Tenant, Company Company, AccountingPeriod Period, BankAccount Bank);

    private sealed record CharityFixture(
        Tenant Tenant,
        Company Company,
        AccountingPeriod Period,
        BankAccount Bank,
        IReadOnlyDictionary<string, int> Ids) : Fixture(Tenant, Company, Period, Bank);
}
