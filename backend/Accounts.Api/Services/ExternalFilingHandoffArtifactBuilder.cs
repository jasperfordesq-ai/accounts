using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Accounts.Api.Services;

/// <summary>
/// Pure, persistence-independent builder for exact manual-handoff bytes. It deliberately cannot
/// submit to CRO or Revenue and does not claim to produce a complete CT1 return.
/// </summary>
public static class ExternalFilingHandoffArtifactBuilder
{
    public const string SchemaVersion = "external-filing-handoff-v1";
    public const string Ct1SupportOutputKind = "corporation-tax-support-data-not-ct1-return";

    public static readonly IReadOnlyList<string> RequiredCroFieldCodes =
    [
        "b1.company.cro-number",
        "b1.company.legal-name",
        "b1.company.type",
        "b1.return.annual-return-date",
        "b1.return.made-up-to-date",
        "b1.return.ard-election",
        "b1.office.registered-office",
        "b1.accounts.financial-year-start",
        "b1.accounts.financial-year-end",
        "b1.accounts.annexed-or-exemption",
        "b1.accounts.audit-basis",
        "b1.officers.directors",
        "b1.officers.secretary",
        "b1.officers.protected-identity-entry",
        "b1.officers.other-directorships",
        "b1.capital.share-classes",
        "b1.members.shareholders",
        "b1.members.allotments",
        "b1.donations.political",
        "b1.presenter.identity",
        "b1.presenter.authority",
        "b1.signing.director",
        "b1.signing.secretary",
        "b1.attachments.accounts-pdf",
        "b1.attachments.signature-page"
    ];

