using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounts.Tests;

public sealed class PeriodChronologyTests
{
    [Theory]
    [MemberData(nameof(InvalidFirstPeriodCases))]
    public async Task FirstPeriod_InvalidChronologyFailsAtomically(
        DateOnly start,
        DateOnly end,
        bool isFirstYear,
        string expectedMessage)
    {
        await using var db = CreateDb();
        var company = await SeedCompanyAsync(db);
        var service = new PeriodChronologyService(db);
        var proposed = Period(company.Id, start, end, isFirstYear);

        var error = await Assert.ThrowsAsync<PeriodChronologyException>(() => service.CreateAsync(proposed));

        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.AccountingPeriods.ToListAsync());
        Assert.Equal(0, proposed.Id);
    }

    [Fact]
    public async Task Periods_RejectOverlapDuplicateFirstYearAndUnexplainedGapWithoutMutation()
    {
        await using var db = CreateDb();
        var company = await SeedCompanyAsync(db);
        var service = new PeriodChronologyService(db);
        var first = await service.CreateAsync(Period(
            company.Id,
            company.IncorporationDate,
            new DateOnly(2025, 12, 31),
            true));

        await AssertRejectedAsync(
            service,
            Period(company.Id, new DateOnly(2025, 6, 1), new DateOnly(2026, 5, 31), false),
            "overlap");
        await AssertRejectedAsync(
            service,
            Period(company.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), true),
            "only one first-year");
        await AssertRejectedAsync(
            service,
            Period(company.Id, new DateOnly(2026, 1, 2), new DateOnly(2026, 12, 31), false),
            "unexplained chronology gaps");

        Assert.Equal([first.Id], await db.AccountingPeriods.Select(period => period.Id).ToArrayAsync());
    }

    [Fact]
    public async Task AdjacentPeriods_CreateAndComparativesSelectOnlyTheExactPriorPeriod()
    {
        await using var db = CreateDb();
        var company = await SeedCompanyAsync(db);
        var service = new PeriodChronologyService(db);
        var first = await service.CreateAsync(Period(
            company.Id,
            company.IncorporationDate,
            new DateOnly(2025, 12, 31),
            true));
        var second = await service.CreateAsync(Period(
            company.Id,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            false));

        var prior = await PeriodChronologyService
            .PriorPeriodQuery(db, company.Id, second.PeriodStart)
            .SingleAsync();
        var noPriorAcrossGap = await PeriodChronologyService
            .PriorPeriodQuery(db, company.Id, new DateOnly(2027, 1, 2))
            .SingleOrDefaultAsync();

        Assert.Equal(first.Id, prior.Id);
        Assert.Null(noPriorAcrossGap);
        Assert.Equal([first.Id, second.Id], await db.AccountingPeriods.OrderBy(period => period.PeriodStart).Select(period => period.Id).ToArrayAsync());
    }

    public static TheoryData<DateOnly, DateOnly, bool, string> InvalidFirstPeriodCases => new()
    {
        { new DateOnly(2024, 12, 31), new DateOnly(2025, 12, 30), true, "before the company incorporation" },
        { new DateOnly(2025, 1, 1), new DateOnly(2026, 7, 1), true, "cannot exceed 18 months" },
        { new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), false, "must be marked as the first year" },
        { new DateOnly(2025, 1, 2), new DateOnly(2025, 12, 31), true, "must begin on the incorporation date" }
    };

    private static async Task AssertRejectedAsync(
        PeriodChronologyService service,
        AccountingPeriod proposed,
        string expectedMessage)
    {
        var error = await Assert.ThrowsAsync<PeriodChronologyException>(() => service.CreateAsync(proposed));
        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static AccountingPeriod Period(
        int companyId,
        DateOnly start,
        DateOnly end,
        bool isFirstYear) => new()
    {
        CompanyId = companyId,
        PeriodStart = start,
        PeriodEnd = end,
        IsFirstYear = isFirstYear
    };

    private static async Task<Company> SeedCompanyAsync(AccountsDbContext db)
    {
        var tenant = new Tenant { Name = "Chronology Firm", Slug = $"chronology-{Guid.NewGuid():N}" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var company = new Company
        {
            TenantId = tenant.Id,
            LegalName = "Chronology Limited",
            CompanyType = CompanyType.Private,
            CroNumber = "123456",
            IncorporationDate = new DateOnly(2025, 1, 1),
            AnnualReturnDate = new DateOnly(2024, 9, 15)
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return company;
    }

    private static AccountsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase($"period-chronology-{Guid.NewGuid():N}")
            .Options;
        return new AccountsDbContext(options);
    }
}
