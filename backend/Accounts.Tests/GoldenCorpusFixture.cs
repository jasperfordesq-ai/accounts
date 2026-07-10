using System.Security.Cryptography;
using System.Text.Json;
using Accounts.Api.Entities;
using Xunit;

namespace Accounts.Tests;

internal sealed record GoldenCorpusYear(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal Turnover,
    decimal BalanceSheetTotal,
    int AverageEmployees);

internal sealed record GoldenCorpusWorkflowFacts(
    decimal OpeningShareCapital,
    decimal CashReceipt,
    decimal ExpectedNetAssets,
    decimal ExpectedCorporationTax);

internal sealed record GoldenCorpusScenario(
    string Code,
    string LegalName,
    string CompanyType,
    string ElectedRegime,
    string ExpectedSizeClass,
    bool IsCharity,
    GoldenCorpusYear PriorYear,
    GoldenCorpusYear CurrentYear,
    GoldenCorpusWorkflowFacts WorkflowFacts,
    IReadOnlyList<string> ExpectedPdfPhrases,
    IReadOnlyList<string> ExpectedIxbrlPhrases)
{
    public CompanyType ParsedCompanyType => Enum.Parse<CompanyType>(CompanyType, ignoreCase: false);
    public ElectedRegime ParsedRegime => Enum.Parse<ElectedRegime>(ElectedRegime, ignoreCase: false);
    public CompanySizeClass ParsedSizeClass => Enum.Parse<CompanySizeClass>(ExpectedSizeClass, ignoreCase: false);
}

internal sealed record GoldenCorpusDocument(
    int SchemaVersion,
    string FixtureId,
    string ExpectationDerivation,
    string IndependentReviewStatus,
    string ExternalValidationStatus,
    IReadOnlyList<GoldenCorpusScenario> Scenarios);

internal static class GoldenCorpusFixture
{
    public const string RelativePath = "Fixtures/golden-corpus-independent-v1.json";
    public const string PinnedSha256 = "85a81475f636b75d304f3f33e6b445321fe8736d8a9feb90ab501f7d580e5477";

    private static readonly Lazy<(byte[] Bytes, GoldenCorpusDocument Document)> Loaded = new(Load);

    public static GoldenCorpusDocument Document => Loaded.Value.Document;
    public static byte[] Bytes => Loaded.Value.Bytes.ToArray();

    public static GoldenCorpusScenario Scenario(string code) =>
        Document.Scenarios.Single(item => string.Equals(item.Code, code, StringComparison.Ordinal));

    public static string ComputeSha256() =>
        Convert.ToHexStringLower(SHA256.HashData(Loaded.Value.Bytes));

    private static (byte[] Bytes, GoldenCorpusDocument Document) Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-corpus-independent-v1.json");
        var bytes = File.ReadAllBytes(path);
        var document = JsonSerializer.Deserialize<GoldenCorpusDocument>(bytes, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException($"Golden corpus fixture {path} is empty or invalid.");
        return (bytes, document);
    }
}

public sealed class GoldenCorpusFixtureIntegrityTests
{
    [Fact]
    public void ImmutableFixture_HasPinnedBytesIndependentArithmeticAndExplicitOpenHumanGates()
    {
        var fixture = GoldenCorpusFixture.Document;

        Assert.Equal(GoldenCorpusFixture.PinnedSha256, GoldenCorpusFixture.ComputeSha256());
        Assert.Equal(1, fixture.SchemaVersion);
        Assert.Equal("irish-statutory-golden-corpus-v1", fixture.FixtureId);
        Assert.Contains("outside the production services", fixture.ExpectationDerivation, StringComparison.Ordinal);
        Assert.Equal("pending-qualified-accountant", fixture.IndependentReviewStatus);
        Assert.Equal("pending-real-ros-validator", fixture.ExternalValidationStatus);
        Assert.Equal(
            ["clg-charity", "dac-small", "medium-audit-required", "micro-ltd", "small-abridged-ltd"],
            fixture.Scenarios.Select(item => item.Code).Order(StringComparer.Ordinal));

        foreach (var scenario in fixture.Scenarios)
        {
            // These checks intentionally use fixed arithmetic, not accounting-engine output.
            Assert.Equal(
                scenario.WorkflowFacts.OpeningShareCapital + scenario.WorkflowFacts.CashReceipt,
                scenario.WorkflowFacts.ExpectedNetAssets);
            Assert.Equal(
                decimal.Round(scenario.WorkflowFacts.CashReceipt * 0.125m, 2, MidpointRounding.AwayFromZero),
                scenario.WorkflowFacts.ExpectedCorporationTax);
            Assert.True(scenario.PriorYear.Turnover > 0);
            Assert.True(scenario.CurrentYear.Turnover > 0);
            Assert.Equal(scenario.PriorYear.PeriodEnd.AddDays(1), scenario.CurrentYear.PeriodStart);
            Assert.True(scenario.PriorYear.PeriodStart < scenario.PriorYear.PeriodEnd);
            Assert.True(scenario.CurrentYear.PeriodStart < scenario.CurrentYear.PeriodEnd);
            Assert.NotEmpty(scenario.ExpectedPdfPhrases);
            Assert.NotEmpty(scenario.ExpectedIxbrlPhrases);
            _ = scenario.ParsedCompanyType;
            _ = scenario.ParsedRegime;
            _ = scenario.ParsedSizeClass;
        }

        var raw = System.Text.Encoding.UTF8.GetString(GoldenCorpusFixture.Bytes);
        Assert.DoesNotContain("accepted", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("externalValidationReference", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("externalValidationArtifactSha256", raw, StringComparison.OrdinalIgnoreCase);
    }
}