    public static readonly IReadOnlyList<string> RequiredRevenueSupportFieldCodes =
    [
        "ct1.company.tax-reference",
        "ct1.period.start",
        "ct1.period.end",
        "ct1.support.output-kind",
        "ct1.support.calculation-hash",
        "ct1.support.worksheet-hash",
        "ct1.support.tax-due",
        "ct1.support.preliminary-tax",
        "ct1.support.balance-due",
        "ct1.ixbrl.artifact-hash",
        "ct1.ixbrl.external-validation",
        "ct1.agent.tain",
        "ct1.agent.engagement",
        "ct1.manual.unsupported-sections-reviewed"
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public static ExternalFilingHandoffBuild BuildInitial(ExternalFilingHandoffBuildRequest request) =>
        Build(request, version: 1, supersedesSnapshotId: null, supersedesArtifactSha256: null, amendmentReason: null);

    public static ExternalFilingHandoffBuild BuildAmendment(
        ExternalFilingHandoffBuildRequest request,
        ExternalFilingHandoffBuild predecessor,
        string amendmentReason)
    {
        ArgumentNullException.ThrowIfNull(predecessor);
        var reason = NormalizeRequired(amendmentReason, "Amendment reason", 10, 2_000);
        EnsureValidSha256(predecessor.ArtifactSha256, "Predecessor artifact SHA-256");
        if (!string.Equals(ComputeSha256(predecessor.ArtifactBytes), predecessor.ArtifactSha256, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("The predecessor artifact bytes do not match their retained SHA-256.");

        var prior = predecessor.Document;
        if (prior.TenantId != request.TenantId
            || prior.CompanyId != request.CompanyId
            || prior.PeriodId != request.PeriodId
            || prior.Workflow != request.Workflow)
        {
            throw new BusinessRuleException("An amendment must remain in the predecessor tenant, company, period and workflow.");
        }

        if (prior.SnapshotId == request.SnapshotId)
            throw new BusinessRuleException("An amendment requires a new snapshot identity; prior evidence is never rewritten.");

        return Build(
            request,
            checked(prior.Version + 1),
            prior.SnapshotId,
            predecessor.ArtifactSha256.ToLowerInvariant(),
            reason);
    }

    public static void AssertReadyForExternalWorkflowAdvance(
        ExternalFilingHandoffBuild snapshot,
        DateTime atUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!string.Equals(ComputeSha256(snapshot.ArtifactBytes), snapshot.ArtifactSha256, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("The handoff snapshot artifact bytes do not match their retained SHA-256.");
        if (!snapshot.Document.ReadyForManualHandoff)
            throw new BusinessRuleException($"The handoff snapshot is blocked: {string.Join("; ", snapshot.Document.BlockingIssues)}");

        var authorityIssue = AuthorityIssue(snapshot.Document.Authority, snapshot.Document, atUtc);
        if (authorityIssue is not null)
            throw new BusinessRuleException(authorityIssue);
    }

    public static void ValidateOutcome(
        ExternalFilingHandoffBuild snapshot,
        ExternalFilingOutcomeInput input,
        ExternalFilingOutcomeKind? previousOutcome,
        DateTime recordedAtUtc,
        ExternalFilingHandoffBuild? supersedingSnapshot = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(input);
        if (recordedAtUtc.Kind != DateTimeKind.Utc)
            throw new BusinessRuleException("The outcome recording timestamp must be UTC.");
        if (input.SnapshotId != snapshot.Document.SnapshotId)
            throw new BusinessRuleException("The external outcome references a different handoff snapshot.");
        EnsureValidSha256(input.SnapshotArtifactSha256, "Snapshot artifact SHA-256");
        if (!string.Equals(input.SnapshotArtifactSha256, snapshot.ArtifactSha256, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("The external outcome must bind the exact retained handoff artifact SHA-256.");

        switch (input.Outcome)
        {
            case ExternalFilingOutcomeKind.ReadyForManualHandoff:
                AssertReadyForExternalWorkflowAdvance(snapshot, recordedAtUtc);
                if (previousOutcome is not null)
                    throw new BusinessRuleException("Ready-for-handoff must be the first outcome for a new immutable snapshot.");
                EnsureNoExternalEvidence(input, "Internal readiness");
                EnsureNoSupersedingSnapshot(input);
                break;
            case ExternalFilingOutcomeKind.ExternallySubmittedRecorded:
            {
                var externalAt = RequireExternalEvidence(input, recordedAtUtc);
                AssertReadyForExternalWorkflowAdvance(snapshot, externalAt);
                if (previousOutcome != ExternalFilingOutcomeKind.ReadyForManualHandoff)
                    throw new BusinessRuleException("Record manual-handoff readiness before recording an external submission.");
                EnsureNoSupersedingSnapshot(input);
                break;
            }
            case ExternalFilingOutcomeKind.CorrectionRequired:
            case ExternalFilingOutcomeKind.ExternallyRejected:
            {
                var externalAt = RequireExternalEvidence(input, recordedAtUtc);
                if (previousOutcome != ExternalFilingOutcomeKind.ExternallySubmittedRecorded)
                    throw new BusinessRuleException("Only an externally submitted snapshot can be corrected or rejected.");
                NormalizeRequired(input.Reason, "Correction or rejection reason", 5, 2_000);
                if (input.Outcome == ExternalFilingOutcomeKind.CorrectionRequired
                    && (input.CorrectionDeadlineUtc is null
                        || input.CorrectionDeadlineUtc.Value.Kind != DateTimeKind.Utc
                        || input.CorrectionDeadlineUtc <= externalAt))
                {
                    throw new BusinessRuleException("A correction event requires a later UTC correction deadline.");
                }
                EnsureNoSupersedingSnapshot(input);
                break;
            }
            case ExternalFilingOutcomeKind.ExternallyAcceptedRecorded:
                RequireExternalEvidence(input, recordedAtUtc);
                if (previousOutcome != ExternalFilingOutcomeKind.ExternallySubmittedRecorded)
                    throw new BusinessRuleException("Only an externally submitted snapshot can be recorded as accepted.");
                EnsureNoSupersedingSnapshot(input);
                break;
            case ExternalFilingOutcomeKind.SupersededByAmendment:
                if (previousOutcome is not ExternalFilingOutcomeKind.CorrectionRequired and not ExternalFilingOutcomeKind.ExternallyRejected)
                    throw new BusinessRuleException("Only a corrected or rejected snapshot can be superseded by an amendment.");
                EnsureNoExternalEvidence(input, "Snapshot supersession");
                ValidateSupersedingSnapshot(snapshot, input, supersedingSnapshot);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input));
        }
    }

    private static DateTime RequireExternalEvidence(ExternalFilingOutcomeInput input, DateTime recordedAtUtc)
    {
        var externalReference = NormalizeRequired(input.ExternalReference, "External reference", 4, 500);
        EnsureOpaqueReference(externalReference, "External reference");
        var evidenceReference = NormalizeRequired(input.EvidenceReference, "External outcome evidence reference", 4, 1_000);
        EnsureOpaqueReference(evidenceReference, "External outcome evidence reference");
        EnsureValidSha256(input.EvidenceSha256, "External outcome evidence SHA-256");
        if (input.ExternalOccurredAtUtc is not { } externalAt
            || externalAt.Kind != DateTimeKind.Utc
            || externalAt > recordedAtUtc)
        {
            throw new BusinessRuleException("The external outcome timestamp must be UTC and cannot be in the future.");
        }
        return externalAt;
    }

    private static void EnsureNoExternalEvidence(ExternalFilingOutcomeInput input, string label)
    {
        if (!string.IsNullOrWhiteSpace(input.ExternalReference)
            || input.ExternalOccurredAtUtc is not null
            || !string.IsNullOrWhiteSpace(input.EvidenceReference)
            || !string.IsNullOrWhiteSpace(input.EvidenceSha256)
            || input.CorrectionDeadlineUtc is not null)
        {
            throw new BusinessRuleException($"{label} is an internal event and must not fabricate external references, timestamps or evidence.");
        }
    }

    private static void EnsureNoSupersedingSnapshot(ExternalFilingOutcomeInput input)
    {
        if (input.SupersedingSnapshotId is not null || !string.IsNullOrWhiteSpace(input.SupersedingSnapshotArtifactSha256))
            throw new BusinessRuleException("Only a supersession event may reference a successor snapshot.");
    }

    private static void ValidateSupersedingSnapshot(
        ExternalFilingHandoffBuild prior,
        ExternalFilingOutcomeInput input,
        ExternalFilingHandoffBuild? successor)
    {
        if (input.SupersedingSnapshotId is null)
            throw new BusinessRuleException("Snapshot supersession requires the new immutable snapshot identity.");
        EnsureValidSha256(input.SupersedingSnapshotArtifactSha256, "Superseding snapshot artifact SHA-256");
        if (successor is null)
            throw new BusinessRuleException("Snapshot supersession requires the exact retained successor artifact.");
        if (!string.Equals(ComputeSha256(successor.ArtifactBytes), successor.ArtifactSha256, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("The superseding snapshot artifact bytes do not match their retained SHA-256.");
        if (successor.Document.SnapshotId != input.SupersedingSnapshotId
            || !string.Equals(successor.ArtifactSha256, input.SupersedingSnapshotArtifactSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException("The supersession event must bind the exact new snapshot identity and artifact SHA-256.");
        }
        if (successor.Document.TenantId != prior.Document.TenantId
            || successor.Document.CompanyId != prior.Document.CompanyId
            || successor.Document.PeriodId != prior.Document.PeriodId
            || successor.Document.Workflow != prior.Document.Workflow
            || successor.Document.Version != prior.Document.Version + 1
            || successor.Document.SupersedesSnapshotId != prior.Document.SnapshotId
            || !string.Equals(successor.Document.SupersedesArtifactSha256, prior.ArtifactSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException("The superseding snapshot does not form the next exact link in the immutable handoff chain.");
        }
    }

    public static string ComputeSha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static void AssertOpaqueReference(string value, string label) =>
        EnsureOpaqueReference(value, label);

    public static ExternalFilingHandoffDocument ParseRetainedArtifact(byte[] bytes, string expectedSha256)
    {
        if (bytes is not { Length: > 0 })
            throw new BusinessRuleException("The retained handoff artifact is empty.");
        EnsureValidSha256(expectedSha256, "Retained handoff artifact SHA-256");
        if (!string.Equals(ComputeSha256(bytes), expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("The retained handoff artifact bytes do not match their SHA-256.");
        try
        {
            return JsonSerializer.Deserialize<ExternalFilingHandoffDocument>(bytes, SerializerOptions)
                ?? throw new BusinessRuleException("The retained handoff artifact JSON is empty.");
        }
        catch (JsonException exception)
        {
            throw new BusinessRuleException($"The retained handoff artifact JSON is invalid: {exception.Message}");
        }
    }

    private static ExternalFilingHandoffBuild Build(
        ExternalFilingHandoffBuildRequest request,
        int version,
        Guid? supersedesSnapshotId,
        string? supersedesArtifactSha256,
        string? amendmentReason)
    {
        ValidateInvariantInputs(request);
        var fields = request.Fields
            .OrderBy(field => field.FieldCode, StringComparer.Ordinal)
            .Select(NormalizeField)
            .ToList();
        if (fields.Select(field => field.FieldCode).Distinct(StringComparer.Ordinal).Count() != fields.Count)
            throw new BusinessRuleException("Handoff field codes must be unique within a snapshot.");

        var attachments = request.Attachments
            .OrderBy(attachment => attachment.Code, StringComparer.Ordinal)
            .Select(NormalizeAttachment)
            .ToList();
        if (attachments.Select(attachment => attachment.Code).Distinct(StringComparer.Ordinal).Count() != attachments.Count)
            throw new BusinessRuleException("Handoff attachment codes must be unique within a snapshot.");

        var blockers = CollectBlockers(request, fields);
        var warnings = CollectExternalCompletionWarnings(request, fields);
        var sources = SourcesFor(request.Workflow);
        var sourceFingerprint = ComputeSourceFingerprint(request, fields, attachments, sources);
        var document = new ExternalFilingHandoffDocument(
            SchemaVersion,
            request.SnapshotId,
            version,
            supersedesSnapshotId,
            supersedesArtifactSha256,
            amendmentReason,
            request.TenantId,
            request.CompanyId,
            request.PeriodId,
            request.Workflow,
            request.PeriodStart,
            request.PeriodEnd,
            request.PreparedAtUtc,
            NormalizeActor(request.PreparedBy),
            NormalizeAuthority(request.Authority),
            request.QualifiedReviewManifestSha256.ToLowerInvariant(),
            request.ReleaseCandidate.Trim(),
            DirectSubmissionSupported: false,
            // A CRO B1 artifact is still a manual CORE worksheet, never an official external return.
            // ReadyForManualHandoff is the only readiness assertion made by this builder.
            IsCompleteExternalReturn: false,
            ReadyForManualHandoff: blockers.Count == 0,
            sourceFingerprint,
            NormalizeCro(request.CroB1),
            NormalizeRevenue(request.RevenueCt1Support),
            fields,
            attachments,
            blockers,
            warnings,
            sources);
        var bytes = Canonicalize(document);
        return new ExternalFilingHandoffBuild(document, bytes, ComputeSha256(bytes));
    }

    private static void ValidateInvariantInputs(ExternalFilingHandoffBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SnapshotId == Guid.Empty)
            throw new BusinessRuleException("A non-empty immutable snapshot identity is required.");
        if (request.TenantId <= 0 || request.CompanyId <= 0 || request.PeriodId <= 0)
            throw new BusinessRuleException("Tenant, company and period identifiers must be positive.");
        if (request.PeriodStart > request.PeriodEnd)
            throw new BusinessRuleException("The handoff period start cannot be after its end.");
        if (request.PreparedAtUtc.Kind != DateTimeKind.Utc)
            throw new BusinessRuleException("The handoff prepared timestamp must be UTC.");
        NormalizeActor(request.PreparedBy);
        EnsureValidSha256(request.QualifiedReviewManifestSha256, "Qualified-review manifest SHA-256");
        NormalizeRequired(request.ReleaseCandidate, "Release candidate", 3, 200);

        var authority = request.Authority;
        if (authority.TenantId != request.TenantId
            || authority.CompanyId != request.CompanyId
            || authority.Workflow != request.Workflow)
        {
            throw new BusinessRuleException("Authority evidence must belong to the handoff tenant, company and workflow.");
        }

        if (request.Workflow == ExternalFilingWorkflow.CroB1)
        {
            if (request.CroB1 is null || request.RevenueCt1Support is not null)
                throw new BusinessRuleException("A CRO B1 snapshot requires only CRO B1 facts.");
            if (authority.Kind is not ExternalFilingAuthorityKind.CroPresenter and not ExternalFilingAuthorityKind.CroElectronicFilingAgent)
                throw new BusinessRuleException("A CRO B1 snapshot requires CRO presenter authority.");
            ValidateCro(request.CroB1, request.PeriodStart, request.PeriodEnd);
        }
        else
        {
            if (request.RevenueCt1Support is null || request.CroB1 is not null)
                throw new BusinessRuleException("A Revenue snapshot requires only CT1 support facts.");
            if (authority.Kind != ExternalFilingAuthorityKind.RevenueRosAgent)
                throw new BusinessRuleException("A Revenue CT1 support snapshot requires ROS agent authority.");
            ValidateRevenue(request.RevenueCt1Support, request.PeriodStart, request.PeriodEnd);
        }
    }

    private static List<string> CollectBlockers(
        ExternalFilingHandoffBuildRequest request,
        IReadOnlyList<ExternalHandoffField> fields)
    {
        var blockers = new List<string>();
        var authorityIssue = AuthorityIssue(request.Authority, request, request.PreparedAtUtc);
        if (authorityIssue is not null)
            blockers.Add(authorityIssue);

        var required = request.Workflow == ExternalFilingWorkflow.CroB1
            ? RequiredCroFieldCodes
            : RequiredRevenueSupportFieldCodes;
        var byCode = fields.ToDictionary(field => field.FieldCode, StringComparer.Ordinal);
        foreach (var code in required)
        {
            if (!byCode.TryGetValue(code, out var field))
            {
                blockers.Add($"Required handoff field '{code}' is absent.");
                continue;
            }

            if (field.Status is ExternalHandoffFieldStatus.Missing or ExternalHandoffFieldStatus.RequiresReview)
                blockers.Add(field.BlockingReason ?? $"{field.Label} is not complete.");
            else if (field.Status == ExternalHandoffFieldStatus.ProtectedManualEntry)
                blockers.Add(field.BlockingReason ?? $"{field.Label} requires protected manual entry in the external service.");
        }

        if (request.Workflow == ExternalFilingWorkflow.RevenueCt1Support)
        {
            var support = request.RevenueCt1Support!;
            blockers.AddRange(support.SupportBlockingReasons.Where(reason => !string.IsNullOrWhiteSpace(reason)).Select(reason => reason.Trim()));
            if (support.ManualCt1CompletionItems.Count > 0)
                blockers.Add("A qualified accountant must complete the unsupported CT1 sections in ROS before any external filing outcome is recorded.");
        }

        return blockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }

    private static IReadOnlyList<string> CollectExternalCompletionWarnings(
        ExternalFilingHandoffBuildRequest request,
        IReadOnlyList<ExternalHandoffField> fields)
    {
        var warnings = fields
            .Where(field => field.Status == ExternalHandoffFieldStatus.ProtectedManualEntry)
            .Select(field => $"Protected external entry: {field.Label}.")
            .ToList();
        if (request.Workflow == ExternalFilingWorkflow.RevenueCt1Support)
        {
            warnings.Add("This artifact is corporation-tax support only; it is not a CT1 return and cannot be submitted to ROS.");
            warnings.AddRange(request.RevenueCt1Support!.ManualCt1CompletionItems.Select(item => $"Manual CT1 completion: {item.Trim()}"));
        }
        else
        {
            warnings.Add("This artifact is a manual CORE worksheet only; the platform does not submit a B1.");
        }
        return warnings.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }

    private static string ComputeSourceFingerprint(
        ExternalFilingHandoffBuildRequest request,
        IReadOnlyList<ExternalHandoffField> fields,
        IReadOnlyList<ExternalFilingAttachment> attachments,
        IReadOnlyList<ExternalFilingSourceReference> sources)
    {
        var source = new
        {
            SchemaVersion,
            request.TenantId,
            request.CompanyId,
            request.PeriodId,
            request.Workflow,
            request.PeriodStart,
            request.PeriodEnd,
            Authority = NormalizeAuthority(request.Authority),
            QualifiedReviewManifestSha256 = request.QualifiedReviewManifestSha256.ToLowerInvariant(),
            ReleaseCandidate = request.ReleaseCandidate.Trim(),
            CroB1 = NormalizeCro(request.CroB1),
            RevenueCt1Support = NormalizeRevenue(request.RevenueCt1Support),
            Fields = fields,
            Attachments = attachments,
            Sources = sources
        };
        return ComputeSha256(Canonicalize(source));
    }

    private static byte[] Canonicalize<T>(T value)
    {
        var node = JsonSerializer.SerializeToNode(value, SerializerOptions)
            ?? throw new InvalidOperationException("The handoff artifact could not be serialized.");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            WriteCanonical(node, writer);
        return stream.ToArray();
    }

    private static void WriteCanonical(JsonNode? node, Utf8JsonWriter writer)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject obj:
                writer.WriteStartObject();
                foreach (var property in obj.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Key);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonArray array:
                writer.WriteStartArray();
                foreach (var item in array)
                    WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            default:
                node.WriteTo(writer, SerializerOptions);
                break;
        }
    }

    private static ExternalHandoffField NormalizeField(ExternalHandoffField field)
    {
        var code = NormalizeRequired(field.FieldCode, "Field code", 3, 150).ToLowerInvariant();
        var section = NormalizeRequired(field.Section, "Field section", 2, 200);
        var label = NormalizeRequired(field.Label, "Field label", 2, 300);
        var source = NormalizeRequired(field.SourceReference, "Field source reference", 2, 1_000);
        EnsureOpaqueReference(source, $"Field '{code}' source reference");
        if (field.IsProtectedManualEntry
            && field.Status is not ExternalHandoffFieldStatus.ProtectedManualEntry and not ExternalHandoffFieldStatus.Complete)
        {
            throw new BusinessRuleException($"Protected field '{code}' must be pending protected entry or a safe completion confirmation.");
        }
        if (field.Status == ExternalHandoffFieldStatus.Complete && string.IsNullOrWhiteSpace(field.Value))
            throw new BusinessRuleException($"Completed handoff field '{code}' requires a scalar display value.");
        if (field.IsProtectedManualEntry
            && field.Status == ExternalHandoffFieldStatus.Complete
            && (field.Value is null
                || !field.Value.Contains("confirmed", StringComparison.OrdinalIgnoreCase)
                || field.Value.Any(char.IsDigit)))
        {
            throw new BusinessRuleException(
                $"Completed protected field '{code}' may retain only a non-identifying confirmation with no digits.");
        }
        return field with
        {
            FieldCode = code,
            Section = section,
            Label = label,
            Value = NormalizeOptional(field.Value, 4_000),
            SourceReference = source,
            BlockingReason = NormalizeOptional(field.BlockingReason, 2_000)
        };
    }

    private static ExternalFilingAttachment NormalizeAttachment(ExternalFilingAttachment attachment)
    {
        if (attachment.ByteSize <= 0)
            throw new BusinessRuleException($"Attachment '{attachment.Code}' must retain a positive byte size.");
        EnsureValidSha256(attachment.Sha256, $"Attachment '{attachment.Code}' SHA-256");
        var sourceReference = NormalizeRequired(attachment.SourceReference, "Attachment source reference", 2, 1_000);
        EnsureOpaqueReference(sourceReference, $"Attachment '{attachment.Code}' source reference");
        return attachment with
        {
            Code = NormalizeRequired(attachment.Code, "Attachment code", 2, 100).ToLowerInvariant(),
            FileName = NormalizeRequired(attachment.FileName, "Attachment file name", 1, 255),
            MediaType = NormalizeRequired(attachment.MediaType, "Attachment media type", 3, 100).ToLowerInvariant(),
            Sha256 = attachment.Sha256.ToLowerInvariant(),
            SourceReference = sourceReference
        };
    }

    private static ExternalFilingActor NormalizeActor(ExternalFilingActor actor) => new(
        NormalizeRequired(actor.UserId, "Actor user ID", 1, 200),
        NormalizeRequired(actor.DisplayName, "Actor display name", 2, 200),
        NormalizeRequired(actor.Role, "Actor role", 2, 100));

    private static ExternalFilingAuthoritySnapshot NormalizeAuthority(ExternalFilingAuthoritySnapshot authority)
    {
        EnsureValidSha256(authority.AuthorityEvidenceSha256, "Authority evidence SHA-256");
        EnsureOpaqueReference(authority.EngagementReference, "Engagement reference");
        EnsureOpaqueReference(authority.ExternalAuthorityReference, "External authority reference");
        return authority with
        {
            LegalName = NormalizeRequired(authority.LegalName, "Authority legal name", 2, 300),
            PracticeName = NormalizeOptional(authority.PracticeName, 300),
            MaskedPresenterOrTain = NormalizeMaskedIdentifier(authority.MaskedPresenterOrTain),
            AuthorityScope = NormalizeRequired(authority.AuthorityScope, "Authority scope", 3, 1_000),
            EngagementReference = NormalizeRequired(authority.EngagementReference, "Engagement reference", 3, 500),
            ExternalAuthorityReference = NormalizeRequired(authority.ExternalAuthorityReference, "External authority reference", 3, 500),
            AuthorityEvidenceSha256 = authority.AuthorityEvidenceSha256.ToLowerInvariant(),
            EvidenceMediaType = NormalizeRequired(authority.EvidenceMediaType, "Authority evidence media type", 3, 100).ToLowerInvariant(),
            EvidenceFileName = NormalizeRequired(authority.EvidenceFileName, "Authority evidence file name", 1, 255),
            ReviewedBy = NormalizeActor(authority.ReviewedBy),
            ReleaseCandidate = NormalizeRequired(authority.ReleaseCandidate, "Authority release candidate", 3, 200)
        };
    }

    private static B1ManualHandoffFacts? NormalizeCro(B1ManualHandoffFacts? facts) => facts is null ? null : facts with
    {
        Officers = facts.Officers.OrderBy(officer => officer.Role, StringComparer.Ordinal).ThenBy(officer => officer.LastName, StringComparer.Ordinal).ThenBy(officer => officer.OfficerId).ToList(),
        ShareClasses = facts.ShareClasses.OrderBy(item => item.Currency, StringComparer.Ordinal).ThenBy(item => item.ShareClass, StringComparer.Ordinal).ToList(),
        Shareholders = facts.Shareholders.OrderBy(item => item.MemberReference, StringComparer.Ordinal).ThenBy(item => item.ShareClass, StringComparer.Ordinal).ToList(),
        Allotments = facts.Allotments.OrderBy(item => item.AllotmentDate).ThenBy(item => item.AllotmentReference, StringComparer.Ordinal).ToList(),
        AccountsPdfSha256 = facts.AccountsPdfSha256.ToLowerInvariant(),
        SignaturePageSha256 = facts.SignaturePageSha256.ToLowerInvariant(),
        ShareholdersListPdfSha256 = facts.ShareholdersListPdfSha256?.ToLowerInvariant()
    };

    private static RevenueCt1SupportHandoffFacts? NormalizeRevenue(RevenueCt1SupportHandoffFacts? facts) => facts is null ? null : facts with
    {
        CalculationSha256 = facts.CalculationSha256.ToLowerInvariant(),
        WorksheetArtifactSha256 = facts.WorksheetArtifactSha256.ToLowerInvariant(),
        IxbrlArtifactSha256 = facts.IxbrlArtifactSha256?.ToLowerInvariant(),
        ExternalValidationEvidenceSha256 = facts.ExternalValidationEvidenceSha256?.ToLowerInvariant(),
        SupportBlockingReasons = facts.SupportBlockingReasons.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
        ManualCt1CompletionItems = facts.ManualCt1CompletionItems.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList()
    };

    private static void ValidateCro(B1ManualHandoffFacts facts, DateOnly periodStart, DateOnly periodEnd)
    {
        NormalizeRequired(facts.CroNumber, "CRO number", 1, 30);
        NormalizeRequired(facts.LegalName, "Company legal name", 2, 300);
        NormalizeRequired(facts.CompanyType, "Company type", 2, 100);
        if (facts.MadeUpToDate > facts.AnnualReturnDate)
            throw new BusinessRuleException("The B1 made-up-to date cannot be after the recorded annual return date.");
        if (facts.FinancialYearStart != periodStart || facts.FinancialYearEnd != periodEnd)
            throw new BusinessRuleException("B1 financial-year dates must match the accounting period exactly.");
        if (facts.PoliticalDonationsAmount < 0)
            throw new BusinessRuleException("Political donations cannot be negative.");
        EnsureOpaqueReference(facts.PoliticalDonationsEvidenceReference, "Political-donations evidence reference");
        if (facts.AuditorReference is not null)
            EnsureOpaqueReference(facts.AuditorReference, "Auditor reference");
        EnsureValidSha256(facts.AccountsPdfSha256, "CRO accounts PDF SHA-256");
        EnsureValidSha256(facts.SignaturePageSha256, "CRO signature page SHA-256");
        if (facts.ShareholdersListPdfSha256 is not null)
            EnsureValidSha256(facts.ShareholdersListPdfSha256, "CRO shareholders-list PDF SHA-256");
        if (facts.Officers.Select(officer => officer.OfficerId).Distinct().Count() != facts.Officers.Count)
            throw new BusinessRuleException("B1 officer IDs must be unique.");
        if (facts.Shareholders.Select(item => item.MemberReference).Distinct(StringComparer.Ordinal).Count() != facts.Shareholders.Count)
            throw new BusinessRuleException("B1 member references must be unique.");
        if (facts.Allotments.Select(item => item.AllotmentReference).Distinct(StringComparer.Ordinal).Count() != facts.Allotments.Count)
            throw new BusinessRuleException("B1 allotment references must be unique.");
        foreach (var officer in facts.Officers)
        {
            EnsureValidSha256(officer.IdentityEvidenceSha256, $"Officer {officer.OfficerId} identity evidence SHA-256");
            EnsureOpaqueReference(officer.IdentityEvidenceReference, $"Officer {officer.OfficerId} identity evidence reference");
            EnsureOpaqueReference(officer.OtherDirectorshipsEvidenceReference, $"Officer {officer.OfficerId} other-directorships evidence reference");
            if (!officer.ProtectedIdentifierEntryRequired)
                throw new BusinessRuleException("Officer protected identifier entry must remain an explicit manual CORE step.");
        }
        foreach (var shareholder in facts.Shareholders)
        {
            EnsureOpaqueReference(shareholder.MemberReference, "Shareholder member reference");
            EnsureOpaqueReference(shareholder.EvidenceReference, $"Shareholder {shareholder.MemberReference} evidence reference");
        }
        foreach (var allotment in facts.Allotments)
        {
            EnsureOpaqueReference(allotment.AllotmentReference, "Allotment reference");
            EnsureOpaqueReference(allotment.AllotteeMemberReference, $"Allotment {allotment.AllotmentReference} member reference");
            EnsureOpaqueReference(allotment.EvidenceReference, $"Allotment {allotment.AllotmentReference} evidence reference");
        }
    }

    private static void ValidateRevenue(RevenueCt1SupportHandoffFacts facts, DateOnly periodStart, DateOnly periodEnd)
    {
        if (facts.PeriodStart != periodStart || facts.PeriodEnd != periodEnd)
            throw new BusinessRuleException("CT1 support dates must match the accounting period exactly.");
        if (!string.Equals(facts.OutputKind, Ct1SupportOutputKind, StringComparison.Ordinal))
            throw new BusinessRuleException("Revenue handoff must retain the bounded CT1-support output discriminator.");
        if (facts.IsCompleteCt1Return)
            throw new BusinessRuleException("The platform must not claim that bounded corporation-tax support is a complete CT1 return.");
        EnsureValidSha256(facts.CalculationSha256, "Tax calculation SHA-256");
        EnsureValidSha256(facts.WorksheetArtifactSha256, "Tax worksheet artifact SHA-256");
        if (facts.IxbrlArtifactSha256 is not null)
            EnsureValidSha256(facts.IxbrlArtifactSha256, "iXBRL artifact SHA-256");
        if (facts.ExternalValidationEvidenceSha256 is not null)
            EnsureValidSha256(facts.ExternalValidationEvidenceSha256, "External validation evidence SHA-256");
        if (facts.ExternalValidationReference is not null)
            EnsureOpaqueReference(facts.ExternalValidationReference, "External validation reference");
    }

    private static string? AuthorityIssue(
        ExternalFilingAuthoritySnapshot authority,
        object document,
        DateTime atUtc)
    {
        _ = document;
        if (atUtc.Kind != DateTimeKind.Utc)
            return "Authority must be assessed at a UTC timestamp.";
        if (authority.Status != ExternalFilingAuthorityStatus.Active)
            return "Current active presenter/agent authority is required before external workflow advancement.";
        if (authority.RevokedAtUtc is not null && authority.RevokedAtUtc <= atUtc)
            return "Presenter/agent authority was revoked before the attempted external workflow advancement.";
        if (authority.EffectiveFromUtc.Kind != DateTimeKind.Utc || authority.EffectiveFromUtc > atUtc)
            return "Presenter/agent authority is not yet effective at the attempted external workflow timestamp.";
        if (authority.EffectiveUntilUtc is { } until
            && (until.Kind != DateTimeKind.Utc || until < atUtc))
            return "Presenter/agent authority expired before the attempted external workflow advancement.";
        if (authority.ReviewedAtUtc.Kind != DateTimeKind.Utc || authority.ReviewedAtUtc > atUtc)
            return "Presenter/agent authority lacks a current prior UTC review.";
        if (!string.Equals(authority.ReleaseCandidate, ExtractReleaseCandidate(document), StringComparison.Ordinal))
            return "Presenter/agent authority was reviewed against a different release candidate.";
        return null;
    }

    private static string ExtractReleaseCandidate(object document) => document switch
    {
        ExternalFilingHandoffBuildRequest request => request.ReleaseCandidate,
        ExternalFilingHandoffDocument snapshot => snapshot.ReleaseCandidate,
        _ => throw new ArgumentOutOfRangeException(nameof(document))
    };

    private static IReadOnlyList<ExternalFilingSourceReference> SourcesFor(ExternalFilingWorkflow workflow)
    {
        var reviewedAtUtc = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        var common = new List<ExternalFilingSourceReference>
        {
            new(
                "CRO-B1-FILING",
                "CRO — Filing an Annual Return",
                "https://cro.ie/annual-return/filing-an-annual-return/",
                "B1 content, manual CORE filing, signatures, shareholder upload and correction/send-back process",
                "Current guidance; no published effective date",
                reviewedAtUtc),
            new(
                "CRO-B1-IDENTITY",
                "CRO — Form B1 Identity Requirements",
                "https://cro.ie/services-and-help/core/core-help/form-b1-identity-requirements/",
                "Protected director identity fields and CORE identity checks",
                "Current guidance; no published effective date",
                reviewedAtUtc)
        };
        if (workflow == ExternalFilingWorkflow.RevenueCt1Support)
        {
            common.Add(new(
                "REVENUE-AGENT-LINK",
                "Revenue — Guidelines for Agent e-linking",
                "https://www.revenue.ie/en/tax-professionals/tdm/income-tax-capital-gains-tax-corporation-tax/part-37/37-00-04c-20250313121038.pdf",
                "TAIN DigiCert, client approval, expiry, revocation and non-digital client authority",
                "February 2025",
                reviewedAtUtc));
        }
        return common;
    }

    private static string NormalizeMaskedIdentifier(string? value)
    {
        var normalized = NormalizeOptional(value, 100);
        if (normalized is null)
            return string.Empty;
        if (!normalized.Contains('*', StringComparison.Ordinal) && normalized.Any(char.IsDigit))
            throw new BusinessRuleException("Presenter/TAIN identifiers exposed in handoff JSON must be masked.");
        return normalized;
    }

    private static void EnsureOpaqueReference(string? value, string label)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (Regex.IsMatch(normalized, @"(?<![A-Za-z0-9])\d{7}[A-Za-z]{1,2}(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || Regex.IsMatch(normalized, @"\b(?:19|20)\d{2}-\d{1,2}-\d{1,2}\b", RegexOptions.CultureInvariant)
            || Regex.IsMatch(normalized, @"\b\d{1,2}[/-]\d{1,2}[/-](?:19|20)\d{2}\b", RegexOptions.CultureInvariant)
            || Regex.IsMatch(normalized, @"\b[^\s@]+@[^\s@]+\.[^\s@]+\b", RegexOptions.CultureInvariant))
        {
            throw new BusinessRuleException($"{label} must be an opaque non-PII record reference, not a raw PPSN, date of birth or email address.");
        }
    }

    private static string NormalizeRequired(string? value, string label, int minLength, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length < minLength || normalized.Length > maxLength)
            throw new BusinessRuleException($"{label} must be between {minLength} and {maxLength} characters.");
        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
            return null;
        if (normalized.Length > maxLength)
            throw new BusinessRuleException($"A handoff value exceeds the {maxLength}-character limit.");
        return normalized;
    }

    private static void EnsureValidSha256(string? value, string label)
    {
        if (value is null || value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new BusinessRuleException($"{label} must be a 64-character hexadecimal SHA-256.");
    }
}
