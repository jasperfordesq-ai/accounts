using System.Text;
using Accounts.Api.Data;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UglyToad.PdfPig;
using Xunit;

namespace Accounts.Tests;

/// <summary>
/// Release-CI execution of every immutable golden-corpus scenario against PostgreSQL. The workflow
/// intentionally stops at the machine boundary: no accountant, signatory, auditor or external ROS
/// evidence is invented to turn a machine test green.
/// </summary>
public sealed partial class GoldenCorpusPostgresReleaseTests : IAsyncLifetime
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";
    private readonly string? baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar);
    private readonly string schemaName = "golden_release_" + Guid.NewGuid().ToString("N");
    private DbContextOptions<AccountsDbContext>? options;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString))
            return;

        var connection = new NpgsqlConnectionStringBuilder(baseConnectionString) { SearchPath = schemaName }
            .ConnectionString;
        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using (var createSchema = admin.CreateCommand())
        {
            createSchema.CommandText = $"CREATE SCHEMA \"{schemaName}\"";
            await createSchema.ExecuteNonQueryAsync();
        }

        options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseNpgsql(connection)
            .Options;
        await using var db = new AccountsDbContext(options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString))
            return;

        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using var dropSchema = admin.CreateCommand();
        dropSchema.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE";
        await dropSchema.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task AllFiveImmutableScenarios_UsePublicDecisionAndArtifactWorkflowsOnPostgres()
    {
        await using var db = new AccountsDbContext(
            options ?? throw new InvalidOperationException($"{ConnectionEnvVar} is required."));

        foreach (var scenario in GoldenCorpusFixture.Document.Scenarios)
        {
            var audited = scenario.Code == "medium-audit-required";
            var period = await FilingGoldenCorpusScenarioTests.SeedBasicScenarioAsync(
                db,
                scenario,
                finalise: !audited);
            var statements = new FinancialStatementsService(db);
            var documents = new DocumentGeneratorService(db, statements);
            var ixbrlBytes = await new IxbrlService(db, statements)
                .GenerateIxbrlAsync(period.CompanyId, period.Id);
            var ixbrl = Encoding.UTF8.GetString(ixbrlBytes);

            foreach (var phrase in scenario.ExpectedIxbrlPhrases)
                Assert.Contains(phrase, ixbrl, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(64, FilingReleaseGate.ComputeSha256(ixbrlBytes).Length);

            var profile = await new FilingReadinessProfileService(db)
                .GetProfileAsync(period.CompanyId, period.Id);
            Assert.Contains(profile.RequiredEvidence, item => item.Code == "accountant-review" && !item.Satisfied);
            Assert.Contains(profile.RequiredEvidence, item => item.Code == "external-ros-validation" && !item.Satisfied);

            if (audited)
            {
                var blocked = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                    documents.GenerateAccountsPackageAsync(period.CompanyId, period.Id));
                Assert.Contains("auditor", blocked.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(profile.RequiredEvidence, item => item.Code == "audit-report" && !item.Satisfied);
                continue;
            }

            var accounts = await documents.GenerateAccountsPackageAsync(period.CompanyId, period.Id);
            var croPack = await documents.GenerateCroFilingPackAsync(period.CompanyId, period.Id);
            var pdfText = ExtractPdfText(accounts) + " " + ExtractPdfText(croPack);
            foreach (var phrase in scenario.ExpectedPdfPhrases)
                Assert.Contains(phrase, pdfText, StringComparison.OrdinalIgnoreCase);

            await FilingGoldenCorpusScenarioTests.RecordGeneratedMachineArtifactsAsync(db, period);
            var package = await db.CroFilingPackages.AsNoTracking()
                .SingleAsync(item => item.PeriodId == period.Id);
            Assert.NotNull(package.AccountsPdfSha256);
            Assert.NotNull(package.SignaturePageSha256);
            Assert.Null(package.SignedPdfSha256);
            Assert.Null(package.ApprovedArtifactManifestSha256);
            Assert.Null(package.ApprovedBy);

            // Retain the machine-generated review bytes through the public gate, then submit a
            // deliberately synthetic rejection payload. Filing-ready generation/internal validation
            // is intentionally disabled, so the request must fail before any external-validation
            // field is persisted; this payload is not represented as validator evidence.
            var releaseGate = new FilingReleaseGate(db, "golden-corpus-machine-candidate");
            var revenuePackage = await releaseGate.RecordRevenueIxbrlArtifactAsync(
                period.CompanyId,
                period.Id,
                ixbrlBytes,
                "golden-corpus-machine");
            var exactGeneratedHash = FilingReleaseGate.ComputeSha256(ixbrlBytes);
            Assert.Equal(exactGeneratedHash, revenuePackage.IxbrlSha256);
            var rejectionBytes = Encoding.UTF8.GetBytes(
                "SYNTHETIC NEGATIVE TEST PAYLOAD - NOT EXTERNAL VALIDATOR EVIDENCE");
            var wrongHash = exactGeneratedHash[0] == 'a'
                ? new string('b', 64)
                : new string('a', 64);
            var hashMismatch = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
                releaseGate.RecordVerifiedExternalRevenueValidationAsync(
                    period.CompanyId,
                    period.Id,
                    new RevenueExternalValidationEvidence(
                        wrongHash,
                        "synthetic-negative-test-only",
                        "NOT-EXTERNAL-EVIDENCE",
                        "test-only",
                        new string('0', 64),
                        "accepted",
                        rejectionBytes,
                        FilingReleaseGate.ComputeSha256(rejectionBytes),
                        DateTime.UtcNow)));
            Assert.Contains("internal iXBRL checks have not passed", hashMismatch.Message, StringComparison.Ordinal);

            var freeFormEvidence = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
                releaseGate.RecordExternalRevenueValidationAsync(
                    period.CompanyId,
                    period.Id,
                    exactGeneratedHash,
                    "NO-REAL-VALIDATOR-EVIDENCE"));
            Assert.Contains("free-form", freeFormEvidence.Message, StringComparison.OrdinalIgnoreCase);
            var retainedRevenue = await db.RevenueFilingPackages.AsNoTracking()
                .SingleAsync(item => item.PeriodId == period.Id);
            Assert.Equal(exactGeneratedHash, retainedRevenue.IxbrlSha256);
            Assert.False(retainedRevenue.IxbrlValidated);
            Assert.Null(retainedRevenue.ExternalValidationArtifactSha256);
            Assert.Null(retainedRevenue.ExternalValidationReference);
            Assert.Null(retainedRevenue.ExternalValidationResponseArtifact);
            Assert.Null(retainedRevenue.ExternalValidationResponseSha256);
        }
    }

    private static string ExtractPdfText(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join(
            ' ',
            document.GetPages().SelectMany(page => page.GetWords()).Select(word => word.Text));
    }

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvVar)))
                Skip = $"{ConnectionEnvVar} is not set.";
        }
    }
}

public sealed class GoldenCorpusPostgresReleaseTestsConfiguration
{
    [Fact]
    public void ReleaseCiConfiguration_CannotRequirePostgresAndOmitItsConnection()
    {
        const string connectionVariable = "ACCOUNTS_POSTGRES_TEST_CONNECTION";
        const string requiredVariable = "ACCOUNTS_REQUIRE_POSTGRES_GOLDEN_CORPUS";
        if (!string.Equals(
                Environment.GetEnvironmentVariable(requiredVariable),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Assert.False(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(connectionVariable)),
            $"{connectionVariable} is mandatory when {requiredVariable}=true.");
    }
}
