using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

public sealed class AccountantWorkingPaperServiceTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Generate_RetainsCandidateBoundSupportOnlyPackAndCompleteDrillDown()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedAsync(db);
        var service = Service(db);
        var actor = Actor(fixture.Tenant.Id, fixture.Company.Id, "Accountant");

        var pack = await service.GenerateAndRetainAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            actor,
            "request-working-papers");

        Assert.Equal(AccountantWorkingPaperService.OutputKind, pack.OutputKind);
        Assert.False(pack.IsFilingArtifact);
        Assert.False(pack.DirectSubmissionSupported);
        Assert.True(pack.QualifiedAccountantReviewRequired);
        Assert.Contains("NOT A CRO OR CT1 RETURN", pack.Warning, StringComparison.Ordinal);
        Assert.Equal("acc005-test-candidate", pack.Identity.ReleaseCandidate);
        Assert.Equal("user:805", pack.Identity.GeneratedByUserId);
        Assert.Equal("Fixture Accountant", pack.Identity.GeneratedByDisplayName);
        Assert.Equal(64, pack.Identity.SourceDataSha256.Length);
        Assert.Equal(64, pack.Identity.ArtifactSha256.Length);
        AccountantWorkingPaperService.ValidatePackHash(pack);

        Assert.Equal(5, pack.WorkingPaperIndex.Entries.Count);
        Assert.All(pack.WorkingPaperIndex.Entries, item => Assert.StartsWith("/api/companies/", item.Endpoint));
        Assert.All(pack.AdjustedTrialBalance.Reconciliations, item => Assert.True(item.Reconciles, item.Description));
        Assert.All(pack.LeadSchedules.Reconciliations, item => Assert.True(item.Reconciles, item.Description));
        Assert.All(pack.CorporationTaxBridge.Reconciliations, item => Assert.True(item.Reconciles, item.Description));

        var bank = Assert.Single(pack.LeadSchedules.Rows, item => item.Code == "1400");
        Assert.Contains(bank.Sources, item => item.SourceType == "opening-balance" && item.EntityId == fixture.BankOpeningId);
        Assert.Contains(bank.Sources, item => item.SourceType == "imported-transaction" && item.EntityId == fixture.SalesTransactionId);
        Assert.Contains(bank.Sources, item => item.SourceType == "imported-transaction" && item.EntityId == fixture.ExpenseTransactionId);
        var taxCharge = Assert.Single(pack.LeadSchedules.Rows, item => item.Code == "8000");
        Assert.Contains(taxCharge.Sources, item => item.SourceType == "journal" && item.EntityId == fixture.TaxJournalId);
        var taxPayable = Assert.Single(pack.LeadSchedules.Rows, item => item.Code == "2400");
        Assert.Contains(taxPayable.Sources, item => item.SourceType == "tax-balance" && item.EntityId == fixture.TaxBalanceId);
        Assert.Contains(pack.CorporationTaxBridge.Rows.SelectMany(item => item.Sources), item => item.SourceType == "corporation-tax-scope-review");
        Assert.Contains(pack.CorporationTaxBridge.Rows.SelectMany(item => item.Sources), item => item.SourceType == "corporation-tax-loss-record");
        Assert.Equal(2, pack.CategorizedTransactions.TotalCount);
        Assert.Equal(2, pack.CategorizedTransactions.CategorizedCount);

        var retained = Assert.Single(await db.Reports.Where(item => item.Type == ReportType.LeadSchedule).ToListAsync());
        Assert.Equal(retained.Id, pack.Identity.ArtifactVersion);
        Assert.Contains(pack.Identity.ArtifactSha256, retained.DataJson, StringComparison.Ordinal);
        Assert.Contains(await db.AuditLogs.ToListAsync(), item =>
            item.Action == AuditEventCodes.AccountantWorkingPaperPackGenerated
            && item.EntityId == retained.Id
            && item.RequestId == "request-working-papers");
    }

    [Fact]
    public async Task Generation_UsesPersistedReportIdentityAndRollsBackWhenAuditFails()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedAsync(db);
        var statements = new FinancialStatementsService(db);
        var failing = new FailingAuditWorkingPaperService(
            db,
            statements,
            new TaxComputationService(db, statements),
            ReleaseIdentity(),
            new AuditService(db),
            new FixedTimeProvider(GeneratedAt));

        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.GenerateAndRetainAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            Actor(fixture.Tenant.Id, fixture.Company.Id, "Accountant")));

        Assert.Empty(await db.Reports.Where(item => item.Type == ReportType.LeadSchedule).ToListAsync());
        Assert.DoesNotContain(await db.AuditLogs.ToListAsync(), item => item.Action == AuditEventCodes.AccountantWorkingPaperPackGenerated);
    }

    [PostgresFact]
    public async Task PostgreSqlGeneration_RollsBackReportAndAuditTogether_ThenRetainsExactReportVersion()
    {
        const string connectionVariable = "ACCOUNTS_POSTGRES_TEST_CONNECTION";
        var baseConnection = Environment.GetEnvironmentVariable(connectionVariable)!;
        var schema = "accountant_working_papers_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(baseConnection);
        await admin.OpenAsync();
        await using (var create = admin.CreateCommand())
        {
            create.CommandText = $"CREATE SCHEMA \"{schema}\"";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var scoped = new NpgsqlConnectionStringBuilder(baseConnection) { SearchPath = schema };
            var options = new DbContextOptionsBuilder<AccountsDbContext>()
                .UseNpgsql(scoped.ConnectionString)
                .Options;
            await using var db = new AccountsDbContext(options);
            await db.Database.MigrateAsync();
            var fixture = await SeedAsync(db);
            var actor = Actor(fixture.Tenant.Id, fixture.Company.Id, "Accountant");
            var statements = new FinancialStatementsService(db);
            var failing = new FailingAuditWorkingPaperService(
                db,
                statements,
                new TaxComputationService(db, statements),
                ReleaseIdentity(),
                new AuditService(db),
                new FixedTimeProvider(GeneratedAt));

            await Assert.ThrowsAsync<InvalidOperationException>(() => failing.GenerateAndRetainAsync(
                fixture.Company.Id,
                fixture.Period.Id,
                actor));
            db.ChangeTracker.Clear();
            Assert.False(await db.Reports.AnyAsync(item => item.Type == ReportType.LeadSchedule));
            Assert.False(await db.AuditLogs.AnyAsync(item => item.Action == AuditEventCodes.AccountantWorkingPaperPackGenerated));

            await using (var outerTransaction = await db.Database.BeginTransactionAsync())
            {
                _ = await Service(db).GenerateAndRetainAsync(
                    fixture.Company.Id,
                    fixture.Period.Id,
                    actor,
                    "postgres-outer-transaction");
                await outerTransaction.RollbackAsync();
            }
            db.ChangeTracker.Clear();
            Assert.False(await db.Reports.AnyAsync(item => item.Type == ReportType.LeadSchedule));
            Assert.False(await db.AuditLogs.AnyAsync(item => item.Action == AuditEventCodes.AccountantWorkingPaperPackGenerated));

            var pack = await Service(db).GenerateAndRetainAsync(
                fixture.Company.Id,
                fixture.Period.Id,
                actor,
                "postgres-working-paper");
            db.ChangeTracker.Clear();
            var report = await db.Reports.SingleAsync(item => item.Type == ReportType.LeadSchedule);
            var audit = await db.AuditLogs.SingleAsync(item => item.Action == AuditEventCodes.AccountantWorkingPaperPackGenerated);
            Assert.Equal(report.Id, pack.Identity.ArtifactVersion);
            Assert.Equal(report.Id, audit.EntityId);
            Assert.Contains(pack.Identity.ArtifactSha256, report.DataJson, StringComparison.Ordinal);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var cleanup = new NpgsqlConnection(baseConnection);
            await cleanup.OpenAsync();
            await using var drop = cleanup.CreateCommand();
            drop.CommandText = $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE";
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task ConcurrentGeneration_BindsDistinctVersionsToPersistedReportIds()
    {
        var databaseName = $"accountant-working-papers-concurrent-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        int tenantId;
        int companyId;
        int periodId;
        await using (var setup = new AccountsDbContext(options))
        {
            var fixture = await SeedAsync(setup);
            tenantId = fixture.Tenant.Id;
            companyId = fixture.Company.Id;
            periodId = fixture.Period.Id;
        }

        await using var firstDb = new AccountsDbContext(options);
        await using var secondDb = new AccountsDbContext(options);
        var actor = Actor(tenantId, companyId, "Accountant");
        var generated = await Task.WhenAll(
            Service(firstDb).GenerateAndRetainAsync(companyId, periodId, actor, "concurrent-a"),
            Service(secondDb).GenerateAndRetainAsync(companyId, periodId, actor, "concurrent-b"));

        Assert.Equal(2, generated.Select(item => item.Identity.ArtifactVersion).Distinct().Count());
        await using var verify = new AccountsDbContext(options);
        var reportIds = await verify.Reports
            .Where(item => item.Type == ReportType.LeadSchedule)
            .Select(item => item.Id)
            .OrderBy(item => item)
            .ToListAsync();
        Assert.Equal(reportIds, generated.Select(item => item.Identity.ArtifactVersion).OrderBy(item => item).ToList());
    }

    [Fact]
    public async Task RetainedPack_FailsClosedAfterSourceDriftOrHashTampering()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedAsync(db);
        var service = Service(db);
        var actor = Actor(fixture.Tenant.Id, fixture.Company.Id, "Accountant");
        var first = await service.GenerateAndRetainAsync(fixture.Company.Id, fixture.Period.Id, actor);

        var current = await service.GetLatestRetainedAsync(fixture.Company.Id, fixture.Period.Id, actor);
        Assert.Equal(first.Identity.ArtifactSha256, current!.Identity.ArtifactSha256);

        var expense = await db.ImportedTransactions.SingleAsync(item => item.Id == fixture.ExpenseTransactionId);
        expense.Description = "Changed after artifact retention";
        await db.SaveChangesAsync();
        var drift = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.GetLatestRetainedAsync(fixture.Company.Id, fixture.Period.Id, actor));
        Assert.Contains("source data changed", drift.Message, StringComparison.OrdinalIgnoreCase);

        var second = await service.GenerateAndRetainAsync(fixture.Company.Id, fixture.Period.Id, actor);
        Assert.Equal(2, second.Identity.ArtifactVersion);
        var report = await db.Reports.OrderByDescending(item => item.Id).FirstAsync(item => item.Type == ReportType.LeadSchedule);
        report.DataJson = report.DataJson!.Replace(second.Identity.ArtifactSha256, new string('0', 64), StringComparison.Ordinal);
        await db.SaveChangesAsync();
        var tampered = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.GetLatestRetainedAsync(fixture.Company.Id, fixture.Period.Id, actor));
        Assert.Contains("hash does not match", tampered.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetainedPack_FailsClosedWhenExplicitCompanyOrBankOpeningIdentityChanges_AndSkipsUnrelatedLegacyRows()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedAsync(db);
        var service = Service(db);
        var actor = Actor(fixture.Tenant.Id, fixture.Company.Id, "Accountant");
        var generated = await service.GenerateAndRetainAsync(fixture.Company.Id, fixture.Period.Id, actor);

        db.Reports.Add(new Report
        {
            PeriodId = fixture.Period.Id,
            Type = ReportType.LeadSchedule,
            DataJson = "{\"legacy\":true}",
            GeneratedAt = GeneratedAt.UtcDateTime.AddMinutes(1)
        });
        await db.SaveChangesAsync();
        Assert.Equal(
            generated.Identity.ArtifactSha256,
            (await service.GetLatestRetainedAsync(fixture.Company.Id, fixture.Period.Id, actor))!.Identity.ArtifactSha256);

        var bank = await db.BankAccounts.SingleAsync(item => item.CompanyId == fixture.Company.Id);
        bank.OpeningBalance = 25m;
        bank.OpeningBalanceDate = fixture.Period.PeriodStart;
        await db.SaveChangesAsync();
        var bankDrift = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.GetLatestRetainedAsync(fixture.Company.Id, fixture.Period.Id, actor));
        Assert.Contains("source data changed", bankDrift.Message, StringComparison.OrdinalIgnoreCase);

        bank.OpeningBalance = 0m;
        bank.OpeningBalanceDate = null;
        var company = await db.Companies.SingleAsync(item => item.Id == fixture.Company.Id);
        company.LegalName = "Changed legal identity Limited";
        await db.SaveChangesAsync();
        var companyDrift = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.GetLatestRetainedAsync(fixture.Company.Id, fixture.Period.Id, actor));
        Assert.Contains("source data changed", companyDrift.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Endpoints_ReturnExplicitSectionContractsAndDenyClientAccess()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedAsync(db);
        var service = Service(db);
        var accountantContext = Context(Actor(fixture.Tenant.Id, fixture.Company.Id, "Accountant"));

        var generated = await AccountantWorkingPaperEndpoints.GenerateEndpointAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            service,
            db,
            accountantContext);
        var generatedOk = Assert.IsType<Ok<AccountantWorkingPaperService.Pack>>(generated);
        Assert.False(generatedOk.Value!.DirectSubmissionSupported);
        Assert.Equal("false", accountantContext.Response.Headers["X-Filing-Artifact"]);
        Assert.Equal("false", accountantContext.Response.Headers["X-Direct-Submission-Supported"]);
        Assert.Equal("true", accountantContext.Response.Headers["X-Qualified-Accountant-Review-Required"]);

        var leadContext = Context(Actor(fixture.Tenant.Id, fixture.Company.Id, "Reviewer"));
        var lead = await AccountantWorkingPaperEndpoints.GetLeadSchedulesEndpointAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            service,
            db,
            leadContext);
        var leadOk = Assert.IsType<Ok<AccountantWorkingPaperEndpoints.SectionResponse<AccountantWorkingPaperService.LeadSchedulesArtifact>>>(lead);
        Assert.Equal(generatedOk.Value.Identity.ArtifactSha256, leadOk.Value!.Identity.ArtifactSha256);
        Assert.NotEmpty(leadOk.Value.Artifact.Rows);

        var taxContext = Context(Actor(fixture.Tenant.Id, fixture.Company.Id, "Reviewer"));
        var tax = await AccountantWorkingPaperEndpoints.GetCorporationTaxBridgeEndpointAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            service,
            db,
            taxContext);
        var taxOk = Assert.IsType<Ok<AccountantWorkingPaperEndpoints.SectionResponse<AccountantWorkingPaperService.CorporationTaxBridgeArtifact>>>(tax);
        Assert.False(taxOk.Value!.Artifact.IsCompleteCt1Return);
        Assert.False(taxOk.Value.Artifact.DirectRosSubmissionSupported);

        var reviewerGenerateContext = Context(Actor(fixture.Tenant.Id, fixture.Company.Id, "Reviewer"));
        var reviewerGenerate = await AccountantWorkingPaperEndpoints.GenerateEndpointAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            service,
            db,
            reviewerGenerateContext);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(reviewerGenerate).StatusCode);

        var clientContext = Context(Actor(fixture.Tenant.Id, fixture.Company.Id, "Client"));
        var denied = await AccountantWorkingPaperEndpoints.GetIndexEndpointAsync(
            fixture.Company.Id,
            fixture.Period.Id,
            service,
            db,
            clientContext);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
    }

    private static AccountantWorkingPaperService Service(AccountsDbContext db)
    {
        var statements = new FinancialStatementsService(db);
        var tax = new TaxComputationService(db, statements);
        return new AccountantWorkingPaperService(
            db,
            statements,
            tax,
            ReleaseIdentity(),
            new AuditService(db),
            new FixedTimeProvider(GeneratedAt));
    }

    private static FilingReleaseIdentityProvider ReleaseIdentity()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FilingRelease:Candidate"] = "acc005-test-candidate"
            })
            .Build();
        return new FilingReleaseIdentityProvider(configuration, new TestEnvironment());
    }

    private static async Task<Fixture> SeedAsync(AccountsDbContext db)
    {
        var tenant = new Tenant { Name = "Working Paper Firm", Slug = $"working-paper-{Guid.NewGuid():N}" };
        var company = new Company
        {
            Tenant = tenant,
            LegalName = "Working Paper Fixture Limited",
            CroNumber = "WP765432",
            TaxReference = "7654321W",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2026, 1, 1),
            IsTrading = true
        };
        db.Add(company);
        await db.SaveChangesAsync();
        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 12, 31),
            IsFirstYear = true
        };
        var bank = new BankAccount
        {
            CompanyId = company.Id,
            Name = "Current account",
            OpeningBalance = 0m
        };
        var categories = new[]
        {
            Category(company.Id, "1400", "Bank control", AccountCategoryType.Asset, TaxTreatment.Other),
            Category(company.Id, "3000", "Share capital", AccountCategoryType.Equity, TaxTreatment.Other),
            Category(company.Id, "4000", "Sales", AccountCategoryType.Income, TaxTreatment.Deductible),
            Category(company.Id, "6000", "Administrative expenses", AccountCategoryType.Expense, TaxTreatment.Deductible),
            Category(company.Id, "8000", "Corporation tax charge", AccountCategoryType.Expense, TaxTreatment.NonDeductible),
            Category(company.Id, "2400", "Corporation tax payable", AccountCategoryType.Liability, TaxTreatment.Other)
        };
        db.AddRange(period, bank);
        db.AddRange(categories);
        await db.SaveChangesAsync();
        var bankCategory = categories.Single(item => item.Code == "1400");
        var equityCategory = categories.Single(item => item.Code == "3000");
        var salesCategory = categories.Single(item => item.Code == "4000");
        var expenseCategory = categories.Single(item => item.Code == "6000");
        var taxExpense = categories.Single(item => item.Code == "8000");
        var taxPayable = categories.Single(item => item.Code == "2400");
        var bankOpening = new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = bankCategory.Id,
            Debit = 100m,
            SourceNote = "Reviewed bank take-on statement",
            EnteredBy = "Fixture Accountant",
            Reviewed = true,
            ReviewedBy = "Fixture Reviewer",
            ReviewedAt = GeneratedAt.UtcDateTime
        };
        var equityOpening = new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = equityCategory.Id,
            Credit = 100m,
            SourceNote = "Reviewed incorporation share capital",
            EnteredBy = "Fixture Accountant",
            Reviewed = true,
            ReviewedBy = "Fixture Reviewer",
            ReviewedAt = GeneratedAt.UtcDateTime
        };
        var sales = new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = period.Id,
            Date = new DateOnly(2026, 3, 1),
            Description = "Customer receipt",
            Amount = 1_000m,
            CategoryId = salesCategory.Id,
            ManualOverride = true
        };
        var expense = new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = period.Id,
            Date = new DateOnly(2026, 4, 1),
            Description = "Office costs",
            Amount = -200m,
            CategoryId = expenseCategory.Id,
            ManualOverride = true
        };
        var taxJournal = new Adjustment
        {
            PeriodId = period.Id,
            Description = "Corporation tax provision",
            DebitCategoryId = taxExpense.Id,
            CreditCategoryId = taxPayable.Id,
            Amount = 100m,
            Source = AdjustmentSource.Manual,
            Reason = "Recognise the supported current tax charge.",
            LegalBasis = "FRS 105 taxation",
            ImpactOnProfit = -100m,
            CreatedBy = "Fixture Accountant",
            ApprovedBy = "Fixture Reviewer",
            ApprovedAt = GeneratedAt.UtcDateTime,
            CreatedAt = GeneratedAt.UtcDateTime
        };
        var taxBalance = new TaxBalance
        {
            PeriodId = period.Id,
            TaxType = TaxType.CorporationTax,
            Liability = 100m,
            Paid = 0m,
            Balance = 100m
        };
        db.AddRange(bankOpening, equityOpening, sales, expense, taxJournal, taxBalance);
        await db.SaveChangesAsync();
        var taxService = new TaxComputationService(db, new FinancialStatementsService(db));
        await taxService.SaveScopeReviewAsync(
            company.Id,
            period.Id,
            new TaxComputationService.CorporationTaxScopeReviewInput(
                IsCloseCompany: false,
                IsServiceCompany: null,
                HasGroupOrConsortiumRelief: false,
                HasChargeableGains: false,
                HasForeignIncomeOrTaxCredits: false,
                HasExceptedTrade: false,
                HasOtherReliefsOrSpecialRegimes: false,
                DeclaredPassiveIncomePresent: false,
                PassiveIncomeClassificationReviewed: false,
                LossTreatment: CorporationTaxLossTreatment.NotApplicable,
                BroughtForwardTradingLoss: 0m,
                BroughtForwardLossEvidence: null,
                EvidenceNote: "Retained simple-company scope evidence for working-paper fixture."),
            "Fixture Accountant");
        return new Fixture(
            tenant,
            company,
            period,
            bankOpening.Id,
            sales.Id,
            expense.Id,
            taxJournal.Id,
            taxBalance.Id);
    }

    private static AccountCategory Category(
        int companyId,
        string code,
        string name,
        AccountCategoryType type,
        TaxTreatment treatment) => new()
    {
        CompanyId = companyId,
        Code = code,
        Name = name,
        Type = type,
        TaxTreatment = treatment
    };

    private static AuthenticatedUser Actor(int tenantId, int companyId, string role) => new(
        805,
        tenantId,
        "Working Paper Firm",
        "fixture.accountant@example.invalid",
        "Fixture Accountant",
        role,
        new HashSet<int> { companyId });

    private static DefaultHttpContext Context(AuthenticatedUser actor)
    {
        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = actor;
        context.TraceIdentifier = "request-working-papers";
        return context;
    }

    private static AccountsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase($"accountant-working-papers-{Guid.NewGuid():N}")
            .Options;
        return new AccountsDbContext(options);
    }

    private sealed record Fixture(
        Tenant Tenant,
        Company Company,
        AccountingPeriod Period,
        int BankOpeningId,
        int SalesTransactionId,
        int ExpenseTransactionId,
        int TaxJournalId,
        int TaxBalanceId);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FailingAuditWorkingPaperService(
        AccountsDbContext db,
        FinancialStatementsService statements,
        TaxComputationService tax,
        FilingReleaseIdentityProvider releaseIdentity,
        AuditService audit,
        TimeProvider timeProvider)
        : AccountantWorkingPaperService(db, statements, tax, releaseIdentity, audit, timeProvider)
    {
        protected override Task WriteAuditAsync(
            int companyId,
            int periodId,
            Report report,
            Pack pack,
            AuthenticatedUser actor,
            string? requestId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Synthetic audit failure.");
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Accounts.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ACCOUNTS_POSTGRES_TEST_CONNECTION")))
                Skip = "ACCOUNTS_POSTGRES_TEST_CONNECTION is not set.";
        }
    }
}
