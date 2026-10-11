using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Accounts.Api.Entities;

namespace Accounts.Api.Services;

public static class DirectorsApprovalIntegrity
{
    public static bool IsValid(DirectorsApprovalPage page) =>
        string.Equals(ComputePageHash(page), page.RecordSha256, StringComparison.OrdinalIgnoreCase);

    public static bool IsValid(DirectorsApprovalEvent approvalEvent) =>
        string.Equals(ComputeEventHash(approvalEvent), approvalEvent.RecordSha256, StringComparison.OrdinalIgnoreCase);

    public static string ComputePageHash(DirectorsApprovalPage page) => Hash(
        "directors-approval-page-v1",
        page.PeriodId.ToString(CultureInfo.InvariantCulture),
        page.ApprovalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        page.FirstDirectorOfficerId.ToString(CultureInfo.InvariantCulture),
        page.SecondDirectorOfficerId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        page.FirstDirectorName,
        page.SecondDirectorName ?? string.Empty,
        page.DirectorsInOfficeCount.ToString(CultureInfo.InvariantCulture),
        page.ResolutionReference,
        page.AccountsPdfSha256,
        page.ArtifactSourceFingerprintSha256,
        page.PageSha256,
        page.GeneratedByUserId,
        page.GeneratedByDisplayName,
        page.GeneratedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

    public static string ComputeEventHash(DirectorsApprovalEvent e) => Hash(
        "directors-approval-event-v1",
        e.PeriodId.ToString(CultureInfo.InvariantCulture),
        e.Kind.ToString(),
        e.ApprovalPageId.ToString(CultureInfo.InvariantCulture),
        e.ApprovalPageSha256,
        e.SignedPageSha256 ?? string.Empty,
        e.ResolutionSha256 ?? string.Empty,
        e.RevocationReason ?? string.Empty,
        e.RecordedByUserId,
        e.RecordedByDisplayName,
        e.RecordedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        e.PreviousRecordSha256 ?? string.Empty);

    public static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    private static string Hash(params string[] parts) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', parts))));
}
