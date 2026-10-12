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

    [Fact]
    public async Task Batch_TreatsRevenueAsNotApplicableForExemptCharityAndRetainsFiledHistory()
    {
        await using var db = CreateDb();
        var company = Assert.Single(await SeedCompaniesAsync(db, 1));
        company.IsCharitableOrganisation = true;
        company.HoldsCharitableTaxExemption = true;
        company.CharitableTaxExemptionReference = "CHY-12345";
        company.CharitableTaxExemptionConfirmedDate = new DateOnly(2022, 12, 16);
        var period = AddPeriod(db, company);
        await db.SaveChangesAsync();
        var today = new DateOnly(2026, 7, 10);
        db.FilingDeadlines.AddRange(
            Deadline(period, today.AddDays(20)),
            Deadline(period, today.AddDays(40), type: DeadlineType.Charity),
            Deadline(period, today.AddDays(-20), today.AddDays(-10), DeadlineType.Revenue));
        await db.SaveChangesAsync();

        var result = await CreateService(db, today).GetAsync(User("Owner"));

        var item = Item(result, company);
        Assert.Equal(DashboardDeadlineStates.DueSoon, item.State);
        Assert.Equal(DeadlineType.CRO, item.Deadline?.DeadlineType);
        Assert.DoesNotContain("not been calculated", item.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Batch_RanksTheLivePeriodAndReportsUnfiledHistoryAsUnverified()
    {
        // Live defect 2026-10-10: periods back-filled to incorporation have deadlines but no
        // recorded filings, so a 2019 deadline outranked the one live period on the dashboard.
        await using var db = CreateDb();
        var company = Assert.Single(await SeedCompaniesAsync(db, 1));
        company.IncorporationDate = new DateOnly(2017, 7, 20);
        var historic = Enumerable.Range(2018, 8)
            .Select(year => AddPeriod(db, company, new DateOnly(year, 8, 1), new DateOnly(year + 1, 7, 31)))
            .ToList();
        var live = AddPeriod(db, company, new DateOnly(2025, 8, 1), new DateOnly(2026, 7, 31));
        await db.SaveChangesAsync();
        var today = new DateOnly(2026, 10, 10);
        foreach (var period in historic)
        {
            db.FilingDeadlines.AddRange(
                Deadline(period, period.PeriodEnd.AddMonths(11)),
                Deadline(period, period.PeriodEnd.AddMonths(9), type: DeadlineType.Revenue));
        }
        db.FilingDeadlines.AddRange(
            Deadline(live, new DateOnly(2027, 3, 18)),
            Deadline(live, new DateOnly(2027, 4, 23), type: DeadlineType.Revenue));
        await db.SaveChangesAsync();

        var item = Item(await CreateService(db, today).GetAsync(User("Owner")), company);

        Assert.Equal(DashboardDeadlineStates.Scheduled, item.State);
        Assert.Equal(live.Id, item.Deadline?.PeriodId);
        Assert.Equal(new DateOnly(2027, 3, 18), item.Deadline?.DueDate);
        // Not hidden: every unfiled historic deadline is counted and the oldest is identified.
        Assert.Equal(16, item.HistoricUnverifiedCount);
        Assert.Equal(historic[0].Id, item.OldestHistoricUnverified?.PeriodId);
        Assert.Equal(DeadlineType.Revenue, item.OldestHistoricUnverified?.DeadlineType);
        Assert.Contains("16 earlier-period deadlines have no recorded filing", item.Message);
    }

    [Fact]
    public async Task Batch_KeepsAnOverduePriorYearWithAccountingWorkLiveAfterTheNextPeriodIsCreated()
    {
        // Review of #79: the next period can be created before last year's accounts are filed.
        // A prior period holding accounting work is live, so its overdue deadline stays urgent.
        await using var db = CreateDb();
        var company = Assert.Single(await SeedCompaniesAsync(db, 1));
        var prior = AddPeriod(db, company, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        var next = AddPeriod(db, company, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var bank = new BankAccount { CompanyId = company.Id, Name = "Current", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = prior.Id,
            Date = new DateOnly(2025, 6, 1),
            Description = "Receipt",
            Amount = 100m
        });
        var today = new DateOnly(2026, 10, 11);
        db.FilingDeadlines.AddRange(
            Deadline(prior, new DateOnly(2026, 9, 30)),
            Deadline(prior, new DateOnly(2026, 9, 23), type: DeadlineType.Revenue),
            Deadline(next, new DateOnly(2027, 9, 30)),
            Deadline(next, new DateOnly(2027, 9, 23), type: DeadlineType.Revenue));
        await db.SaveChangesAsync();

        var item = Item(await CreateService(db, today).GetAsync(User("Owner")), company);

        Assert.Equal(DashboardDeadlineStates.Overdue, item.State);
        Assert.Equal(prior.Id, item.Deadline?.PeriodId);
        Assert.Equal(new DateOnly(2026, 9, 23), item.Deadline?.DueDate);
        Assert.Equal(0, item.HistoricUnverifiedCount);
    }

    [Fact]
    public async Task Batch_LiveOverdueStillRanksAsOverdueAndRecordedHistoryIsNotCounted()
    {
        await using var db = CreateDb();
        var company = Assert.Single(await SeedCompaniesAsync(db, 1));
        var prior = AddPeriod(db, company, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
        var live = AddPeriod(db, company, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        await db.SaveChangesAsync();
        var today = new DateOnly(2026, 10, 10);
        db.FilingDeadlines.AddRange(
            Deadline(prior, new DateOnly(2025, 9, 30), new DateOnly(2025, 9, 1)),
            Deadline(prior, new DateOnly(2025, 9, 23), new DateOnly(2025, 9, 2), DeadlineType.Revenue),
            Deadline(live, new DateOnly(2026, 9, 30)),
            Deadline(live, new DateOnly(2026, 9, 23), type: DeadlineType.Revenue));
        await db.SaveChangesAsync();

        var item = Item(await CreateService(db, today).GetAsync(User("Owner")), company);

        Assert.Equal(DashboardDeadlineStates.Overdue, item.State);
        Assert.Equal(live.Id, item.Deadline?.PeriodId);
        Assert.Equal(0, item.HistoricUnverifiedCount);
        Assert.Null(item.OldestHistoricUnverified);
    }

    // Batch B fix round: an exempt charity whose corporation-tax liability for the period is confirmed
    // as due has a Revenue (CT1) obligation for that period, exactly as a non-exempt company does.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Batch_TreatsRevenueAsApplicableForAnExemptCharityWithCorporationTaxConfirmedDue(bool revenueCalculated)
    {
        await using var db = CreateDb();
        var company = Assert.Single(await SeedCompaniesAsync(db, 1));
        company.IsCharitableOrganisation = true;
        company.HoldsCharitableTaxExemption = true;
        company.CharitableTaxExemptionReference = "CHY-00000";
        company.CharitableTaxExemptionConfirmedDate = new DateOnly(2022, 12, 16);
        var period = AddPeriod(db, company);
        await db.SaveChangesAsync();
        db.TaxBalances.Add(new TaxBalance
        {
            PeriodId = period.Id,
            TaxType = TaxType.CorporationTax,
            Liability = 150m,
            Balance = 150m,
            ConfirmedDueBy = "Example Reviewer",
            ConfirmedDueAt = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc),
            ConfirmedDueReason = "Income outside the exemption",
            ConfirmedDueLiability = 150m,
            ConfirmedTaxableProfitOutsideExemption = 1_200m
        });
        var today = new DateOnly(2026, 7, 10);
        db.FilingDeadlines.AddRange(
            Deadline(period, today.AddDays(20)),
            Deadline(period, today.AddDays(40), type: DeadlineType.Charity));
        if (revenueCalculated)
            db.FilingDeadlines.Add(Deadline(period, today.AddDays(5), type: DeadlineType.Revenue));
        await db.SaveChangesAsync();

        var item = Item(await CreateService(db, today).GetAsync(User("Owner")), company);

        if (revenueCalculated)
        {
            Assert.Equal(DashboardDeadlineStates.DueSoon, item.State);
            Assert.Equal(DeadlineType.Revenue, item.Deadline?.DeadlineType);
        }
        else
        {
            // The confirmed period is expected to carry a Revenue deadline.
            Assert.Equal(DashboardDeadlineStates.NotConfigured, item.State);
        }
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

    private static AccountingPeriod AddPeriod(
        AccountsDbContext db,
        Company company,
        DateOnly? start = null,
        DateOnly? end = null)
    {
        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = start ?? new DateOnly(2025, 1, 1),
            PeriodEnd = end ?? new DateOnly(2025, 12, 31),
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
