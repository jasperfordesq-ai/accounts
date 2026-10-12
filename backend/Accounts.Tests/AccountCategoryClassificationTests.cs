using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Accounts.Api.Data;
using Accounts.Api.Data.Migrations;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Accounts.Tests;

// Chart task C1: explicit statement line / cash-flow class / system role per account. Nothing in the
// engine reads the new attributes yet; these tests pin that the one derivation function (used by the
// runtime fallback, the create paths and the migration backfill) reproduces today's code predicates.
public sealed class AccountCategoryClassificationTests(ITestOutputHelper output)
{
    private const string PostgresConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";

    // Codes AdjustmentService creates on demand (GetOrCreateCategoryIdAsync call sites, the asset-class
    // switch and the accrual/creditor keyword switch).
    private static readonly string[] AutoCreatedCodes =
    [
        "7000", "1200", "1000", "5000", "2100", "8000", "2400", "3200", "2800", "1400", "7050", "9000",
        "1100", "4000", "2000", "2500", "1150",
        "0010", "0020", "0030", "0040", "0050",
        "6900", "6810", "6820", "6200", "6300", "6400", "7300", "6100", "7900"
    ];

    // Edge codes from the brief plus codes that probe every prefix boundary of the old predicates,
    // the charity chart's clashing codes and a legacy re-code shape.
    private static readonly string[] EdgeCodes =
    [
        "9000", "2750", "2950", "8100", "1410", "3300", "2450", "2350", "2250", "2650", "2550", "2700",
        "2900", "2999", "0011", "0000", "00", "0", "1", "14", "1499", "1300", "1230", "1120", "1110",
        "6950", "6999", "69", "6", "5999", "5", "8", "8999", "8200", "7000", "7001", "700", "4999", "4",
        "4900", "3000", "3001", "3100", "3200", "9998", "9999", "2200", "2210", "2300", "2310", "G-1200",
        "G-1400", "X14", "", "40", "24", "27", "29", "26", "25", "28", "2800", "2801"
    ];

    private static async Task<string[]> SeedCodesAsync()
    {
        await using var db = CreateDb();
        var company = new Company { LegalName = "Seed Codes Ltd", CompanyType = CompanyType.Private, IncorporationDate = new DateOnly(2024, 1, 1) };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var seeded = await new CategoryService(db).SeedDefaultCategoriesAsync(company.Id);
        return seeded.Select(category => category.Code).ToArray();
    }

    private static async Task<IReadOnlyList<(AccountCategoryType Type, string Code, bool NonTrading)>> AllCasesAsync()
    {
        var codes = (await SeedCodesAsync()).Concat(AutoCreatedCodes).Concat(EdgeCodes).Distinct(StringComparer.Ordinal).ToArray();
        var cases = new List<(AccountCategoryType, string, bool)>();
        foreach (var code in codes)
        foreach (var type in Enum.GetValues<AccountCategoryType>())
        foreach (var nonTrading in new[] { false, true })
            cases.Add((type, code, nonTrading));
        return cases;
    }

