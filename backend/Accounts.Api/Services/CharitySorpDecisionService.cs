using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Accounts.Api.Entities;

namespace Accounts.Api.Services;

public sealed record CharitySorpSource(
    string SourceId,
    string Title,
    string Url,
    string? DocumentSha256,
    string Basis);

public sealed record CharitySorpDecision(
    string FrameworkCode,
    string FrameworkTitle,
    DateOnly EffectiveFrom,
    int? Tier,
    string SofaBasis,
    bool AutomatedArtifactsSupported,
    bool ManualProfessionalHandoffRequired,
    string DecisionReason,
    IReadOnlyList<CharitySorpSource> Sources,
    string DecisionSha256);

/// <summary>
/// Effective-dated, fail-closed SORP routing for Republic of Ireland charity accounts.
/// This service decides only what the platform has implemented and evidenced. It does not
/// purport to decide whether an unsupported charity can or should apply a different framework.
/// </summary>
public sealed class CharitySorpDecisionService
{
    public const string Sorp2026Framework = "SORP-2026-FRS102";
    public const string Sorp2019Framework = "SORP-2019-FRS102";
    public const string Sorp2026DocumentSha256 = "7814211b25ac1305d98f805d3b272564ce1f2d92a4930c5b17d47a674ae70f3a";
    public static readonly DateOnly Sorp2026EffectiveFrom = new(2026, 1, 1);

    public static readonly CharitySorpSource Sorp2026Source = new(
        "charities-sorp-2026",
        "Charities SORP 2026 (FRS 102)",
        "https://www.charitysorp.org/documents/d/guest/charities-sorp-2026-1",
        Sorp2026DocumentSha256,
        "Paragraph 21 fixes the effective date; paragraphs 35-38 set annual gross-income tiers; Appendix 3 A.5/A.9 describes the Republic of Ireland company/non-company position.");

    public static readonly CharitySorpSource Sorp2019Source = new(
        "charities-sorp-2019",
        "Charities SORP (FRS 102), second edition 2019",
        "https://charitiessorp.org/download-a-full-sorp/",
        null,
        "The official SORP download page routes periods starting before 1 January 2026 to the 2019 second edition.");

    public static CharitySorpDecision Decide(
        DateOnly periodStart,
        CompanyType companyType,
        string? charityType,
        decimal grossIncome)
    {
        var framework = periodStart >= Sorp2026EffectiveFrom ? Sorp2026Framework : Sorp2019Framework;
        var source = periodStart >= Sorp2026EffectiveFrom ? Sorp2026Source : Sorp2019Source;
        int? tier = periodStart >= Sorp2026EffectiveFrom && grossIncome >= 0m
            ? Determine2026Tier(grossIncome)
            : null;
        var basis = tier is >= 2 ? "activity" : tier == 1 ? "natural-or-activity" : "undetermined";

        string reason;
        var supported = true;

        if (grossIncome < 0m)
        {
            supported = false;
            reason = "Gross income cannot be negative, so the SORP tier is indeterminate.";
        }
        else if (!IsSupportedCompanyCharity(companyType, charityType))
        {
            supported = false;
            reason = "Automated charity artifacts are limited to an explicitly identified Republic of Ireland company charity (CLG). Non-company or indeterminate charity forms require manual professional handoff.";
        }
        else if (framework == Sorp2019Framework)
        {
            supported = false;
            reason = "The period falls under SORP 2019. This release has not implemented and source-verified the pre-2026 artifact framework, so manual professional handoff is required.";
        }
        else if (tier is >= 2)
        {
            supported = false;
            reason = "SORP 2026 tiers 2 and 3 require an activity-basis SoFA. The platform does not yet retain the required activity allocation evidence, so manual professional handoff is required.";
        }
        else
        {
            reason = "SORP 2026 Tier 1 applies and the supported Republic of Ireland CLG artifact path is available, subject to reconciliation and retained human evidence.";
        }

        var unsigned = new
        {
            framework,
            EffectiveFrom = framework == Sorp2026Framework ? Sorp2026EffectiveFrom : new DateOnly(2019, 1, 1),
            tier,
            basis,
            supported,
            reason,
            source.SourceId,
            source.Url,
            source.DocumentSha256
        };
        var decisionHash = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(unsigned))));

        return new CharitySorpDecision(
            framework,
            source.Title,
            unsigned.EffectiveFrom,
            tier,
            basis,
            supported,
            !supported,
            reason,
            [source],
            decisionHash);
    }

    public static int Determine2026Tier(decimal grossIncome)
    {
        if (grossIncome < 0m)
            throw new BusinessRuleException("Gross income cannot be negative when determining a SORP tier.");

        return grossIncome switch
        {
            <= 500_000m => 1,
            <= 15_000_000m => 2,
            _ => 3
        };
    }

    private static bool IsSupportedCompanyCharity(CompanyType companyType, string? charityType)
    {
        if (companyType != CompanyType.CompanyLimitedByGuarantee || string.IsNullOrWhiteSpace(charityType))
            return false;

        var normalized = new string(charityType
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
        return normalized is "CLG" or "COMPANYLIMITEDBYGUARANTEE" or "COMPANYCHARITY";
    }
}
