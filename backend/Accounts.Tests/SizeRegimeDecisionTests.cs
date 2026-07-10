using System.Text;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounts.Tests;

public sealed class SizeRegimeDecisionTests
{
    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, true, true)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, false)]
    public void SubsequentYearRule_CoversEveryRawAndPriorQualificationCombination(
        bool currentRaw,
        bool priorRaw,
        bool priorQualified,
        bool expected) =>
        Assert.Equal(expected, SizeClassificationService.QualifiesInSubsequentYear(currentRaw, priorRaw, priorQualified));

    [Theory]
    [InlineData("2025-01-01", "2025-06-30", 450000, 0.5)]
    [InlineData("2025-01-01", "2025-12-31", 900000, 1.0)]
    [InlineData("2025-01-01", "2026-06-30", 1350000, 1.5)]
    public async Task Turnover_IsAnnualisedForSixTwelveAndEighteenMonthPeriods(
        string startValue,
        string endValue,
        decimal turnover,
        decimal expectedYears)
    {
        await using var db = CreateDbContext();
        var period = await SeedPeriodAsync(
            db,
            DateOnly.Parse(startValue),
            DateOnly.Parse(endValue),
            true,
            turnover,
            450000m,
            10);

        var result = await Service(db).ClassifyAsync(period.CompanyId, period.Id);

        Assert.Equal(CompanySizeClass.Micro, result.CalculatedClass);
        Assert.Equal(900000m, result.AnnualisedTurnover);
        Assert.Equal(expectedYears, result.PeriodLengthInYears);
    }

    [Fact]
    public async Task ThresholdElection_SelectsHistoricalOrCurrentScheduleFor2023()
    {
        await using var historicalDb = CreateDbContext();
        var historical = await SeedPeriodAsync(
            historicalDb,
            new DateOnly(2023, 1, 1),
            new DateOnly(2023, 12, 31),
            true,
            800000m,
            400000m,
            11,
            new DateOnly(2024, 1, 1));
        var historicalResult = await Service(historicalDb).ClassifyAsync(historical.CompanyId, historical.Id);

        await using var electedDb = CreateDbContext();
        var elected = await SeedPeriodAsync(
            electedDb,
            new DateOnly(2023, 1, 1),
            new DateOnly(2023, 12, 31),
            true,
            800000m,
            400000m,
            11,
            new DateOnly(2023, 1, 1));
        var electedResult = await Service(electedDb).ClassifyAsync(elected.CompanyId, elected.Id);

        Assert.Equal(CompanySizeClass.Small, historicalResult.CalculatedClass);
        Assert.Equal("CA-2014-2017-HISTORICAL", historicalResult.ThresholdScheduleCode);
        Assert.Equal(CompanySizeClass.Micro, electedResult.CalculatedClass);
        Assert.Equal("SI-301-2024", electedResult.ThresholdScheduleCode);
    }

    [Theory]
    [InlineData("2022-01-01", "2022-12-31", 700000, 350000, 10, CompanySizeClass.Micro)]
    [InlineData("2022-01-01", "2022-12-31", 12000000, 6000000, 50, CompanySizeClass.Small)]
    [InlineData("2022-01-01", "2022-12-31", 40000000, 20000000, 250, CompanySizeClass.Medium)]
    [InlineData("2025-01-01", "2025-12-31", 900000, 450000, 10, CompanySizeClass.Micro)]
    [InlineData("2025-01-01", "2025-12-31", 15000000, 7500000, 50, CompanySizeClass.Small)]
    [InlineData("2025-01-01", "2025-12-31", 50000000, 25000000, 250, CompanySizeClass.Medium)]
    public async Task HistoricalAndCurrentThresholdBoundaries_AreInclusive(
        string startValue,
        string endValue,
        decimal turnover,
        decimal balanceSheet,
        int employees,
        CompanySizeClass expected)
    {
        await using var db = CreateDbContext();
        var period = await SeedPeriodAsync(
            db,
            DateOnly.Parse(startValue),
            DateOnly.Parse(endValue),
            true,
            turnover,
            balanceSheet,
            employees);
        var result = await Service(db).ClassifyAsync(period.CompanyId, period.Id);
        Assert.Equal(expected, result.CalculatedClass);
    }

    [Fact]
    public async Task FirstYearUsesCurrentRawTest_SubsequentYearRequiresRetainedPriorRawFigures()
    {
        await using var firstDb = CreateDbContext();
        var first = await SeedPeriodAsync(firstDb, new(2025, 1, 1), new(2025, 12, 31), true, 900000m, 450000m, 10);
        var firstResult = await Service(firstDb).ClassifyAsync(first.CompanyId, first.Id);
        Assert.Equal(CompanySizeClass.Micro, firstResult.CalculatedClass);

        await using var subsequentDb = CreateDbContext();
        var subsequent = await SeedPeriodAsync(subsequentDb, new(2025, 1, 1), new(2025, 12, 31), false, 900000m, 450000m, 10);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Service(subsequentDb).ClassifyAsync(subsequent.CompanyId, subsequent.Id));
        Assert.Null((await subsequentDb.SizeClassifications.SingleAsync()).QualificationNotes);
    }

    [Fact]
    public async Task UpwardAndDownwardGrace_UsesRawPriorAndPriorEffectiveDecisions()
    {
        // First micro breach retains Micro for one year.
        await using var upwardDb = CreateDbContext();
        var priorMicro = await SeedPeriodAsync(upwardDb, new(2024, 1, 1), new(2024, 12, 31), true, 900000m, 450000m, 10);
        await Service(upwardDb).ClassifyAsync(priorMicro.CompanyId, priorMicro.Id);
        var currentSmallRaw = await AddPeriodAsync(upwardDb, priorMicro.CompanyId, new(2025, 1, 1), new(2025, 12, 31), false, 1000000m, 500000m, 11);
        var upward = await Service(upwardDb).ClassifyAsync(currentSmallRaw.CompanyId, currentSmallRaw.Id);
        Assert.Equal(CompanySizeClass.Micro, upward.CalculatedClass);
        Assert.Equal(CompanySizeClass.Small, upward.RawCurrentClass);

        // First qualifying Micro year after Small remains Small; the second consecutive raw Micro year becomes Micro.
        await using var downwardDb = CreateDbContext();
        var priorSmall = await SeedPeriodAsync(downwardDb, new(2023, 1, 1), new(2023, 12, 31), true, 1000000m, 500000m, 11);
        await Service(downwardDb).ClassifyAsync(priorSmall.CompanyId, priorSmall.Id);
        var firstMicroRaw = await AddPeriodAsync(downwardDb, priorSmall.CompanyId, new(2024, 1, 1), new(2024, 12, 31), false, 900000m, 450000m, 10);
        var firstDown = await Service(downwardDb).ClassifyAsync(firstMicroRaw.CompanyId, firstMicroRaw.Id);
        Assert.Equal(CompanySizeClass.Small, firstDown.CalculatedClass);
        var secondMicroRaw = await AddPeriodAsync(downwardDb, priorSmall.CompanyId, new(2025, 1, 1), new(2025, 12, 31), false, 900000m, 450000m, 10);
        var secondDown = await Service(downwardDb).ClassifyAsync(secondMicroRaw.CompanyId, secondMicroRaw.Id);
        Assert.Equal(CompanySizeClass.Micro, secondDown.CalculatedClass);
    }

    [Fact]
    public async Task SubsequentYearRejectsAStalePriorDecisionBeforeMutation()
    {
        await using var db = CreateDbContext();
        var prior = await SeedPeriodAsync(db, new(2024, 1, 1), new(2024, 12, 31), true, 900000m, 450000m, 10);
        await Service(db).ClassifyAsync(prior.CompanyId, prior.Id);
        var current = await AddPeriodAsync(db, prior.CompanyId, new(2025, 1, 1), new(2025, 12, 31), false, 900000m, 450000m, 10);

        (await db.SizeClassifications.SingleAsync(s => s.PeriodId == prior.Id)).Turnover = 20000000m;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db).ClassifyAsync(current.CompanyId, current.Id));
        var unchanged = await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == current.Id);
        Assert.Null(unchanged.DecisionInputFingerprintSha256);
        Assert.Null(unchanged.QualificationNotes);
    }

    [Fact]
    public async Task ThresholdElectionIsConsistentAcrossEveryYearBeginningIn2023OrLater()
    {
        await using var db = CreateDbContext();
        var prior = await SeedPeriodAsync(
            db,
            new(2023, 1, 1),
            new(2023, 12, 31),
            true,
            800000m,
            400000m,
            11,
            new DateOnly(2024, 1, 1));
        await Service(db).ClassifyAsync(prior.CompanyId, prior.Id);
        var current = await AddPeriodAsync(
            db,
            prior.CompanyId,
            new(2024, 1, 1),
            new(2024, 12, 31),
            false,
            800000m,
            400000m,
            11,
            new DateOnly(2023, 1, 1));

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db).ClassifyAsync(current.CompanyId, current.Id));
        Assert.Contains("applied consistently", error.Message);
    }

    [Fact]
    public async Task CurrentPriorOverrideControlsTheConsecutiveYearGraceDecision()
    {
        await using var db = CreateDbContext();
        var prior = await SeedPeriodAsync(db, new(2024, 1, 1), new(2024, 12, 31), true, 900000m, 450000m, 10);
        var service = Service(db);
        await service.ClassifyAsync(prior.CompanyId, prior.Id);
        var evidence = Encoding.UTF8.GetBytes("retained-prior-year-professional-classification-review");
        await service.ApplyOverrideAsync(
            prior.CompanyId,
            prior.Id,
            new(
                CompanySizeClass.Small,
                "Conservative prior-year treatment supported by retained advice.",
                evidence,
                FilingReleaseGate.ComputeSha256(evidence)),
            "Reviewer",
            "Reviewer User");
        var current = await AddPeriodAsync(db, prior.CompanyId, new(2025, 1, 1), new(2025, 12, 31), false, 1000000m, 500000m, 11);

        var result = await service.ClassifyAsync(current.CompanyId, current.Id);

        Assert.Equal(CompanySizeClass.Small, result.CalculatedClass);
    }

    [Fact]
    public async Task Pre2017PeriodsFailClosedWithoutARetainedTransitionElection()
    {
        await using var db = CreateDbContext();
        var period = await SeedPeriodAsync(db, new(2016, 1, 1), new(2016, 12, 31), true, 100000m, 100000m, 1);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db).ClassifyAsync(period.CompanyId, period.Id));
        Assert.Contains("beginning on or after 2017-01-01", error.Message);
    }

    [Fact]
    public async Task IneligibleAndMicroExclusionFlags_AreAppliedFailClosed()
    {
        var ineligibleFlags = new Action<Company>[]
        {
            c => c.CompanyType = CompanyType.PublicLimitedCompany,
            c => c.IsListedSecurities = true,
            c => c.IsCreditInstitution = true,
            c => c.IsInsuranceUndertaking = true,
            c => c.IsPensionFund = true,
            c => c.IsFifthScheduleEntity = true,
            c => c.IsOtherIneligibleEntity = true
        };
        foreach (var apply in ineligibleFlags)
        {
            await using var db = CreateDbContext();
            var period = await SeedPeriodAsync(db, new(2025, 1, 1), new(2025, 12, 31), true, 100000m, 100000m, 1, mutateCompany: apply);
            var result = await Service(db).ClassifyAsync(period.CompanyId, period.Id);
            Assert.Equal(CompanySizeClass.Large, result.CalculatedClass);
            Assert.True(result.IsIneligibleEntity);
            Assert.Equal(["Full"], result.AvailableRegimes);
        }

        var microExclusions = new Action<Company>[]
        {
            c => c.IsInvestment = true,
            c => c.IsFinancialHoldingUndertaking = true,
            c => c.PreparesGroupFinancialStatements = true,
            c => c.IncludedInHigherConsolidatedFinancialStatements = true,
            c => c.IsSubsidiary = true
        };
        foreach (var apply in microExclusions)
        {
            await using var db = CreateDbContext();
            var period = await SeedPeriodAsync(db, new(2025, 1, 1), new(2025, 12, 31), true, 100000m, 100000m, 1, mutateCompany: apply);
            var result = await Service(db).ClassifyAsync(period.CompanyId, period.Id);
            Assert.Equal(CompanySizeClass.Small, result.CalculatedClass);
            Assert.False(result.CanUseMicro);
            Assert.True(result.CanFileAbridged);
        }

        await using var holdingDb = CreateDbContext();
        var holding = await SeedPeriodAsync(holdingDb, new(2025, 1, 1), new(2025, 12, 31), true, 100000m, 100000m, 1, mutateCompany: c => c.IsHolding = true);
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service(holdingDb).ClassifyAsync(holding.CompanyId, holding.Id));
    }

    [Fact]
    public async Task EveryIncompatibleElectionIsRejectedWithoutPersistence()
    {
        var cases = new[]
        {
            (100000m, 100000m, 1, ElectedRegime.Medium),
            (1000000m, 500000m, 11, ElectedRegime.Micro),
            (1000000m, 500000m, 11, ElectedRegime.Medium),
            (20000000m, 10000000m, 100, ElectedRegime.Micro),
            (20000000m, 10000000m, 100, ElectedRegime.Small),
            (20000000m, 10000000m, 100, ElectedRegime.SmallAbridged),
            (60000000m, 30000000m, 300, ElectedRegime.Micro),
            (60000000m, 30000000m, 300, ElectedRegime.Small),
            (60000000m, 30000000m, 300, ElectedRegime.SmallAbridged),
            (60000000m, 30000000m, 300, ElectedRegime.Medium)
        };

        foreach (var item in cases)
        {
            await using var db = CreateDbContext();
            var period = await SeedPeriodAsync(db, new(2025, 1, 1), new(2025, 12, 31), true, item.Item1, item.Item2, item.Item3);
            await Service(db).ClassifyAsync(period.CompanyId, period.Id);
            await Assert.ThrowsAsync<BusinessRuleException>(() =>
                new FilingRegimeService(db).DetermineAsync(period.CompanyId, period.Id, item.Item4));
            Assert.Empty(await db.FilingRegimes.ToListAsync());
        }
    }

    [Fact]
    public async Task InvalidElectionLeavesExistingRegimeUnchanged()
    {
        await using var db = CreateDbContext();
        var period = await SeedPeriodAsync(db, new(2025, 1, 1), new(2025, 12, 31), true, 100000m, 100000m, 1);
        await Service(db).ClassifyAsync(period.CompanyId, period.Id);
        var regimeService = new FilingRegimeService(db);
        await regimeService.DetermineAsync(period.CompanyId, period.Id, ElectedRegime.Micro);
        var before = await db.FilingRegimes.AsNoTracking().SingleAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            regimeService.DetermineAsync(period.CompanyId, period.Id, ElectedRegime.Medium));

        var after = await db.FilingRegimes.AsNoTracking().SingleAsync();
        Assert.Equal(before.ElectedRegime, after.ElectedRegime);
        Assert.Equal(before.DeterminedAt, after.DeterminedAt);
        Assert.Equal(before.RequiredStatementsJson, after.RequiredStatementsJson);
    }

    [Fact]
    public async Task NegativePersistedInputsFailBeforeDecisionMutation()
    {
        await using var db = CreateDbContext();
        var period = await SeedPeriodAsync(db, new(2025, 1, 1), new(2025, 12, 31), true, -1m, 0m, 0);
        var before = await db.SizeClassifications.AsNoTracking().SingleAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db).ClassifyAsync(period.CompanyId, period.Id));
        var after = await db.SizeClassifications.AsNoTracking().SingleAsync();
        Assert.Equal(before.CalculatedClass, after.CalculatedClass);
        Assert.Equal(before.CalculatedAt, after.CalculatedAt);
        Assert.Null(after.DecisionInputFingerprintSha256);
        Assert.Null(after.QualificationNotes);
    }

    [Fact]
    public async Task OverrideCannotReduceCalculatedStatutoryClass()
    {
        await using var db = CreateDbContext();
        var period = await SeedPeriodAsync(db, new(2025, 1, 1), new(2025, 12, 31), true, 1000000m, 500000m, 11);
        var service = Service(db);
        await service.ClassifyAsync(period.CompanyId, period.Id);
        var evidence = Encoding.UTF8.GetBytes("retained-professional-classification-review");
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.ApplyOverrideAsync(
            period.CompanyId,
            period.Id,
            new(
                CompanySizeClass.Micro,
                "Unsupported downward reclassification request with evidence.",
                evidence,
                FilingReleaseGate.ComputeSha256(evidence)),
            "Reviewer",
            "Reviewer User"));
        Assert.Null((await db.SizeClassifications.SingleAsync()).OverrideClass);
    }

    [Fact]
    public async Task OverrideRequiresAuthorityReasonExactEvidenceCompatibilityAndRereview()
    {
        await using var db = CreateDbContext();
        var period = await SeedPeriodAsync(db, new(2025, 1, 1), new(2025, 12, 31), true, 100000m, 100000m, 1);
        var audit = new AuditService(db);
        var service = Service(db, audit);
        await service.ClassifyAsync(period.CompanyId, period.Id, "reviewer@example.ie");
        var evidence = Encoding.UTF8.GetBytes("retained-professional-classification-review");
        var hash = FilingReleaseGate.ComputeSha256(evidence);

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.ApplyOverrideAsync(
            period.CompanyId,
            period.Id,
            new(CompanySizeClass.Small, "Conservative classification supported by retained advice.", evidence, hash),
            "Accountant",
            "Accountant User"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.ApplyOverrideAsync(
            period.CompanyId,
            period.Id,
            new(CompanySizeClass.Small, "Too short", evidence, hash),
            "Reviewer",
            "Reviewer User"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.ApplyOverrideAsync(
            period.CompanyId,
            period.Id,
            new(CompanySizeClass.Small, "Conservative classification supported by retained advice.", evidence, new string('a', 64)),
            "Reviewer",
            "Reviewer User"));

        db.FilingRegimes.Add(new FilingRegime { PeriodId = period.Id, ElectedRegime = ElectedRegime.Micro, CanUseMicro = true, CanFileAbridged = true, AuditExempt = true });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.ApplyOverrideAsync(
            period.CompanyId,
            period.Id,
            new(CompanySizeClass.Small, "Conservative classification supported by retained advice.", evidence, hash),
            "Reviewer",
            "Reviewer User"));
        Assert.Null((await db.SizeClassifications.SingleAsync()).OverrideClass);

        db.FilingRegimes.RemoveRange(db.FilingRegimes);
        await db.SaveChangesAsync();
        var applied = await service.ApplyOverrideAsync(
            period.CompanyId,
            period.Id,
            new(CompanySizeClass.Small, "Conservative classification supported by retained advice.", evidence, hash),
            "Reviewer",
            "Reviewer User",
            "reviewer@example.ie");
        Assert.Equal(CompanySizeClass.Small, applied.OverrideClass);
        Assert.False(applied.OverrideRequiresRereview);
        Assert.Equal(hash, applied.OverrideEvidenceSha256);
        Assert.Contains(await db.AuditLogs.ToListAsync(), entry => entry.Action == AuditEventCodes.SizeClassificationOverrideApplied);

        applied.Turnover = 20000000m;
        await db.SaveChangesAsync();
        await service.ClassifyAsync(period.CompanyId, period.Id);
        Assert.True(applied.OverrideRequiresRereview);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new FilingRegimeService(db).DetermineAsync(period.CompanyId, period.Id, ElectedRegime.Full));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void NegativeInputsAreRejected(decimal turnover, decimal balanceSheet, int employees) =>
        Assert.Throws<BusinessRuleException>(() =>
            SizeClassificationService.ValidateInputs(turnover, balanceSheet, employees, null));

    private static AccountsDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static SizeClassificationService Service(AccountsDbContext db, AuditService? audit = null) =>
        new(db, Options.Create(new SizeThresholdConfig()), audit);

    private static async Task<AccountingPeriod> SeedPeriodAsync(
        AccountsDbContext db,
        DateOnly start,
        DateOnly end,
        bool firstYear,
        decimal turnover,
        decimal balanceSheet,
        int employees,
        DateOnly? election = null,
        Action<Company>? mutateCompany = null)
    {
        var tenant = new Tenant { Name = "Decision Test Practice", Slug = Guid.NewGuid().ToString("N") };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var company = new Company
        {
            TenantId = tenant.Id,
            LegalName = "Decision Table Limited",
            CompanyType = CompanyType.Private,
            IncorporationDate = start,
            AnnualReturnDate = new DateOnly(2024, 9, 15)
        };
        mutateCompany?.Invoke(company);
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return await AddPeriodAsync(db, company.Id, start, end, firstYear, turnover, balanceSheet, employees, election);
    }

    private static async Task<AccountingPeriod> AddPeriodAsync(
        AccountsDbContext db,
        int companyId,
        DateOnly start,
        DateOnly end,
        bool firstYear,
        decimal turnover,
        decimal balanceSheet,
        int employees,
        DateOnly? election = null)
    {
        var period = new AccountingPeriod
        {
            CompanyId = companyId,
            PeriodStart = start,
            PeriodEnd = end,
            IsFirstYear = firstYear
        };
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            Turnover = turnover,
            BalanceSheetTotal = balanceSheet,
            AvgEmployees = employees,
            ThresholdElectionEffectiveFrom = election
        });
        await db.SaveChangesAsync();
        return period;
    }
}
