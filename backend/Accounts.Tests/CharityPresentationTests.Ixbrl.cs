using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounts.Tests;

// Batch C-1: the review iXBRL uses the charity wording (members' funds, income and expenditure account,
// surplus/(deficit), bank overdraft within creditors) only on the charity chart. The tagged concepts and
// contexts are the generic ones; a charity with nil share capital carries no share-capital fact.
public sealed partial class CharityPresentationTests
{
    // Captured from IxbrlService before batch C. Each digest is the SHA-256 of the generated XHTML with the
    // CRO number fixed and the platform newline normalised to "\n" (the generator uses AppendLine, so the
    // bytes differ only by newline between Windows and Linux CI). A change here changes every generic
    // company's iXBRL and must be deliberate.
    [Theory]
    [InlineData("micro-ltd", "8b7521326f798e19805d70be125ddcce5558c6dbf17db716c11ec347a830dd30")]
    [InlineData("small-abridged-ltd", "b604e51207c89a446f66c760bd5f4351bb1f43c0ee39b0316a060f3f3c9005d0")]
    [InlineData("dac-small", "c6e44fe41a21a059471307c43224aa51a47b352a982003d3dcd3a365364d15e6")]
    [InlineData("medium-audit-required", "00f6f19ce23afd6922e101e61325fe62f3d18664077825ca8f87bec9f8f4b660")]
    [InlineData("clg-charity", "82b34613129123ca45a0a59a7b2a02d28866cd85a2a23c1715ef81fcb3918195")]
    public async Task Ixbrl_GoldenScenariosOnTheGenericChart_AreUnchanged(string scenarioCode, string ixbrlSha256)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            await using var db = CreateDb();
            var period = await FilingGoldenCorpusScenarioTests.SeedBasicScenarioAsync(db, GoldenCorpusFixture.Scenario(scenarioCode), finalise: false);
            var company = await db.Companies.SingleAsync(item => item.Id == period.CompanyId);
            Assert.Equal(ChartOfAccountsVariant.GenericTrading, company.ChartOfAccountsVariant);
            company.CroNumber = "G0LDEN01";
            await db.SaveChangesAsync();

            var xhtml = await IxbrlTextAsync(db, period.CompanyId, period.Id);

            Assert.Contains("core:CalledUpShareCapital", xhtml, StringComparison.Ordinal);
            Assert.Contains("<td>Shareholders' funds</td>", xhtml, StringComparison.Ordinal);
            Assert.Contains("<h2>Profit and Loss Account</h2>", xhtml, StringComparison.Ordinal);
            Assert.Contains("<td>Turnover</td>", xhtml, StringComparison.Ordinal);
            Assert.True(ixbrlSha256 == Sha256(xhtml), $"{scenarioCode}: iXBRL {Sha256(xhtml)}\n{xhtml}");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    // The overdrawn fixture on the generic chart, alone and as the comparative of a second year (so the
    // prior-year columns, the after-one-year creditors row and negative values are pinned too).
    [Theory]
    [InlineData(false, "db52196d687deeb2252e59efa3ffa9c7868fe2b7e96d1407688046dc2b50454f")]
    [InlineData(true, "bdd960a9cbc91411d5363a167e8e58ead7a50db5300179da23b8d5177093d901")]
    public async Task Ixbrl_TheOverdrawnFixtureOnTheGenericChart_IsUnchanged(bool secondYear, string ixbrlSha256)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            await using var db = CreateDb();
            var fixture = await SeedOverdrawnCharityAsync(db, ChartOfAccountsVariant.GenericTrading);
            var period = secondYear ? await AddNextPeriodAsync(db, fixture) : fixture.Period;

            var xhtml = await IxbrlTextAsync(db, fixture.Company.Id, period.Id);

