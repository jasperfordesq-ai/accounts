using System.Text.Json;
using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Middleware;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounts.Tests;

// Chart task C6: an existing (generic-chart) company adopts the charity / CLG chart without moving a
// transaction, deleting an account or creating an opening balance. Only the bank control and retained
// earnings accounts are renamed in place; every other account with entries keeps its id and stays
// selectable under a legacy code, so the owner re-categorises (and splits) in the app.
public sealed class ChartAdoptionTests
{
    // ---- Plan ---------------------------------------------------------------------------------

    [Fact]
    public async Task Plan_ForTheLiveShape_RenamesOnlyBankAndRetainedEarnings_AndKeepsEveryPostedAccountAsLegacy()
    {
        await using var db = CreateDb();
        var shape = await ChartAdoptionFixture.SeedLiveShapeAsync(db);
        var before = await ChartAdoptionFixture.CategorySnapshotAsync(db, shape.CompanyId);
        var service = Service(db);

        var plan = await service.PlanAsync(shape.CompanyId);

        Assert.Empty(plan.BlockedBy);
        Assert.Empty(plan.StaleFilingArtifacts);
        Assert.Equal(ChartOfAccountsVariant.GenericTrading, plan.CurrentVariant);
        Assert.Equal(ChartOfAccountsVariant.CharityClg, plan.TargetVariant);
        Assert.Matches("^[0-9a-f]{64}$", plan.PlanSha256);

        // Spec §2.4: exactly two structural renames, ids preserved.
        Assert.Collection(
            plan.Renames,
            bank =>
            {
                Assert.Equal(AccountSystemRole.BankControl, bank.Role);
                Assert.Equal(shape.Ids["1400"], bank.CategoryId);
                Assert.Equal(("1400", "1200", "Bank current account"), (bank.FromCode, bank.ToCode, bank.ToName));
            },
            retained =>
            {
                Assert.Equal(AccountSystemRole.RetainedEarnings, retained.Role);
                Assert.Equal(shape.Ids["3100"], retained.CategoryId);
                Assert.Equal(("3100", "3000", "Members' funds brought forward"), (retained.FromCode, retained.ToCode, retained.ToName));
            });

        // The six accounts carrying the owner's categorisation stay, under legacy codes, with their counts.
        Assert.Equal(
            ChartAdoptionFixture.LiveShapeCounts.Keys.Select(code => shape.Ids[code]).Order(),
            plan.LegacyRecodes.Select(row => row.CategoryId).Order());
        foreach (var (code, count) in ChartAdoptionFixture.LiveShapeCounts)
        {
            var legacy = Assert.Single(plan.LegacyRecodes, row => row.CategoryId == shape.Ids[code]);
            Assert.Equal(code, legacy.FromCode);
            Assert.Equal($"G-{code}", legacy.ToCode);
            Assert.EndsWith(" (legacy)", legacy.ToName, StringComparison.Ordinal);
            Assert.Equal(count, legacy.TransactionCount);
            Assert.Equal(0, legacy.JournalCount);
            Assert.Equal(0, legacy.OpeningBalanceCount);
        }
        Assert.Equal(AccountSystemRole.DirectorLoanLiability, plan.LegacyRecodes.Single(row => row.FromCode == "2500").ReleasedRole);
        Assert.Empty(plan.Retained);

        // Every other generic account has no entries: archived, re-coded only where a charity code needs freeing.
        Assert.Equal(52 - 2 - 6, plan.Archives.Count);
        Assert.Equal("G-1200", plan.Archives.Single(row => row.CategoryId == shape.Ids["1200"]).ToCode);
        Assert.Equal("6600", plan.Archives.Single(row => row.CategoryId == shape.Ids["6600"]).ToCode);
        Assert.Equal("G-7000", plan.Archives.Single(row => row.CategoryId == shape.Ids["7000"]).ToCode);

        // The charity rows whose codes are then free; 1200 and 3000 come from the renames.
        Assert.Equal(ChartDefinitions.CharityClg.Accounts.Count - 2, plan.Inserts.Count);
        foreach (var code in ChartAdoptionFixture.ExpectedNewCharityCodes)
            Assert.Contains(plan.Inserts, insert => insert.Code == code);
        Assert.DoesNotContain(plan.Inserts, insert => insert.Code is "1200" or "3000");

        // A dry run writes nothing and is deterministic.
        Assert.Equal(before, await ChartAdoptionFixture.CategorySnapshotAsync(db, shape.CompanyId));
        Assert.Equal(ChartOfAccountsVariant.GenericTrading, (await db.Companies.AsNoTracking().SingleAsync(c => c.Id == shape.CompanyId)).ChartOfAccountsVariant);
        Assert.Equal(plan.PlanSha256, (await service.PlanAsync(shape.CompanyId)).PlanSha256);
    }

    // ---- Execute: the live-shape regression fixture ---------------------------------------------

    [Fact]
    public async Task Adopt_TheLiveShape_Keeps61Transactions_NoneUncategorised_NetMinus33_72_AndEveryCategoryId()
    {
        await using var db = CreateDb();
        var shape = await ChartAdoptionFixture.SeedLiveShapeAsync(db);

        await ChartAdoptionFixture.AdoptAndAssertLiveShapeAsync(db, shape);
    }

