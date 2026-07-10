using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounts.Tests;

public sealed class AnnualReturnDateDeadlineTests
{
    private const string EvidenceSha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task CroPeakExample_RetainsExactArdMadeUpToDateAndDeliveryDeadlineSeparately()
    {
        await using var db = CreateDb();
        var period = await SeedAsync(db, new DateOnly(2025, 9, 30), new DateOnly(2024, 12, 31));

        var deadline = (await new DeadlineService(db).CalculateDeadlinesAsync(period.CompanyId, period.Id))
            .Single(item => item.DeadlineType == DeadlineType.CRO);

        Assert.Equal(new DateOnly(2025, 9, 30), deadline.AnnualReturnDate);
        Assert.Equal(new DateOnly(2025, 9, 30), deadline.ReturnMadeUpToDate);
        Assert.Equal(new DateOnly(2025, 9, 30), deadline.FinancialStatementsLatestMadeUpToDate);
        Assert.Equal(new DateOnly(2025, 11, 25), deadline.DeliveryDueDate);
        Assert.Equal(deadline.DeliveryDueDate, deadline.CalculatedDueDate);
        Assert.Equal(deadline.CalculatedDueDate, deadline.DueDate);
        Assert.False(deadline.MadeUpToDateBroughtForwardForAccountsAge);
        Assert.Equal(DeadlineService.CroRuleVersion, deadline.CalculationRuleVersion);
        Assert.Equal(DeadlineService.CroGuidanceUrl, deadline.CalculationSourceUrl);
        Assert.Matches("^[a-f0-9]{64}$", deadline.CalculationFingerprintSha256!);
    }

    [Fact]
    public async Task ExactNonMonthEndArd_DoesNotAssumeLastDayAndMovesSundayExpiry()
    {
        await using var db = CreateDb();
        var period = await SeedAsync(db, new DateOnly(2025, 8, 10), new DateOnly(2024, 12, 31));

        var deadline = (await new DeadlineService(db).CalculateDeadlinesAsync(period.CompanyId, period.Id))
            .Single(item => item.DeadlineType == DeadlineType.CRO);

        Assert.Equal(new DateOnly(2025, 8, 10), deadline.ReturnMadeUpToDate);
        Assert.Equal(new DateOnly(2025, 10, 6), deadline.DueDate);
        Assert.NotEqual(new DateOnly(2025, 10, 26), deadline.DueDate);
    }

    [Fact]
    public async Task NineMonthRule_BringsMadeUpToDateForwardWithoutRelabellingItAsTheArd()
    {
        await using var db = CreateDb();
        var period = await SeedAsync(db, new DateOnly(2025, 10, 15), new DateOnly(2024, 12, 31));

        var deadline = (await new DeadlineService(db).CalculateDeadlinesAsync(period.CompanyId, period.Id))
            .Single(item => item.DeadlineType == DeadlineType.CRO);

        Assert.Equal(new DateOnly(2025, 10, 15), deadline.AnnualReturnDate);
        Assert.Equal(new DateOnly(2025, 9, 30), deadline.FinancialStatementsLatestMadeUpToDate);
        Assert.Equal(new DateOnly(2025, 9, 30), deadline.ReturnMadeUpToDate);
        Assert.True(deadline.MadeUpToDateBroughtForwardForAccountsAge);
        Assert.Equal(new DateOnly(2025, 11, 25), deadline.DueDate);
    }

    [Theory]
    [InlineData("2023-12-05", "2024-09-05")]
    [InlineData("2023-12-31", "2024-09-23")]
    public async Task RevenueDeadline_UsesEarlierOfExactNineMonthDateAndRosDay23(
        string periodEndValue,
        string expectedDueValue)
    {
        await using var db = CreateDb();
        var periodEnd = DateOnly.Parse(periodEndValue);
        var period = await SeedAsync(db, new DateOnly(2024, 9, 30), periodEnd);

        var deadline = (await new DeadlineService(db).CalculateDeadlinesAsync(period.CompanyId, period.Id))
            .Single(item => item.DeadlineType == DeadlineType.Revenue);

        Assert.Equal(DateOnly.Parse(expectedDueValue), deadline.CalculatedDueDate);
        Assert.Equal(deadline.CalculatedDueDate, deadline.DueDate);
    }

