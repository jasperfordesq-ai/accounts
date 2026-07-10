namespace Accounts.Api.Rules;

/// <summary>
/// Controller-approved privacy retention policy. Statutory accounting/audit evidence is governed
/// separately by the six-year floor and may not be shortened through configuration.
/// </summary>
public sealed class PrivacyGovernanceConfig
{
    public int LoginSecurityEventRetentionDays { get; set; } = 30;
    public int TerminalIdentityArtifactRetentionHours { get; set; } = 24;
    public int UsedRecoveryCodeRetentionDays { get; set; } = 30;
    public int SubjectRequestMetadataRetentionYears { get; set; } = 3;
    public int StatutoryRecordMinimumYears { get; set; } = 6;
    public int RetentionWorkerIntervalHours { get; set; } = 24;

    public static bool IsValid(PrivacyGovernanceConfig value) =>
        value.LoginSecurityEventRetentionDays is >= 7 and <= 180
        && value.TerminalIdentityArtifactRetentionHours is >= 1 and <= 168
        && value.UsedRecoveryCodeRetentionDays is >= 1 and <= 180
        && value.SubjectRequestMetadataRetentionYears is >= 1 and <= 6
        && value.StatutoryRecordMinimumYears >= 6
        && value.StatutoryRecordMinimumYears <= 12
        && value.RetentionWorkerIntervalHours is >= 1 and <= 168;
}

public static class PrivacyRecordKinds
{
    public const string LoginSecurityEvent = "login-security-event";
    public const string FinancialAuditEvidence = "financial-audit-evidence";
    public const string FilingApprovalEvidence = "filing-approval-evidence";
    public const string IdentityLifecycleEvidence = "identity-lifecycle-evidence";
}

public static class PrivacyRequestKinds
{
    public const string AccessExport = "access-export";
    public const string ErasureReview = "erasure-review";
}

public static class PrivacyRequestStates
{
    public const string Requested = "requested";
    public const string Completed = "completed";
    public const string PartiallyCompletedStatutoryOverride = "partially-completed-statutory-override";
    public const string Refused = "refused";
}