            Assert.True(ixbrlSha256 == Sha256(xhtml), $"overdrawn generic (second year: {secondYear}): iXBRL {Sha256(xhtml)}\n{xhtml}");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CharityIxbrl_UsesMembersFundsAndSurplusLabels_WithTheGenericTags_AndNoShareCapitalFact(bool secondYear)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            await using var db = CreateDb();
            var fixture = await SeedOverdrawnCharityAsync(db, ChartOfAccountsVariant.CharityClg);
            var period = secondYear ? await AddNextPeriodAsync(db, fixture) : fixture.Period;
            await using var genericDb = CreateDb();
            var generic = await SeedOverdrawnCharityAsync(genericDb, ChartOfAccountsVariant.GenericTrading);
            var genericPeriod = secondYear ? await AddNextPeriodAsync(genericDb, generic) : generic.Period;

            var xhtml = await IxbrlTextAsync(db, fixture.Company.Id, period.Id);
            var genericXhtml = await IxbrlTextAsync(genericDb, generic.Company.Id, genericPeriod.Id);

            foreach (var label in new[]
            {
                "<td>Creditors: due within one year (including bank overdraft)</td>",
                "<td>Unrestricted and restricted funds</td>",
                "<td>Total members' funds</td>",
                "<h2>Income and Expenditure Account</h2>",
                "<td>Income</td>",
                "<td>Gross surplus/(deficit)</td>",
                "<td>Operating surplus/(deficit)</td>",
                "<td>Surplus/(deficit) before taxation</td>",
                "<td>Tax on surplus</td>",
                "<td>Surplus/(deficit) for the financial year</td>"
            })
            {
                Assert.Contains(label, xhtml, StringComparison.Ordinal);
            }
            foreach (var genericLabel in new[]
            {
                "<td>Share capital</td>",
                "<td>Profit and loss account</td>",
                "<td>Shareholders' funds</td>",
                "<td>Income and expenditure account</td>",
                "<h2>Profit and Loss Account</h2>",
                "<td>Turnover</td>",
                "<td>Gross profit</td>",
                "<td>Operating profit</td>",
                "<td>Profit before taxation</td>",
                "<td>Tax on profit</td>",
                "<td>Profit for the year</td>",
                "<td>Creditors: due within one year</td>",
                "core:CalledUpShareCapital"
            })
            {
                Assert.DoesNotContain(genericLabel, xhtml, StringComparison.Ordinal);
            }

            // Same concepts and contexts, in the same order, as the generic presentation of the same ledger,
            // less the nil share-capital fact.
            var facts = Facts(xhtml);
            var genericFacts = Facts(genericXhtml);
            Assert.Contains(genericFacts, fact => fact.Concept == "core:CalledUpShareCapital");
            Assert.Equal(
                genericFacts.Where(fact => fact.Concept != "core:CalledUpShareCapital").Select(fact => (fact.Concept, fact.Context)),
                facts.Select(fact => (fact.Concept, fact.Context)));
            Assert.Equal(secondYear, facts.Any(fact => fact.Context == "priorInstant"));

            // The tagged totals follow the charity balance sheet: cash in hand only, the 1,620 overdraft
            // within creditors (1,870 in all), and members' funds equal to net assets.
            var instant = secondYear ? "priorInstant" : "instant";
            decimal Fact(string concept, string context) => facts.Single(fact => fact.Concept == concept && fact.Context == context).Value;
            Assert.Equal(200m, Fact("core:CashBankInHand", instant));
            Assert.Equal(200m, Fact("core:CurrentAssets", instant));
            Assert.Equal(-1_870m, Fact("core:CreditorsAmountsFallingDueWithinOneYear", instant));
            Assert.Equal(-1_670m, Fact("core:NetCurrentAssetsLiabilities", instant));
            Assert.Equal(-1_270m, Fact("core:NetAssetsLiabilities", instant));
            Assert.Equal(-1_270m, Fact("core:ProfitLossAccountReserve", instant));
            Assert.Equal(-1_270m, Fact("core:ShareholderFunds", instant));
            Assert.Equal(Fact("core:NetAssetsLiabilities", "instant"), Fact("core:ShareholderFunds", "instant"));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public async Task CharityIxbrl_WithoutAnOverdraft_KeepsThePlainCreditorsLabel()
    {
        await using var db = CreateDb();
        var fixture = await SeedOverdrawnCharityAsync(db, ChartOfAccountsVariant.CharityClg, overdrawn: false);

        var xhtml = await IxbrlTextAsync(db, fixture.Company.Id, fixture.Period.Id);

        Assert.Contains("<td>Creditors: due within one year</td>", xhtml, StringComparison.Ordinal);
        Assert.DoesNotContain("including bank overdraft", xhtml, StringComparison.Ordinal);
        Assert.Contains("<td>Total members' funds</td>", xhtml, StringComparison.Ordinal);
        Assert.DoesNotContain("core:CalledUpShareCapital", xhtml, StringComparison.Ordinal);
    }