    [Fact]
    public void LeapYearAnniversary_PreservesExactDayUsingCalendarYearSemantics()
    {
        var occurrence = DeadlineService.ResolveAnnualReturnDateOccurrence(
            new DateOnly(2024, 2, 29),
            new DateOnly(2025, 1, 31));

        Assert.Equal(new DateOnly(2025, 2, 28), occurrence);
        Assert.Equal(new DateOnly(2025, 4, 25), DeadlineService.MoveToNextWorkingDay(occurrence.AddDays(56)));
    }

    [Theory]
    [InlineData(2025, 11, 23, 2025, 11, 24)] // Sunday
    [InlineData(2025, 3, 17, 2025, 3, 18)] // St Patrick's Day
    [InlineData(2026, 2, 2, 2026, 2, 3)] // St Brigid's Day, first Monday
    [InlineData(2030, 2, 1, 2030, 2, 4)] // St Brigid's Day when 1 February is Friday
    [InlineData(2021, 12, 27, 2021, 12, 29)] // Christmas weekend substitute collision
    public void IrishNonWorkingExpiry_MovesToNextWorkingDay(
        int year,
        int month,
        int day,
        int expectedYear,
        int expectedMonth,
        int expectedDay)
    {
        Assert.Equal(
            new DateOnly(expectedYear, expectedMonth, expectedDay),
            DeadlineService.MoveToNextWorkingDay(new DateOnly(year, month, day)));
    }

