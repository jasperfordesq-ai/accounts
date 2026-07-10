using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

public sealed class PersistenceOwnershipInvariantTests
{
    [Theory]
    [InlineData("period")]
    [InlineData("batch")]
    [InlineData("category")]
    public async Task SaveChanges_RejectsCrossOwnerTransactionRelationshipsAtomically(string relationship)
    {
        var options = InMemoryOptions();
        var fixture = await SeedAsync(options);

        await using (var db = TenantContext(options, fixture.TenantAId, fixture.CompanyAId))
        {
            var transaction = new ImportedTransaction
            {
                BankAccountId = fixture.BankAId,
                PeriodId = relationship == "period" ? fixture.PeriodBId : fixture.PeriodAId,
                ImportBatchId = relationship == "batch" ? fixture.BatchBId : fixture.BatchAId,
                CategoryId = relationship == "category" ? fixture.CategoryBId : fixture.CategoryAId,
                Date = new DateOnly(2025, 6, 1),
                Description = $"Invalid {relationship}",
                Amount = 10m
            };
            db.ImportedTransactions.Add(transaction);

            var error = await Assert.ThrowsAsync<PersistenceOwnershipException>(() => db.SaveChangesAsync());
            Assert.Equal("ImportedTransaction ownership relationship is invalid.", error.Message);
        }

        await using var verify = new AccountsDbContext(options);
        Assert.DoesNotContain(
            await verify.ImportedTransactions.AsNoTracking().ToListAsync(),
            transaction => transaction.Description.StartsWith("Invalid ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("bank")]
    [InlineData("period")]
    [InlineData("cro-package")]
    public async Task SaveChanges_RejectsCreatingVictimOwnedRowsFromAnotherTenant(string entity)
    {
        var options = InMemoryOptions();
        var fixture = await SeedAsync(options);

        await using var db = TenantContext(options, fixture.TenantAId, fixture.CompanyAId);
        switch (entity)
        {
            case "bank":
                db.BankAccounts.Add(new BankAccount
                {
                    CompanyId = fixture.CompanyBId,
                    Name = "Poisoned victim bank",
                    OpeningBalance = 0m
                });
                break;
            case "period":
                db.AccountingPeriods.Add(new AccountingPeriod
                {
                    CompanyId = fixture.CompanyBId,
                    PeriodStart = new DateOnly(2026, 1, 1),
                    PeriodEnd = new DateOnly(2026, 12, 31)
                });
                break;
            case "cro-package":
                db.CroFilingPackages.Add(new CroFilingPackage { PeriodId = fixture.PeriodBId });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(entity));
        }

        await Assert.ThrowsAsync<PersistenceOwnershipException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChanges_RejectsCrossTenantReleaseEvidenceUpdate()
    {
        var options = InMemoryOptions();
        var fixture = await SeedAsync(options);

        await using (var db = TenantContext(options, fixture.TenantAId, fixture.CompanyAId))
        {
            var victimPackage = await db.RevenueFilingPackages
                .IgnoreQueryFilters()
                .SingleAsync(package => package.Id == fixture.RevenuePackageBId);
            victimPackage.ApprovedReleaseCandidate = "attacker-candidate";
            victimPackage.ApprovedArtifactManifestSha256 = new string('a', 64);

            await Assert.ThrowsAsync<PersistenceOwnershipException>(() => db.SaveChangesAsync());
        }

        await using var verify = new AccountsDbContext(options);
        var retained = await verify.RevenueFilingPackages
            .AsNoTracking()
            .SingleAsync(package => package.Id == fixture.RevenuePackageBId);
        Assert.Null(retained.ApprovedReleaseCandidate);
        Assert.Null(retained.ApprovedArtifactManifestSha256);
    }

    [Theory]
    [InlineData("period")]
    [InlineData("bank")]
    [InlineData("batch")]
    [InlineData("category")]
    [InlineData("transaction")]
    [InlineData("cro-package")]
    [InlineData("revenue-package")]
    [InlineData("charity-package")]
    public async Task SaveChanges_RejectsOwnershipAnchorReassignmentWithoutRequestContext(string entity)
    {
        var options = InMemoryOptions();
        var fixture = await SeedAsync(options);

        await using var db = new AccountsDbContext(options);
        switch (entity)
        {
            case "period":
                (await db.AccountingPeriods.FindAsync(fixture.PeriodAId))!.CompanyId = fixture.CompanyBId;
                break;
            case "bank":
                (await db.BankAccounts.FindAsync(fixture.BankAId))!.CompanyId = fixture.CompanyBId;
                break;
            case "batch":
                (await db.ImportBatches.FindAsync(fixture.BatchAId))!.BankAccountId = fixture.BankBId;
                break;
            case "category":
                (await db.AccountCategories.FindAsync(fixture.CategoryAId))!.CompanyId = fixture.CompanyBId;
                break;
            case "transaction":
                (await db.ImportedTransactions.FindAsync(fixture.TransactionAId))!.BankAccountId = fixture.BankBId;
                break;
            case "cro-package":
                (await db.CroFilingPackages.FindAsync(fixture.CroPackageAId))!.PeriodId = fixture.PeriodBId;
                break;
            case "revenue-package":
                (await db.RevenueFilingPackages.FindAsync(fixture.RevenuePackageAId))!.PeriodId = fixture.PeriodBId;
                break;
            case "charity-package":
                (await db.CharityFilingPackages.FindAsync(fixture.CharityPackageAId))!.PeriodId = fixture.PeriodBId;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(entity));
        }

        await Assert.ThrowsAsync<PersistenceOwnershipException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChanges_AllowsInitialTenantAssignmentButRejectsTenantReassignment()
    {
        var options = InMemoryOptions();
        await using var db = new AccountsDbContext(options);
        var firstTenant = new Tenant { Name = "First", Slug = $"first-{Guid.NewGuid():N}" };
        var secondTenant = new Tenant { Name = "Second", Slug = $"second-{Guid.NewGuid():N}" };
        var company = CompanyFor(null, "Legacy unassigned company");
        db.AddRange(firstTenant, secondTenant, company);
        await db.SaveChangesAsync();

        company.TenantId = firstTenant.Id;
        await db.SaveChangesAsync();
        company.TenantId = secondTenant.Id;

        await Assert.ThrowsAsync<PersistenceOwnershipException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task AuditService_InfersCompanyTenantAndRejectsExplicitMismatch()
    {
        var options = InMemoryOptions();
        var fixture = await SeedAsync(options);
        await using var db = new AccountsDbContext(options);
        var audit = new AuditService(db);

        await audit.LogAsync(
            fixture.CompanyAId,
            fixture.PeriodAId,
            "AccountingPeriod",
            fixture.PeriodAId,
            "ImplicitTenantAudit");

        var retained = await db.AuditLogs.SingleAsync(log => log.Action == "ImplicitTenantAudit");
        Assert.Equal(fixture.TenantAId, retained.TenantId);

        await Assert.ThrowsAsync<PersistenceOwnershipException>(() => audit.LogAsync(
            fixture.CompanyAId,
            fixture.PeriodAId,
            "AccountingPeriod",
            fixture.PeriodAId,
            "MismatchedTenantAudit",
            tenantId: fixture.TenantBId));
        Assert.False(await db.AuditLogs.AnyAsync(log => log.Action == "MismatchedTenantAudit"));
    }

    [Fact]
    public async Task SaveChanges_RejectsAuditCheckpointWithForeignOrIncorrectAnchor()
    {
        var options = InMemoryOptions();
        var fixture = await SeedAsync(options);
        int auditId;
        string integrityHash;
        await using (var db = new AccountsDbContext(options))
        {
            await new AuditService(db).LogAsync(
                fixture.CompanyAId,
                fixture.PeriodAId,
                "AccountingPeriod",
                fixture.PeriodAId,
                "CheckpointAnchor");
            var audit = await db.AuditLogs.SingleAsync(log => log.Action == "CheckpointAnchor");
            auditId = audit.Id;
            integrityHash = audit.IntegrityHash!;
        }

        await using var invalid = new AccountsDbContext(options);
        invalid.AuditIntegrityCheckpoints.Add(new AuditIntegrityCheckpoint
        {
            CompanyId = fixture.CompanyBId,
            TenantId = fixture.TenantBId,
            LastAuditLogId = auditId,
            LastIntegrityHash = integrityHash,
            CheckedEntries = 1,
            KeyId = "test-key",
            Signature = new string('b', 64)
        });
        await Assert.ThrowsAsync<PersistenceOwnershipException>(() => invalid.SaveChangesAsync());
    }

    [Fact]
    public async Task ModelMetadata_RetainsTenantFiltersForeignKeysTriggersAndCategoryConstraint()
    {
        await using var db = new AccountsDbContext(InMemoryOptions());
        var model = db.GetService<IDesignTimeModel>().Model;

        foreach (var filteredType in new[]
        {
            typeof(Company), typeof(AccountingPeriod), typeof(BankAccount), typeof(ImportBatch),
            typeof(ImportedTransaction), typeof(TransactionRule), typeof(AccountCategory),
            typeof(CroFilingPackage), typeof(RevenueFilingPackage), typeof(CharityFilingPackage),
            typeof(FilingDeadline), typeof(FilingHistory)
        })
        {
            Assert.NotEmpty(model.FindEntityType(filteredType)!.GetDeclaredQueryFilters());
        }

        AssertForeignKey<AccountingPeriod, Company>(model);
        AssertForeignKey<BankAccount, Company>(model);
        AssertForeignKey<ImportBatch, BankAccount>(model);
        AssertForeignKey<CroFilingPackage, AccountingPeriod>(model);
        AssertForeignKey<RevenueFilingPackage, AccountingPeriod>(model);
        AssertForeignKey<CharityFilingPackage, AccountingPeriod>(model);

        AssertTriggers<AccountCategory>(model,
            "TR_account_categories_company_immutable", "TR_account_categories_ownership");
        AssertTriggers<ImportedTransaction>(model,
            "TR_imported_transactions_bank_immutable", "TR_imported_transactions_ownership");
        AssertTriggers<TransactionRule>(model,
            "TR_transaction_rules_company_immutable", "TR_transaction_rules_ownership");
        AssertTriggers<FilingDeadline>(model, "TR_filing_deadlines_ownership");
        AssertTriggers<FilingHistory>(model, "TR_filing_histories_ownership");
        AssertTriggers<AuditLog>(model, "TR_audit_logs_scope_immutable", "TR_audit_logs_ownership");
        AssertTriggers<AuditIntegrityCheckpoint>(model,
            "TR_audit_integrity_checkpoints_scope_immutable", "TR_audit_integrity_checkpoints_ownership");

        Assert.Contains(
            model.FindEntityType(typeof(AccountCategory))!.GetCheckConstraints(),
            constraint => constraint.Name == "CK_account_categories_global_requires_system");
    }

    private static void AssertForeignKey<TDependent, TPrincipal>(Microsoft.EntityFrameworkCore.Metadata.IModel model)
    {
        Assert.Contains(
            model.FindEntityType(typeof(TDependent))!.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(TPrincipal));
    }

    private static void AssertTriggers<TEntity>(
        Microsoft.EntityFrameworkCore.Metadata.IModel model,
        params string[] expectedNames)
    {
        var actual = model.FindEntityType(typeof(TEntity))!
            .GetDeclaredTriggers()
            .Select(trigger => trigger.ModelName)
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(expectedNames, expected => Assert.Contains(expected, actual));
    }

    private static DbContextOptions<AccountsDbContext> InMemoryOptions() =>
        new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase($"ownership-{Guid.NewGuid():N}")
            .Options;

    private static AccountsDbContext TenantContext(
        DbContextOptions<AccountsDbContext> options,
        int tenantId,
        int companyId)
    {
        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 1,
            TenantId: tenantId,
            TenantName: $"Tenant {tenantId}",
            Email: "owner@example.ie",
            DisplayName: "Owner",
            Role: "Owner",
            AllowedCompanyIds: new HashSet<int> { companyId });
        return new AccountsDbContext(
            options,
            new HttpContextAccessor { HttpContext = context });
    }

    private static async Task<OwnershipFixture> SeedAsync(DbContextOptions<AccountsDbContext> options)
    {
        await using var db = new AccountsDbContext(options);
        var tenantA = new Tenant { Name = "Tenant A", Slug = $"tenant-a-{Guid.NewGuid():N}" };
        var tenantB = new Tenant { Name = "Tenant B", Slug = $"tenant-b-{Guid.NewGuid():N}" };
        var companyA = CompanyFor(tenantA, "Company A Limited");
        var companyB = CompanyFor(tenantB, "Company B Limited");
        db.Companies.AddRange(companyA, companyB);
        await db.SaveChangesAsync();

        var periodA = PeriodFor(companyA.Id, 2025);
        var periodB = PeriodFor(companyB.Id, 2025);
        var bankA = BankFor(companyA.Id, "Bank A");
        var bankB = BankFor(companyB.Id, "Bank B");
        var categoryA = CategoryFor(companyA.Id, "4000", "Sales A");
        var categoryB = CategoryFor(companyB.Id, "4000", "Sales B");
        var globalCategory = new AccountCategory
        {
            CompanyId = null,
            Code = "9990",
            Name = "Global system category",
            Type = AccountCategoryType.Expense,
            IsSystem = true
        };
        db.AddRange(periodA, periodB, bankA, bankB, categoryA, categoryB, globalCategory);
        await db.SaveChangesAsync();

        var batchA = new ImportBatch { BankAccountId = bankA.Id, Filename = "a.csv" };
        var batchB = new ImportBatch { BankAccountId = bankB.Id, Filename = "b.csv" };
        db.ImportBatches.AddRange(batchA, batchB);
        await db.SaveChangesAsync();

        var transactionA = new ImportedTransaction
        {
            BankAccountId = bankA.Id,
            PeriodId = periodA.Id,
            ImportBatchId = batchA.Id,
            CategoryId = categoryA.Id,
            Date = new DateOnly(2025, 1, 2),
            Description = "Valid transaction A",
            Amount = 1m
        };
        var croA = new CroFilingPackage { PeriodId = periodA.Id };
        var revenueA = new RevenueFilingPackage { PeriodId = periodA.Id };
        var charityA = new CharityFilingPackage { PeriodId = periodA.Id };
        var revenueB = new RevenueFilingPackage { PeriodId = periodB.Id };
        db.AddRange(transactionA, croA, revenueA, charityA, revenueB);
        await db.SaveChangesAsync();

        return new OwnershipFixture(
            tenantA.Id,
            tenantB.Id,
            companyA.Id,
            companyB.Id,
            periodA.Id,
            periodB.Id,
            bankA.Id,
            bankB.Id,
            batchA.Id,
            batchB.Id,
            categoryA.Id,
            categoryB.Id,
            globalCategory.Id,
            transactionA.Id,
            croA.Id,
            revenueA.Id,
            revenueB.Id,
            charityA.Id);
    }

    private static Company CompanyFor(Tenant? tenant, string name) => new()
    {
        Tenant = tenant,
        LegalName = name,
        CompanyType = CompanyType.Private,
        IncorporationDate = new DateOnly(2025, 1, 1),
        AnnualReturnDate = new DateOnly(2024, 9, 15),
        IsTrading = true
    };

    private static AccountingPeriod PeriodFor(int companyId, int year) => new()
    {
        CompanyId = companyId,
        PeriodStart = new DateOnly(year, 1, 1),
        PeriodEnd = new DateOnly(year, 12, 31),
        IsFirstYear = true
    };

    private static BankAccount BankFor(int companyId, string name) => new()
    {
        CompanyId = companyId,
        Name = name,
        Currency = "EUR",
        OpeningBalance = 0m
    };

    private static AccountCategory CategoryFor(int companyId, string code, string name) => new()
    {
        CompanyId = companyId,
        Code = code,
        Name = name,
        Type = AccountCategoryType.Income
    };

    private sealed record OwnershipFixture(
        int TenantAId,
        int TenantBId,
        int CompanyAId,
        int CompanyBId,
        int PeriodAId,
        int PeriodBId,
        int BankAId,
        int BankBId,
        int BatchAId,
        int BatchBId,
        int CategoryAId,
        int CategoryBId,
        int GlobalCategoryId,
        int TransactionAId,
        int CroPackageAId,
        int RevenuePackageAId,
        int RevenuePackageBId,
        int CharityPackageAId);
}

public sealed class PersistenceOwnershipPostgresTests
{
    private const string ConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";

    [PostgresFact]
    public async Task RawPostgresWrites_EnforceOwnershipForeignKeysAndTriggers()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(ConnectionEnvVar)!;
        var schemaName = $"ownership_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(baseConnectionString);
        await admin.OpenAsync();
        await using (var create = admin.CreateCommand())
        {
            create.CommandText = $"CREATE SCHEMA \"{schemaName}\"";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var scopedConnection = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                SearchPath = schemaName
            };
            var options = new DbContextOptionsBuilder<AccountsDbContext>()
                .UseNpgsql(scopedConnection.ConnectionString)
                .Options;
            await using var db = new AccountsDbContext(options);
            await db.Database.MigrateAsync();
            var fixture = await SeedPostgresAsync(db);

            await AssertForeignKeyViolationAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO bank_accounts ("CompanyId", "Name", "Currency", "OpeningBalance")
                VALUES ({int.MaxValue}, 'Missing owner', 'EUR', 0)
                """));

            await AssertCheckViolationAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE accounting_periods SET "CompanyId" = {fixture.CompanyBId} WHERE "Id" = {fixture.PeriodAId}
                """));
            await AssertCheckViolationAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE bank_accounts SET "CompanyId" = {fixture.CompanyBId} WHERE "Id" = {fixture.BankAId}
                """));
            await AssertCheckViolationAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE import_batches SET "BankAccountId" = {fixture.BankBId} WHERE "Id" = {fixture.BatchAId}
                """));
            await AssertCheckViolationAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cro_filing_packages SET "PeriodId" = {fixture.PeriodBId} WHERE "Id" = {fixture.CroPackageAId}
                """));
            await AssertCheckViolationAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE revenue_filing_packages SET "PeriodId" = {fixture.PeriodBId} WHERE "Id" = {fixture.RevenuePackageAId}
                """));
            await AssertCheckViolationAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE charity_filing_packages SET "PeriodId" = {fixture.PeriodBId} WHERE "Id" = {fixture.CharityPackageAId}
                """));

            await AssertCheckViolationAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE imported_transactions SET "PeriodId" = {fixture.PeriodBId} WHERE "Id" = {fixture.TransactionAId}
                """));
            await AssertCheckViolationAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE imported_transactions SET "ImportBatchId" = {fixture.BatchBId} WHERE "Id" = {fixture.TransactionAId}
                """));
            await AssertCheckViolationAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE imported_transactions SET "CategoryId" = {fixture.CategoryBId} WHERE "Id" = {fixture.TransactionAId}
                """));
            await AssertCheckViolationAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE account_categories SET "ParentId" = {fixture.CategoryBId} WHERE "Id" = {fixture.CategoryAId}
                """));
            await AssertCheckViolationAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO account_categories ("Code", "Name", "Type", "TaxTreatment", "IsSystem", "IsNonTradingIncome")
                VALUES ('BADG', 'Invalid global category', 'Expense', 'Deductible', FALSE, FALSE)
                """));
            await AssertCheckViolationAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO transaction_rules ("CompanyId", "Pattern", "CategoryId", "Priority")
                VALUES ({fixture.CompanyAId}, 'invalid', {fixture.CategoryBId}, 1)
                """));
            await AssertCheckViolationAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE filing_deadlines SET "CompanyId" = {fixture.CompanyBId} WHERE "Id" = {fixture.DeadlineAId}
                """));
            await AssertCheckViolationAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE filing_histories SET "CompanyId" = {fixture.CompanyBId} WHERE "Id" = {fixture.HistoryAId}
                """));

            await AssertCheckViolationAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO audit_logs
                    ("CompanyId", "PeriodId", "TenantId", "EntityType", "EntityId", "Action", "Timestamp")
                VALUES
                    ({fixture.CompanyAId}, {fixture.PeriodAId}, {fixture.TenantBId}, 'RawSql', 1, 'InvalidScope', NOW())
                """));
            await AssertCheckViolationAsync(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO audit_integrity_checkpoints
                    ("CompanyId", "TenantId", "LastAuditLogId", "LastIntegrityHash", "CheckedEntries",
                     "CreatedAtUtc", "KeyId", "Signature")
                VALUES
                    ({fixture.CompanyBId}, {fixture.TenantBId}, {fixture.AuditAId}, {fixture.AuditAHash}, 1,
                     NOW(), 'raw-test', {new string('c', 64)})
                """));

            db.ChangeTracker.Clear();
            db.ImportedTransactions.Add(new ImportedTransaction
            {
                BankAccountId = fixture.BankAId,
                PeriodId = fixture.PeriodAId,
                ImportBatchId = fixture.BatchAId,
                CategoryId = fixture.GlobalCategoryId,
                Date = new DateOnly(2025, 2, 1),
                Description = "Valid global category control",
                Amount = 5m
            });
            await db.SaveChangesAsync();
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var drop = admin.CreateCommand();
            drop.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task<PostgresFixture> SeedPostgresAsync(AccountsDbContext db)
    {
        var tenantA = new Tenant { Name = "Tenant A", Slug = $"pg-a-{Guid.NewGuid():N}" };
        var tenantB = new Tenant { Name = "Tenant B", Slug = $"pg-b-{Guid.NewGuid():N}" };
        var companyA = CompanyFor(tenantA, "Postgres A Limited");
        var companyB = CompanyFor(tenantB, "Postgres B Limited");
        db.Companies.AddRange(companyA, companyB);
        await db.SaveChangesAsync();

        var periodA = PeriodFor(companyA.Id);
        var periodB = PeriodFor(companyB.Id);
        var bankA = BankFor(companyA.Id, "A bank");
        var bankB = BankFor(companyB.Id, "B bank");
        var categoryA = CategoryFor(companyA.Id, "4000", "A category");
        var categoryB = CategoryFor(companyB.Id, "4000", "B category");
        var global = new AccountCategory
        {
            Code = "9900",
            Name = "Global system",
            Type = AccountCategoryType.Expense,
            IsSystem = true
        };
        db.AddRange(periodA, periodB, bankA, bankB, categoryA, categoryB, global);
        await db.SaveChangesAsync();

        var batchA = new ImportBatch { BankAccountId = bankA.Id, Filename = "a.csv" };
        var batchB = new ImportBatch { BankAccountId = bankB.Id, Filename = "b.csv" };
        db.AddRange(batchA, batchB);
        await db.SaveChangesAsync();

        var transaction = new ImportedTransaction
        {
            BankAccountId = bankA.Id,
            PeriodId = periodA.Id,
            ImportBatchId = batchA.Id,
            CategoryId = categoryA.Id,
            Date = new DateOnly(2025, 1, 1),
            Description = "Valid transaction",
            Amount = 1m
        };
        var cro = new CroFilingPackage { PeriodId = periodA.Id };
        var revenue = new RevenueFilingPackage { PeriodId = periodA.Id };
        var charity = new CharityFilingPackage { PeriodId = periodA.Id };
        var deadline = new FilingDeadline
        {
            CompanyId = companyA.Id,
            PeriodId = periodA.Id,
            DeadlineType = DeadlineType.CRO,
            DueDate = new DateOnly(2026, 9, 30)
        };
        var history = new FilingHistory
        {
            CompanyId = companyA.Id,
            PeriodId = periodA.Id,
            DeadlineType = DeadlineType.Revenue,
            DueDate = new DateOnly(2026, 9, 23),
            FiledDate = new DateOnly(2026, 9, 20)
        };
        db.AddRange(transaction, cro, revenue, charity, deadline, history);
        await db.SaveChangesAsync();

        await new AuditService(db).LogAsync(
            companyA.Id,
            periodA.Id,
            "AccountingPeriod",
            periodA.Id,
            "RawPostgresAnchor");
        var audit = await db.AuditLogs.SingleAsync(log => log.Action == "RawPostgresAnchor");

        return new PostgresFixture(
            tenantB.Id,
            companyA.Id,
            companyB.Id,
            periodA.Id,
            periodB.Id,
            bankA.Id,
            bankB.Id,
            batchA.Id,
            batchB.Id,
            categoryA.Id,
            categoryB.Id,
            global.Id,
            transaction.Id,
            cro.Id,
            revenue.Id,
            charity.Id,
            deadline.Id,
            history.Id,
            audit.Id,
            audit.IntegrityHash!);
    }

    private static async Task AssertCheckViolationAsync(Func<Task<int>> action)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.False(string.IsNullOrWhiteSpace(error.ConstraintName));
    }

    private static async Task AssertForeignKeyViolationAsync(Func<Task<int>> action)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    private static Company CompanyFor(Tenant tenant, string name) => new()
    {
        Tenant = tenant,
        LegalName = name,
        CompanyType = CompanyType.Private,
        IncorporationDate = new DateOnly(2025, 1, 1),
        AnnualReturnDate = new DateOnly(2024, 9, 15),
        IsTrading = true
    };

    private static AccountingPeriod PeriodFor(int companyId) => new()
    {
        CompanyId = companyId,
        PeriodStart = new DateOnly(2025, 1, 1),
        PeriodEnd = new DateOnly(2025, 12, 31),
        IsFirstYear = true
    };

    private static BankAccount BankFor(int companyId, string name) => new()
    {
        CompanyId = companyId,
        Name = name,
        OpeningBalance = 0m
    };

    private static AccountCategory CategoryFor(int companyId, string code, string name) => new()
    {
        CompanyId = companyId,
        Code = code,
        Name = name,
        Type = AccountCategoryType.Income
    };

    private sealed record PostgresFixture(
        int TenantBId,
        int CompanyAId,
        int CompanyBId,
        int PeriodAId,
        int PeriodBId,
        int BankAId,
        int BankBId,
        int BatchAId,
        int BatchBId,
        int CategoryAId,
        int CategoryBId,
        int GlobalCategoryId,
        int TransactionAId,
        int CroPackageAId,
        int RevenuePackageAId,
        int CharityPackageAId,
        int DeadlineAId,
        int HistoryAId,
        int AuditAId,
        string AuditAHash);

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionEnvVar)))
                Skip = $"{ConnectionEnvVar} is not set.";
        }
    }
}