    // A charity carrying legacy share capital, in the current year or only in the comparative year, keeps
    // the share-capital row and fact; only a nil share capital in both years drops it.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CharityIxbrl_WithNonNilShareCapital_KeepsTheShareCapitalFact(bool onlyInThePriorYear)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            await using var db = CreateDb();
            var fixture = await SeedOverdrawnCharityAsync(db, ChartOfAccountsVariant.CharityClg);
            var shareCapital = new AccountCategory
            {
                CompanyId = fixture.Company.Id,
                Code = "3050",
                Name = "Legacy share capital",
                Type = AccountCategoryType.Equity,
                StatementLine = AccountStatementLine.ShareCapital,
                CashFlowClass = AccountCashFlowClass.ShareIssue
            };
            db.AccountCategories.Add(shareCapital);
            await db.SaveChangesAsync();
            var cashInHand = await db.AccountCategories.SingleAsync(category => category.CompanyId == fixture.Company.Id && category.Code == "1230");
            foreach (var (categoryId, debit, credit) in new[] { (cashInHand.Id, 100m, 0m), (shareCapital.Id, 0m, 100m) })
            {
                db.OpeningBalances.Add(new OpeningBalance
                {
                    PeriodId = fixture.Period.Id,
                    AccountCategoryId = categoryId,
                    Debit = debit,
                    Credit = credit,
                    SourceNote = "Legacy share capital take-on",
                    EnteredBy = "Reviewer",
                    Reviewed = true,
                    ReviewedBy = "Reviewer",
                    ReviewedAt = ReviewedAt
                });
            }
            await db.SaveChangesAsync();
            var period = fixture.Period;
            if (onlyInThePriorYear)
            {
                period = await AddNextPeriodAsync(db, fixture);
                var membersFunds = await db.AccountCategories.SingleAsync(category => category.CompanyId == fixture.Company.Id && category.Code == "3000");
                db.Adjustments.Add(Journal(period, "Legacy share capital transferred to members' funds", shareCapital.Id, membersFunds.Id, 100m));
                await db.SaveChangesAsync();
            }

            var xhtml = await IxbrlTextAsync(db, fixture.Company.Id, period.Id);

            Assert.Contains("<td>Share capital</td>", xhtml, StringComparison.Ordinal);
            var shareFacts = Facts(xhtml).Where(fact => fact.Concept == "core:CalledUpShareCapital").ToList();
            if (onlyInThePriorYear)
            {
                Assert.Equal([("instant", 0m), ("priorInstant", 100m)], shareFacts.Select(fact => (fact.Context, fact.Value)));
            }
            else
            {
                Assert.Equal([("instant", 100m)], shareFacts.Select(fact => (fact.Context, fact.Value)));
            }
            Assert.Contains("<td>Unrestricted and restricted funds</td>", xhtml, StringComparison.Ordinal);
            Assert.Contains("<td>Total members' funds</td>", xhtml, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    private static async Task<string> IxbrlTextAsync(AccountsDbContext db, int companyId, int periodId) =>
        Encoding.UTF8.GetString(await new IxbrlService(db, new FinancialStatementsService(db)).GenerateIxbrlAsync(companyId, periodId))
            .ReplaceLineEndings("\n");

    private static List<(string Concept, string Context, decimal Value)> Facts(string xhtml) =>
        Regex.Matches(xhtml, "<ix:nonFraction name=\"([^\"]+)\" contextRef=\"([^\"]+)\"[^>]*>([^<]+)</ix:nonFraction>")
            .Select(match => (match.Groups[1].Value, match.Groups[2].Value, decimal.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)))
            .ToList();
}
