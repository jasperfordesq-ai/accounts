using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounts.Tests;

// Chart task C4: suspense must be nil before approval; archived accounts and the bank control are not
// posting targets; no corporation-tax provision for a company holding a charitable tax exemption; and
// re-categorising a categorised transaction is possible, reversible, audited and locked with the period.
public sealed partial class AccountChartSafeguardTests
{
    private const string SuspenseMessage200 =
        "Suspense account 9998 has a balance of €200.00; clear it before the statements are approved.";

    // ---- Suspense -------------------------------------------------------------------------------

    [Fact]
    public async Task Suspense_WithACreditBalance_BlocksReadiness_SitsInOtherCreditors_AndClearsWhenRecategorised()
    {
        await using var db = CreateDb();
        var (company, period, bank, ids) = await SeedCharityCompanyAsync(db);
        var statements = new FinancialStatementsService(db);
        db.ImportedTransactions.Add(Transaction(bank, period, new DateOnly(2025, 2, 1), "Donations received", 1_000m, ids["4000"]));
        await db.SaveChangesAsync();

        // A suspense account that exists but is nil is not a blocker.
        var nil = await statements.GetReadinessScoreAsync(company.Id, period.Id);
        Assert.DoesNotContain(nil.MissingItems, item => item.Contains("Suspense", StringComparison.Ordinal));

        var parked = Transaction(bank, period, new DateOnly(2025, 3, 1), "Unidentified receipt", 200m, ids["9998"]);
        db.ImportedTransactions.Add(parked);
        await db.SaveChangesAsync();

        var blocked = await statements.GetReadinessScoreAsync(company.Id, period.Id);
        Assert.Contains(SuspenseMessage200, blocked.MissingItems);
        var balanceSheet = await statements.GetBalanceSheetAsync(company.Id, period.Id);
        Assert.Equal(200m, balanceSheet.CreditorsWithinYear.OtherCreditors);
        Assert.Equal(200m, balanceSheet.CreditorsWithinYear.Total);
        Assert.Equal(0m, balanceSheet.CurrentAssets.Debtors);
        Assert.Equal(6_200m, balanceSheet.CurrentAssets.Cash);
        Assert.True(balanceSheet.Balances);

        // Clearing the suspense (re-categorising the receipt) removes the blocker; the check is not
        // counted, so the completeness percentage does not move either way.
        parked.CategoryId = ids["4000"];
        await db.SaveChangesAsync();
        var cleared = await statements.GetReadinessScoreAsync(company.Id, period.Id);
        Assert.DoesNotContain(cleared.MissingItems, item => item.Contains("Suspense", StringComparison.Ordinal));
        Assert.Equal(blocked.CompletenessPercent, cleared.CompletenessPercent);
        Assert.Equal(nil.CompletenessPercent, cleared.CompletenessPercent);
        Assert.Equal(blocked.MissingItems.Count - 1, cleared.MissingItems.Count);
    }

    [Fact]
    public async Task Suspense_WithADebitBalance_IsPresentedInDebtors_SoTheSheetStillBalances()
    {
        await using var db = CreateDb();
        var (company, period, bank, ids) = await SeedCharityCompanyAsync(db);
        db.ImportedTransactions.Add(Transaction(bank, period, new DateOnly(2025, 3, 1), "Unidentified payment", -120m, ids["9998"]));
        await db.SaveChangesAsync();
        var statements = new FinancialStatementsService(db);

        var readiness = await statements.GetReadinessScoreAsync(company.Id, period.Id);
        var balanceSheet = await statements.GetBalanceSheetAsync(company.Id, period.Id);

        Assert.Contains(
            "Suspense account 9998 has a balance of €120.00; clear it before the statements are approved.",
            readiness.MissingItems);
        Assert.Equal(120m, balanceSheet.CurrentAssets.Debtors);
        Assert.Equal(4_880m, balanceSheet.CurrentAssets.Cash);
        Assert.Equal(5_000m, balanceSheet.CurrentAssets.Total);
        Assert.Equal(0m, balanceSheet.CreditorsWithinYear.OtherCreditors);
        Assert.Equal(0m, balanceSheet.CreditorsWithinYear.Total);
        Assert.Equal(5_000m, balanceSheet.NetAssets);
        Assert.True(balanceSheet.Balances);
    }

    [Fact]
    public async Task Suspense_BlocksFinalisation_ThroughTheExistingFinalOutputReadinessAssertion()
    {
        await using var db = CreateDb();
        var (company, period, bank, ids) = await SeedCharityCompanyAsync(db);
        var parked = Transaction(bank, period, new DateOnly(2025, 3, 1), "Unidentified receipt", 200m, ids["9998"]);
        db.ImportedTransactions.Add(parked);
        await db.SaveChangesAsync();
        var statements = new FinancialStatementsService(db);

        Assert.Contains(SuspenseMessage200, await statements.GetFinalOutputReadinessBlockersAsync(company.Id, period.Id));
        var assertion = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            statements.AssertFinalOutputReadinessAsync(company.Id, period.Id, "accounts finalisation"));
        Assert.Contains(SuspenseMessage200, assertion.Message);

