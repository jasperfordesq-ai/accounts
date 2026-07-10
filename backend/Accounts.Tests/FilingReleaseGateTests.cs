using System.Text;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Accounts.Api.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig;
using Xunit;

namespace Accounts.Tests;

public sealed class FilingReleaseGateTests
{
    static FilingReleaseGateTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Fact]
    public async Task ReviewArtifacts_AreConspicuouslyMarked_AndFinalExportIsBlockedBeforeApproval()
    {
        await using var db = CreateDbContext();
        var period = await SeedReleaseReadyPeriodAsync(db);
        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);
        var ixbrl = new IxbrlService(db, statements);
        var gate = new FilingReleaseGate(db, "candidate-a");

        var reviewPdf = await documents.GenerateCroFilingReviewPackAsync(period.CompanyId, period.Id);
        var reviewIxbrl = Encoding.UTF8.GetString(await ixbrl.GenerateReviewIxbrlAsync(period.CompanyId, period.Id));
        Assert.Contains("DRAFT — NOT FOR FILING", ExtractPdfText(reviewPdf));
        Assert.Contains("DRAFT - NOT FOR FILING - INCOMPLETE REVIEW PROTOTYPE", reviewIxbrl, StringComparison.Ordinal);
        Assert.Contains("data-generation-support=\"manual-handoff-only\"", reviewIxbrl, StringComparison.Ordinal);

        await gate.RecordCroArtifactAsync(
            period.CompanyId,
            period.Id,
            FilingReleaseArtifact.CroAccountsPdf,
            await documents.GenerateCroFilingPackAsync(period.CompanyId, period.Id));
        await gate.RecordCroArtifactAsync(
            period.CompanyId,
            period.Id,
            FilingReleaseArtifact.CroSignaturePage,
            await documents.GenerateSignaturePageAsync(period.CompanyId, period.Id));

        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            gate.GetFinalArtifactAsync(period.CompanyId, period.Id, FilingReleaseArtifact.CroAccountsPdf));
        Assert.Contains("qualified-accountant", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CroApproval_BindsExactManifest_AndChangedBytesRevokeFinalEligibility()
    {
        await using var db = CreateDbContext();
        var period = await SeedReleaseReadyPeriodAsync(db);
        var audit = new AuditService(db);
        var gate = new FilingReleaseGate(db, "candidate-a", audit);

        await gate.RecordCroArtifactAsync(
            period.CompanyId,
            period.Id,
            FilingReleaseArtifact.CroAccountsPdf,
            Encoding.UTF8.GetBytes("clean-cro-accounts-v1"),
            "reviewer@example.ie");
        var package = await gate.RecordCroArtifactAsync(
            period.CompanyId,
            period.Id,
            FilingReleaseArtifact.CroSignaturePage,
            Encoding.UTF8.GetBytes("clean-cro-signature-v1"),
            "reviewer@example.ie");
        await gate.RecordVerifiedCroSignatureAsync(
            period.CompanyId,
            period.Id,
            SignatureEvidence(),
            "reviewer@example.ie");

        await gate.BindVerifiedApprovalAsync(
            period.CompanyId,
            period.Id,
            FilingReleaseWorkflow.Cro,
            ApprovalEvidence(period, FilingReleaseWorkflow.Cro),
            "reviewer@example.ie");
        var released = await gate.GetFinalArtifactAsync(
            period.CompanyId,
            period.Id,
            FilingReleaseArtifact.CroAccountsPdf);
        Assert.Equal("clean-cro-accounts-v1", Encoding.UTF8.GetString(released.Content));
        Assert.Equal(FilingReleaseGate.ComputeSha256(released.Content), released.Sha256);

        var company = await db.Companies.SingleAsync(item => item.Id == period.CompanyId);
        var originalLegalName = company.LegalName;
        company.LegalName = "Changed Legal Name Limited";
        await db.SaveChangesAsync();
        var staleLegalEvidence = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            gate.GetFinalArtifactAsync(period.CompanyId, period.Id, FilingReleaseArtifact.CroAccountsPdf));
        Assert.Contains("legal, tax or accounting source evidence changed", staleLegalEvidence.Message);
        company.LegalName = originalLegalName;
        await db.SaveChangesAsync();

        var taxBalance = await db.TaxBalances.SingleAsync(item => item.PeriodId == period.Id);
        taxBalance.Liability += 1m;
        taxBalance.Balance += 1m;
        await db.SaveChangesAsync();
        var staleTaxEvidence = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            gate.GetFinalArtifactAsync(period.CompanyId, period.Id, FilingReleaseArtifact.CroAccountsPdf));
        Assert.Contains("legal, tax or accounting source evidence changed", staleTaxEvidence.Message);
        taxBalance.Liability -= 1m;
        taxBalance.Balance -= 1m;
        await db.SaveChangesAsync();

        package.AccountsPdfArtifact![0] ^= 0x01;
        await db.SaveChangesAsync();
        var mismatch = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            gate.GetFinalArtifactAsync(period.CompanyId, period.Id, FilingReleaseArtifact.CroAccountsPdf, "reviewer@example.ie"));
        Assert.True(
            mismatch.Message.Contains("do not match", StringComparison.OrdinalIgnoreCase)
            || mismatch.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(await db.AuditLogs.ToListAsync(), entry =>
            entry.Action == AuditEventCodes.FilingReleaseRejected
            && entry.CompanyId == period.CompanyId
            && entry.PeriodId == period.Id);

        package = await db.CroFilingPackages.SingleAsync(p => p.PeriodId == period.Id);
        package.AccountsPdfArtifact = Encoding.UTF8.GetBytes("clean-cro-accounts-v1");
        package.AccountsPdfSha256 = FilingReleaseGate.ComputeSha256(package.AccountsPdfArtifact);
        await db.SaveChangesAsync();
        await gate.RecordCroArtifactAsync(
            period.CompanyId,
            period.Id,
            FilingReleaseArtifact.CroAccountsPdf,
            Encoding.UTF8.GetBytes("clean-cro-accounts-v2"));

        package = await db.CroFilingPackages.SingleAsync(p => p.PeriodId == period.Id);
        Assert.Null(package.ApprovedArtifactManifestSha256);
        Assert.Null(package.ApprovedBy);
        await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            gate.GetFinalArtifactAsync(period.CompanyId, period.Id, FilingReleaseArtifact.CroAccountsPdf));
    }

    [Fact]
    public async Task Revenue_ExactExternalEvidenceStillCannotBypassDisabledFilingReadyGeneration()
    {
        await using var db = CreateDbContext();
        var period = await SeedReleaseReadyPeriodAsync(db);
        var gate = new FilingReleaseGate(db, "candidate-a");
        var clean = Encoding.UTF8.GetBytes("<html>clean retained ixbrl</html>");
        var package = await gate.RecordRevenueIxbrlArtifactAsync(period.CompanyId, period.Id, clean);
        package.IxbrlValidationErrors = "Internal checks passed. External ROS/iXBRL validation is still required before Revenue filing.";
        package.IxbrlValidated = true;
        await db.SaveChangesAsync();

        var disabledBeforeExternalEvidence = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            gate.BindVerifiedApprovalAsync(
                period.CompanyId,
                period.Id,
                FilingReleaseWorkflow.Revenue,
                ApprovalEvidence(period, FilingReleaseWorkflow.Revenue)));
        Assert.Equal(RevenueIxbrlGenerationPolicy.ManualHandoffReason, disabledBeforeExternalEvidence.Message);

        var freeForm = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            gate.RecordExternalRevenueValidationAsync(
                period.CompanyId,
                period.Id,
                package.IxbrlSha256!,
                "ROS-VALIDATION-001"));
        Assert.Contains("free-form", freeForm.Message, StringComparison.OrdinalIgnoreCase);

        var wrongArtifactEvidence = ExternalValidationEvidence(package.IxbrlSha256!);
        wrongArtifactEvidence = wrongArtifactEvidence with { IxbrlArtifactSha256 = new string('a', 64) };
        await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            gate.RecordVerifiedExternalRevenueValidationAsync(
                period.CompanyId,
                period.Id,
                wrongArtifactEvidence));

        await gate.RecordVerifiedExternalRevenueValidationAsync(
            period.CompanyId,
            period.Id,
            ExternalValidationEvidence(package.IxbrlSha256!));
        var disabledAfterExactEvidence = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            gate.BindVerifiedApprovalAsync(
                period.CompanyId,
                period.Id,
                FilingReleaseWorkflow.Revenue,
                ApprovalEvidence(period, FilingReleaseWorkflow.Revenue)));
        Assert.Equal(RevenueIxbrlGenerationPolicy.ManualHandoffReason, disabledAfterExactEvidence.Message);

        var finalExportBlocked = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() => gate.GetFinalArtifactAsync(
            period.CompanyId,
            period.Id,
            FilingReleaseArtifact.RevenueIxbrl));
        Assert.Equal(RevenueIxbrlGenerationPolicy.ManualHandoffReason, finalExportBlocked.Message);
    }

    [Theory]
    [InlineData(FilingStatus.Submitted)]
    [InlineData(FilingStatus.Accepted)]
    public async Task Revenue_SubmissionAndAcceptanceTransitionsRemainDisabled(FilingStatus targetStatus)
    {
        await using var db = CreateDbContext();
        var period = await SeedReleaseReadyPeriodAsync(db);
        var gate = new FilingReleaseGate(db, "candidate-a");

        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            gate.AssertTransitionAsync(
                period.CompanyId,
                period.Id,
                FilingReleaseWorkflow.Revenue,
                targetStatus,
                "ROS-REFERENCE-001"));

        Assert.Equal(RevenueIxbrlGenerationPolicy.ManualHandoffReason, error.Message);
    }

    [Fact]
    public async Task Revenue_FiledTransitionRemainsDisabledEvenWithAReference()
    {
        await using var db = CreateDbContext();
        var period = await SeedReleaseReadyPeriodAsync(db);
        var gate = new FilingReleaseGate(db, "candidate-a");

        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            gate.AssertCanRecordFiledAsync(
                period.CompanyId,
                period.Id,
                FilingReleaseWorkflow.Revenue,
                "ROS-REFERENCE-001"));

        Assert.Equal(RevenueIxbrlGenerationPolicy.ManualHandoffReason, error.Message);
    }

    [Fact]
    public async Task AccountantApproval_RejectsExpiredUnqualifiedSelfDeclaredWrongScopeDecisionAndCrossTenantEvidence()
    {
        await using var db = CreateDbContext();
        var period = await SeedReleaseReadyPeriodAsync(db);
        var otherTenant = new Tenant { Name = "Other Practice", Slug = Guid.NewGuid().ToString("N") };
        db.Tenants.Add(otherTenant);
        await db.SaveChangesAsync();
        var gate = new FilingReleaseGate(db, "candidate-a");
        await gate.RecordCroArtifactAsync(period.CompanyId, period.Id, FilingReleaseArtifact.CroAccountsPdf, [1, 2, 3]);
        await gate.RecordCroArtifactAsync(period.CompanyId, period.Id, FilingReleaseArtifact.CroSignaturePage, [4, 5, 6]);
        await gate.RecordVerifiedCroSignatureAsync(period.CompanyId, period.Id, SignatureEvidence());

        var current = ApprovalEvidence(period, FilingReleaseWorkflow.Cro);
        var invalidEvidence = new[]
        {
            current with { CredentialValidUntilUtc = DateTime.UtcNow.AddMinutes(-1) },
            current with { Capacity = "bookkeeper" },
            current with { ProfessionalBody = "Self-declared practice" },
            current with { Scope = "revenue-final-filing" },
            current with { Decision = "recommended" },
            current with { TenantId = otherTenant.Id }
        };

        foreach (var evidence in invalidEvidence)
        {
            await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
                gate.BindVerifiedApprovalAsync(
                    period.CompanyId,
                    period.Id,
                    FilingReleaseWorkflow.Cro,
                    evidence));
        }

        var package = await db.CroFilingPackages.SingleAsync(p => p.PeriodId == period.Id);
        Assert.Null(package.ApprovedBy);
        Assert.Null(package.ApprovedArtifactManifestSha256);
    }

    [Fact]
    public async Task AuditRequiredRelease_AttachesExactSignedOpinionAndNeverReleasesTheTemplateAsFinal()
    {
        await using var db = CreateDbContext();
        var period = await SeedReleaseReadyPeriodAsync(db, auditRequired: true);
        var gate = new FilingReleaseGate(db, "candidate-a");

        var missingReport = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            gate.RecordCroArtifactAsync(
                period.CompanyId,
                period.Id,
                FilingReleaseArtifact.CroAccountsPdf,
                PdfArtifact("unsigned accounts base")));
        Assert.Contains("signed-opinion PDF bytes", missingReport.Message, StringComparison.OrdinalIgnoreCase);

        var signedOpinion = PdfArtifact("ACTUAL SIGNED INDEPENDENT AUDITOR OPINION");
        await gate.RecordVerifiedAuditorReportAsync(
            period.CompanyId,
            period.Id,
            new AuditorReportEvidence(
                "Independent Audit Firm",
                "Named Statutory Auditor",
                "Chartered Accountants Ireland",
                "auditor-member-456",
                "AUDITOR-REPORT-2025-001",
                DateTime.UtcNow.AddMinutes(-10),
                "Named Qualified Accountant",
                DateTime.UtcNow.AddMinutes(-5),
                "accepted",
                signedOpinion,
                FilingReleaseGate.ComputeSha256(signedOpinion)));

        var statements = new FinancialStatementsService(db);
        var documents = new DocumentGeneratorService(db, statements);
        var accountsBase = await documents.GenerateCroFilingPackAsync(period.CompanyId, period.Id);
        var signaturePage = await documents.GenerateSignaturePageAsync(period.CompanyId, period.Id);
        var package = await gate.RecordCroArtifactAsync(
            period.CompanyId,
            period.Id,
            FilingReleaseArtifact.CroAccountsPdf,
            accountsBase);
        await gate.RecordCroArtifactAsync(
            period.CompanyId,
            period.Id,
            FilingReleaseArtifact.CroSignaturePage,
            signaturePage);
        await gate.RecordVerifiedCroSignatureAsync(period.CompanyId, period.Id, SignatureEvidence());
        await gate.BindVerifiedApprovalAsync(
            period.CompanyId,
            period.Id,
            FilingReleaseWorkflow.Cro,
            ApprovalEvidence(period, FilingReleaseWorkflow.Cro));

        var released = await gate.GetFinalArtifactAsync(
            period.CompanyId,
            period.Id,
            FilingReleaseArtifact.CroAccountsPdf);

        Assert.Equal(period.AuditorsReportSha256, package.AttachedAuditorReportSha256);
        Assert.Equal(package.AccountsPdfSha256, released.Sha256);
        Assert.DoesNotContain("TEMPLATE — to be completed and signed", ExtractPdfText(released.Content));
        using var finalPdf = PdfDocument.Open(released.Content);
        Assert.True(finalPdf.Advanced.TryGetEmbeddedFiles(out var embeddedFiles));
        var embedded = Assert.Single(embeddedFiles);
        Assert.Equal("signed-auditor-report.pdf", embedded.Name);
        Assert.Equal(signedOpinion, embedded.Bytes.ToArray());

    }

    [Fact]
    public async Task DifferentReleaseCandidateCannotExportPreviouslyApprovedArtifacts()
    {
        await using var db = CreateDbContext();
        var period = await SeedReleaseReadyPeriodAsync(db);
        var firstGate = new FilingReleaseGate(db, "candidate-a");
        await firstGate.RecordCroArtifactAsync(period.CompanyId, period.Id, FilingReleaseArtifact.CroAccountsPdf, [1, 2, 3]);
        var package = await firstGate.RecordCroArtifactAsync(period.CompanyId, period.Id, FilingReleaseArtifact.CroSignaturePage, [4, 5, 6]);
        await firstGate.RecordVerifiedCroSignatureAsync(period.CompanyId, period.Id, SignatureEvidence());
        await firstGate.BindVerifiedApprovalAsync(period.CompanyId, period.Id, FilingReleaseWorkflow.Cro, ApprovalEvidence(period, FilingReleaseWorkflow.Cro));

        var nextGate = new FilingReleaseGate(db, "candidate-b");
        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            nextGate.GetFinalArtifactAsync(period.CompanyId, period.Id, FilingReleaseArtifact.CroAccountsPdf));
        Assert.Contains("different release candidate", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExactCroEvidence_AllowsBoundSubmittedAcceptedAndFiledChecks()
    {
        await using var db = CreateDbContext();
        var period = await SeedReleaseReadyPeriodAsync(db);
        var gate = new FilingReleaseGate(db, "candidate-a");
        await gate.RecordCroArtifactAsync(period.CompanyId, period.Id, FilingReleaseArtifact.CroAccountsPdf, [1, 2, 3]);
        await gate.RecordCroArtifactAsync(period.CompanyId, period.Id, FilingReleaseArtifact.CroSignaturePage, [4, 5, 6]);
        await gate.RecordVerifiedCroSignatureAsync(period.CompanyId, period.Id, SignatureEvidence());
        await gate.BindVerifiedApprovalAsync(
            period.CompanyId,
            period.Id,
            FilingReleaseWorkflow.Cro,
            ApprovalEvidence(period, FilingReleaseWorkflow.Cro));

        var package = await db.CroFilingPackages.SingleAsync(p => p.PeriodId == period.Id);
        package.FilingStatus = FilingStatus.Approved;
        await db.SaveChangesAsync();
        await gate.AssertTransitionAsync(
            period.CompanyId,
            period.Id,
            FilingReleaseWorkflow.Cro,
            FilingStatus.Submitted,
            "CORE-EXACT-001");

        package.FilingStatus = FilingStatus.Submitted;
        package.CroSubmissionReference = "CORE-EXACT-001";
        package.PaymentCompleted = true;
        await db.SaveChangesAsync();
        await gate.AssertTransitionAsync(
            period.CompanyId,
            period.Id,
            FilingReleaseWorkflow.Cro,
            FilingStatus.Accepted);

        package.FilingStatus = FilingStatus.Accepted;
        await db.SaveChangesAsync();
        await gate.AssertCanRecordFiledAsync(
            period.CompanyId,
            period.Id,
            FilingReleaseWorkflow.Cro,
            "CORE-EXACT-001");

        await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            gate.AssertCanRecordFiledAsync(
                period.CompanyId,
                period.Id,
                FilingReleaseWorkflow.Cro,
                "CORE-DIFFERENT"));
        Assert.Equal(FilingStatus.Accepted, package.FilingStatus);
    }

    [Fact]
    public async Task CharityGenerationBooleansCannotReplaceRetainedExactArtifacts()
    {
        await using var db = CreateDbContext();
        var period = await SeedReleaseReadyPeriodAsync(db, charity: true);
        db.CharityFilingPackages.Add(new CharityFilingPackage
        {
            PeriodId = period.Id,
            SofaGenerated = true,
            TrusteesReportGenerated = true,
            FilingStatus = FilingStatus.PackageGenerated
        });
        await db.SaveChangesAsync();
        var gate = new FilingReleaseGate(db, "candidate-a");

        var booleanOnly = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            gate.BindVerifiedApprovalAsync(period.CompanyId, period.Id, FilingReleaseWorkflow.Charity, ApprovalEvidence(period, FilingReleaseWorkflow.Charity)));
        Assert.Contains("artifacts", booleanOnly.Message, StringComparison.OrdinalIgnoreCase);

        var reporting = new CharityReportingService(db);
        var package = await reporting.RecordTrusteeReviewAsync(
            period.CompanyId,
            period.Id,
            accepted: true,
            evidenceReference: "TRUSTEE-MINUTE-2026-01",
            evidenceArtifact: Encoding.UTF8.GetBytes("signed trustee population review"),
            reviewer: "Named Reviewer");
        var balanceSheet = await new FinancialStatementsService(db).GetBalanceSheetAsync(period.CompanyId, period.Id);
        var evidence = await reporting.BuildArtifactEvidenceAsync(
            period.CompanyId,
            period.Id,
            balanceSheet.NetAssets,
            package);
        var pdf = new CharityPdfService();
        await gate.RecordCharityArtifactAsync(
            period.CompanyId,
            period.Id,
            FilingReleaseArtifact.CharitySofa,
            pdf.GenerateSofa(evidence, reviewCopy: false),
            evidence: evidence);
        await gate.RecordCharityArtifactAsync(
            period.CompanyId,
            period.Id,
            FilingReleaseArtifact.CharityTrusteesReport,
            pdf.GenerateTrusteesAnnualReport(evidence, reviewCopy: false),
            evidence: evidence);
        await gate.BindVerifiedApprovalAsync(period.CompanyId, period.Id, FilingReleaseWorkflow.Charity, ApprovalEvidence(period, FilingReleaseWorkflow.Charity));

        var released = await gate.GetFinalArtifactAsync(
            period.CompanyId,
            period.Id,
            FilingReleaseArtifact.CharitySofa);
        Assert.Equal("application/pdf", released.MediaType);
        Assert.EndsWith(".pdf", released.FileName, StringComparison.Ordinal);
        Assert.Contains("STATEMENT OF FINANCIAL ACTIVITIES", ExtractPdfText(released.Content));
        Assert.Equal(FilingReleaseGate.ComputeSha256(released.Content), released.Sha256);
    }

    [Fact]
    public async Task PublicFreeFormApproval_FailsClosedWithoutPersistingApprovedStatus()
    {
        await using var db = CreateDbContext();
        var period = await SeedReleaseReadyPeriodAsync(db);
        var statements = new FinancialStatementsService(db);
        var gate = new FilingReleaseGate(db, "candidate-a");
        var workflow = new FilingWorkflowService(
            db,
            statements,
            new IxbrlService(db, statements),
            releaseGate: gate);

        await gate.RecordCroArtifactAsync(period.CompanyId, period.Id, FilingReleaseArtifact.CroAccountsPdf, [1, 2, 3]);
        await gate.RecordCroArtifactAsync(period.CompanyId, period.Id, FilingReleaseArtifact.CroSignaturePage, [4, 5, 6]);
        await gate.RecordVerifiedCroSignatureAsync(period.CompanyId, period.Id, SignatureEvidence());

        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            workflow.UpdateCroStatusAsync(
                period.CompanyId,
                period.Id,
                FilingStatus.Approved,
                "Reviewer supplied in request"));
        Assert.Contains("Free-form reviewer names", error.Message, StringComparison.OrdinalIgnoreCase);

        db.ChangeTracker.Clear();
        var package = await db.CroFilingPackages.SingleAsync(p => p.PeriodId == period.Id);
        Assert.Equal(FilingStatus.PackageGenerated, package.FilingStatus);
        Assert.Null(package.ApprovedBy);
        Assert.Null(package.ApprovedArtifactManifestSha256);
    }

    [Fact]
    public async Task ReleaseGateConflictIsReturnedAsHttp409()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionMiddleware(
            _ => throw new FilingReleaseBlockedException("Exact artifact approval is stale."),
            NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("Exact artifact approval is stale", body, StringComparison.Ordinal);
    }

    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AccountsDbContext(options);
    }

    private static QualifiedAccountantApprovalEvidence ApprovalEvidence(
        AccountingPeriod period,
        FilingReleaseWorkflow workflow)
    {
        var artifact = Encoding.UTF8.GetBytes("professional-body-register-response:member-123:current");
        return new QualifiedAccountantApprovalEvidence(
            "Named Qualified Accountant",
            period.Company.TenantId!.Value,
            workflow switch
            {
                FilingReleaseWorkflow.Cro => "cro-final-filing",
                FilingReleaseWorkflow.Revenue => "revenue-final-filing",
                FilingReleaseWorkflow.Charity => "charity-final-filing",
                _ => throw new ArgumentOutOfRangeException(nameof(workflow))
            },
            "qualified-accountant",
            "approved",
            "Chartered Accountants Ireland",
            "member-123",
            "https://register.example.test/member-123/check-2026-07-10",
            artifact,
            FilingReleaseGate.ComputeSha256(artifact),
            DateTime.UtcNow.AddMinutes(-5),
            DateTime.UtcNow.AddDays(30));
    }

    private static CroSignatureEvidence SignatureEvidence()
    {
        var artifact = Encoding.UTF8.GetBytes("executed-cro-signing-pack");
        return new CroSignatureEvidence(
            "A Director",
            "B Secretary",
            DateTime.UtcNow.AddMinutes(-2),
            artifact,
            FilingReleaseGate.ComputeSha256(artifact));
    }

    private static RevenueExternalValidationEvidence ExternalValidationEvidence(string ixbrlSha256)
    {
        var response = Encoding.UTF8.GetBytes("trusted-validator-response:accepted:no-warnings");
        return new RevenueExternalValidationEvidence(
            ixbrlSha256,
            "Trusted ROS test validator",
            "ROS-VALIDATION-001",
            "validator-2026.1",
            new string('b', 64),
            "accepted",
            response,
            FilingReleaseGate.ComputeSha256(response),
            DateTime.UtcNow.AddMinutes(-1));
    }

    private static async Task<AccountingPeriod> SeedReleaseReadyPeriodAsync(
        AccountsDbContext db,
        bool charity = false,
        bool auditRequired = false)
    {
        var tenant = new Tenant { Name = "Release Gate Practice", Slug = Guid.NewGuid().ToString("N") };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var company = new Company
        {
            TenantId = tenant.Id,
            LegalName = "Release Gate Limited",
            CroNumber = "765432",
            TaxReference = "1234567A",
            CompanyType = charity ? CompanyType.CompanyLimitedByGuarantee : CompanyType.Private,
            IncorporationDate = new DateOnly(2024, 1, 1),
            AnnualReturnDate = new DateOnly(2024, 9, 15),
            IsTrading = true,
            IsCharitableOrganisation = charity,
            RegisteredOfficeAddress1 = "1 Evidence Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        if (charity)
        {
            var governanceArtifact = Encoding.UTF8.GetBytes("signed governance review evidence");
            db.CharityInfos.Add(new CharityInfo
            {
                CompanyId = company.Id,
                CharityNumber = "CHY-765432",
                CharityType = "CLG",
                GrossIncome = 100m,
                SorpTier = 1,
                CharitableObjectives = "Public benefit.",
                PrincipalActivities = "Community services.",
                GovernanceCodeCompliant = true,
                GovernanceCodeNote = "Board review completed.",
                GovernanceEvidenceReference = "GOV-MINUTE-2026-01",
                GovernanceReviewedBy = "Named Reviewer",
                GovernanceReviewedAtUtc = DateTime.UtcNow,
                GovernanceEvidenceArtifact = governanceArtifact,
                GovernanceEvidenceArtifactSha256 = FilingReleaseGate.ComputeSha256(governanceArtifact)
            });
        }
        db.CompanyOfficers.AddRange(
            new CompanyOfficer { CompanyId = company.Id, Name = "A Director", Role = OfficerRole.Director, AppointedDate = charity ? new DateOnly(2026, 1, 1) : null },
            new CompanyOfficer { CompanyId = company.Id, Name = "B Secretary", Role = OfficerRole.Secretary });

        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            Company = company,
            PeriodStart = charity ? new DateOnly(2026, 1, 1) : new DateOnly(2025, 1, 1),
            PeriodEnd = charity ? new DateOnly(2026, 12, 31) : new DateOnly(2025, 12, 31),
            IsFirstYear = true,
            Status = PeriodStatus.Finalised,
            LockedAt = DateTime.UtcNow,
            LockedBy = "Named Reviewer",
            ApprovalDate = new DateOnly(2026, 3, 1)
        };
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();

        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            CalculatedClass = CompanySizeClass.Micro,
            Turnover = 100m,
            BalanceSheetTotal = 101m,
            AvgEmployees = 1
        });
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            CanUseMicro = true,
            CanFileAbridged = true,
            AuditExempt = !auditRequired,
            ElectedRegime = ElectedRegime.Micro,
            RequiredNotesJson = "[]",
            RequiredStatementsJson = "[]"
        });

        var bankCategory = AddCategory(db, company.Id, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var salesCategory = AddCategory(db, company.Id, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var shareCategory = AddCategory(db, company.Id, "3000", "Share Capital", AccountCategoryType.Equity);
        var taxChargeCategory = AddCategory(db, company.Id, "8000", "Corporation Tax Charge", AccountCategoryType.Expense);
        var taxPayableCategory = AddCategory(db, company.Id, "2400", "Corporation Tax Payable", AccountCategoryType.Liability);
        var bank = new BankAccount
        {
            CompanyId = company.Id,
            Name = "Current Account",
            OpeningBalance = 1m,
            OpeningBalanceDate = period.PeriodStart
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();

        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = shareCategory.Id,
            Credit = 1m,
            SourceNote = "Opening share capital",
            EnteredBy = "Named Reviewer",
            Reviewed = true,
            ReviewedBy = "Named Reviewer",
            ReviewedAt = DateTime.UtcNow
        });
        db.ShareCapitals.Add(new ShareCapital
        {
            CompanyId = company.Id,
            ShareClass = "Ordinary",
            NumberIssued = 1,
            NominalValue = 1m,
            TotalValue = 1m,
            IssueDate = period.PeriodStart
        });
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = period.Id,
            Date = period.PeriodStart.AddMonths(2),
            Description = "Customer receipt",
            Amount = 100m,
            CategoryId = salesCategory.Id
        });
        db.YearEndReviewConfirmations.Add(new YearEndReviewConfirmation
        {
            PeriodId = period.Id,
            SectionKey = "adjustments",
            Confirmed = true,
            ConfirmedBy = "Named Reviewer",
            ConfirmedAt = DateTime.UtcNow
        });
        db.TaxBalances.Add(new TaxBalance
        {
            PeriodId = period.Id,
            TaxType = TaxType.CorporationTax,
            Liability = 12.50m,
            Paid = 0m,
            Balance = 12.50m
        });
        db.Adjustments.Add(new Adjustment
        {
            PeriodId = period.Id,
            Description = "Corporation tax provision",
            DebitCategoryId = taxChargeCategory.Id,
            CreditCategoryId = taxPayableCategory.Id,
            Amount = 12.50m,
            ImpactOnProfit = -12.50m,
            Source = AdjustmentSource.Auto,
            IsAuto = true,
            CreatedBy = "System",
            ApprovedBy = "Named Reviewer",
            ApprovedAt = DateTime.UtcNow
        });
        db.YearEndReviewConfirmations.AddRange(
            NilReview(period.Id, "debtors"),
            NilReview(period.Id, "creditors"),
            NilReview(period.Id, "payroll"),
            NilReview(period.Id, "tax"),
            NilReview(period.Id, "dividends"),
            NilReview(period.Id, "post-balance-sheet-events"),
            NilReview(period.Id, "related-parties"),
            NilReview(period.Id, "contingent-liabilities"),
            NilReview(period.Id, "going-concern"));
        if (charity)
        {
            db.FundBalances.Add(new FundBalance
            {
                PeriodId = period.Id,
                FundName = "Unrestricted funds",
                FundType = "Unrestricted",
                OpeningBalance = 1m,
                IncomingResources = 100m,
                ResourcesExpended = 12.50m,
                ClosingBalance = 88.50m
            });
        }
        await db.SaveChangesAsync();
        await new NotesDisclosureService(db).GenerateNotesAsync(company.Id, period.Id);

        await new SizeClassificationService(db, Microsoft.Extensions.Options.Options.Create(new Accounts.Api.Rules.SizeThresholdConfig()))
            .ClassifyAsync(company.Id, period.Id);
        await new FilingRegimeService(db).DetermineAsync(company.Id, period.Id, ElectedRegime.Micro);
        if (auditRequired)
        {
            var filingRegime = await db.FilingRegimes.SingleAsync(f => f.PeriodId == period.Id);
            filingRegime.AuditExempt = false;
            await db.SaveChangesAsync();
        }

        var taxSupport = await new TaxComputationService(db, new FinancialStatementsService(db))
            .SaveScopeReviewAsync(
                company.Id,
                period.Id,
                new TaxComputationService.CorporationTaxScopeReviewInput(
                    IsCloseCompany: false,
                    IsServiceCompany: false,
                    HasGroupOrConsortiumRelief: false,
                    HasChargeableGains: false,
                    HasForeignIncomeOrTaxCredits: false,
                    HasExceptedTrade: false,
                    HasOtherReliefsOrSpecialRegimes: false,
                    DeclaredPassiveIncomePresent: false,
                    PassiveIncomeClassificationReviewed: true,
                    LossTreatment: CorporationTaxLossTreatment.NotApplicable,
                    BroughtForwardTradingLoss: 0m,
                    BroughtForwardLossEvidence: null,
                    EvidenceNote: "Fixture scope review confirms the supported corporation-tax path."),
                "Named Reviewer");
        Assert.True(taxSupport.FinalTaxChargeSupported);

        var blockers = await new FinancialStatementsService(db)
            .GetFinalOutputReadinessBlockersAsync(company.Id, period.Id);
        if (auditRequired)
            Assert.Contains(blockers, blocker => blocker.Contains("auditor", StringComparison.OrdinalIgnoreCase));
        else
            Assert.Empty(blockers);
        _ = bankCategory;
        return period;
    }

    private static AccountCategory AddCategory(
        AccountsDbContext db,
        int companyId,
        string code,
        string name,
        AccountCategoryType type)
    {
        var category = new AccountCategory
        {
            CompanyId = companyId,
            Code = code,
            Name = name,
            Type = type
        };
        db.AccountCategories.Add(category);
        return category;
    }

    private static YearEndReviewConfirmation NilReview(int periodId, string sectionKey) => new()
    {
        PeriodId = periodId,
        SectionKey = sectionKey,
        Confirmed = true,
        ConfirmedBy = "Named Reviewer",
        Note = "Nil position reviewed."
    };

    private static string ExtractPdfText(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join("\n", document.GetPages().Select(page => page.Text));
    }

    private static byte[] PdfArtifact(string text) =>
        Document.Create(container =>
        {
            container.Page(page => page.Content().Text(text));
        }).GeneratePdf();
}
