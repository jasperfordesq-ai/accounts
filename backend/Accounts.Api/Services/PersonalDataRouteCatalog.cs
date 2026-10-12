using System.Text.RegularExpressions;

namespace Accounts.Api.Services;

/// <summary>
/// Server-side floor of the connector personal-data gate. The connector filters returned fields
/// per model; the API additionally refuses, for a <c>Withheld</c> connector session, the routes whose
/// whole payload is personal or unfilterable (user administration, privacy subject work, binary
/// artefacts) and writes that would set an identifying field.
/// </summary>
public static partial class PersonalDataRouteCatalog
{
    private static readonly IReadOnlySet<string> NoFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlySet<string> OfficerFields =
        new HashSet<string>(["address"], StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlySet<string> DirectorLoanFields =
        new HashSet<string>(["counterpartyName", "loanTerms", "reviewNote", "reviewedBy", "reviewerRole"], StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlySet<string> RelatedPartyFields =
        new HashSet<string>(["partyName", "terms"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Routes refused outright for a Withheld connector session.</summary>
    public static bool IsWithheldRoute(PathString path, string method)
    {
        if (path.StartsWithSegments("/api/admin/users", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.StartsWithSegments("/api/privacy", StringComparison.OrdinalIgnoreCase)) return true;
        return HttpMethods.IsGet(method) && IsBinaryArtefact(path);
    }

    /// <summary>Reads whose response carries personal data even after field filtering is possible.</summary>
    public static bool IsPersonalDataRead(PathString path, string method)
    {
        if (!HttpMethods.IsGet(method)) return false;
        if (IsWithheldRoute(path, method)) return true;
        var value = Normalize(path);
        return OfficersRoute().IsMatch(value)
            || DirectorLoansRoute().IsMatch(value)
            || RelatedPartyRoute().IsMatch(value)
            || value.EndsWith("/audit-log", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>JSON property names a Withheld connector session may not set on this route (any depth).</summary>
    public static IReadOnlySet<string> WithheldBodyFields(PathString path, string method)
    {
        if (!(HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method)))
            return NoFields;
        var value = Normalize(path);
        if (OfficersRoute().IsMatch(value) || value.Equals("/api/companies/onboard", StringComparison.OrdinalIgnoreCase))
            return OfficerFields;
        if (DirectorLoansRoute().IsMatch(value)) return DirectorLoanFields;
        if (RelatedPartyRoute().IsMatch(value)) return RelatedPartyFields;
        return NoFields;
    }

    public static bool IsBinaryArtefact(PathString path)
    {
        var value = Normalize(path);
        if (!value.StartsWith("/api/companies/", StringComparison.OrdinalIgnoreCase)) return false;
        return PeriodDocumentRoute().IsMatch(value)
            || value.EndsWith("/revenue/ixbrl", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/revenue/ixbrl/final", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/revenue/ct1-support/worksheet.csv", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/charity/sofa/review-pdf", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/charity/trustees-report/review-pdf", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/charity/sofa/final", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/charity/trustees-report/final", StringComparison.OrdinalIgnoreCase)
            || HandoffArtefactRoute().IsMatch(value)
            || HistoricEvidenceRoute().IsMatch(value)
            || DirectorsApprovalPageRoute().IsMatch(value);
    }

    private static string Normalize(PathString path) => (path.Value ?? "").TrimEnd('/');

    [GeneratedRegex(@"^/api/companies/\d+/officers(/\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex OfficersRoute();

    [GeneratedRegex(@"^/api/companies/\d+/periods/\d+/director-loans(/\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex DirectorLoansRoute();

    [GeneratedRegex(@"^/api/companies/\d+/periods/\d+/related-party-transactions(/\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex RelatedPartyRoute();

    [GeneratedRegex(@"^/api/companies/\d+/periods/\d+/documents/(accounts-package|agm-pack|cro-filing-pack|cro-filing-pack/final|signature-page|signature-page/final)$", RegexOptions.IgnoreCase)]
    private static partial Regex PeriodDocumentRoute();

    [GeneratedRegex(@"^/api/companies/\d+/periods/\d+/external-filing-handoff/snapshots/[0-9a-f-]+/artifact$", RegexOptions.IgnoreCase)]
    private static partial Regex HandoffArtefactRoute();

    [GeneratedRegex(@"^/api/companies/\d+/periods/\d+/classification/historic-anchor/archive/\d+/evidence$", RegexOptions.IgnoreCase)]
    private static partial Regex HistoricEvidenceRoute();

    [GeneratedRegex(@"^/api/companies/\d+/periods/\d+/filing/directors-approval-page/\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex DirectorsApprovalPageRoute();
}
