using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounts.Tests;

public sealed class DashboardDeadlineBatchTests
{
    [Fact]
    public async Task Batch_ReconcilesEveryAccessibleCompanyAcrossExplicitDeadlineStates()
    {
        await using var db = CreateDb();
        var companies = await SeedCompaniesAsync(db, 105);
        var today = new DateOnly(2026, 7, 10);

        var noDeadlinePeriod = AddPeriod(db, companies[1]);
        var overduePeriod = AddPeriod(db, companies[2]);
        var dueSoonPeriod = AddPeriod(db, companies[3]);
        var scheduledPeriod = AddPeriod(db, companies[4]);
        var filedPeriod = AddPeriod(db, companies[5]);
        var invalidPeriod = AddPeriod(db, companies[6]);
        await db.SaveChangesAsync();

        db.FilingDeadlines.AddRange(
            Deadline(overduePeriod, today.AddDays(-1)),
            Deadline(overduePeriod, today.AddDays(60), type: DeadlineType.Revenue),
            Deadline(dueSoonPeriod, today.AddDays(14)),
            Deadline(dueSoonPeriod, today.AddDays(45), type: DeadlineType.Revenue),
            Deadline(scheduledPeriod, today.AddDays(90)),
            Deadline(scheduledPeriod, today.AddDays(120), type: DeadlineType.Revenue),
            Deadline(filedPeriod, today.AddDays(-30), today.AddDays(-5)),
            Deadline(filedPeriod, today.AddDays(-20), today.AddDays(-4), DeadlineType.Revenue),
            Deadline(invalidPeriod, new DateOnly(2010, 1, 1)),
            Deadline(invalidPeriod, today.AddDays(30), type: DeadlineType.Revenue));
        await db.SaveChangesAsync();

        var service = CreateService(db, today);
        var result = await service.GetAsync(User("Owner"));

        Assert.Equal(105, result.TotalCompanies);
        Assert.Equal(result.TotalCompanies, result.Items.Count);
        Assert.Equal(result.TotalCompanies, result.Counts.Values.Sum());
        Assert.Equal(1, result.UnavailableCount);
        Assert.Equal(DashboardDeadlineStates.NotApplicable, Item(result, companies[0]).State);
        Assert.Equal(DashboardDeadlineStates.NotConfigured, Item(result, companies[1]).State);
        Assert.Equal(DashboardDeadlineStates.Overdue, Item(result, companies[2]).State);
        Assert.Equal(DashboardDeadlineStates.DueSoon, Item(result, companies[3]).State);
        Assert.Equal(DashboardDeadlineStates.Scheduled, Item(result, companies[4]).State);
        Assert.Equal(DashboardDeadlineStates.Filed, Item(result, companies[5]).State);
        Assert.Equal(DashboardDeadlineStates.Unavailable, Item(result, companies[6]).State);
        Assert.Contains("cannot be relied on", Item(result, companies[6]).Message, StringComparison.OrdinalIgnoreCase);

        _ = noDeadlinePeriod;
    }

    [Fact]
    public async Task Batch_RestrictsClientToAssignedCompaniesWithoutInferringMissingRows()
    {
        await using var db = CreateDb();
        var companies = await SeedCompaniesAsync(db, 4);
        var today = new DateOnly(2026, 7, 10);
        var period = AddPeriod(db, companies[2]);
        await db.SaveChangesAsync();
        db.FilingDeadlines.AddRange(
            Deadline(period, today.AddDays(5)),
            Deadline(period, today.AddDays(40), type: DeadlineType.Revenue));
        await db.SaveChangesAsync();

        var result = await CreateService(db, today).GetAsync(User("Client", companies[0].Id, companies[2].Id));

        Assert.Equal(2, result.TotalCompanies);
        Assert.Equal([companies[0].Id, companies[2].Id], result.Items.Select(item => item.CompanyId).Order().ToArray());
        Assert.Equal(DashboardDeadlineStates.NotApplicable, Item(result, companies[0]).State);
        Assert.Equal(DashboardDeadlineStates.DueSoon, Item(result, companies[2]).State);
        Assert.DoesNotContain(result.Items, item => item.CompanyId == companies[1].Id || item.CompanyId == companies[3].Id);
    }

    private static DashboardDeadlineItem Item(DashboardDeadlineBatch batch, Company company) =>
        Assert.Single(batch.Items, item => item.CompanyId == company.Id);

    private static DashboardDeadlineService CreateService(AccountsDbContext db, DateOnly today)
    {
        var instant = new DateTimeOffset(today.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));
        var deadlineService = new DeadlineService(db, timeProvider: new FixedTimeProvider(instant));
        return new DashboardDeadlineService(db, deadlineService);
    }

    private static FilingDeadline Deadline(
        AccountingPeriod period,
        DateOnly dueDate,
        DateOnly? filedDate = null,
        DeadlineType type = DeadlineType.CRO) => new()
    {
        CompanyId = period.CompanyId,
        PeriodId = period.Id,
        DeadlineType = type,
        DueDate = dueDate,
        FiledDate = filedDate,
        FilingReference = filedDate is null ? null : $"CORE-{period.Id}"
    };

    private static AccountingPeriod AddPeriod(AccountsDbContext db, Company company)
    {
        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = true
        };
        db.AccountingPeriods.Add(period);
        return period;
    }

    private static async Task<List<Company>> SeedCompaniesAsync(AccountsDbContext db, int count)
    {
        var tenant = new Tenant { Name = "Deadline Test Firm", Slug = $"deadline-test-{Guid.NewGuid():N}" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var companies = Enumerable.Range(1, count)
            .Select(index => new Company
            {
                TenantId = tenant.Id,
                LegalName = $"Company {index:D3}",
                CroNumber = $"{index:D6}",
                CompanyType = CompanyType.Private,
                IncorporationDate = new DateOnly(2020, 1, 1),
                AnnualReturnDate = new DateOnly(2024, 9, 15)
            })
            .ToList();
        db.Companies.AddRange(companies);
        await db.SaveChangesAsync();
        return companies;
    }

    private static AuthenticatedUser User(string role, params int[] allowedCompanyIds) => new(
        UserId: 1,
        TenantId: 1,
        TenantName: "Deadline Test Firm",
        Email: "deadline@example.ie",
        DisplayName: "Deadline Tester",
        Role: role,
        AllowedCompanyIds: allowedCompanyIds.ToHashSet());

    private static AccountsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase($"dashboard-deadlines-{Guid.NewGuid():N}")
            .Options;
        return new AccountsDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
