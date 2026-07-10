using Accounts.Api.Data;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;

namespace Accounts.Api.Endpoints;

public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/companies/{companyId:int}/periods/{periodId:int}/documents").WithTags("Documents");

        // Full accounts package for AGM/member approval.
        group.MapGet("/accounts-package", async (int companyId, int periodId, DocumentGeneratorService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var pdf = await service.GenerateAccountsReviewPackageAsync(companyId, periodId);
            return Results.File(pdf, "application/pdf", $"DRAFT_NOT_FOR_FILING_accounts_{periodId}.pdf");
        });

        group.MapGet("/agm-pack", async (int companyId, int periodId, DocumentGeneratorService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var pdf = await service.GenerateAgmReviewPackAsync(companyId, periodId);
            return Results.File(pdf, "application/pdf", $"DRAFT_NOT_FOR_FILING_agm_pack_{periodId}.pdf");
        });

        group.MapGet("/cro-filing-pack", async (int companyId, int periodId, DocumentGeneratorService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var pdf = await service.GenerateCroFilingReviewPackAsync(companyId, periodId);

            return Results.File(pdf, "application/pdf", $"DRAFT_NOT_FOR_FILING_cro_filing_{periodId}.pdf");
        });

        group.MapPost("/cro-filing-pack", async (
            int companyId,
            int periodId,
            DocumentGeneratorService service,
            FilingWorkflowService workflow,
            AccountsDbContext db,
            HttpContext context,
            ApiAccessService apiAccess,
            [FromServices] IdempotencyService? idempotency = null,
            [FromServices] AccountingConcurrencyCoordinator? concurrency = null) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            var actor = AuthContext.RequireUser(context);
            idempotency ??= new IdempotencyService(db);
            concurrency ??= new AccountingConcurrencyCoordinator(db);
            var command = await IdempotencyHttpContract.ExecuteAsync(
                context,
                idempotency,
                actor,
                IdempotencyOperations.CroAccountsGenerate,
                new { companyId, periodId, DocumentType = "accounts" },
                async cancellationToken =>
                {
                    await using var lease = await concurrency.AcquirePeriodAsync(companyId, periodId, cancellationToken);
                    var retainedFinal = await service.GenerateCroFilingPackAsync(companyId, periodId);
                    var review = await service.GenerateCroFilingReviewPackAsync(companyId, periodId);
                    var package = await workflow.RecordCroDocumentGeneratedAsync(
                        companyId,
                        periodId,
                        "accounts",
                        AuthenticatedIdentity.AuditUserId(actor),
                        retainedFinal);
                    await lease.CommitIfOwnedAsync(cancellationToken);
                    return new IdempotencyOperationOutcome<GeneratedDocumentReplay>(
                        new GeneratedDocumentReplay(
                            review,
                            "application/pdf",
                            $"DRAFT_NOT_FOR_FILING_cro_filing_{periodId}.pdf",
                            Convert.ToHexStringLower(SHA256.HashData(retainedFinal))),
                        "CroFilingPackage",
                        package.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                });
            if (command.Error is not null)
                return command.Error;
            var replay = command.Execution!.Result;
            return Results.File(replay.Content, replay.MediaType, replay.FileName);
        });

        group.MapGet("/cro-filing-pack/final", async (int companyId, int periodId, [FromServices] FilingReleaseGate gate, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var artifact = await gate.GetFinalArtifactAsync(
                companyId,
                periodId,
                FilingReleaseArtifact.CroAccountsPdf,
                AuditUserId(context));
            context.Response.Headers["X-Artifact-Sha256"] = artifact.Sha256;
            context.Response.Headers["X-Release-Candidate"] = artifact.ReleaseCandidate;
            return Results.File(artifact.Content, artifact.MediaType, artifact.FileName);
        });

        group.MapGet("/signature-page", async (int companyId, int periodId, DocumentGeneratorService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var pdf = await service.GenerateSignatureReviewPageAsync(companyId, periodId);

            return Results.File(pdf, "application/pdf", $"DRAFT_NOT_FOR_FILING_signature_page_{periodId}.pdf");
        });

        group.MapPost("/signature-page", async (
            int companyId,
            int periodId,
            DocumentGeneratorService service,
            FilingWorkflowService workflow,
            AccountsDbContext db,
            HttpContext context,
            ApiAccessService apiAccess,
            [FromServices] IdempotencyService? idempotency = null,
            [FromServices] AccountingConcurrencyCoordinator? concurrency = null) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;

            var actor = AuthContext.RequireUser(context);
            idempotency ??= new IdempotencyService(db);
            concurrency ??= new AccountingConcurrencyCoordinator(db);
            var command = await IdempotencyHttpContract.ExecuteAsync(
                context,
                idempotency,
                actor,
                IdempotencyOperations.CroSignatureGenerate,
                new { companyId, periodId, DocumentType = "signature" },
                async cancellationToken =>
                {
                    await using var lease = await concurrency.AcquirePeriodAsync(companyId, periodId, cancellationToken);
                    var retainedFinal = await service.GenerateSignaturePageAsync(companyId, periodId);
                    var review = await service.GenerateSignatureReviewPageAsync(companyId, periodId);
                    var package = await workflow.RecordCroDocumentGeneratedAsync(
                        companyId,
                        periodId,
                        "signature",
                        AuthenticatedIdentity.AuditUserId(actor),
                        retainedFinal);
                    await lease.CommitIfOwnedAsync(cancellationToken);
                    return new IdempotencyOperationOutcome<GeneratedDocumentReplay>(
                        new GeneratedDocumentReplay(
                            review,
                            "application/pdf",
                            $"DRAFT_NOT_FOR_FILING_signature_page_{periodId}.pdf",
                            Convert.ToHexStringLower(SHA256.HashData(retainedFinal))),
                        "CroFilingPackage",
                        package.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                });
            if (command.Error is not null)
                return command.Error;
            var replay = command.Execution!.Result;
            return Results.File(replay.Content, replay.MediaType, replay.FileName);
        });

        group.MapGet("/signature-page/final", async (int companyId, int periodId, [FromServices] FilingReleaseGate gate, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var artifact = await gate.GetFinalArtifactAsync(
                companyId,
                periodId,
                FilingReleaseArtifact.CroSignaturePage,
                AuditUserId(context));
            context.Response.Headers["X-Artifact-Sha256"] = artifact.Sha256;
            context.Response.Headers["X-Release-Candidate"] = artifact.ReleaseCandidate;
            return Results.File(artifact.Content, artifact.MediaType, artifact.FileName);
        });

        group.MapGet("/directors-report-data", async (int companyId, int periodId, DirectorsReportService service, AccountsDbContext db, HttpContext context) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();

            var result = await service.GenerateAsync(companyId, periodId);
            return Results.Ok(result);
        });
    }

    private static string? AuditUserId(HttpContext context)
    {
        var user = AuthContext.GetUser(context);
        return user is null ? null : AuthenticatedIdentity.AuditUserId(user);
    }

}

public sealed record GeneratedDocumentReplay(
    byte[] Content,
    string MediaType,
    string FileName,
    string RetainedArtifactSha256);