        // The period-status endpoint refuses to finalise through that assertion.
        var finalise = await Assert.ThrowsAsync<BusinessRuleException>(() => PeriodStatusEndpoint.UpdateAsync(
            company.Id,
            period.Id,
            new PeriodStatusUpdate(PeriodStatus.Finalised, null, null, new DateOnly(2026, 3, 1)),
            db,
            new AuditService(db),
            statements,
            StatusRequest(company, period),
            DisabledApiAccess()));
        Assert.Contains("Cannot generate final accounts finalisation", finalise.Message);
        Assert.Contains(SuspenseMessage200, finalise.Message);
        Assert.NotEqual(PeriodStatus.Finalised, (await db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == period.Id)).Status);

        parked.CategoryId = ids["4000"];
        await db.SaveChangesAsync();
        Assert.DoesNotContain(
            await statements.GetFinalOutputReadinessBlockersAsync(company.Id, period.Id),
            blocker => blocker.Contains("Suspense", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GenericCompany_HasNoSuspenseAccount_AndNoSuspenseCheck()
    {
        await using var db = CreateDb();
        var company = await SeedCompanyAsync(db, ChartOfAccountsVariant.GenericTrading, CompanyType.Private);
        var period = await AddPeriodAsync(db, company);
        var seeded = await new CategoryService(db).SeedDefaultCategoriesAsync(company.Id);

        var readiness = await new FinancialStatementsService(db).GetReadinessScoreAsync(company.Id, period.Id);

        Assert.DoesNotContain(seeded, category => category.StatementLine == AccountStatementLine.Suspense);
        Assert.DoesNotContain(readiness.MissingItems, item => item.Contains("Suspense", StringComparison.Ordinal));
    }

    // ---- Archived and bank-control targets ------------------------------------------------------

    [Theory]
    [InlineData(ChartOfAccountsVariant.CharityClg, "1200")]
    [InlineData(ChartOfAccountsVariant.GenericTrading, "1400")]
    public async Task Categorise_RejectsAnArchivedAccountAndTheBankControl_WithoutChangeOrAudit(ChartOfAccountsVariant variant, string bankCode)
    {
        await using var host = await SafeguardHost.StartAsync(variant);
        var (periodId, transactionIds, ids) = await host.SeedPeriodAsync();

        foreach (var (target, expected) in new[]
        {
            (ids["ARCHIVED"], "is archived"),
            (ids[bankCode], "is the bank control account")
        })
        {
            var response = await host.Client.PutAsJsonAsync(
                $"/api/companies/{host.CompanyId}/periods/{periodId}/transactions/{transactionIds[0]}/categorise",
                new { categoryId = target });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(expected, await ErrorAsync(response), StringComparison.Ordinal);

            var bulk = await host.Client.PostAsJsonAsync(
                $"/api/companies/{host.CompanyId}/periods/{periodId}/transactions/bulk-categorise",
                new { transactionIds, categoryId = target });
            Assert.Equal(HttpStatusCode.BadRequest, bulk.StatusCode);
            Assert.Contains(expected, await ErrorAsync(bulk), StringComparison.Ordinal);

            var rule = await host.Client.PostAsJsonAsync(
                $"/api/companies/{host.CompanyId}/transaction-rules",
                new { pattern = "BANK FEE", categoryId = target, priority = 1 });
            Assert.Equal(HttpStatusCode.BadRequest, rule.StatusCode);
            Assert.Contains(expected, await ErrorAsync(rule), StringComparison.Ordinal);
        }

        await host.InScopeAsync(async db =>
        {
            Assert.All(await db.ImportedTransactions.ToListAsync(), transaction => Assert.Equal(ids["INCOME"], transaction.CategoryId));
            Assert.Empty(await db.TransactionRules.ToListAsync());
            Assert.Empty(await db.AuditLogs.ToListAsync());
        });

        // An active account is still accepted on all three paths.
        var accepted = await host.Client.PutAsJsonAsync(
            $"/api/companies/{host.CompanyId}/periods/{periodId}/transactions/{transactionIds[0]}/categorise",
            new { categoryId = ids["EXPENSE"] });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var acceptedBulk = await host.Client.PostAsJsonAsync(
            $"/api/companies/{host.CompanyId}/periods/{periodId}/transactions/bulk-categorise",
            new { transactionIds, categoryId = ids["EXPENSE"] });
        Assert.Equal(HttpStatusCode.OK, acceptedBulk.StatusCode);
        var acceptedRule = await host.Client.PostAsJsonAsync(
            $"/api/companies/{host.CompanyId}/transaction-rules",
            new { pattern = "BANK FEE", categoryId = ids["EXPENSE"], priority = 1 });
        Assert.Equal(HttpStatusCode.Created, acceptedRule.StatusCode);
    }

    [Theory]
    [InlineData(ChartOfAccountsVariant.CharityClg, "1200")]
    [InlineData(ChartOfAccountsVariant.GenericTrading, "1400")]
    public async Task OpeningBalanceUpsert_RejectsAnArchivedAccountAndTheBankControl(ChartOfAccountsVariant variant, string bankCode)
    {
        await using var db = CreateDb();
        var company = await SeedCompanyAsync(db, variant, CompanyType.CompanyLimitedByGuarantee);
        var period = await AddPeriodAsync(db, company);
        var ids = (await new CategoryService(db).SeedDefaultCategoriesAsync(company.Id)).ToDictionary(c => c.Code, c => c.Id);
        var archived = AddArchived(db, company.Id, "2990", "Old loan account", AccountCategoryType.Liability);
        await db.SaveChangesAsync();
        var audit = new AuditService(db);

        foreach (var (target, expected) in new[]
        {
            (archived.Id, "is archived"),
            (ids[bankCode], "is the bank control account")
        })
        {
            var result = await YearEndEndpoints.UpsertOpeningBalanceEndpointAsync(
                company.Id,
                period.Id,
                target,
                new OpeningBalanceInput(500m, 0m, "Take-on", null, true),
                db,
                audit,
                OwnerRequest(company, HttpMethods.Put, $"/api/companies/{company.Id}/periods/{period.Id}/opening-balances/{target}"));

            Assert.Equal(StatusCodes.Status400BadRequest, StatusCode(result));
            Assert.Contains(expected, ErrorOf(result), StringComparison.Ordinal);
        }

        Assert.Empty(await db.OpeningBalances.ToListAsync());
        Assert.Empty(await db.AuditLogs.ToListAsync());

        var accepted = await YearEndEndpoints.UpsertOpeningBalanceEndpointAsync(
            company.Id,
            period.Id,
            ids["2000"],
            new OpeningBalanceInput(0m, 500m, "Take-on", null, true),
            db,
            audit,
            OwnerRequest(company, HttpMethods.Put, $"/api/companies/{company.Id}/periods/{period.Id}/opening-balances/{ids["2000"]}"));
        Assert.Equal(StatusCodes.Status200OK, StatusCode(accepted));
    }

    [Fact]
    public async Task CategoryList_HidesArchivedRows_UnlessTheyCarryABalanceOrAreReferenced_AndIncludeAllListsEveryRow()
    {
        await using var host = await SafeguardHost.StartAsync(ChartOfAccountsVariant.CharityClg);
        var (periodId, _, ids) = await host.SeedPeriodAsync();
        int withTransactions = 0, withOpening = 0, nettedToNil = 0, ruleOnly = 0, journalsNettedToNil = 0, unused = 0;
        await host.InScopeAsync(async db =>
        {
            var bank = await db.BankAccounts.SingleAsync();
            var period = await db.AccountingPeriods.SingleAsync(p => p.Id == periodId);
            var later = new AccountingPeriod
            {
                CompanyId = host.CompanyId,
                PeriodStart = new DateOnly(2026, 1, 1),
                PeriodEnd = new DateOnly(2026, 12, 31)
            };
            db.AccountingPeriods.Add(later);
            var transactionRow = AddArchived(db, host.CompanyId, "G-6800", "Legacy expenses (legacy)", AccountCategoryType.Expense);
            var openingRow = AddArchived(db, host.CompanyId, "G-2500", "Legacy loan (legacy)", AccountCategoryType.Liability);
            var nilRow = AddArchived(db, host.CompanyId, "G-7300", "Legacy software (legacy)", AccountCategoryType.Expense);
            var ruleRow = AddArchived(db, host.CompanyId, "G-6100", "Legacy rent (legacy)", AccountCategoryType.Expense);
            var journalRow = AddArchived(db, host.CompanyId, "G-6200", "Legacy insurance (legacy)", AccountCategoryType.Expense);
            await db.SaveChangesAsync();
            db.ImportedTransactions.AddRange(
                Transaction(bank, period, new DateOnly(2025, 6, 1), "Legacy expense", -45m, transactionRow.Id),
                Transaction(bank, period, new DateOnly(2025, 6, 2), "Refunded subscription", -30m, nilRow.Id),
                Transaction(bank, period, new DateOnly(2025, 6, 3), "Subscription refund", 30m, nilRow.Id));
            db.TransactionRules.Add(new TransactionRule { CompanyId = host.CompanyId, Pattern = "RENT", CategoryId = ruleRow.Id, Priority = 1 });
            db.Adjustments.AddRange(
                Journal(period, "Insurance reclassified", journalRow.Id, ids["EXPENSE"], 50m),
                Journal(period, "Insurance reclassified back", ids["EXPENSE"], journalRow.Id, 50m));
            db.OpeningBalances.Add(new OpeningBalance
            {
                PeriodId = later.Id,
                AccountCategoryId = openingRow.Id,
                Credit = 250m,
                EnteredBy = "Reviewer"
            });
            await db.SaveChangesAsync();
            withTransactions = transactionRow.Id;
            withOpening = openingRow.Id;
            nettedToNil = nilRow.Id;
            ruleOnly = ruleRow.Id;
            journalsNettedToNil = journalRow.Id;
            unused = ids["ARCHIVED"];
        });

        var listed = await ListAsync(host, "");
        Assert.Contains(withTransactions, listed);
        Assert.Contains(withOpening, listed);
        // Referenced by a transaction or a rule: listed even with no balance, so the transaction register
        // and the rules panel can still name the account.
        Assert.Contains(nettedToNil, listed);
        Assert.Contains(ruleOnly, listed);
        Assert.DoesNotContain(journalsNettedToNil, listed);
        Assert.DoesNotContain(unused, listed);
        Assert.Contains(ids["1200"], listed);
        Assert.Contains(ids["9998"], listed);

        var all = await ListAsync(host, "?include=all");
        Assert.Contains(journalsNettedToNil, all);
        Assert.Contains(unused, all);
        await host.InScopeAsync(async db => Assert.Equal(await db.AccountCategories.CountAsync(), all.Count));
        Assert.Equal(all.Count - 2, listed.Count);
    }

    // ---- Charitable tax exemption ---------------------------------------------------------------

    [Theory]
    [InlineData(ChartOfAccountsVariant.CharityClg, true)]
    [InlineData(ChartOfAccountsVariant.GenericTrading, true)]
    [InlineData(ChartOfAccountsVariant.CharityClg, false)]
    [InlineData(ChartOfAccountsVariant.GenericTrading, false)]
    public async Task CorporationTaxProvision_IsPostedOnlyForACompanyWithoutACharitableTaxExemption(ChartOfAccountsVariant variant, bool exempt)
    {
        await using var db = CreateDb();
        var company = await SeedCompanyAsync(db, variant, CompanyType.CompanyLimitedByGuarantee);
        company.IsCharitableOrganisation = true;
        company.HoldsCharitableTaxExemption = exempt;
        var period = await AddPeriodAsync(db, company);
        if (variant == ChartOfAccountsVariant.CharityClg)
            await new CategoryService(db).SeedDefaultCategoriesAsync(company.Id);
        db.TaxBalances.Add(new TaxBalance { PeriodId = period.Id, TaxType = TaxType.CorporationTax, Liability = 100m, Balance = 100m });
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(company.Id, period.Id);

        var provisions = await db.Adjustments.Where(adjustment => adjustment.PeriodId == period.Id && adjustment.Description == "Corporation tax provision").ToListAsync();
        var categories = await db.AccountCategories.Where(category => category.CompanyId == company.Id).ToListAsync();
        var ctAccounts = categories.Where(category => category.SystemRole is AccountSystemRole.CorporationTaxCharge or AccountSystemRole.CorporationTaxPayable).ToList();
        if (exempt)
        {
            Assert.Empty(provisions);
            Assert.Empty(ctAccounts);
            Assert.Equal(0m, (await new FinancialStatementsService(db).GetProfitAndLossAsync(company.Id, period.Id)).TaxCharge);
        }
        else
        {
            var provision = Assert.Single(provisions);
            Assert.Equal(100m, provision.Amount);
            Assert.Equal(2, ctAccounts.Count);
            Assert.Equal(100m, (await new FinancialStatementsService(db).GetProfitAndLossAsync(company.Id, period.Id)).TaxCharge);
        }
    }

    [Theory]
    [InlineData(true, 150, true)]
    [InlineData(true, 0, false)]
    [InlineData(true, null, false)]
    [InlineData(false, 150, false)]
    public async Task ExemptCompany_WithAnEnteredCorporationTaxLiability_IsBlockedUntilConfirmedOrRemoved(bool exempt, int? liability, bool blocked)
    {
        await using var db = CreateDb();
        var (company, period, _, _) = await SeedCharityCompanyAsync(db);
        company.HoldsCharitableTaxExemption = exempt;
        if (liability is { } amount)
            db.TaxBalances.Add(new TaxBalance { PeriodId = period.Id, TaxType = TaxType.CorporationTax, Liability = amount, Balance = amount });
        await db.SaveChangesAsync();
        var statements = new FinancialStatementsService(db);
        const string message =
            "This company holds a charitable tax exemption, but a corporation tax liability of €150.00 is entered; confirm it is due (for example on income outside the exemption) or remove it.";

        var readiness = await statements.GetReadinessScoreAsync(company.Id, period.Id);
        var blockers = await statements.GetFinalOutputReadinessBlockersAsync(company.Id, period.Id);

        Assert.DoesNotContain(readiness.MissingItems, item => item.Contains("charitable tax exemption", StringComparison.Ordinal));
        if (blocked)
        {
            Assert.Contains(message, readiness.Warnings);
            Assert.Contains(message, blockers);
            var finalise = await Assert.ThrowsAsync<BusinessRuleException>(() => PeriodStatusEndpoint.UpdateAsync(
                company.Id,
                period.Id,
                new PeriodStatusUpdate(PeriodStatus.Finalised, null, null, new DateOnly(2026, 3, 1)),
                db,
                new AuditService(db),
                statements,
                StatusRequest(company, period),
                DisabledApiAccess()));
            Assert.Contains(message, finalise.Message);
        }
        else
        {
            Assert.DoesNotContain(readiness.Warnings, warning => warning.Contains("charitable tax exemption", StringComparison.Ordinal));
            Assert.DoesNotContain(blockers, blocker => blocker.Contains("charitable tax exemption", StringComparison.Ordinal));
        }
    }

    // ---- Import auto-categorisation ---------------------------------------------------------------

    [Theory]
    [InlineData(ChartOfAccountsVariant.CharityClg, "1200")]
    [InlineData(ChartOfAccountsVariant.GenericTrading, "1400")]
    public async Task Import_NeverAppliesARuleThatTargetsAnArchivedAccountOrTheBankControl(ChartOfAccountsVariant variant, string bankCode)
    {
        await using var db = CreateDb();
        var company = await SeedCompanyAsync(db, variant, CompanyType.CompanyLimitedByGuarantee);
        var period = await AddPeriodAsync(db, company);
        var bank = new BankAccount { CompanyId = company.Id, Name = "Current account" };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        var ids = (await new CategoryService(db).SeedDefaultCategoriesAsync(company.Id)).ToDictionary(c => c.Code, c => c.Id);
        var archived = AddArchived(db, company.Id, "G-6800", "Legacy expenses (legacy)", AccountCategoryType.Expense);
        await db.SaveChangesAsync();
        // Rules created before the account was archived, or written directly: the API now refuses both.
        db.TransactionRules.AddRange(
            new TransactionRule { CompanyId = company.Id, Pattern = "LEGACY", CategoryId = archived.Id, Priority = 1 },
            new TransactionRule { CompanyId = company.Id, Pattern = "TRANSFER", CategoryId = ids[bankCode], Priority = 2 },
            new TransactionRule { CompanyId = company.Id, Pattern = "DONATION", CategoryId = ids["4000"], Priority = 3 });
        await db.SaveChangesAsync();
        var csv = "Date,Description,Amount\n"
            + "01/03/2025,LEGACY supplier,-40.00\n"
            + "02/03/2025,LEGACY supplier again,-10.00\n"
            + "03/03/2025,TRANSFER between accounts,-200.00\n"
            + "04/03/2025,DONATION received,500.00\n";

        var import = await new ImportService(db).ImportCsvAsync(
            company.Id, bank.Id, period.Id, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv)), "statement.csv");

        Assert.Equal(4, import.ImportedRows);
        Assert.Equal(1, import.AutoCategorised);
        var rows = await db.ImportedTransactions.AsNoTracking().ToDictionaryAsync(t => t.Description);
        Assert.Null(rows["LEGACY supplier"].CategoryId);
        Assert.Null(rows["LEGACY supplier again"].CategoryId);
        Assert.Null(rows["TRANSFER between accounts"].CategoryId);
        Assert.Equal(ids["4000"], rows["DONATION received"].CategoryId);
        // One warning per refused rule, naming the rule and the reason.
        Assert.Equal(1, import.Warnings.Count(warning => warning.Contains("\"LEGACY\"", StringComparison.Ordinal) && warning.Contains("is archived", StringComparison.Ordinal)));
        Assert.Equal(1, import.Warnings.Count(warning => warning.Contains("\"TRANSFER\"", StringComparison.Ordinal) && warning.Contains("is the bank control account", StringComparison.Ordinal)));

        // The ledger builds: no row was posted against the bank control.
        await new FinancialStatementsService(db).GetTrialBalanceAsync(company.Id, period.Id);
    }

    // ---- Re-categorisation: possible, reversible, audited, locked with the period ---------------

    [Fact]
    public async Task Recategorising_ToAnotherAccountAndBack_IsAudited_WithOldAndNewValues()
    {
        await using var host = await SafeguardHost.StartAsync(ChartOfAccountsVariant.CharityClg);
        var (periodId, transactionIds, ids) = await host.SeedPeriodAsync();
        var url = $"/api/companies/{host.CompanyId}/periods/{periodId}/transactions/{transactionIds[0]}/categorise";

        Assert.Equal(HttpStatusCode.OK, (await host.Client.PutAsJsonAsync(url, new { categoryId = ids["EXPENSE"] })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PutAsJsonAsync(url, new { categoryId = ids["INCOME"] })).StatusCode);
        var bulkUrl = $"/api/companies/{host.CompanyId}/periods/{periodId}/transactions/bulk-categorise";
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsJsonAsync(bulkUrl, new { transactionIds, categoryId = ids["EXPENSE"] })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsJsonAsync(bulkUrl, new { transactionIds, categoryId = ids["INCOME"] })).StatusCode);

        await host.InScopeAsync(async db =>
        {
            Assert.All(await db.ImportedTransactions.ToListAsync(), transaction => Assert.Equal(ids["INCOME"], transaction.CategoryId));
            var singles = await db.AuditLogs
                .Where(log => log.Action == AuditEventCodes.TransactionCategorised)
                .OrderBy(log => log.Id)
                .ToListAsync();
            Assert.Equal(2, singles.Count);
            Assert.Equal((ids["INCOME"], ids["EXPENSE"]), (CategoryIdIn(singles[0].OldValueJson), CategoryIdIn(singles[0].NewValueJson)));
            Assert.Equal((ids["EXPENSE"], ids["INCOME"]), (CategoryIdIn(singles[1].OldValueJson), CategoryIdIn(singles[1].NewValueJson)));
            Assert.All(singles, log => Assert.Equal(transactionIds[0], log.EntityId));

            var bulks = await db.AuditLogs
                .Where(log => log.Action == AuditEventCodes.TransactionsBulkCategorised)
                .OrderBy(log => log.Id)
                .ToListAsync();
            Assert.Equal(2, bulks.Count);
            Assert.All(CategoryIdsIn(bulks[0].OldValueJson), id => Assert.Equal(ids["INCOME"], id));
            Assert.All(CategoryIdsIn(bulks[0].NewValueJson, "Transactions"), id => Assert.Equal(ids["EXPENSE"], id));
            Assert.All(CategoryIdsIn(bulks[1].OldValueJson), id => Assert.Equal(ids["EXPENSE"], id));
            Assert.All(CategoryIdsIn(bulks[1].NewValueJson, "Transactions"), id => Assert.Equal(ids["INCOME"], id));
        });
    }

    [Theory]
    [InlineData(PeriodStatus.Finalised, false)]
    [InlineData(PeriodStatus.Filed, false)]
    [InlineData(PeriodStatus.Draft, true)]
    public async Task Recategorising_IsRefusedInAFinalisedOrLockedPeriod(PeriodStatus status, bool lockedOnly)
    {
        await using var host = await SafeguardHost.StartAsync(ChartOfAccountsVariant.CharityClg);
        var (periodId, transactionIds, ids) = await host.SeedPeriodAsync();
        await host.InScopeAsync(async db =>
        {
            var period = await db.AccountingPeriods.SingleAsync(p => p.Id == periodId);
            period.Status = status;
            period.LockedAt = lockedOnly ? DateTime.UtcNow : null;
            await db.SaveChangesAsync();
        });

        var single = await host.Client.PutAsJsonAsync(
            $"/api/companies/{host.CompanyId}/periods/{periodId}/transactions/{transactionIds[0]}/categorise",
            new { categoryId = ids["EXPENSE"] });
        var bulk = await host.Client.PostAsJsonAsync(
            $"/api/companies/{host.CompanyId}/periods/{periodId}/transactions/bulk-categorise",
            new { transactionIds, categoryId = ids["EXPENSE"] });

        Assert.Equal(HttpStatusCode.Conflict, single.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, bulk.StatusCode);
        Assert.Contains("Reopen the period before categorising transactions", await ErrorAsync(single), StringComparison.Ordinal);
        await host.InScopeAsync(async db =>
        {
            Assert.All(await db.ImportedTransactions.ToListAsync(), transaction => Assert.Equal(ids["INCOME"], transaction.CategoryId));
            Assert.Empty(await db.AuditLogs.ToListAsync());
        });
    }

    // ---- Helpers --------------------------------------------------------------------------------

    // ---- Land and buildings on the charity chart -------------------------------------------------

    private const string NoLandAccount =
        "The charity chart has no land and buildings account. Add one (a 00xx tangible fixed asset account) or choose another asset class.";

    [Theory]
    [InlineData("Land & Buildings")]
    [InlineData("Property")]
    public async Task CharityChart_RefusesALandAndBuildingsAsset_WhenSaved_AndWhenItsAccountIsResolved(string assetClass)
    {
        var resolved = Assert.Throws<BusinessRuleException>(() =>
            ChartAccountDefaults.FixedAssetAccount(ChartOfAccountsVariant.CharityClg, assetClass));
        Assert.Equal(NoLandAccount, resolved.Message);

        await using var db = CreateDb();
        var (company, period, _, _) = await SeedCharityCompanyAsync(db);
        var audit = new AuditService(db);
        var path = $"/api/companies/{company.Id}/fixed-assets";

        var created = await YearEndEndpoints.CreateFixedAssetEndpointAsync(
            company.Id, Asset("Community hall", assetClass, period), db, new AccountingWriteGuard(db), audit,
            OwnerRequest(company, HttpMethods.Post, path));

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCode(created));
        Assert.Equal(NoLandAccount, ErrorOf(created));
        Assert.Empty(await db.FixedAssets.ToListAsync());

        // An asset in another class is saved, but cannot be moved into the land class.
        var equipment = await YearEndEndpoints.CreateFixedAssetEndpointAsync(
            company.Id, Asset("Office printer", "Equipment", period), db, new AccountingWriteGuard(db), audit,
            OwnerRequest(company, HttpMethods.Post, path));
        Assert.Equal(StatusCodes.Status201Created, StatusCode(equipment));
        var saved = await db.FixedAssets.SingleAsync();
        var moved = await YearEndEndpoints.UpdateFixedAssetEndpointAsync(
            company.Id, saved.Id, Asset("Office printer", assetClass, period), db, new AccountingWriteGuard(db), audit,
            OwnerRequest(company, HttpMethods.Put, $"{path}/{saved.Id}"));
        Assert.Equal(StatusCodes.Status400BadRequest, StatusCode(moved));
        Assert.Equal(NoLandAccount, ErrorOf(moved));
        Assert.Equal("Equipment", (await db.FixedAssets.AsNoTracking().SingleAsync()).Category);
        Assert.Single(await db.AuditLogs.ToListAsync());

        // A land asset that reached a charity company another way fails clearly when its journal is generated.
        db.FixedAssets.Add(new FixedAsset
        {
            CompanyId = company.Id,
            Name = "Community hall",
            Category = assetClass,
            Cost = 10_000m,
            AcquisitionDate = period.PeriodStart,
            UsefulLifeYears = 50,
            DepreciationMethod = DepreciationMethod.StraightLine
        });
        await db.SaveChangesAsync();
        var generated = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new AdjustmentService(db).GenerateAutoAdjustmentsAsync(company.Id, period.Id));
        Assert.Equal(NoLandAccount, generated.Message);
    }

    [Theory]
    [InlineData("Land & Buildings")]
    [InlineData("Property")]
    public async Task GenericChart_StillTakesALandAndBuildingsAssetTo0010(string assetClass)
    {
        Assert.Equal("0010", ChartAccountDefaults.FixedAssetAccount(ChartOfAccountsVariant.GenericTrading, assetClass).Code);

        await using var db = CreateDb();
        var company = await SeedCompanyAsync(db, ChartOfAccountsVariant.GenericTrading, CompanyType.Private);
        var period = await AddPeriodAsync(db, company);

        var created = await YearEndEndpoints.CreateFixedAssetEndpointAsync(
            company.Id, Asset("Warehouse", assetClass, period), db, new AccountingWriteGuard(db), new AuditService(db),
            OwnerRequest(company, HttpMethods.Post, $"/api/companies/{company.Id}/fixed-assets"));

        Assert.Equal(StatusCodes.Status201Created, StatusCode(created));
        Assert.Equal(assetClass, (await db.FixedAssets.SingleAsync()).Category);
    }

    private static FixedAsset Asset(string name, string assetClass, AccountingPeriod period) => new()
    {
        Name = name,
        Category = assetClass,
        Cost = 1_000m,
        AcquisitionDate = period.PeriodStart.AddDays(1),
        UsefulLifeYears = 5,
        DepreciationMethod = DepreciationMethod.StraightLine
    };

    // ---- Manual journals -------------------------------------------------------------------------

    [Theory]
    [InlineData(ChartOfAccountsVariant.CharityClg, "1200", "7210")]
    [InlineData(ChartOfAccountsVariant.GenericTrading, "1400", "6810")]
    public async Task ManualJournal_RejectsAnArchivedAccountAndTheBankControl_OnCreateAndUpdate(
        ChartOfAccountsVariant variant, string bankCode, string expenseCode)
    {
        await using var db = CreateDb();
        var company = await SeedCompanyAsync(db, variant, CompanyType.CompanyLimitedByGuarantee);
        var period = await AddPeriodAsync(db, company);
        var ids = (await new CategoryService(db).SeedDefaultCategoriesAsync(company.Id)).ToDictionary(c => c.Code, c => c.Id);
        var archived = AddArchived(db, company.Id, "2990", "Old loan account", AccountCategoryType.Liability);
        var existing = Journal(period, "Accrued accountancy fee", ids[expenseCode], ids["2100"], 100m);
        db.Adjustments.Add(existing);
        await db.SaveChangesAsync();
        var audit = new AuditService(db);
        var basePath = $"/api/companies/{company.Id}/periods/{period.Id}/adjustments";

        foreach (var (target, expected) in new[]
        {
            (archived.Id, "is archived"),
            (ids[bankCode], "is the bank control account")
        })
        {
            foreach (var input in new[]
            {
                JournalInput(target, ids["2100"]),
                JournalInput(ids[expenseCode], target)
            })
            {
                var created = await AdjustmentEndpoints.CreateAdjustmentEndpointAsync(
                    company.Id, period.Id, input, db, audit,
                    OwnerRequest(company, HttpMethods.Post, basePath), DisabledApiAccess());
                Assert.Equal(StatusCodes.Status400BadRequest, StatusCode(created));
                Assert.Contains(expected, ErrorOf(created), StringComparison.Ordinal);

                var updated = await AdjustmentEndpoints.UpdateAdjustmentEndpointAsync(
                    company.Id, period.Id, existing.Id, input, db, audit,
                    OwnerRequest(company, HttpMethods.Put, $"{basePath}/{existing.Id}"), DisabledApiAccess());
                Assert.Equal(StatusCodes.Status400BadRequest, StatusCode(updated));
                Assert.Contains(expected, ErrorOf(updated), StringComparison.Ordinal);
            }
        }

        var stored = Assert.Single(await db.Adjustments.AsNoTracking().ToListAsync());
        Assert.Equal((ids[expenseCode], ids["2100"]), (stored.DebitCategoryId!.Value, stored.CreditCategoryId!.Value));
        Assert.Empty(await db.AuditLogs.ToListAsync());

        // Active accounts are still accepted.
        var accepted = await AdjustmentEndpoints.CreateAdjustmentEndpointAsync(
            company.Id, period.Id, JournalInput(ids[expenseCode], ids["2100"]), db, audit,
            OwnerRequest(company, HttpMethods.Post, basePath), DisabledApiAccess());
        Assert.Equal(StatusCodes.Status201Created, StatusCode(accepted));
    }

    [Fact]
    public async Task SystemGeneratedJournals_StillPostToTheBankControl_AndEditingOneKeepsThatSide()
    {
        await using var db = CreateDb();
        var company = await SeedCompanyAsync(db, ChartOfAccountsVariant.GenericTrading, CompanyType.Private);
        var period = await AddPeriodAsync(db, company);
        var ids = (await new CategoryService(db).SeedDefaultCategoriesAsync(company.Id)).ToDictionary(c => c.Code, c => c.Id);
        var archived = AddArchived(db, company.Id, "2990", "Old loan account", AccountCategoryType.Liability);
        db.Dividends.Add(new Dividend { PeriodId = period.Id, Amount = 300m, DateDeclared = new DateOnly(2025, 9, 1), DatePaid = new DateOnly(2025, 10, 1) });
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(company.Id, period.Id);

        var generated = Assert.Single(await db.Adjustments.Where(a => a.IsAuto && a.CreditCategoryId == ids["1400"]).ToListAsync());
        var path = $"/api/companies/{company.Id}/periods/{period.Id}/adjustments/{generated.Id}";
        var kept = await AdjustmentEndpoints.UpdateAdjustmentEndpointAsync(
            company.Id, period.Id, generated.Id,
            new AdjustmentInput("Dividend paid (reviewed)", generated.DebitCategoryId, ids["1400"], generated.Amount, "Reviewed", "FRS 102", 0m, 0m),
            db, new AuditService(db), OwnerRequest(company, HttpMethods.Put, path), DisabledApiAccess());
        Assert.Equal(StatusCodes.Status200OK, StatusCode(kept));
        Assert.Equal(ids["1400"], (await db.Adjustments.AsNoTracking().SingleAsync(a => a.Id == generated.Id)).CreditCategoryId);

        // A newly chosen archived account is still refused on a system journal.
        var refused = await AdjustmentEndpoints.UpdateAdjustmentEndpointAsync(
            company.Id, period.Id, generated.Id,
            new AdjustmentInput("Dividend paid (reviewed)", archived.Id, ids["1400"], generated.Amount, "Reviewed", "FRS 102", 0m, 0m),
            db, new AuditService(db), OwnerRequest(company, HttpMethods.Put, path), DisabledApiAccess());
        Assert.Equal(StatusCodes.Status400BadRequest, StatusCode(refused));
        Assert.Contains("is archived", ErrorOf(refused), StringComparison.Ordinal);
    }

    private static AdjustmentInput JournalInput(int debitId, int creditId) =>
        new("Year-end journal", debitId, creditId, 100m, "Invoice received after year end", "FRS 102 accruals concept", 0m, 0m);

    private static async Task<List<int>> ListAsync(SafeguardHost host, string query)
    {
        var response = await host.Client.GetAsync($"/api/companies/{host.CompanyId}/categories{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.EnumerateArray().Select(row => row.GetProperty("id").GetInt32()).ToList();
    }

    private static async Task<string> ErrorAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("error").GetString() ?? "";
    }

    private static int? StatusCode(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static string ErrorOf(IResult result)
    {
        var value = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        return JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement.GetProperty("error").GetString() ?? "";
    }

    private static int CategoryIdIn(string? json)
    {
        using var document = JsonDocument.Parse(json!);
        return document.RootElement.GetProperty("CategoryId").GetInt32();
    }

    private static List<int> CategoryIdsIn(string? json, string? property = null)
    {
        using var document = JsonDocument.Parse(json!);
        var rows = property is null ? document.RootElement : document.RootElement.GetProperty(property);
        var ids = rows.EnumerateArray().Select(row => row.GetProperty("CategoryId").GetInt32()).ToList();
        Assert.NotEmpty(ids);
        return ids;
    }

    private static AccountCategory AddArchived(AccountsDbContext db, int companyId, string code, string name, AccountCategoryType type)
    {
        var category = new AccountCategory
        {
            CompanyId = companyId,
            Code = code,
            Name = name,
            Type = type,
            IsSystem = true,
            IsArchived = true
        };
        CategoryClassification.ClassifyNew(category, []);
        db.AccountCategories.Add(category);
        return category;
    }

    private static readonly DateTime ReviewedAt = new(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc);

    // A charity-seeded company with bank opening 5,000 and members' funds 5,000 brought forward.
    private static async Task<(Company Company, AccountingPeriod Period, BankAccount Bank, IReadOnlyDictionary<string, int> Ids)> SeedCharityCompanyAsync(AccountsDbContext db)
    {
        var company = await SeedCompanyAsync(db, ChartOfAccountsVariant.CharityClg, CompanyType.CompanyLimitedByGuarantee);
        var period = await AddPeriodAsync(db, company);
        var bank = new BankAccount
        {
            CompanyId = company.Id,
            Name = "Current account",
            OpeningBalance = 5_000m,
            OpeningBalanceDate = new DateOnly(2025, 1, 1)
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        var ids = (await new CategoryService(db).SeedDefaultCategoriesAsync(company.Id)).ToDictionary(c => c.Code, c => c.Id);
        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = ids["3000"],
            Credit = 5_000m,
            SourceNote = "Members' funds take-on",
            EnteredBy = "Reviewer",
            Reviewed = true,
            ReviewedBy = "Reviewer",
            ReviewedAt = ReviewedAt
        });
        await db.SaveChangesAsync();
        return (company, period, bank, ids);
    }

    private static Adjustment Journal(AccountingPeriod period, string description, int debitId, int creditId, decimal amount) => new()
    {
        PeriodId = period.Id,
        Description = description,
        DebitCategoryId = debitId,
        CreditCategoryId = creditId,
        Amount = amount,
        Source = AdjustmentSource.Manual,
        Reason = "Reviewed year-end journal",
        LegalBasis = "FRS 102",
        CreatedBy = "Reviewer",
        ApprovedBy = "Reviewer",
        ApprovedAt = ReviewedAt,
        CreatedAt = ReviewedAt
    };

    private static async Task<AccountingPeriod> AddPeriodAsync(AccountsDbContext db, Company company)
    {
        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = true
        };
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();
        return period;
    }

    private static ImportedTransaction Transaction(BankAccount bank, AccountingPeriod period, DateOnly date, string description, decimal amount, int categoryId) => new()
    {
        BankAccountId = bank.Id,
        PeriodId = period.Id,
        Date = date,
        Description = description,
        Amount = amount,
        CategoryId = categoryId,
        ManualOverride = true
    };

    private static async Task<Company> SeedCompanyAsync(AccountsDbContext db, ChartOfAccountsVariant variant, CompanyType companyType)
    {
        var tenant = new Tenant { Name = "Chart Safeguard Firm", Slug = $"chart-c4-{Guid.NewGuid():N}" };
        var company = new Company
        {
            Tenant = tenant,
            LegalName = "Chart Safeguard Example",
            CroNumber = "C40000",
            CompanyType = companyType,
            IncorporationDate = new DateOnly(2020, 1, 1),
            ChartOfAccountsVariant = variant
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return company;
    }

    private static AuthenticatedUser Owner(Company company) => new(
        UserId: 1,
        TenantId: company.TenantId!.Value,
        TenantName: "Chart Safeguard Firm",
        Email: "chart.owner@example.ie",
        DisplayName: "Chart Owner",
        Role: "Owner",
        AllowedCompanyIds: new HashSet<int> { company.Id });

    private static DefaultHttpContext OwnerRequest(Company company, string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Items[AuthContext.ItemKey] = Owner(company);
        return context;
    }

    private static DefaultHttpContext StatusRequest(Company company, AccountingPeriod period)
    {
        var context = OwnerRequest(company, HttpMethods.Put, $"/api/companies/{company.Id}/periods/{period.Id}/status");
        context.Request.Headers[IdempotencyHttpContract.RequestHeader] = $"period-status:{Guid.NewGuid():N}";
        return context;
    }

    private static ApiAccessService DisabledApiAccess() =>
        new(Options.Create(new ApiAccessConfig { Enabled = false }), new TestEnvironment());

    private static AccountsDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AccountsDbContext>().UseInMemoryDatabase($"chart-c4-{Guid.NewGuid():N}").Options);

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Accounts.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    // The banking endpoints over an in-memory database, for one company on the given chart variant.
    private sealed class SafeguardHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private SafeguardHost(WebApplication app, HttpClient client, int companyId)
        {
            _app = app;
            Client = client;
            CompanyId = companyId;
        }

        public HttpClient Client { get; }
        public int CompanyId { get; }

        public static async Task<SafeguardHost> StartAsync(ChartOfAccountsVariant variant)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddHttpContextAccessor();
            var databaseName = $"chart-c4-host-{Guid.NewGuid():N}";
            builder.Services.AddDbContext<AccountsDbContext>(options => options.UseInMemoryDatabase(databaseName));
            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                // As Program.cs: a categorised transaction carries its tracked category.
                options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
                options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
            builder.Services.Configure<ApiAccessConfig>(config => config.Enabled = false);
            builder.Services.Configure<ImportLimitConfig>(_ => { });
            builder.Services.AddSingleton<ApiAccessService>();
            builder.Services.AddScoped<AccountingWriteGuard>();
            builder.Services.AddScoped<AuditService>();
            builder.Services.AddScoped<CategoryService>();
            builder.Services.AddScoped<ImportService>();

            var app = builder.Build();
            Company company;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
                company = await SeedCompanyAsync(db, variant, CompanyType.CompanyLimitedByGuarantee);
            }

            var owner = Owner(company);
            app.Use(async (context, next) =>
            {
                context.Items[AuthContext.ItemKey] = owner;
                await next();
            });
            app.MapBankingEndpoints();
            await app.StartAsync();
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
            var client = new HttpClient { BaseAddress = new Uri(Assert.Single(addresses!.Addresses)) };
            return new SafeguardHost(app, client, company.Id);
        }

        // The company's chart, one draft period, a bank account, an archived unused account ("ARCHIVED"),
        // an income ("INCOME") and an expense ("EXPENSE") account, and two transactions on INCOME.
        public async Task<(int PeriodId, List<int> TransactionIds, Dictionary<string, int> Ids)> SeedPeriodAsync()
        {
            var periodId = 0;
            var transactionIds = new List<int>();
            var ids = new Dictionary<string, int>();
            await InScopeAsync(async db =>
            {
                var company = await db.Companies.SingleAsync(c => c.Id == CompanyId);
                var period = await AddPeriodAsync(db, company);
                var bank = new BankAccount { CompanyId = CompanyId, Name = "Current account" };
                db.BankAccounts.Add(bank);
                await db.SaveChangesAsync();
                foreach (var category in await new CategoryService(db).SeedDefaultCategoriesAsync(CompanyId))
                    ids[category.Code] = category.Id;
                var archived = AddArchived(db, CompanyId, "G-7000", "Unused legacy account (legacy)", AccountCategoryType.Expense);
                await db.SaveChangesAsync();
                ids["ARCHIVED"] = archived.Id;
                ids["INCOME"] = ids["4000"];
                ids["EXPENSE"] = ids["6000"];
                var rows = new[]
                {
                    Transaction(bank, period, new DateOnly(2025, 2, 1), "Receipt one", 100m, ids["INCOME"]),
                    Transaction(bank, period, new DateOnly(2025, 2, 2), "Receipt two", 50m, ids["INCOME"])
                };
                db.ImportedTransactions.AddRange(rows);
                await db.SaveChangesAsync();
                periodId = period.Id;
                transactionIds.AddRange(rows.Select(row => row.Id));
            });
            return (periodId, transactionIds, ids);
        }

        public async Task InScopeAsync(Func<AccountsDbContext, Task> action)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<AccountsDbContext>());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
