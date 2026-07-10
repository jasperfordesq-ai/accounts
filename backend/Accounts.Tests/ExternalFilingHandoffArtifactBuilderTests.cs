using System.Text;
using Accounts.Api.Services;
using Xunit;

namespace Accounts.Tests;

public sealed class ExternalFilingHandoffArtifactBuilderTests
{
    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string HashC = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    private const string HashD = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
    private static readonly DateTime PreparedAt = new(2026, 7, 10, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void CroArtifact_IsCanonicalAndDoesNotRetainProtectedIdentifiers()
    {
        var request = CroRequest();
        var reversed = request with
        {
            Fields = request.Fields.Reverse().ToList(),
            Attachments = request.Attachments.Reverse().ToList(),
            CroB1 = request.CroB1! with
            {
                Shareholders = request.CroB1.Shareholders.Reverse().ToList(),
                Allotments = request.CroB1.Allotments.Reverse().ToList()
            }
        };

        var first = ExternalFilingHandoffArtifactBuilder.BuildInitial(request);
        var second = ExternalFilingHandoffArtifactBuilder.BuildInitial(reversed);

        Assert.Equal(first.ArtifactSha256, second.ArtifactSha256);
        Assert.Equal(first.ArtifactBytes, second.ArtifactBytes);
        Assert.True(first.Document.ReadyForManualHandoff);
        Assert.False(first.Document.IsCompleteExternalReturn);
        Assert.False(first.Document.DirectSubmissionSupported);
        var json = Encoding.UTF8.GetString(first.ArtifactBytes);
        Assert.DoesNotContain("1234567A", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1980-01-01", json, StringComparison.Ordinal);
        Assert.Contains("protected identifier not retained", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"isCompleteExternalReturn\":false", json, StringComparison.Ordinal);
        Assert.Equal(first.ArtifactSha256, ExternalFilingHandoffArtifactBuilder.ComputeSha256(first.ArtifactBytes));
        Assert.All(first.Document.Sources, source => Assert.Equal(
            new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc),
            source.ReviewedAtUtc));
        Assert.Contains(first.Document.Sources, source => source.Code == "CRO-B1-FILING" && source.EffectiveDate.Contains("no published effective date", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AuthorityExpiryAndRevocation_AreHashBoundAndFailClosed()
    {
        var currentRequest = CroRequest();
        var current = ExternalFilingHandoffArtifactBuilder.BuildInitial(currentRequest);
        var expiredRequest = currentRequest with
        {
            Authority = currentRequest.Authority with { EffectiveUntilUtc = PreparedAt.AddMinutes(-1) }
        };
        var revokedRequest = currentRequest with
        {
            Authority = currentRequest.Authority with
            {
                Status = ExternalFilingAuthorityStatus.Revoked,
                RevokedAtUtc = PreparedAt.AddDays(-1)
            }
        };

        var expired = ExternalFilingHandoffArtifactBuilder.BuildInitial(expiredRequest);
        var revoked = ExternalFilingHandoffArtifactBuilder.BuildInitial(revokedRequest);

        Assert.NotEqual(current.Document.SourceFingerprintSha256, expired.Document.SourceFingerprintSha256);
        Assert.NotEqual(current.Document.SourceFingerprintSha256, revoked.Document.SourceFingerprintSha256);
        Assert.Contains(expired.Document.BlockingIssues, issue => issue.Contains("expired", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(revoked.Document.BlockingIssues, issue => issue.Contains("active", StringComparison.OrdinalIgnoreCase));
        Assert.Throws<BusinessRuleException>(() =>
            ExternalFilingHandoffArtifactBuilder.AssertReadyForExternalWorkflowAdvance(expired, PreparedAt));
        Assert.Throws<BusinessRuleException>(() =>
            ExternalFilingHandoffArtifactBuilder.AssertReadyForExternalWorkflowAdvance(revoked, PreparedAt));
    }

    [Fact]
    public void Amendment_BindsExactPredecessorAndNeverReusesItsIdentity()
    {
        var original = ExternalFilingHandoffArtifactBuilder.BuildInitial(CroRequest());
        var amendmentRequest = CroRequest() with { SnapshotId = Guid.Parse("22222222-2222-2222-2222-222222222222") };

        var amendment = ExternalFilingHandoffArtifactBuilder.BuildAmendment(
            amendmentRequest,
            original,
            "Correct shareholder holding after CRO send-back.");

        Assert.Equal(2, amendment.Document.Version);
        Assert.Equal(original.Document.SnapshotId, amendment.Document.SupersedesSnapshotId);
        Assert.Equal(original.ArtifactSha256, amendment.Document.SupersedesArtifactSha256);
        Assert.NotEqual(original.ArtifactSha256, amendment.ArtifactSha256);
        Assert.Contains(original.ArtifactSha256, Encoding.UTF8.GetString(amendment.ArtifactBytes), StringComparison.Ordinal);

        Assert.Throws<BusinessRuleException>(() =>
            ExternalFilingHandoffArtifactBuilder.BuildAmendment(
                CroRequest(),
                original,
                "Attempt to reuse the prior immutable identity."));

        var tampered = original with { ArtifactBytes = [.. original.ArtifactBytes, (byte)' '] };
        Assert.Throws<BusinessRuleException>(() =>
            ExternalFilingHandoffArtifactBuilder.BuildAmendment(
                amendmentRequest,
                tampered,
                "Attempt to link against tampered predecessor bytes."));
    }

    [Fact]
    public void Amendment_RejectsCrossTenantCompanyPeriodOrWorkflowLinks()
    {
        var original = ExternalFilingHandoffArtifactBuilder.BuildInitial(CroRequest());
        var request = CroRequest() with
        {
            SnapshotId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            CompanyId = 43,
            Authority = CroRequest().Authority with { CompanyId = 43 }
        };

        var error = Assert.Throws<BusinessRuleException>(() =>
            ExternalFilingHandoffArtifactBuilder.BuildAmendment(
                request,
                original,
                "Cross-company amendments must be rejected."));

        Assert.Contains("tenant, company, period and workflow", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RevenueArtifact_RemainsSupportOnlyAndSurfacesManualCompletionBlockers()
    {
        var request = RevenueRequest();
        var result = ExternalFilingHandoffArtifactBuilder.BuildInitial(request);

        Assert.False(result.Document.IsCompleteExternalReturn);
        Assert.False(result.Document.ReadyForManualHandoff);
        Assert.False(result.Document.DirectSubmissionSupported);
        Assert.Contains(result.Document.BlockingIssues, issue => issue.Contains("unsupported CT1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Document.ExternalCompletionWarnings, issue => issue.Contains("not a CT1 return", StringComparison.OrdinalIgnoreCase));

        var invalid = request with
        {
            RevenueCt1Support = request.RevenueCt1Support! with { IsCompleteCt1Return = true }
        };
        Assert.Throws<BusinessRuleException>(() => ExternalFilingHandoffArtifactBuilder.BuildInitial(invalid));
    }

    [Fact]
    public void OutcomeChronology_RequiresExactHashAuthorityAndPrecedingState()
    {
        var snapshot = ExternalFilingHandoffArtifactBuilder.BuildInitial(CroRequest());
        var ready = Outcome(ExternalFilingOutcomeKind.ReadyForManualHandoff, snapshot, PreparedAt.AddMinutes(1));
        ExternalFilingHandoffArtifactBuilder.ValidateOutcome(snapshot, ready, null, PreparedAt.AddMinutes(2));
        Assert.Null(ready.ExternalReference);
        Assert.Null(ready.ExternalOccurredAtUtc);
        Assert.Null(ready.EvidenceReference);
        Assert.Null(ready.EvidenceSha256);

        var fabricatedReady = ready with
        {
            ExternalReference = "INVENTED-EXTERNAL-REFERENCE",
            ExternalOccurredAtUtc = PreparedAt.AddMinutes(1),
            EvidenceReference = "Invented evidence",
            EvidenceSha256 = HashA
        };
        Assert.Throws<BusinessRuleException>(() =>
            ExternalFilingHandoffArtifactBuilder.ValidateOutcome(
                snapshot,
                fabricatedReady,
                null,
                PreparedAt.AddMinutes(2)));

        var submitted = Outcome(ExternalFilingOutcomeKind.ExternallySubmittedRecorded, snapshot, PreparedAt.AddMinutes(3));
        ExternalFilingHandoffArtifactBuilder.ValidateOutcome(
            snapshot,
            submitted,
            ExternalFilingOutcomeKind.ReadyForManualHandoff,
            PreparedAt.AddMinutes(4));

        var accepted = Outcome(ExternalFilingOutcomeKind.ExternallyAcceptedRecorded, snapshot, PreparedAt.AddMinutes(5));
        ExternalFilingHandoffArtifactBuilder.ValidateOutcome(
            snapshot,
            accepted,
            ExternalFilingOutcomeKind.ExternallySubmittedRecorded,
            PreparedAt.AddMinutes(6));

        Assert.Throws<BusinessRuleException>(() =>
            ExternalFilingHandoffArtifactBuilder.ValidateOutcome(
                snapshot,
                accepted,
                ExternalFilingOutcomeKind.ReadyForManualHandoff,
                PreparedAt.AddMinutes(6)));

        var wrongHash = submitted with { SnapshotArtifactSha256 = HashD };
        Assert.Throws<BusinessRuleException>(() =>
            ExternalFilingHandoffArtifactBuilder.ValidateOutcome(
                snapshot,
                wrongHash,
                ExternalFilingOutcomeKind.ReadyForManualHandoff,
                PreparedAt.AddMinutes(4)));
    }

    [Fact]
    public void Supersession_BindsTheExactNewSnapshotIdentityHashAndPredecessorLink()
    {
        var prior = ExternalFilingHandoffArtifactBuilder.BuildInitial(CroRequest());
        var successor = ExternalFilingHandoffArtifactBuilder.BuildAmendment(
            CroRequest() with { SnapshotId = Guid.Parse("22222222-2222-2222-2222-222222222222") },
            prior,
            "Correct shareholder holding after CRO send-back.");
        var superseded = new ExternalFilingOutcomeInput(
            ExternalFilingOutcomeKind.SupersededByAmendment,
            prior.Document.SnapshotId,
            prior.ArtifactSha256,
            null,
            null,
            null,
            null,
            null,
            null,
            successor.Document.SnapshotId,
            successor.ArtifactSha256);

        ExternalFilingHandoffArtifactBuilder.ValidateOutcome(
            prior,
            superseded,
            ExternalFilingOutcomeKind.CorrectionRequired,
            PreparedAt.AddHours(1),
            successor);

        Assert.Throws<BusinessRuleException>(() =>
            ExternalFilingHandoffArtifactBuilder.ValidateOutcome(
                prior,
                superseded with { SupersedingSnapshotArtifactSha256 = HashA },
                ExternalFilingOutcomeKind.CorrectionRequired,
                PreparedAt.AddHours(1),
                successor));
        Assert.Throws<BusinessRuleException>(() =>
            ExternalFilingHandoffArtifactBuilder.ValidateOutcome(
                prior,
                superseded,
                ExternalFilingOutcomeKind.CorrectionRequired,
                PreparedAt.AddHours(1)));
    }

    [Fact]
    public void OfficerEvidenceReferences_RejectObviousRawPpsnDobAndEmailValues()
    {
        foreach (var rawValue in new[] { "1234567A", "1980-01-01", "31/12/1980", "director@example.ie" })
        {
            var request = CroRequest();
            var officer = request.CroB1!.Officers[0];
            request = request with
            {
                CroB1 = request.CroB1 with
                {
                    Officers =
                    [
                        officer with { IdentityEvidenceReference = rawValue },
                        .. request.CroB1.Officers.Skip(1)
                    ]
                }
            };
            var identityError = Assert.Throws<BusinessRuleException>(() =>
                ExternalFilingHandoffArtifactBuilder.BuildInitial(request));
            Assert.Contains("opaque non-PII", identityError.Message, StringComparison.OrdinalIgnoreCase);

            request = CroRequest();
            officer = request.CroB1!.Officers[0];
            request = request with
            {
                CroB1 = request.CroB1 with
                {
                    Officers =
                    [
                        officer with { OtherDirectorshipsEvidenceReference = rawValue },
                        .. request.CroB1.Officers.Skip(1)
                    ]
                }
            };
            var directorshipError = Assert.Throws<BusinessRuleException>(() =>
                ExternalFilingHandoffArtifactBuilder.BuildInitial(request));
            Assert.Contains("opaque non-PII", directorshipError.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void MissingRequiredScalarField_IsAnExplicitBlocker()
    {
        var request = CroRequest();
        request = request with
        {
            Fields = request.Fields.Where(field => field.FieldCode != "b1.members.shareholders").ToList()
        };

        var snapshot = ExternalFilingHandoffArtifactBuilder.BuildInitial(request);

        Assert.False(snapshot.Document.ReadyForManualHandoff);
        Assert.Contains(snapshot.Document.BlockingIssues, issue => issue.Contains("b1.members.shareholders", StringComparison.Ordinal));
    }

    private static ExternalFilingHandoffBuildRequest CroRequest()
    {
        var actor = new ExternalFilingActor("user-1", "Aisling Accountant", "Accountant");
        var authority = new ExternalFilingAuthoritySnapshot(
            8,
            3,
            42,
            ExternalFilingWorkflow.CroB1,
            ExternalFilingAuthorityKind.CroElectronicFilingAgent,
            ExternalFilingAuthorityStatus.Active,
            "Example Filing Services Limited",
            "Example Filing Services",
            "EFA-****42",
            "Prepare and sign the B1 only; no authority to certify the financial statements",
            "ENG-2026-42",
            "B77-CORE-42",
            PreparedAt.AddMonths(-1),
            PreparedAt.AddMonths(3),
            null,
            HashA,
            "application/pdf",
            "b77-authority.pdf",
            new ExternalFilingActor("reviewer-1", "Niamh Reviewer", "Reviewer"),
            PreparedAt.AddDays(-2),
            "candidate-abc");
        var address = new ExternalHandoffAddress("1 Main Street", "Dublin 2", null, null, null, null, "D02 TEST");
        var facts = new B1ManualHandoffFacts(
            "123456",
            "Example Trading Limited",
            "Private company limited by shares",
            new DateOnly(2026, 9, 30),
            new DateOnly(2026, 9, 30),
            "RetainExistingAnnualReturnDate",
            address,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            true,
            false,
            true,
            null,
            "EUR",
            false,
            0m,
            "Board confirmation POL-2026-01",
            "Aoife Director",
            "Sean Secretary",
            [
                new B1OfficerHandoff(
                    4,
                    "Aoife",
                    "Director",
                    "Director",
                    new DateOnly(2020, 1, 2),
                    null,
                    address,
                    "PPSN",
                    "Protected identity vault record IDV-4",
                    HashB,
                    "presenter@example.invalid",
                    "Officer declaration ODR-4",
                    true),
                new B1OfficerHandoff(
                    5,
                    "Sean",
                    "Secretary",
                    "Secretary",
                    new DateOnly(2020, 1, 2),
                    null,
                    address,
                    "IPN",
                    "Protected identity vault record IDV-5",
                    HashC,
                    "presenter@example.invalid",
                    "Officer declaration ODR-5",
                    true)
            ],
            [new B1ShareClassHandoff("Ordinary", "EUR", 1m, 100, 100m, 100m, 0m)],
            [
                new B1ShareholderHandoff("MEM-2", "Beta Nominees Limited", address, "Ordinary", "EUR", 40, 40, "40 Ordinary", "Register of members row 2"),
                new B1ShareholderHandoff("MEM-1", "Alpha Holdings Limited", address, "Ordinary", "EUR", 60, 60, "60 Ordinary", "Register of members row 1")
            ],
            [
                new B1AllotmentHandoff("ALL-2", new DateOnly(2020, 2, 1), "Ordinary", "EUR", 40, 1m, 40m, "MEM-2", "B5 ALL-2"),
                new B1AllotmentHandoff("ALL-1", new DateOnly(2020, 1, 1), "Ordinary", "EUR", 60, 1m, 60m, "MEM-1", "B5 ALL-1")
            ],
            HashA,
            HashB,
            HashC);
        return new ExternalFilingHandoffBuildRequest(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            3,
            42,
            9,
            ExternalFilingWorkflow.CroB1,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            PreparedAt,
            actor,
            authority,
            HashD,
            "candidate-abc",
            facts,
            null,
            CompleteFields(ExternalFilingHandoffArtifactBuilder.RequiredCroFieldCodes),
            [
                new ExternalFilingAttachment("signature-page", "signature.pdf", "application/pdf", 120, HashB, "CroFilingPackage.SignaturePageArtifact"),
                new ExternalFilingAttachment("accounts-pdf", "accounts.pdf", "application/pdf", 1_200, HashA, "CroFilingPackage.AccountsPdfArtifact")
            ]);
    }

    private static ExternalFilingHandoffBuildRequest RevenueRequest()
    {
        var authority = CroRequest().Authority with
        {
            AuthorityId = 12,
            Workflow = ExternalFilingWorkflow.RevenueCt1Support,
            Kind = ExternalFilingAuthorityKind.RevenueRosAgent,
            MaskedPresenterOrTain = "TAIN-****42",
            AuthorityScope = "Corporation Tax return preparation and ROS filing",
            ExternalAuthorityReference = "ROS-LINK-42"
        };
        var support = new RevenueCt1SupportHandoffFacts(
            "Example Trading Limited",
            "1234567A",
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            ExternalFilingHandoffArtifactBuilder.Ct1SupportOutputKind,
            false,
            HashA,
            HashB,
            HashC,
            HashD,
            "VALIDATOR-42",
            12_500m,
            11_250m,
            1_250m,
            "QualifiedReviewRequired",
            [],
            ["Complete every unsupported CT1 panel in ROS", "Obtain final ROS calculation"]);
        return new ExternalFilingHandoffBuildRequest(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            3,
            42,
            9,
            ExternalFilingWorkflow.RevenueCt1Support,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            PreparedAt,
            new ExternalFilingActor("user-1", "Aisling Accountant", "Accountant"),
            authority,
            HashD,
            "candidate-abc",
            null,
            support,
            CompleteFields(ExternalFilingHandoffArtifactBuilder.RequiredRevenueSupportFieldCodes),
            [new ExternalFilingAttachment("ct1-support", "ct1-support.json", "application/json", 500, HashB, "CorporationTaxFilingSupport.Worksheet")]);
    }

    private static IReadOnlyList<ExternalHandoffField> CompleteFields(IReadOnlyList<string> codes) =>
        codes.Select(code => new ExternalHandoffField(
            code,
            code.Split('.')[0],
            code,
            code == "b1.officers.protected-identity-entry"
                ? "Confirmed entered in CORE; protected identifier not retained"
                : "Confirmed",
            ExternalHandoffFieldStatus.Complete,
            "Retained source evidence",
            null,
            code == "b1.officers.protected-identity-entry")).ToList();

    private static ExternalFilingOutcomeInput Outcome(
        ExternalFilingOutcomeKind kind,
        ExternalFilingHandoffBuild snapshot,
        DateTime occurredAt) => kind == ExternalFilingOutcomeKind.ReadyForManualHandoff
            ? new(
                kind,
                snapshot.Document.SnapshotId,
                snapshot.ArtifactSha256,
                null,
                null,
                null,
                null,
                null,
                null)
            : new(
                kind,
                snapshot.Document.SnapshotId,
                snapshot.ArtifactSha256,
                "CORE-EXTERNAL-42",
                occurredAt,
                null,
                null,
                "Retained external acknowledgement",
                HashC);
}