    [Fact]
    public async Task Derive_ReproducesTodaysEnginePredicates_ForEverySeedAutoCreatedAndEdgeCode()
    {
        var seedCodes = await SeedCodesAsync();
        Assert.True(seedCodes.Length >= 50);
        var divergences = new List<string>();
        var checkedCases = 0;

        foreach (var (type, code, nonTrading) in await AllCasesAsync())
        {
            var category = new AccountCategory { CompanyId = 1, Code = code, Name = code, Type = type, IsNonTradingIncome = nonTrading };
            var derived = LegacyCategoryClassification.Derive(type, code, nonTrading);
            var line = derived.StatementLine;
            var label = $"{type} '{code}' nonTrading={nonTrading}";
            checkedCases++;

            // P&L buckets (FinancialStatementsService.GetProfitAndLossForPeriodAsync).
            Assert.True(OldEngine.Turnover(category) == (line == AccountStatementLine.Turnover), label);
            Assert.True(OldEngine.CostOfSales(category) == (line == AccountStatementLine.CostOfSales), label);
            Assert.True(OldEngine.OtherIncome(category) == (line == AccountStatementLine.OtherOperatingIncome), label);
            Assert.True(OldEngine.Overheads(category) == (line is AccountStatementLine.AdministrativeExpenses or AccountStatementLine.Depreciation), label);
            Assert.True(OldEngine.InterestPayable(category) == (line == AccountStatementLine.InterestPayable), label);
            Assert.True(OldEngine.TaxCharge(category) == (line == AccountStatementLine.TaxOnProfit), label);

            // Balance sheet buckets (GetBalanceSheetForPeriodAsync) and SOCIE (same equity rules).
            Assert.True(OldEngine.FixedAsset(category) == (line == AccountStatementLine.TangibleFixedAssets), label);
            Assert.True(OldEngine.Cash(category) == (line == AccountStatementLine.CashAtBank), label);
            Assert.True(OldEngine.Stock(category) == (line == AccountStatementLine.Stocks), label);
            Assert.True(OldEngine.Prepayments(category) == (line == AccountStatementLine.Prepayments), label);
            Assert.True(OldEngine.Debtors(category) == (line == AccountStatementLine.Debtors), label);
            Assert.True(OldEngine.LoansAfterYear(category) == (line == AccountStatementLine.LoansAfterYear), label);
            Assert.True(OldEngine.OtherCreditorsAfterYear(category) == (line == AccountStatementLine.OtherCreditorsAfterYear), label);
            Assert.True(OldEngine.TradeCreditors(category) == (line == AccountStatementLine.TradeCreditors), label);
            Assert.True(OldEngine.Accruals(category) == (line == AccountStatementLine.Accruals), label);
            Assert.True(OldEngine.TaxCreditors(category) == (line == AccountStatementLine.TaxCreditors), label);
            Assert.True(OldEngine.OtherCreditorsWithinYear(category) == (line == AccountStatementLine.OtherCreditorsWithinYear), label);
            Assert.True(OldEngine.ShareCapital(category) == (line == AccountStatementLine.ShareCapital), label);
            Assert.True(OldEngine.DividendsPaid(category) == (line == AccountStatementLine.DividendsPaid), label);
            Assert.True(OldEngine.OtherReserves(category) == (line == AccountStatementLine.Reserves), label);

            // Ledger (AccountingLedgerService): cash side of a journal and carry-forward persistence.
            Assert.True(AccountingLedgerServiceIsCash(category) == CategoryClassification.IsCash(category), label);
            var persistentByLine = type is AccountCategoryType.Asset or AccountCategoryType.Liability
                || line == AccountStatementLine.ShareCapital;
            Assert.True(OldEngine.Persistent(category) == persistentByLine, label);

            // System roles: every engine site that resolves an account by code.
            Assert.True(OldEngine.RoleByCode(code) == derived.SystemRole, label);

            // Cash-flow classification of a cash movement's contra category.
            var oldBuckets = OldEngine.CashFlowBuckets(category);
            if (OldEngine.Cash(category))
            {
                Assert.Empty(oldBuckets);
                Assert.Equal(AccountCashFlowClass.Cash, derived.CashFlowClass);
            }
            else if (oldBuckets.Count == 0)
            {
                Assert.True(AccountCashFlowClass.Operating == derived.CashFlowClass, label);
            }
            else if (oldBuckets.Count == 1)
            {
                Assert.True(oldBuckets[0] == derived.CashFlowClass, label);
            }
            else
            {
                // Today's predicates put these movements in two cash-flow buckets at once.
                Assert.Equal(AccountCategoryType.Equity, type);
                Assert.Contains(derived.CashFlowClass, oldBuckets);
                divergences.Add($"cash-flow overlap: {label} old=[{string.Join(",", oldBuckets)}] derived={derived.CashFlowClass}");
            }

            // Depreciation add-back and interest-paid adjustment key on code with no Type check today.
            if (OldEngine.DepreciationAddBack(category) != (line == AccountStatementLine.Depreciation))
            {
                Assert.NotEqual(AccountCategoryType.Expense, type);
                divergences.Add($"cash-flow depreciation add-back: {label}");
            }
            if (OldEngine.InterestPaidAdjustment(category) != (line == AccountStatementLine.InterestPayable))
            {
                Assert.NotEqual(AccountCategoryType.Expense, type);
                divergences.Add($"cash-flow interest-paid adjustment: {label}");
            }

            // The runtime view of an unclassified row is exactly Derive.
            Assert.Equal(line, CategoryClassification.Line(category));
            Assert.Equal(derived.CashFlowClass, CategoryClassification.CashFlow(category));
            Assert.Equal(derived.SystemRole, CategoryClassification.Role(category));
        }

        Assert.True(checkedCases > 1000);
        foreach (var divergence in divergences.Distinct())
            output.WriteLine(divergence);
        // Divergences are confined to non-Expense 7000/69xx and Equity-typed tax/loan/dividend codes;
        // no seed or auto-created account (with its real type) is affected.
        Assert.DoesNotContain(divergences, d =>
            seedCodes.Concat(AutoCreatedCodes).Any(code => d.Contains($" '{code}' ", StringComparison.Ordinal))
            && d.Contains("Expense ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Derive_SeedAndAutoCreatedAccountsWithTheirRealTypes_HaveNoDivergence()
    {
        await using var db = CreateDb();
        var company = new Company { LegalName = "Real Types Ltd", CompanyType = CompanyType.Private, IncorporationDate = new DateOnly(2024, 1, 1) };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var seeded = await new CategoryService(db).SeedDefaultCategoriesAsync(company.Id);
        var rows = seeded.Select(c => (c.Type, c.Code)).ToList();
        rows.AddRange([
            (AccountCategoryType.Asset, "1150"), (AccountCategoryType.Liability, "2800"),
            (AccountCategoryType.Expense, "7050"), (AccountCategoryType.Income, "9000")
        ]);

        foreach (var (type, code) in rows)
        {
            var category = new AccountCategory { Code = code, Name = code, Type = type };
            var derived = LegacyCategoryClassification.Derive(type, code);
            Assert.True(OldEngine.CashFlowBuckets(category).Count <= 1, code);
            Assert.Equal(OldEngine.DepreciationAddBack(category), derived.StatementLine == AccountStatementLine.Depreciation);
            Assert.Equal(OldEngine.InterestPaidAdjustment(category), derived.StatementLine == AccountStatementLine.InterestPayable);
        }

        var byCode = rows.ToDictionary(r => r.Code, r => LegacyCategoryClassification.Derive(r.Type, r.Code));
        Assert.Equal(AccountStatementLine.CashAtBank, byCode["1400"].StatementLine);
        Assert.Equal(AccountSystemRole.BankControl, byCode["1400"].SystemRole);
        Assert.Equal(AccountStatementLine.CashAtBank, byCode["1410"].StatementLine);
        Assert.Null(byCode["1410"].SystemRole);
        Assert.Equal(AccountStatementLine.Prepayments, byCode["1200"].StatementLine);
        Assert.Equal(AccountStatementLine.Depreciation, byCode["7000"].StatementLine);
        Assert.Equal(AccountStatementLine.TaxOnProfit, byCode["8000"].StatementLine);
        Assert.Equal(AccountCashFlowClass.Tax, byCode["8000"].CashFlowClass);
        Assert.Equal(AccountStatementLine.ShareCapital, byCode["3000"].StatementLine);
        Assert.Equal(AccountCashFlowClass.ShareIssue, byCode["3000"].CashFlowClass);
        Assert.Equal(AccountSystemRole.RetainedEarnings, byCode["3100"].SystemRole);
        Assert.Equal(AccountStatementLine.LoansAfterYear, byCode["2700"].StatementLine);
        Assert.Equal(AccountCashFlowClass.Loan, byCode["2600"].CashFlowClass);
        Assert.Equal(AccountCashFlowClass.OtherFinancing, byCode["2500"].CashFlowClass);
        Assert.Equal(AccountStatementLine.OtherOperatingIncome, byCode["9000"].StatementLine);
        Assert.Equal(AccountSystemRole.DisposalGain, byCode["9000"].SystemRole);
        Assert.Equal(AccountStatementLine.Turnover, LegacyCategoryClassification.Derive(AccountCategoryType.Income, "4200").StatementLine);
        Assert.Equal(AccountStatementLine.OtherOperatingIncome, LegacyCategoryClassification.Derive(AccountCategoryType.Income, "4200", isNonTradingIncome: true).StatementLine);
    }

    [Fact]
    public void CategoryClassification_UsesExplicitValues_AndDerivesRolesOnlyForUnclassifiedRows()
    {
        var explicitRow = new AccountCategory
        {
            Code = "1200",
            Name = "Bank current account",
            Type = AccountCategoryType.Asset,
            StatementLine = AccountStatementLine.CashAtBank,
            CashFlowClass = AccountCashFlowClass.Cash,
            SystemRole = AccountSystemRole.BankControl
        };
        Assert.Equal(AccountStatementLine.CashAtBank, CategoryClassification.Line(explicitRow));
        Assert.Equal(AccountCashFlowClass.Cash, CategoryClassification.CashFlow(explicitRow));
        Assert.Equal(AccountSystemRole.BankControl, CategoryClassification.Role(explicitRow));
        Assert.True(CategoryClassification.IsCash(explicitRow));

        // Classified (line set) with no role: a role is never inferred from the code.
        var classifiedNoRole = new AccountCategory
        {
            Code = "1400",
            Name = "Second bank",
            Type = AccountCategoryType.Asset,
            StatementLine = AccountStatementLine.CashAtBank
        };
        Assert.Null(CategoryClassification.Role(classifiedNoRole));
        Assert.Equal(AccountCashFlowClass.Cash, CategoryClassification.CashFlow(classifiedNoRole));

        // Unclassified: today's rules.
        var unclassified = new AccountCategory { Code = "1400", Name = "Bank", Type = AccountCategoryType.Asset };
        Assert.Equal(AccountSystemRole.BankControl, CategoryClassification.Role(unclassified));

        // A non-trading flag set after classification still keeps the income out of turnover.
        var reviewedPassive = new AccountCategory
        {
            Code = "4200",
            Name = "Interest received",
            Type = AccountCategoryType.Income,
            StatementLine = AccountStatementLine.Turnover,
            IsNonTradingIncome = true
        };
        Assert.Equal(AccountStatementLine.OtherOperatingIncome, CategoryClassification.Line(reviewedPassive));
    }

    [Fact]
    public void LineTypeCompatibility_RejectsIncomeOnBalanceSheetLinesAndAcceptsEveryDerivedLine()
    {
        Assert.False(CategoryClassification.IsLineAllowedForType(AccountCategoryType.Income, AccountStatementLine.CashAtBank));
        Assert.False(CategoryClassification.IsLineAllowedForType(AccountCategoryType.Asset, AccountStatementLine.Turnover));
        Assert.False(CategoryClassification.IsLineAllowedForType(AccountCategoryType.Equity, AccountStatementLine.TradeCreditors));
        Assert.True(CategoryClassification.IsLineAllowedForType(AccountCategoryType.Liability, AccountStatementLine.Suspense));
        Assert.True(CategoryClassification.IsLineAllowedForType(AccountCategoryType.Equity, AccountStatementLine.OpeningBalanceControl));
        foreach (var line in Enum.GetValues<AccountStatementLine>())
            Assert.Single(Enum.GetValues<AccountCategoryType>(), type => CategoryClassification.IsLineAllowedForType(type, line));
        foreach (var code in AutoCreatedCodes.Concat(EdgeCodes))
        foreach (var type in Enum.GetValues<AccountCategoryType>())
            Assert.True(CategoryClassification.IsLineAllowedForType(type, LegacyCategoryClassification.Derive(type, code).StatementLine), $"{type} {code}");
    }

    [Fact]
    public async Task SeedDefaultCategories_WritesExplicitAttributesFromDerive_WithOneRowPerRole()
    {
        await using var db = CreateDb();
        var company = new Company { LegalName = "Explicit Seed Ltd", CompanyType = CompanyType.Private, IncorporationDate = new DateOnly(2024, 1, 1) };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var seeded = await new CategoryService(db).SeedDefaultCategoriesAsync(company.Id);

        Assert.All(seeded, category =>
        {
            var derived = LegacyCategoryClassification.Derive(category.Type, category.Code, category.IsNonTradingIncome);
            Assert.Equal(derived.StatementLine, category.StatementLine);
            Assert.Equal(derived.CashFlowClass, category.CashFlowClass);
            Assert.Equal(derived.SystemRole, category.SystemRole);
            Assert.False(category.IsArchived);
        });
        var roles = seeded.Where(c => c.SystemRole != null).Select(c => c.SystemRole).ToList();
        Assert.Equal(roles.Count, roles.Distinct().Count());
        Assert.Equal(ChartOfAccountsVariant.GenericTrading, company.ChartOfAccountsVariant);
    }

    [Fact]
    public async Task AdjustmentAutoCreatedCategories_AreExplicit_AndNeverDuplicateARoleHeldByAnUnclassifiedRow()
    {
        await using var db = CreateDb();
        var company = new Company { LegalName = "Auto Create Ltd", CompanyType = CompanyType.Private, IncorporationDate = new DateOnly(2024, 1, 1) };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = true
        };
        // A pre-existing, unclassified bank row (the shape ~190 test sites and old rows have).
        var legacyBank = new AccountCategory { CompanyId = company.Id, Code = "1400", Name = "Bank", Type = AccountCategoryType.Asset };
        db.AddRange(period, legacyBank);
        await db.SaveChangesAsync();

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(company.Id, period.Id);

        var rows = await db.AccountCategories.Where(c => c.CompanyId == company.Id).ToListAsync();
        Assert.Null(rows.Single(c => c.Code == "1400").StatementLine);
        foreach (var code in new[] { "7000", "1200", "1000", "5000", "2100", "8000", "2400", "3200", "2800", "7050", "9000" })
        {
            var row = rows.Single(c => c.Code == code);
            var derived = LegacyCategoryClassification.Derive(row.Type, row.Code, row.IsNonTradingIncome);
            Assert.Equal(derived.StatementLine, row.StatementLine);
            Assert.Equal(derived.CashFlowClass, row.CashFlowClass);
            Assert.Equal(derived.SystemRole, row.SystemRole);
        }
        var resolvedRoles = rows.Select(CategoryClassification.Role).Where(role => role != null).ToList();
        Assert.Equal(resolvedRoles.Count, resolvedRoles.Distinct().Count());
    }

    [Fact]
    public void ClassifyNew_LeavesTheRoleUnsetWhenAnotherRowOfTheCompanyAlreadyResolvesToIt()
    {
        var existing = new AccountCategory { CompanyId = 7, Code = "1400", Name = "Bank", Type = AccountCategoryType.Asset };
        var otherCompany = new AccountCategory { CompanyId = 8, Code = "3100", Name = "Retained", Type = AccountCategoryType.Equity };
        var duplicateBank = new AccountCategory { CompanyId = 7, Code = "1400", Name = "Bank 2", Type = AccountCategoryType.Asset };
        var retained = new AccountCategory { CompanyId = 7, Code = "3100", Name = "Retained", Type = AccountCategoryType.Equity };

        CategoryClassification.ClassifyNew(duplicateBank, [existing, otherCompany]);
        CategoryClassification.ClassifyNew(retained, [existing, otherCompany]);

        Assert.Equal(AccountStatementLine.CashAtBank, duplicateBank.StatementLine);
        Assert.Equal(AccountCashFlowClass.Cash, duplicateBank.CashFlowClass);
        Assert.Null(duplicateBank.SystemRole);
        Assert.Equal(AccountSystemRole.RetainedEarnings, retained.SystemRole);
        Assert.Equal(AccountStatementLine.Reserves, retained.StatementLine);
    }

    [Fact]
    public async Task CreateCategoryEndpoint_DerivesExplicitAttributes_AcceptsAConsistentLine_AndRejectsATypeLineMismatch()
    {
        await using var host = await CategoryHost.StartAsync();

        var derivedResponse = await host.Client.PostAsJsonAsync(
            $"/api/companies/{host.CompanyId}/categories",
            new { code = "2700", name = "Bank loan", type = "Liability", taxTreatment = "Other" });
        Assert.Equal(HttpStatusCode.Created, derivedResponse.StatusCode);

        var explicitResponse = await host.Client.PostAsJsonAsync(
            $"/api/companies/{host.CompanyId}/categories",
            new { code = "2300", name = "Finance lease due after one year", type = "Liability", statementLine = "OtherCreditorsAfterYear", cashFlowClass = "Loan" });
        Assert.Equal(HttpStatusCode.Created, explicitResponse.StatusCode);
        var explicitBody = await explicitResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"statementLine\":\"OtherCreditorsAfterYear\"", explicitBody, StringComparison.Ordinal);

        var mismatch = await host.Client.PostAsJsonAsync(
            $"/api/companies/{host.CompanyId}/categories",
            new { code = "4500", name = "Donations", type = "Income", statementLine = "CashAtBank" });
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        Assert.Contains("statementLine", await mismatch.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // A second bank-control code for the same company is created without stealing the role.
        var secondBank = await host.Client.PostAsJsonAsync(
            $"/api/companies/{host.CompanyId}/categories",
            new { code = "1400", name = "Second bank", type = "Asset" });
        Assert.Equal(HttpStatusCode.Created, secondBank.StatusCode);

        await host.InScopeAsync(async db =>
        {
            var loan = await db.AccountCategories.SingleAsync(c => c.Code == "2700");
            Assert.Equal(AccountStatementLine.LoansAfterYear, loan.StatementLine);
            Assert.Equal(AccountCashFlowClass.Loan, loan.CashFlowClass);
            Assert.Null(loan.SystemRole);

            var lease = await db.AccountCategories.SingleAsync(c => c.Code == "2300");
            Assert.Equal(AccountStatementLine.OtherCreditorsAfterYear, lease.StatementLine);
            Assert.Equal(AccountCashFlowClass.Loan, lease.CashFlowClass);

            Assert.False(await db.AccountCategories.AnyAsync(c => c.Code == "4500"));
            var banks = await db.AccountCategories.Where(c => c.Code == "1400").OrderBy(c => c.Id).ToListAsync();
            Assert.Equal(2, banks.Count);
            Assert.Equal(AccountSystemRole.BankControl, CategoryClassification.Role(banks[0]));
            Assert.Null(banks[1].SystemRole);
            Assert.Equal(AccountStatementLine.CashAtBank, banks[1].StatementLine);
        });
    }

    [Fact]
    public void CategoryAuditSnapshot_RecordsTheExplicitAttributes()
    {
        var snapshot = System.Text.Json.JsonSerializer.Serialize(DomainAuditCoverage.CategorySnapshot(new AccountCategory
        {
            Id = 5,
            CompanyId = 3,
            Code = "1400",
            Name = "Bank",
            Type = AccountCategoryType.Asset,
            StatementLine = AccountStatementLine.CashAtBank,
            CashFlowClass = AccountCashFlowClass.Cash,
            SystemRole = AccountSystemRole.BankControl,
            IsArchived = true
        }));
        Assert.Contains("\"StatementLine\"", snapshot, StringComparison.Ordinal);
        Assert.Contains("\"CashFlowClass\"", snapshot, StringComparison.Ordinal);
        Assert.Contains("\"SystemRole\"", snapshot, StringComparison.Ordinal);
        Assert.Contains("\"IsArchived\":true", snapshot, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task MigrationBackfillSql_ProducesExactlyDerive_ForEveryCase()
    {
        var cases = await AllCasesAsync();
        await using var scratch = await ScratchSchema.CreateAsync();
        await scratch.ExecuteAsync("""
            CREATE TABLE account_categories (
                "Id" integer PRIMARY KEY,
                "CompanyId" integer NULL,
                "Code" character varying(20) NOT NULL,
                "Type" text NOT NULL,
                "IsNonTradingIncome" boolean NOT NULL,
                "StatementLine" text NULL,
                "CashFlowClass" text NULL,
                "SystemRole" text NULL,
                "IsArchived" boolean NOT NULL DEFAULT FALSE)
            """);
        await using (var connection = await scratch.OpenAsync())
        {
            for (var index = 0; index < cases.Count; index++)
            {
                var (type, code, nonTrading) = cases[index];
                await using var insert = new NpgsqlCommand(
                    """INSERT INTO account_categories ("Id", "CompanyId", "Code", "Type", "IsNonTradingIncome") VALUES (@id, @company, @code, @type, @nonTrading)""",
                    connection);
                insert.Parameters.AddWithValue("id", index + 1);
                insert.Parameters.AddWithValue("company", index + 1);
                insert.Parameters.AddWithValue("code", code);
                insert.Parameters.AddWithValue("type", type.ToString());
                insert.Parameters.AddWithValue("nonTrading", nonTrading);
                await insert.ExecuteNonQueryAsync();
            }
        }

        await scratch.ExecuteAsync(AddExplicitAccountCategoryClassification.PreflightSql);
        await scratch.ExecuteAsync(AddExplicitAccountCategoryClassification.BackfillSql);
        await scratch.ExecuteAsync(AddExplicitAccountCategoryClassification.RoleConflictSql);

        await using (var connection = await scratch.OpenAsync())
        {
            await using var select = new NpgsqlCommand(
                """SELECT "Id", "StatementLine", "CashFlowClass", "SystemRole" FROM account_categories ORDER BY "Id" """,
                connection);
            await using var reader = await select.ExecuteReaderAsync();
            var compared = 0;
            while (await reader.ReadAsync())
            {
                var (type, code, nonTrading) = cases[reader.GetInt32(0) - 1];
                var derived = LegacyCategoryClassification.Derive(type, code, nonTrading);
                var label = $"{type} '{code}' nonTrading={nonTrading}";
                Assert.True(derived.StatementLine.ToString() == reader.GetString(1), $"{label}: line {reader.GetString(1)}");
                Assert.True(derived.CashFlowClass.ToString() == reader.GetString(2), $"{label}: cash flow {reader.GetString(2)}");
                Assert.True(derived.SystemRole?.ToString() == (reader.IsDBNull(3) ? null : reader.GetString(3)), $"{label}: role");
                compared++;
            }
            Assert.Equal(cases.Count, compared);
        }
    }

    [PostgresFact]
    public async Task MigrationPreflight_FailsClearlyOnDuplicateCodesOrRoles_InsteadOfGuessing()
    {
        await using var scratch = await ScratchSchema.CreateAsync();
        await scratch.ExecuteAsync("""
            CREATE TABLE account_categories (
                "Id" integer PRIMARY KEY,
                "CompanyId" integer NULL,
                "Code" character varying(20) NOT NULL,
                "Type" text NOT NULL,
                "IsNonTradingIncome" boolean NOT NULL DEFAULT FALSE,
                "StatementLine" text NULL,
                "CashFlowClass" text NULL,
                "SystemRole" text NULL,
                "IsArchived" boolean NOT NULL DEFAULT FALSE);
            INSERT INTO account_categories ("Id", "CompanyId", "Code", "Type") VALUES
                (1, 41, '6100', 'Expense'), (2, 41, '6100', 'Expense'), (3, 42, '6100', 'Expense');
            """);

        var duplicateCode = await Assert.ThrowsAsync<PostgresException>(
            () => scratch.ExecuteAsync(AddExplicitAccountCategoryClassification.PreflightSql));
        Assert.Contains("duplicate account codes", duplicateCode.MessageText, StringComparison.Ordinal);
        Assert.Contains("company 41 code 6100", duplicateCode.MessageText, StringComparison.Ordinal);

        await scratch.ExecuteAsync("""
            DELETE FROM account_categories;
            INSERT INTO account_categories ("Id", "CompanyId", "Code", "Type", "SystemRole") VALUES
                (1, 41, '1400', 'Asset', 'BankControl'), (2, 41, '1410', 'Asset', 'BankControl');
            """);
        await scratch.ExecuteAsync(AddExplicitAccountCategoryClassification.PreflightSql);
        var duplicateRole = await Assert.ThrowsAsync<PostgresException>(
            () => scratch.ExecuteAsync(AddExplicitAccountCategoryClassification.RoleConflictSql));
        Assert.Contains("company 41 role BankControl", duplicateRole.MessageText, StringComparison.Ordinal);
    }

    [PostgresFact]
    public async Task MigratedSchema_HasTheColumns_DefaultsGeneric_AndEnforcesOneRolePerCompany()
    {
        await using var scratch = await ScratchSchema.CreateAsync();
        var options = new DbContextOptionsBuilder<AccountsDbContext>().UseNpgsql(scratch.ConnectionString).Options;
        await using (var db = new AccountsDbContext(options))
        {
            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            var tenant = new Tenant { Name = "Chart Tenant", Slug = $"chart-{Guid.NewGuid():N}" };
            var company = new Company { Tenant = tenant, LegalName = "Chart Role Ltd", CompanyType = CompanyType.Private, IncorporationDate = new DateOnly(2024, 1, 1) };
            db.Companies.Add(company);
            await db.SaveChangesAsync();

            var seeded = await new CategoryService(db).SeedDefaultCategoriesAsync(company.Id);
            Assert.All(seeded, category => Assert.NotNull(category.StatementLine));

            db.AccountCategories.Add(new AccountCategory
            {
                CompanyId = company.Id,
                Code = "1490",
                Name = "Rogue bank control",
                Type = AccountCategoryType.Asset,
                IsSystem = true,
                SystemRole = AccountSystemRole.BankControl
            });
            var violation = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(violation.InnerException).SqlState);
        }

        await using var connection = await scratch.OpenAsync();
        await using var variant = new NpgsqlCommand("""SELECT "ChartOfAccountsVariant" FROM companies""", connection);
        Assert.Equal("GenericTrading", await variant.ExecuteScalarAsync());
    }

    private static AccountsDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AccountsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static bool AccountingLedgerServiceIsCash(AccountCategory category) =>
        category.Type == AccountCategoryType.Asset && category.Code.StartsWith("14", StringComparison.Ordinal);

    // Verbatim copies of today's code predicates (file:line in the C1 report). They are the oracle the
    // derivation function is compared against; the engine itself is unchanged in C1.
    private static class OldEngine
    {
        private static bool S(AccountCategory c, string prefix) => c.Code.StartsWith(prefix, StringComparison.Ordinal);

        // FinancialStatementsService P&L
        public static bool Turnover(AccountCategory c) => c.Type == AccountCategoryType.Income && S(c, "4") && !c.IsNonTradingIncome;
        public static bool CostOfSales(AccountCategory c) => c.Type == AccountCategoryType.Expense && c.Code.StartsWith("5");
        public static bool OtherIncome(AccountCategory c) => c.Type == AccountCategoryType.Income && (c.IsNonTradingIncome || !S(c, "4"));
        public static bool Overheads(AccountCategory c) => c.Type == AccountCategoryType.Expense
            && !c.Code.StartsWith("5") && !c.Code.StartsWith("69") && !c.Code.StartsWith("8");
        public static bool InterestPayable(AccountCategory c) => c.Type == AccountCategoryType.Expense && c.Code.StartsWith("69");
        public static bool TaxCharge(AccountCategory c) => c.Type == AccountCategoryType.Expense && c.Code.StartsWith("8");

        // FinancialStatementsService balance sheet
        public static bool FixedAsset(AccountCategory c) => c.Type == AccountCategoryType.Asset && S(c, "00");
        public static bool Cash(AccountCategory c) => AccountingLedgerServiceIsCash(c);
        private static bool CurrentAsset(AccountCategory c) => c.Type == AccountCategoryType.Asset && !FixedAsset(c);
        public static bool Stock(AccountCategory c) => CurrentAsset(c) && c.Code == "1000";
        public static bool Prepayments(AccountCategory c) => CurrentAsset(c) && c.Code == "1200";
        public static bool Debtors(AccountCategory c) => CurrentAsset(c) && c.Code != "1000" && c.Code != "1200" && !Cash(c);
        private static bool LongTerm(AccountCategory c) => c.Type == AccountCategoryType.Liability && (S(c, "27") || S(c, "29"));
        private static bool CurrentLiability(AccountCategory c) => c.Type == AccountCategoryType.Liability && !LongTerm(c);
        public static bool LoansAfterYear(AccountCategory c) => LongTerm(c) && c.Code == "2700";
        public static bool OtherCreditorsAfterYear(AccountCategory c) => LongTerm(c) && c.Code != "2700";
        public static bool TradeCreditors(AccountCategory c) => CurrentLiability(c) && c.Code == "2000";
        public static bool Accruals(AccountCategory c) => CurrentLiability(c) && c.Code == "2100";
        public static bool TaxCreditors(AccountCategory c) => CurrentLiability(c) && (S(c, "22") || S(c, "23") || S(c, "24"));
        public static bool OtherCreditorsWithinYear(AccountCategory c) => CurrentLiability(c)
            && c.Code != "2000" && c.Code != "2100" && !S(c, "22") && !S(c, "23") && !S(c, "24");
        public static bool ShareCapital(AccountCategory c) => c.Type == AccountCategoryType.Equity && c.Code == "3000";
        public static bool DividendsPaid(AccountCategory c) => c.Type == AccountCategoryType.Equity && c.Code == "3200";
        public static bool OtherReserves(AccountCategory c) => c.Type == AccountCategoryType.Equity && c.Code is not "3000" and not "3200";

        // AccountingLedgerService carry-forward
        public static bool Persistent(AccountCategory c) => c.Type is AccountCategoryType.Asset or AccountCategoryType.Liability
            || (c.Type == AccountCategoryType.Equity && c.Code == "3000");

        // FinancialStatementsService cash-flow statement
        public static List<AccountCashFlowClass> CashFlowBuckets(AccountCategory c)
        {
            var buckets = new List<AccountCashFlowClass>();
            if (c.Type == AccountCategoryType.Asset && S(c, "00")) buckets.Add(AccountCashFlowClass.Investing);
            if (S(c, "24") || S(c, "8")) buckets.Add(AccountCashFlowClass.Tax);
            if (c.Code is "2800" or "3200") buckets.Add(AccountCashFlowClass.Dividend);
            if (S(c, "26") || S(c, "27")) buckets.Add(AccountCashFlowClass.Loan);
            if (c.Code == "3000") buckets.Add(AccountCashFlowClass.ShareIssue);
            if (S(c, "25") || (c.Type == AccountCategoryType.Equity && c.Code is not "3000" and not "3200"))
                buckets.Add(AccountCashFlowClass.OtherFinancing);
            return buckets;
        }

        public static bool DepreciationAddBack(AccountCategory c) => c.Code == "7000";
        public static bool InterestPaidAdjustment(AccountCategory c) => S(c, "69");

        // Accounts resolved by code: ledger bank/retained, AdjustmentService GetOrCreate call sites,
        // VAT reconciliation control accounts.
        public static AccountSystemRole? RoleByCode(string code) => code switch
        {
            "1400" => AccountSystemRole.BankControl,
            "3100" => AccountSystemRole.RetainedEarnings,
            "7000" => AccountSystemRole.Depreciation,
            "1200" => AccountSystemRole.Prepayments,
            "1000" => AccountSystemRole.Stock,
            "5000" => AccountSystemRole.CostOfSalesStockMovement,
            "2100" => AccountSystemRole.Accruals,
            "8000" => AccountSystemRole.CorporationTaxCharge,
            "2400" => AccountSystemRole.CorporationTaxPayable,
            "3200" => AccountSystemRole.DividendsPaid,
            "2800" => AccountSystemRole.DividendsPayable,
            "7050" => AccountSystemRole.DisposalLoss,
            "9000" => AccountSystemRole.DisposalGain,
            "1100" => AccountSystemRole.TradeDebtors,
            "4000" => AccountSystemRole.TradeDebtorIncome,
            "2000" => AccountSystemRole.TradeCreditors,
            "2500" => AccountSystemRole.DirectorLoanLiability,
            "1150" => AccountSystemRole.DirectorLoanReceivable,
            "2200" => AccountSystemRole.VatPayable,
            "1300" => AccountSystemRole.VatReceivable,
            _ => null
        };
    }

    private sealed class ScratchSchema : IAsyncDisposable
    {
        private readonly string _baseConnectionString;
        private readonly string _schema;

        private ScratchSchema(string baseConnectionString, string schema)
        {
            _baseConnectionString = baseConnectionString;
            _schema = schema;
            ConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString) { SearchPath = schema }.ConnectionString;
        }

        public string ConnectionString { get; }

        public static async Task<ScratchSchema> CreateAsync()
        {
            var baseConnection = Environment.GetEnvironmentVariable(PostgresConnectionEnvVar)
                ?? throw new InvalidOperationException($"{PostgresConnectionEnvVar} is required.");
            var schema = $"chart_c1_{Guid.NewGuid():N}";
            await using var admin = new NpgsqlConnection(baseConnection);
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin);
            await create.ExecuteNonQueryAsync();
            return new ScratchSchema(baseConnection, schema);
        }

        public async Task<NpgsqlConnection> OpenAsync()
        {
            var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            return connection;
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = await OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(_baseConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class CategoryHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private CategoryHost(WebApplication app, HttpClient client, int companyId)
        {
            _app = app;
            Client = client;
            CompanyId = companyId;
        }

        public HttpClient Client { get; }
        public int CompanyId { get; }

        public static async Task<CategoryHost> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddHttpContextAccessor();
            var databaseName = $"chart-c1-{Guid.NewGuid():N}";
            builder.Services.AddDbContext<AccountsDbContext>(options => options.UseInMemoryDatabase(databaseName));
            builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
            builder.Services.Configure<ApiAccessConfig>(config => config.Enabled = false);
            builder.Services.Configure<ImportLimitConfig>(_ => { });
            builder.Services.AddSingleton<ApiAccessService>();
            builder.Services.AddScoped<AccountingWriteGuard>();
            builder.Services.AddScoped<AuditService>();
            builder.Services.AddScoped<CategoryService>();
            builder.Services.AddScoped<ImportService>();

            var app = builder.Build();
            int tenantId;
            int companyId;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
                var tenant = new Tenant { Name = "Chart Tenant", Slug = $"chart-{Guid.NewGuid():N}" };
                var company = new Company { Tenant = tenant, LegalName = "Chart Route Ltd", CompanyType = CompanyType.Private, IncorporationDate = new DateOnly(2025, 1, 1) };
                db.Companies.Add(company);
                await db.SaveChangesAsync();
                // Pre-existing unclassified bank control row.
                db.AccountCategories.Add(new AccountCategory { CompanyId = company.Id, Code = "1400", Name = "Bank", Type = AccountCategoryType.Asset });
                await db.SaveChangesAsync();
                tenantId = tenant.Id;
                companyId = company.Id;
            }

            app.Use(async (context, next) =>
            {
                context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
                    UserId: 1,
                    TenantId: tenantId,
                    TenantName: "Chart Tenant",
                    Email: "chart.owner@example.ie",
                    DisplayName: "Chart Owner",
                    Role: "Owner",
                    AllowedCompanyIds: new HashSet<int> { companyId });
                await next();
            });
            app.MapBankingEndpoints();
            await app.StartAsync();
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
            var client = new HttpClient { BaseAddress = new Uri(Assert.Single(addresses!.Addresses)) };
            return new CategoryHost(app, client, companyId);
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

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresConnectionEnvVar)))
                Skip = $"{PostgresConnectionEnvVar} is not set.";
        }
    }
}