    [Fact]
    public async Task MissingExactArd_BlocksInsteadOfInventingMonthEnd()
    {
        await using var db = CreateDb();
        var company = new Company
        {
            LegalName = "Legacy Month Only Limited",
            IncorporationDate = new DateOnly(2020, 1, 1),
            AnnualReturnDate = null
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2024, 1, 1),
            PeriodEnd = new DateOnly(2024, 12, 31)
        };
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new DeadlineService(db).CalculateDeadlinesAsync(company.Id, period.Id));

        Assert.Contains("exact Annual Return Date", error.Message);
        Assert.Empty(db.FilingDeadlines);
    }

    [Fact]
    public async Task ChangedArd_IsAppendOnlyEvidenceWithActorAndIntegrityHash()
    {
        await using var db = CreateDb();
        var period = await SeedAsync(db, new DateOnly(2025, 9, 30), new DateOnly(2024, 12, 31));
        var actor = Actor();
        var service = new AnnualReturnDateService(db, new AuditService(db));

        var record = await service.RecordChangeAsync(
            period.CompanyId,
            new AnnualReturnDateChangeInput(
                new DateOnly(2025, 6, 30),
                new DateOnly(2025, 6, 30),
                AnnualReturnDateSource.BroughtForward,
                "CRO-B1-EARLY-2025",
                EvidenceSha256,
                "The annual return was made up early and the new anniversary was selected."),
            actor);

        Assert.Equal(new DateOnly(2025, 9, 30), record.PreviousAnnualReturnDate);
        Assert.Equal(new DateOnly(2025, 6, 30), record.AnnualReturnDate);
        Assert.Equal(AnnualReturnDateSource.BroughtForward, record.Source);
        Assert.Equal("user:7", record.RecordedByUserId);
        Assert.Equal("Qualified Reviewer", record.RecordedByDisplayName);
        Assert.True(AnnualReturnDateEvidenceIntegrity.IsValid(record));
        Assert.Equal(new DateOnly(2025, 6, 30), (await db.Companies.SingleAsync()).AnnualReturnDate);
        Assert.Equal(2, await db.AnnualReturnDateRecords.CountAsync());
        Assert.Contains(await db.AuditLogs.ToListAsync(), log =>
            log.Action == AuditEventCodes.AnnualReturnDateRecorded
            && log.UserId == "user:7");

        record.ChangeReason = "tampered";
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ManualDeadlineOverride_RetainsStatutoryDateAndNeedsReviewWhenArdBasisChanges()
    {
        await using var db = CreateDb();
        var period = await SeedAsync(db, new DateOnly(2025, 9, 30), new DateOnly(2024, 12, 31));
        var audit = new AuditService(db);
        var deadlineService = new DeadlineService(db, audit);
        var original = (await deadlineService.CalculateDeadlinesAsync(period.CompanyId, period.Id))
            .Single(item => item.DeadlineType == DeadlineType.CRO);
        var overrideDate = original.CalculatedDueDate.AddDays(14);

        var overridden = await deadlineService.RecordManualOverrideAsync(
            period.CompanyId,
            period.Id,
            DeadlineType.CRO,
            overrideDate,
            "District Court order extended this return's delivery deadline.",
            "COURT-ORDER-2025-001",
            EvidenceSha256,
            Actor());

        Assert.Equal(original.CalculatedDueDate, overridden.CalculatedDueDate);
        Assert.Equal(overrideDate, overridden.DueDate);
        Assert.Equal("Active", overridden.ManualOverrideStatus);
        Assert.Equal(original.CalculationFingerprintSha256, overridden.ManualOverrideCalculationFingerprintSha256);

        await new AnnualReturnDateService(db, audit).RecordChangeAsync(
            period.CompanyId,
            new AnnualReturnDateChangeInput(
                new DateOnly(2025, 10, 31),
                new DateOnly(2025, 10, 1),
                AnnualReturnDateSource.CroRecord,
                "CRO-CORE-CORRECTION-2025",
                EvidenceSha256,
                "CORE now records a corrected exact ARD for the company."),
            Actor());
        var recalculated = (await deadlineService.CalculateDeadlinesAsync(period.CompanyId, period.Id))
            .Single(item => item.DeadlineType == DeadlineType.CRO);

        Assert.Equal("NeedsReview", recalculated.ManualOverrideStatus);
        Assert.Equal(recalculated.CalculatedDueDate, recalculated.DueDate);
        Assert.Equal("COURT-ORDER-2025-001", recalculated.ManualOverrideEvidenceReference);
        Assert.NotEqual(recalculated.CalculationFingerprintSha256, recalculated.ManualOverrideCalculationFingerprintSha256);
    }

    [Fact]
    public void ArdChangeValidation_RejectsOverlongB73AndUnevidencedManualOverride()
    {
        var current = new DateOnly(2025, 9, 30);
        var b73Errors = AnnualReturnDateService.Validate(
            current,
            new AnnualReturnDateChangeInput(
                new DateOnly(2026, 4, 1),
                new DateOnly(2025, 10, 1),
                AnnualReturnDateSource.ExtendedB73,
                "B73-2025-001",
                EvidenceSha256,
                "The company approved an ARD extension supported by the retained B73."),
            initial: false);
        var manualErrors = AnnualReturnDateService.Validate(
            current,
            new AnnualReturnDateChangeInput(
                new DateOnly(2025, 10, 15),
                new DateOnly(2025, 10, 1),
                AnnualReturnDateSource.ManualOverride,
                "MANUAL-ARD-001",
                null,
                "Manual correction supported by a reviewer decision."),
            initial: false);

        Assert.Contains("annualReturnDate", b73Errors.Keys);
        Assert.Contains("annualReturnDateEvidenceSha256", manualErrors.Keys);
    }

    private static async Task<AccountingPeriod> SeedAsync(
        AccountsDbContext db,
        DateOnly annualReturnDate,
        DateOnly periodEnd)
    {
        var actor = Actor();
        var company = new Company
        {
            TenantId = 1,
            LegalName = "Exact ARD Limited",
            IncorporationDate = new DateOnly(2020, 1, 1),
            IsTrading = true
        };
        db.Companies.Add(company);
        new AnnualReturnDateService(db, new AuditService(db)).PrepareInitial(
            company,
            new AnnualReturnDateChangeInput(
                annualReturnDate,
                annualReturnDate,
                AnnualReturnDateSource.CroRecord,
                "CRO-CORE-ARD-2025",
                EvidenceSha256,
                null),
            actor);
        await db.SaveChangesAsync();
        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = periodEnd.AddYears(-1).AddDays(1),
            PeriodEnd = periodEnd
        };
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();
        return period;
    }

    private static AuthenticatedUser Actor() => new(
        7,
        1,
        "Tenant",
        "reviewer@example.ie",
        "Qualified Reviewer",
        "Owner");

    private static AccountsDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase($"ard-deadlines-{Guid.NewGuid():N}")
            .Options);
}