    [Fact]
    public async Task Adopt_PermutesCodes_APostedGenericPrepaymentsBecomesG1200_WhileTheBankBecomes1200()
    {
        await using var db = CreateDb();
        var shape = await ChartAdoptionFixture.SeedLiveShapeAsync(db);
        await ChartAdoptionFixture.AddTransactionAsync(db, shape, shape.PeriodId, "Insurance paid in advance", -120m, shape.Ids["1200"]);
        await ChartAdoptionFixture.AddTransactionAsync(db, shape, shape.PeriodId, "Loan drawdown", 1_000m, shape.Ids["2600"]);
        var legacyRule = await ChartAdoptionFixture.AddRuleAsync(db, shape, "DIRECTOR", shape.Ids["2500"]);
        var archivedRule = await ChartAdoptionFixture.AddRuleAsync(db, shape, "FUEL", shape.Ids["6600"]);
        var service = Service(db);

        var plan = await service.PlanAsync(shape.CompanyId);
        var prepayments = Assert.Single(plan.LegacyRecodes, row => row.CategoryId == shape.Ids["1200"]);
        Assert.Equal(("1200", "G-1200", AccountSystemRole.Prepayments), (prepayments.FromCode, prepayments.ToCode, prepayments.ReleasedRole));
        // An account with entries whose code and role mean nothing on the charity chart keeps its code.
        Assert.Equal("2600", Assert.Single(plan.Retained).Code);
        Assert.Equal(1, plan.LegacyRecodes.Single(row => row.CategoryId == shape.Ids["2500"]).RuleCount);
        Assert.Equal(1, plan.Archives.Single(row => row.CategoryId == shape.Ids["6600"]).RuleCount);
        Assert.Equal(0, plan.LegacyRecodes.Single(row => row.CategoryId == shape.Ids["7300"]).RuleCount);
        var outcome = await service.AdoptAsync(shape.CompanyId, Input(plan), ChartAdoptionFixture.Owner(shape));

        Assert.True(outcome.Succeeded, outcome.Message);
        var rows = await db.AccountCategories.AsNoTracking().Where(c => c.CompanyId == shape.CompanyId).ToListAsync();
        var legacy = rows.Single(c => c.Id == shape.Ids["1200"]);
        Assert.Equal("G-1200", legacy.Code);
        Assert.Equal(AccountStatementLine.Prepayments, legacy.StatementLine);
        Assert.Null(legacy.SystemRole);
        Assert.False(legacy.IsArchived);
        var bank = rows.Single(c => c.Code == "1200");
        Assert.Equal(shape.Ids["1400"], bank.Id);
        Assert.Equal(AccountSystemRole.BankControl, bank.SystemRole);
        Assert.Equal(AccountSystemRole.Prepayments, rows.Single(c => c.Code == "1120").SystemRole);
        // Rules are never re-pointed: they follow the account id.
        Assert.Equal(shape.Ids["2500"], (await db.TransactionRules.AsNoTracking().SingleAsync(rule => rule.Id == legacyRule)).CategoryId);
        Assert.Equal(shape.Ids["6600"], (await db.TransactionRules.AsNoTracking().SingleAsync(rule => rule.Id == archivedRule)).CategoryId);
        var loan = rows.Single(c => c.Id == shape.Ids["2600"]);
        Assert.Equal(("2600", "Bank Loan (< 1 year)", false), (loan.Code, loan.Name, loan.IsArchived));
        Assert.Equal(rows.Count, rows.Select(c => c.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(120m, (await new FinancialStatementsService(db).GetBalanceSheetAsync(shape.CompanyId, shape.PeriodId)).CurrentAssets.Prepayments);
    }

    // ---- Refusals ---------------------------------------------------------------------------------

    [Fact]
    public async Task Adopt_RefusesAPlanThatNoLongerMatches_AndChangesNothing()
    {
        await using var db = CreateDb();
        var shape = await ChartAdoptionFixture.SeedLiveShapeAsync(db);
        var service = Service(db);
        var reviewed = await service.PlanAsync(shape.CompanyId);

        // After review, an entry lands on an account the reviewed plan would have archived.
        await ChartAdoptionFixture.AddTransactionAsync(db, shape, shape.PeriodId, "Fuel", -40m, shape.Ids["6600"]);
        var before = await ChartAdoptionFixture.CategorySnapshotAsync(db, shape.CompanyId);
        var outcome = await service.AdoptAsync(shape.CompanyId, Input(reviewed), ChartAdoptionFixture.Owner(shape));

        Assert.Equal(ChartAdoptionRefusal.PlanChanged, outcome.Refusal);
        Assert.NotEqual(reviewed.PlanSha256, outcome.Plan.PlanSha256);
        Assert.Contains(outcome.Plan.LegacyRecodes, row => row.CategoryId == shape.Ids["6600"] && row.Reason == ChartAdoptionLegacyReason.CharityCodeRange);
        Assert.Equal(before, await ChartAdoptionFixture.CategorySnapshotAsync(db, shape.CompanyId));
        Assert.Equal(ChartOfAccountsVariant.GenericTrading, (await db.Companies.AsNoTracking().SingleAsync(c => c.Id == shape.CompanyId)).ChartOfAccountsVariant);
        Assert.False(await db.AuditLogs.AnyAsync(log => log.Action == AuditEventCodes.AccountCategoriesChartAdopted));
    }

    [Theory]
    [InlineData(PeriodStatus.Finalised, false)]
    [InlineData(PeriodStatus.Filed, false)]
    [InlineData(PeriodStatus.Draft, true)]
    public async Task Plan_IsBlocked_WhenAnAffectedAccountHasEntriesInALockedPeriod(PeriodStatus status, bool lockedAt)
    {
        await using var db = CreateDb();
        var shape = await ChartAdoptionFixture.SeedLiveShapeAsync(db, withPriorPeriod: true);
        await ChartAdoptionFixture.AddTransactionAsync(db, shape, shape.PriorPeriodId!.Value, "Prior-year software", -10m, shape.Ids["7300"]);
        await ChartAdoptionFixture.LockAsync(db, shape.PriorPeriodId!.Value, status, lockedAt);
        var service = Service(db);
        var before = await ChartAdoptionFixture.CategorySnapshotAsync(db, shape.CompanyId);

        var plan = await service.PlanAsync(shape.CompanyId);

        var blocker = Assert.Single(plan.BlockedBy);
        Assert.Contains("01/01/2024", blocker, StringComparison.Ordinal);
        Assert.Contains("1400", blocker, StringComparison.Ordinal);
        Assert.Contains("7300", blocker, StringComparison.Ordinal);
        var outcome = await service.AdoptAsync(shape.CompanyId, Input(plan), ChartAdoptionFixture.Owner(shape));
        Assert.Equal(ChartAdoptionRefusal.Blocked, outcome.Refusal);
        Assert.Equal(before, await ChartAdoptionFixture.CategorySnapshotAsync(db, shape.CompanyId));
    }

    [Fact]
    public async Task Plan_IsNotBlocked_ByALockedPeriodWithoutEntriesOnAnAffectedAccount()
    {
        await using var db = CreateDb();
        var shape = await ChartAdoptionFixture.SeedLiveShapeAsync(db, withPriorPeriod: true);
        await ChartAdoptionFixture.LockAsync(db, shape.PriorPeriodId!.Value, PeriodStatus.Finalised, lockedAt: true);
        var service = Service(db);

        var plan = await service.PlanAsync(shape.CompanyId);

        Assert.Empty(plan.BlockedBy);
        Assert.True((await service.AdoptAsync(shape.CompanyId, Input(plan), ChartAdoptionFixture.Owner(shape))).Succeeded);
    }

    // The variant change re-presents every period's statements, so any entry at all in a locked period blocks,
    // even one on accounts the adoption does not re-code (here: no account entry at all, or retained accounts).
    [Theory]
    [InlineData("uncategorised")]
    [InlineData("journal")]
    [InlineData("opening")]
    public async Task Plan_IsBlocked_ByAnyEntryInALockedPeriod_EvenOnAccountsItDoesNotChange(string entry)
    {
        await using var db = CreateDb();
        var shape = await ChartAdoptionFixture.SeedLiveShapeAsync(db, withPriorPeriod: true);
        // Current-period entries make 2600 and 2700 "retained" accounts (kept unchanged by the plan).
        await ChartAdoptionFixture.AddTransactionAsync(db, shape, shape.PeriodId, "Loan drawdown", 1_000m, shape.Ids["2600"]);
        await ChartAdoptionFixture.AddTransactionAsync(db, shape, shape.PeriodId, "Long loan drawdown", 2_000m, shape.Ids["2700"]);
        var prior = shape.PriorPeriodId!.Value;
        switch (entry)
        {
            case "uncategorised":
                await ChartAdoptionFixture.AddTransactionAsync(db, shape, prior, "Unidentified receipt", 5m, shape.Ids["2600"]);
                var uncategorisedRow = await db.ImportedTransactions.SingleAsync(t => t.PeriodId == prior);
                uncategorisedRow.CategoryId = null;
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                break;
            case "journal":
                await ChartAdoptionFixture.AddJournalAsync(db, prior, shape.Ids["2700"], shape.Ids["2600"], 100m);
                break;
            default:
                await ChartAdoptionFixture.AddOpeningBalanceAsync(db, prior, shape.Ids["2600"], 50m);
                break;
        }
        await ChartAdoptionFixture.LockAsync(db, prior, PeriodStatus.Finalised, lockedAt: true);

        var plan = await Service(db).PlanAsync(shape.CompanyId);

        Assert.Contains(plan.Retained, row => row.CategoryId == shape.Ids["2600"]);
        var blocker = Assert.Single(plan.BlockedBy);
        Assert.Contains("01/01/2024", blocker, StringComparison.Ordinal);
        Assert.Contains("finalised", blocker, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CompanyType.Private)]
    [InlineData(CompanyType.DesignatedActivityCompany)]
    public async Task Plan_IsBlocked_ForACompanyThatIsNotLimitedByGuarantee(CompanyType companyType)
    {
        await using var db = CreateDb();
        var shape = await ChartAdoptionFixture.SeedLiveShapeAsync(db, companyType: companyType);
        var service = Service(db);
        var before = await ChartAdoptionFixture.CategorySnapshotAsync(db, shape.CompanyId);

        var plan = await service.PlanAsync(shape.CompanyId);
        var outcome = await service.AdoptAsync(shape.CompanyId, Input(plan), ChartAdoptionFixture.Owner(shape));

        Assert.Equal("Only a company limited by guarantee can adopt the charity / CLG chart of accounts.", Assert.Single(plan.BlockedBy));
        Assert.Equal(ChartAdoptionRefusal.Blocked, outcome.Refusal);
        Assert.Null(db.ChartAdoptionAllowance);
        Assert.Equal(before, await ChartAdoptionFixture.CategorySnapshotAsync(db, shape.CompanyId));
    }

    // The charity chart has no land and buildings account, so after adoption a land-class asset would
    // make journal generation and the balance sheet fail. The register's land classes ("Land &
    // Buildings", "Property") are matched case-insensitively; other classes do not block.
    [Theory]
    [InlineData("Land & Buildings")]
    [InlineData("Property")]
    [InlineData("land & buildings")]
    [InlineData("PROPERTY")]
    public async Task Plan_IsBlocked_ByALandOrPropertyFixedAsset_NamingTheAssets(string assetClass)
    {
        await using var db = CreateDb();
        var shape = await ChartAdoptionFixture.SeedLiveShapeAsync(db);
        var hall = LandTestAsset(shape, "Community hall", assetClass);
        var store = LandTestAsset(shape, "Storage unit", "Property");
        db.FixedAssets.AddRange(hall, LandTestAsset(shape, "Laptop", "IT"), store, LandTestAsset(shape, "Van", "Vehicles"));
        await db.SaveChangesAsync();
        var service = Service(db);
        var before = await ChartAdoptionFixture.CategorySnapshotAsync(db, shape.CompanyId);

        var plan = await service.PlanAsync(shape.CompanyId);
        var outcome = await service.AdoptAsync(shape.CompanyId, Input(plan), ChartAdoptionFixture.Owner(shape));

        Assert.Equal(
            $"The charity chart has no land and buildings account, so these assets would have nowhere to post: Community hall ({assetClass}); Storage unit (Property). Keep the current chart, or change an asset's class if it is not land or buildings.",
            Assert.Single(plan.BlockedBy));
        Assert.Equal(ChartAdoptionRefusal.Blocked, outcome.Refusal);
        Assert.Null(db.ChartAdoptionAllowance);
        Assert.Equal(before, await ChartAdoptionFixture.CategorySnapshotAsync(db, shape.CompanyId));
        Assert.Equal(ChartOfAccountsVariant.GenericTrading, (await db.Companies.AsNoTracking().SingleAsync(c => c.Id == shape.CompanyId)).ChartOfAccountsVariant);

        // Re-classing the assets clears the blocker, and the chart can then be adopted.
        foreach (var asset in await db.FixedAssets.Where(a => a.Id == hall.Id || a.Id == store.Id).ToListAsync())
            asset.Category = "Equipment";
        await db.SaveChangesAsync();
        var cleared = await service.PlanAsync(shape.CompanyId);
        Assert.Empty(cleared.BlockedBy);
        Assert.True((await service.AdoptAsync(shape.CompanyId, Input(cleared), ChartAdoptionFixture.Owner(shape))).Succeeded);
    }

    private static FixedAsset LandTestAsset(ChartAdoptionFixture.LiveShape shape, string name, string assetClass) => new()
    {
        CompanyId = shape.CompanyId,
        Name = name,
        Category = assetClass,
        Cost = 1_000m,
        AcquisitionDate = new DateOnly(2025, 2, 1),
        UsefulLifeYears = 5
    };

    [Fact]
    public async Task Adopt_RequiresAcknowledgement_WhenACroPackWouldGoStale()
    {
        await using var db = CreateDb();
        var shape = await ChartAdoptionFixture.SeedLiveShapeAsync(db);
        await ChartAdoptionFixture.AddCroPackAsync(db, shape.PeriodId, FilingPackageStatus.Generated);
        var service = Service(db);

        var plan = await service.PlanAsync(shape.CompanyId);
        var stale = Assert.Single(plan.StaleFilingArtifacts);
        Assert.Equal(shape.PeriodId, stale.PeriodId);
        Assert.Empty(plan.BlockedBy);

        var refused = await service.AdoptAsync(shape.CompanyId, Input(plan, acknowledge: false), ChartAdoptionFixture.Owner(shape));
        Assert.Equal(ChartAdoptionRefusal.StaleFilingArtifactsNotAcknowledged, refused.Refusal);
        Assert.Equal(ChartOfAccountsVariant.GenericTrading, (await db.Companies.AsNoTracking().SingleAsync(c => c.Id == shape.CompanyId)).ChartOfAccountsVariant);

        var adopted = await service.AdoptAsync(shape.CompanyId, Input(plan, acknowledge: true), ChartAdoptionFixture.Owner(shape));
        Assert.True(adopted.Succeeded, adopted.Message);
    }

    [Theory]
    [InlineData(PeriodStatus.Filed, FilingPackageStatus.Generated)]
    [InlineData(PeriodStatus.Review, FilingPackageStatus.Submitted)]
    public async Task Plan_IsBlocked_WhenAFiledOrSubmittedCroPackWouldGoStale(PeriodStatus status, FilingPackageStatus packageStatus)
    {
        await using var db = CreateDb();
        var shape = await ChartAdoptionFixture.SeedLiveShapeAsync(db, withPriorPeriod: true);
        await ChartAdoptionFixture.AddCroPackAsync(db, shape.PriorPeriodId!.Value, packageStatus);
        await ChartAdoptionFixture.LockAsync(db, shape.PriorPeriodId!.Value, status, lockedAt: false);

        var plan = await Service(db).PlanAsync(shape.CompanyId);

        Assert.Single(plan.StaleFilingArtifacts);
        Assert.Contains(plan.BlockedBy, reason => reason.Contains("CRO", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Adopt_IsRefused_OnceTheCompanyUsesTheCharityChart()
    {
        await using var db = CreateDb();
        var shape = await ChartAdoptionFixture.SeedLiveShapeAsync(db);
        var service = Service(db);
        Assert.True((await service.AdoptAsync(shape.CompanyId, Input(await service.PlanAsync(shape.CompanyId)), ChartAdoptionFixture.Owner(shape))).Succeeded);
        var after = await ChartAdoptionFixture.CategorySnapshotAsync(db, shape.CompanyId);

        var again = await service.PlanAsync(shape.CompanyId);
        var rerun = await service.AdoptAsync(shape.CompanyId, Input(again), ChartAdoptionFixture.Owner(shape));

        Assert.Contains(again.BlockedBy, reason => reason.Contains("already uses the charity", StringComparison.Ordinal));
        Assert.Empty(again.Inserts);
        Assert.Equal(ChartAdoptionRefusal.Blocked, rerun.Refusal);
        Assert.Equal(after, await ChartAdoptionFixture.CategorySnapshotAsync(db, shape.CompanyId));
    }

    [Fact]
    public async Task Adopt_RefusesAnyOtherTargetVariant()
    {
        await using var db = CreateDb();
        var shape = await ChartAdoptionFixture.SeedLiveShapeAsync(db);
        var plan = await Service(db).PlanAsync(shape.CompanyId);

        await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db).AdoptAsync(
            shape.CompanyId,
            new ChartAdoptionInput(ChartOfAccountsVariant.GenericTrading, plan.PlanSha256, false),
            ChartAdoptionFixture.Owner(shape)));
    }

    // ---- Endpoints, roles and recent verification -------------------------------------------------

    [Theory]
    [InlineData("Owner", true)]
    [InlineData("Accountant", false)]
    [InlineData("Reviewer", false)]
    [InlineData("Client", false)]
    public void ChartAdoptionRoutes_AreOwnerOnly(string role, bool allowed)
    {
        var user = new AuthenticatedUser(7, 1, "Chart Firm", $"{role.ToLowerInvariant()}@example.ie", role, role);
        foreach (var path in new[] { "/api/companies/3/categories/chart-adoption", "/api/companies/3/categories/chart-adoption/plan" })
        {
            var decision = RoleAuthorizationService.Authorize(user, new PathString(path), HttpMethods.Post);
            Assert.Equal(allowed, decision.IsAllowed);
            if (!allowed) Assert.Contains("chart of accounts", decision.DenialReason, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Adoption_NeedsRecentVerification_ButTheDryRunDoesNot()
    {
        Assert.True(RecentAuthenticationMiddleware.RequiresRecentMfa(new PathString("/api/companies/3/categories/chart-adoption"), HttpMethods.Post));
        Assert.True(RecentAuthenticationMiddleware.RequiresRecentMfa(new PathString("/api/companies/3/categories/chart-adoption/"), HttpMethods.Post));
        Assert.False(RecentAuthenticationMiddleware.RequiresRecentMfa(new PathString("/api/companies/3/categories/chart-adoption/plan"), HttpMethods.Post));
    }

    [Theory]
    [InlineData("Accountant")]
    [InlineData("Client")]
    public async Task Endpoints_DenyNonOwners_AndChangeNothing(string role)
    {
        await using var db = CreateDb();
        var shape = await ChartAdoptionFixture.SeedLiveShapeAsync(db);
        var plan = await Service(db).PlanAsync(shape.CompanyId);
        var before = await ChartAdoptionFixture.CategorySnapshotAsync(db, shape.CompanyId);

        var planResult = await ChartAdoptionEndpoints.PlanEndpointAsync(
            shape.CompanyId, Service(db), db, Request(shape, role, "/plan"), DisabledApiAccess());
        var adoptResult = await ChartAdoptionEndpoints.AdoptEndpointAsync(
            shape.CompanyId, Input(plan), Service(db), db, Request(shape, role, ""), DisabledApiAccess());

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(planResult).StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(adoptResult).StatusCode);
        Assert.Equal(before, await ChartAdoptionFixture.CategorySnapshotAsync(db, shape.CompanyId));
    }

    [Fact]
    public async Task AdoptEndpoint_Returns409WithTheCurrentPlan_ForAStaleHash_And200WhenItMatches()
    {
        await using var db = CreateDb();
        var shape = await ChartAdoptionFixture.SeedLiveShapeAsync(db);
        var planResult = await ChartAdoptionEndpoints.PlanEndpointAsync(
            shape.CompanyId, Service(db), db, Request(shape, "Owner", "/plan"), DisabledApiAccess());
        var plan = Assert.IsType<Ok<ChartAdoptionPlan>>(planResult).Value!;

        var stale = await ChartAdoptionEndpoints.AdoptEndpointAsync(
            shape.CompanyId, Input(plan) with { PlanSha256 = new string('0', 64) }, Service(db), db, Request(shape, "Owner", ""), DisabledApiAccess());
        var conflict = Assert.IsType<Conflict<ChartAdoptionRefusalResponse>>(stale);
        Assert.Equal("chart_adoption_plan_changed", conflict.Value!.Code);
        Assert.Equal(plan.PlanSha256, conflict.Value.Plan.PlanSha256);

        var adopted = await ChartAdoptionEndpoints.AdoptEndpointAsync(
            shape.CompanyId, Input(plan), Service(db), db, Request(shape, "Owner", ""), DisabledApiAccess());
        Assert.Equal(plan.PlanSha256, Assert.IsType<Ok<ChartAdoptionPlan>>(adopted).Value!.PlanSha256);
    }

    // ---- Re-categorising after adoption -----------------------------------------------------------

    [Fact]
    public async Task Recategorising_FromALegacyAccountToACharityAccountAndBack_IsAuditedAndReversible()
    {
        await using var db = CreateDb();
        var shape = await ChartAdoptionFixture.SeedLiveShapeAsync(db);
        var service = Service(db);
        Assert.True((await service.AdoptAsync(shape.CompanyId, Input(await service.PlanAsync(shape.CompanyId)), ChartAdoptionFixture.Owner(shape))).Succeeded);
        var legacyId = shape.Ids["2500"];
        var charityId = (await db.AccountCategories.AsNoTracking().SingleAsync(c => c.CompanyId == shape.CompanyId && c.Code == "2230")).Id;
        var transactionId = shape.TransactionCategories.First(pair => pair.Value == legacyId).Key;

        foreach (var target in new[] { charityId, legacyId })
        {
            var result = await BankingEndpoints.CategoriseTransactionEndpointAsync(
                shape.CompanyId, shape.PeriodId, transactionId, new CategoriseInput(target), db, new AuditService(db),
                Request(shape, "Owner", $"/periods/{shape.PeriodId}/transactions/{transactionId}/categorise", "/api/companies/{0}"),
                DisabledApiAccess());
            Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
            Assert.Equal(target, (await db.ImportedTransactions.AsNoTracking().SingleAsync(t => t.Id == transactionId)).CategoryId);
        }

        var audits = await db.AuditLogs.AsNoTracking()
            .Where(log => log.Action == AuditEventCodes.TransactionCategorised && log.EntityId == transactionId)
            .OrderBy(log => log.Id)
            .ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.Contains($"\"CategoryId\":{legacyId}", audits[0].OldValueJson!, StringComparison.Ordinal);
        Assert.Contains($"\"CategoryId\":{charityId}", audits[0].NewValueJson!, StringComparison.Ordinal);
        Assert.Contains($"\"CategoryId\":{charityId}", audits[1].OldValueJson!, StringComparison.Ordinal);
        Assert.Contains($"\"CategoryId\":{legacyId}", audits[1].NewValueJson!, StringComparison.Ordinal);
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private static ChartAdoptionInput Input(ChartAdoptionPlan plan, bool acknowledge = false) =>
        new(ChartOfAccountsVariant.CharityClg, plan.PlanSha256, acknowledge);

    private static ChartAdoptionService Service(AccountsDbContext db) => new(db, new AuditService(db));

    private static DefaultHttpContext Request(ChartAdoptionFixture.LiveShape shape, string role, string suffix, string prefix = "/api/companies/{0}/categories/chart-adoption")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = suffix.EndsWith("/categorise", StringComparison.Ordinal) ? HttpMethods.Put : HttpMethods.Post;
        context.Request.Path = string.Format(System.Globalization.CultureInfo.InvariantCulture, prefix, shape.CompanyId) + suffix;
        context.Items[AuthContext.ItemKey] = ChartAdoptionFixture.User(shape, role);
        return context;
    }

    private static ApiAccessService DisabledApiAccess() =>
        new(Options.Create(new ApiAccessConfig { Enabled = false }), new TestEnvironment());

    private static AccountsDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AccountsDbContext>().UseInMemoryDatabase($"chart-c6-{Guid.NewGuid():N}").Options);

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Accounts.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}

// The live-shape regression fixture (fictitious descriptions; no real bank number): a generic-chart
// company whose period holds 61 categorised transactions on six accounts, netting -33.72.
internal static class ChartAdoptionFixture
{
    public static readonly IReadOnlyDictionary<string, int> LiveShapeCounts = new Dictionary<string, int>
    {
        ["2500"] = 30,
        ["4100"] = 6,
        ["4300"] = 1,
        ["6800"] = 1,
        ["6900"] = 9,
        ["7300"] = 14
    };

    public static readonly string[] ExpectedNewCharityCodes = ["2230", "4000", "4300", "4800", "6000", "7100", "7600", "8100", "8200"];

    public const decimal LiveShapeNet = -33.72m;

    public sealed record LiveShape(
        int TenantId,
        int CompanyId,
        int PeriodId,
        int? PriorPeriodId,
        int BankAccountId,
        IReadOnlyDictionary<string, int> Ids,
        Dictionary<int, int?> TransactionCategories);

    public static async Task<LiveShape> SeedLiveShapeAsync(
        AccountsDbContext db,
        bool withPriorPeriod = false,
        int? companyId = null,
        int? periodId = null,
        int? priorPeriodId = null,
        CompanyType companyType = CompanyType.CompanyLimitedByGuarantee)
    {
        var tenant = new Tenant { Name = "Chart Adoption Firm", Slug = $"chart-c6-{Guid.NewGuid():N}" };
        var company = new Company
        {
            Tenant = tenant,
            LegalName = "Example Community CLG",
            CroNumber = Guid.NewGuid().ToString("N")[..12],
            CompanyType = companyType,
            IsCharitableOrganisation = companyType == CompanyType.CompanyLimitedByGuarantee,
            IncorporationDate = withPriorPeriod ? new DateOnly(2024, 1, 1) : new DateOnly(2025, 1, 1),
            ChartOfAccountsVariant = ChartOfAccountsVariant.GenericTrading
        };
        if (companyId is { } explicitCompanyId) company.Id = explicitCompanyId;
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        AccountingPeriod? prior = null;
        if (withPriorPeriod)
        {
            prior = new AccountingPeriod
            {
                CompanyId = company.Id,
                PeriodStart = new DateOnly(2024, 1, 1),
                PeriodEnd = new DateOnly(2024, 12, 31),
                IsFirstYear = true
            };
            if (priorPeriodId is { } explicitPrior) prior.Id = explicitPrior;
            db.AccountingPeriods.Add(prior);
            await db.SaveChangesAsync();
        }
        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = !withPriorPeriod
        };
        if (periodId is { } explicitPeriod) period.Id = explicitPeriod;
        db.AccountingPeriods.Add(period);
        var bank = new BankAccount { CompanyId = company.Id, Name = "Current account" };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var category in await new CategoryService(db).SeedDefaultCategoriesAsync(company.Id))
            ids[category.Code] = category.Id;

        var rows = LiveShapeTransactions()
            .Select((row, index) => new ImportedTransaction
            {
                BankAccountId = bank.Id,
                PeriodId = period.Id,
                Date = new DateOnly(2025, 1, 1).AddDays(index * 5),
                Description = row.Description,
                Amount = row.Amount,
                CategoryId = ids[row.Code],
                ManualOverride = true
            })
            .ToList();
        Assert.Equal(61, rows.Count);
        Assert.Equal(LiveShapeNet, rows.Sum(row => row.Amount));
        db.ImportedTransactions.AddRange(rows);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return new LiveShape(
            tenant.Id,
            company.Id,
            period.Id,
            prior?.Id,
            bank.Id,
            ids,
            rows.ToDictionary(row => row.Id, row => row.CategoryId));
    }

    private static IEnumerable<(string Code, decimal Amount, string Description)> LiveShapeTransactions()
    {
        for (var i = 1; i <= 15; i++) yield return ("2500", 40.00m, $"Director advance {i}");
        for (var i = 1; i <= 15; i++) yield return ("2500", -30.00m, $"Director repayment {i}");
        for (var i = 1; i <= 6; i++) yield return ("4100", 25.00m, $"Reimbursed expense {i}");
        yield return ("4300", 500.00m, "Community grant");
        yield return ("6800", -250.00m, "Professional fee");
        for (var i = 1; i <= 9; i++) yield return ("6900", -4.88m, $"Monthly account fee {i}");
        for (var i = 1; i <= 13; i++) yield return ("7300", -38.55m, $"Software subscription {i}");
        yield return ("7300", -38.65m, "Software subscription 14");
    }

    public static async Task<int> AddTransactionAsync(AccountsDbContext db, LiveShape shape, int periodId, string description, decimal amount, int categoryId)
    {
        var row = new ImportedTransaction
        {
            BankAccountId = shape.BankAccountId,
            PeriodId = periodId,
            Date = (await db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == periodId)).PeriodStart.AddDays(40),
            Description = description,
            Amount = amount,
            CategoryId = categoryId,
            ManualOverride = true
        };
        db.ImportedTransactions.Add(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return row.Id;
    }

    public static async Task AddJournalAsync(AccountsDbContext db, int periodId, int debitId, int creditId, decimal amount)
    {
        db.Adjustments.Add(new Adjustment
        {
            PeriodId = periodId,
            Description = "Reviewed reclassification",
            DebitCategoryId = debitId,
            CreditCategoryId = creditId,
            Amount = amount,
            Source = AdjustmentSource.Manual,
            Reason = "Reviewed year-end journal",
            CreatedBy = "Reviewer",
            ApprovedBy = "Reviewer",
            ApprovedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    public static async Task AddOpeningBalanceAsync(AccountsDbContext db, int periodId, int categoryId, decimal debit)
    {
        db.OpeningBalances.Add(new OpeningBalance { PeriodId = periodId, AccountCategoryId = categoryId, Debit = debit, SourceNote = "Take-on" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    public static async Task<int> AddRuleAsync(AccountsDbContext db, LiveShape shape, string pattern, int categoryId)
    {
        var rule = new TransactionRule { CompanyId = shape.CompanyId, Pattern = pattern, CategoryId = categoryId, Priority = 10 };
        db.TransactionRules.Add(rule);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return rule.Id;
    }

    public static async Task LockAsync(AccountsDbContext db, int periodId, PeriodStatus status, bool lockedAt)
    {
        var period = await db.AccountingPeriods.SingleAsync(p => p.Id == periodId);
        period.Status = status;
        if (lockedAt)
        {
            period.LockedAt = DateTime.UtcNow;
            period.LockedBy = "owner@example.ie";
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    public static async Task AddCroPackAsync(AccountsDbContext db, int periodId, FilingPackageStatus status)
    {
        db.Add(new CroFilingPackage
        {
            PeriodId = periodId,
            Status = status,
            AccountsPdfGenerated = true,
            AccountsPdfSha256 = new string('b', 64),
            ArtifactSourceFingerprintSha256 = new string('a', 64)
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    // Every company row's id, code, name, attributes and archive flag, in id order.
    public static async Task<string> CategorySnapshotAsync(AccountsDbContext db, int companyId) =>
        JsonSerializer.Serialize(await db.AccountCategories.AsNoTracking()
            .Where(c => c.CompanyId == companyId)
            .OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.Code, c.Name, c.Type, c.StatementLine, c.CashFlowClass, c.SystemRole, c.IsArchived, c.TaxTreatment })
            .ToListAsync());

    public static AuthenticatedUser User(LiveShape shape, string role) => new(
        UserId: 41,
        TenantId: shape.TenantId,
        TenantName: "Chart Adoption Firm",
        Email: $"{role.ToLowerInvariant()}@example.ie",
        DisplayName: $"Chart {role}",
        Role: role,
        AllowedCompanyIds: new HashSet<int> { shape.CompanyId });

    public static AuthenticatedUser Owner(LiveShape shape) => User(shape, "Owner");

    // Adopts the charity chart on the live shape and proves nothing the owner categorised moved.
    public static async Task AdoptAndAssertLiveShapeAsync(AccountsDbContext db, LiveShape shape)
    {
        var statements = new FinancialStatementsService(db);
        var pnlBefore = await statements.GetProfitAndLossAsync(shape.CompanyId, shape.PeriodId);
        var bsBefore = await statements.GetBalanceSheetAsync(shape.CompanyId, shape.PeriodId);
        var tbBefore = await statements.GetTrialBalanceAsync(shape.CompanyId, shape.PeriodId);
        var originalIds = await db.AccountCategories.AsNoTracking()
            .Where(c => c.CompanyId == shape.CompanyId).Select(c => c.Id).ToListAsync();
        var service = new ChartAdoptionService(db, new AuditService(db));
        var plan = await service.PlanAsync(shape.CompanyId);

        var outcome = await service.AdoptAsync(
            shape.CompanyId,
            new ChartAdoptionInput(ChartOfAccountsVariant.CharityClg, plan.PlanSha256, false),
            Owner(shape));

        Assert.True(outcome.Succeeded, outcome.Message);
        Assert.Null(db.ChartAdoptionAllowance);
        db.ChangeTracker.Clear();

        // 61 transactions, 0 uncategorised, net -33.72; every CategoryId unchanged.
        var transactions = await db.ImportedTransactions.AsNoTracking()
            .Where(t => t.PeriodId == shape.PeriodId && t.BankAccount.CompanyId == shape.CompanyId && !t.IsDuplicate)
            .ToListAsync();
        Assert.Equal(61, transactions.Count);
        Assert.Equal(0, transactions.Count(t => t.CategoryId == null));
        Assert.Equal(LiveShapeNet, transactions.Sum(t => t.Amount));
        Assert.All(transactions, t => Assert.Equal(shape.TransactionCategories[t.Id], t.CategoryId));
        Assert.False(await db.OpeningBalances.AnyAsync(o => o.Period.CompanyId == shape.CompanyId));

        var rows = await db.AccountCategories.AsNoTracking().Where(c => c.CompanyId == shape.CompanyId).ToListAsync();
        // No account deleted; ids preserved; codes unique.
        Assert.All(originalIds, id => Assert.Contains(rows, row => row.Id == id));
        Assert.Equal(originalIds.Count + plan.Inserts.Count, rows.Count);
        Assert.Equal(rows.Count, rows.Select(c => c.Code).Distinct(StringComparer.Ordinal).Count());

        // The six categorised accounts: same ids, active (selectable), legacy-coded, presentation unchanged.
        foreach (var code in LiveShapeCounts.Keys)
        {
            var row = rows.Single(c => c.Id == shape.Ids[code]);
            Assert.Equal($"G-{code}", row.Code);
            Assert.EndsWith(" (legacy)", row.Name, StringComparison.Ordinal);
            Assert.False(row.IsArchived);
            Assert.Null(AccountCategoryTargets.RefusalReason(row));
        }
        Assert.Equal(AccountStatementLine.OtherCreditorsWithinYear, rows.Single(c => c.Id == shape.Ids["2500"]).StatementLine);
        Assert.Equal(AccountStatementLine.InterestPayable, rows.Single(c => c.Id == shape.Ids["6900"]).StatementLine);

        // The charity codes exist as new active rows.
        foreach (var code in ExpectedNewCharityCodes)
        {
            var row = rows.Single(c => c.Code == code);
            Assert.DoesNotContain(row.Id, originalIds);
            Assert.False(row.IsArchived);
        }
        Assert.Equal(AccountSystemRole.DirectorLoanLiability, rows.Single(c => c.Code == "2230").SystemRole);

        // Bank control is 1200 with the same id; members' funds brought forward is the old retained earnings.
        var bank = rows.Single(c => c.Code == "1200");
        Assert.Equal(shape.Ids["1400"], bank.Id);
        Assert.Equal(("Bank current account", AccountSystemRole.BankControl), (bank.Name, bank.SystemRole));
        Assert.Equal(shape.Ids["3100"], rows.Single(c => c.Code == "3000").Id);
        Assert.Equal(ChartOfAccountsVariant.CharityClg, (await db.Companies.AsNoTracking().SingleAsync(c => c.Id == shape.CompanyId)).ChartOfAccountsVariant);

        // Statement totals are unchanged (only presentation labels follow the chart).
        var pnlAfter = await statements.GetProfitAndLossAsync(shape.CompanyId, shape.PeriodId);
        var bsAfter = await statements.GetBalanceSheetAsync(shape.CompanyId, shape.PeriodId);
        var tbAfter = await statements.GetTrialBalanceAsync(shape.CompanyId, shape.PeriodId);
        Assert.Equal(
            (pnlBefore.Turnover, pnlBefore.OtherIncome, pnlBefore.TotalOverheads, pnlBefore.InterestPayable, pnlBefore.ProfitAfterTax),
            (pnlAfter.Turnover, pnlAfter.OtherIncome, pnlAfter.TotalOverheads, pnlAfter.InterestPayable, pnlAfter.ProfitAfterTax));
        Assert.Equal(-183.72m, pnlAfter.ProfitAfterTax);
        Assert.Equal(bsBefore.NetAssets, bsAfter.NetAssets);
        Assert.Equal(bsBefore.CapitalAndReserves.Total, bsAfter.CapitalAndReserves.Total);
        Assert.Equal(
            bsBefore.CurrentAssets.Cash - (bsBefore.CreditorsWithinYear.BankOverdraft ?? 0m),
            bsAfter.CurrentAssets.Cash - (bsAfter.CreditorsWithinYear.BankOverdraft ?? 0m));
        Assert.True(bsAfter.Balances);
        Assert.Equal((tbBefore.Sum(l => l.Debit), tbBefore.Sum(l => l.Credit)), (tbAfter.Sum(l => l.Debit), tbAfter.Sum(l => l.Credit)));

        // One audit event with before/after snapshots and the plan hash.
        var audit = Assert.Single(await db.AuditLogs.AsNoTracking()
            .Where(log => log.CompanyId == shape.CompanyId && log.Action == AuditEventCodes.AccountCategoriesChartAdopted)
            .ToListAsync());
        Assert.Contains(plan.PlanSha256, audit.NewValueJson!, StringComparison.Ordinal);
        Assert.Contains("\"Code\":\"1400\"", audit.OldValueJson!, StringComparison.Ordinal);
        Assert.Contains("\"Code\":\"G-2500\"", audit.NewValueJson!, StringComparison.Ordinal);
        Assert.Contains("\"ChartOfAccountsVariant\":\"GenericTrading\"", audit.OldValueJson!, StringComparison.Ordinal);
        Assert.Contains("\"ChartOfAccountsVariant\":\"CharityClg\"", audit.NewValueJson!, StringComparison.Ordinal);
    }
}
