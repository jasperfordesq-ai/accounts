using System.Text.Json;
using System.Text.Json.Serialization;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Xunit;

namespace Accounts.Tests;

/// <summary>
/// Response-contract regressions. The API serialises with WhenWritingNull, so a null member is
/// omitted, not sent as null, and a frontend contract that expects the key then rejects the whole
/// response. These tests serialise with the API's own options so an absent-key defect shows here.
/// </summary>
public partial class AccountsWorkflowTests
{
    internal static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web)
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void ApiJsonMirror_MatchesProgramSerializerOptions()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Program.cs"));
        Assert.Contains("DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull", program);
        Assert.Contains("ReferenceHandler = ReferenceHandler.IgnoreCycles", program);
        Assert.Contains("Converters.Add(new JsonStringEnumConverter())", program);
    }

    // Each type's frontend contract declares these members as nullable-but-required. Under
    // WhenWritingNull an unannotated null member vanished from the response and the whole payload
    // failed its contract (found by the 2026-10-10 sweep that followed the payroll defect).
    [Theory]
    [InlineData(typeof(ImportService.ImportResult), new[] { "importBatchId" })]
    [InlineData(typeof(CharitySorpSource), new[] { "documentSha256" })]
    [InlineData(typeof(CharitySorpDecision), new[] { "tier" })]
    [InlineData(typeof(CharityArtifactStatusResponse), new[] { "package" })]
    [InlineData(typeof(CharityArtifactPackageView), new[]
    {
        "sofaSha256", "trusteesReportSha256", "artifactReleaseCandidate", "artifactSourceFingerprintSha256",
        "sorpFrameworkCode", "sorpTier", "sofaBasis", "charityNumberSnapshot", "sofaClosingFunds",
        "balanceSheetNetAssets", "reconciliationDifference", "reconciledAtUtc", "trusteeReviewReference",
        "trusteeReviewedBy", "trusteeReviewedAtUtc", "trusteeReviewArtifactSha256", "trusteePopulationSha256",
        "manualProfessionalHandoffReason", "approvedBy", "approvedAt", "approvedArtifactManifestSha256",
        "approvedReleaseCandidate"
    })]
    [InlineData(typeof(UserAdministrationSummary), new[] { "lockedUntilUtc", "inviteAcceptedAtUtc", "deactivatedAtUtc", "offboardedAtUtc" })]
    [InlineData(typeof(AccountantWorkingPaperService.SourceReference), new[] { "amount", "evidenceReference", "reviewedBy", "reviewedAtUtc" })]
    [InlineData(typeof(AccountantWorkingPaperService.CategorizedTransactionRow), new[]
    {
        "importBatchId", "importFilename", "sourceFileSha256", "categoryId", "categoryCode", "categoryName",
        "confidenceScore", "duplicateDecisionBy", "duplicateDecisionAtUtc"
    })]
    public void NullableRequiredResponseMembers_AreWrittenAsNull(Type responseType, string[] members)
    {
        var instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(responseType);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(instance, responseType, ApiJson));

        foreach (var member in members)
        {
            Assert.True(json.RootElement.TryGetProperty(member, out var value), $"{responseType.Name}.{member} was omitted.");
            Assert.Equal(JsonValueKind.Null, value.ValueKind);
        }
    }

    [Fact]
    public void YearEndPayroll_WithoutPayroll_IsExplicitNotOmitted()
    {
        // Live defect 2026-10-10: a charity with no employees has no payroll record.
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            new { payroll = YearEndPayrollSummary.From(null) },
            ApiJson));

        var payroll = json.RootElement.GetProperty("payroll");
        Assert.False(payroll.GetProperty("hasPayroll").GetBoolean());
        Assert.False(payroll.TryGetProperty("grossWages", out _));
        Assert.False(payroll.TryGetProperty("staffCount", out _));
    }

    [Fact]
    public void YearEndPayroll_WithPayroll_ReportsWagesAndStaff()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            new { payroll = YearEndPayrollSummary.From(new PayrollSummary { GrossWages = 1_200m, StaffCount = 1 }) },
            ApiJson));

        var payroll = json.RootElement.GetProperty("payroll");
        Assert.True(payroll.GetProperty("hasPayroll").GetBoolean());
        Assert.Equal(1_200m, payroll.GetProperty("grossWages").GetDecimal());
        Assert.Equal(1, payroll.GetProperty("staffCount").GetInt32());
    }

}
