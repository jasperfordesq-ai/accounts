using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounts.Tests;

public sealed class CompanyQuarantineTests
{
    private const string QuarantineReason = "Owner-requested quarantine after the retained engagement closure review.";
    private const string RecoveryReason = "Owner-approved recovery after the engagement was formally reopened.";

    public static IEnumerable<object[]> OwnedTableNames() =>
        CompanyDependentInventoryService.RequiredTableNames
            .Where(name => name != "companies")
            .Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(OwnedTableNames))]
    public async Task EveryOwnedTableRequiresOwnerConfirmationAndIsRetainedInImmutableInventory(string tableName)
    {
        var options = OptionsFor(Guid.NewGuid().ToString("N"));
        await using var db = new AccountsDbContext(options);
        var fixture = await SeedBaseAsync(db);
        await SeedOwnedTableAsync(db, fixture, tableName);
        var before = await new CompanyDependentInventoryService(db).CaptureAsync(fixture.CompanyId);
        Assert.True(before.TableCounts[tableName] > 0, $"Seeder did not populate {tableName}.");

        var apiAccess = DisabledApiAccess();
        var reviewer = await CompanyDeletionEndpoint.DeleteAsync(
            fixture.CompanyId,
            new(fixture.LegalName, QuarantineReason),
            Request(Actor(fixture.TenantId, "Reviewer"), HttpMethods.Delete, $"/api/companies/{fixture.CompanyId}"),
            apiAccess,
            db,
            new AccountingWriteGuard(db),
            new AuditService(db));
        Assert.Equal(StatusCodes.Status403Forbidden, StatusCode(reviewer));
        var wrongConfirmation = await CompanyDeletionEndpoint.DeleteAsync(
            fixture.CompanyId,
            new("wrong legal name", QuarantineReason),
            Request(Actor(fixture.TenantId), HttpMethods.Delete, $"/api/companies/{fixture.CompanyId}"),
            apiAccess,
            db,
            new AccountingWriteGuard(db),
            new AuditService(db));
        Assert.Equal(StatusCodes.Status400BadRequest, StatusCode(wrongConfirmation));
        Assert.False((await db.Companies.IgnoreQueryFilters().SingleAsync(x => x.Id == fixture.CompanyId)).IsQuarantined);

        var quarantineResult = await CompanyDeletionEndpoint.DeleteAsync(
            fixture.CompanyId,
            new(fixture.LegalName, QuarantineReason),
            Request(Actor(fixture.TenantId), HttpMethods.Delete, $"/api/companies/{fixture.CompanyId}"),
            apiAccess,
            db,
            new AccountingWriteGuard(db),
            new AuditService(db));
        Assert.Equal(StatusCodes.Status200OK, StatusCode(quarantineResult));
        var outcome = Assert.IsType<CompanyQuarantineOutcome>(
            Assert.IsAssignableFrom<IValueHttpResult>(quarantineResult).Value);

        Assert.Equal("Quarantined", outcome.Status);
        Assert.Equal(before.TableCounts[tableName], outcome.Inventory[tableName]);
        db.ChangeTracker.Clear();
        Assert.Null(await db.Companies.SingleOrDefaultAsync(x => x.Id == fixture.CompanyId));
        Assert.True((await db.Companies.IgnoreQueryFilters().SingleAsync(x => x.Id == fixture.CompanyId)).IsQuarantined);
        var after = await new CompanyDependentInventoryService(db).CaptureAsync(fixture.CompanyId);
        Assert.True(after.TableCounts[tableName] >= before.TableCounts[tableName]);

        var evidence = await db.CompanyQuarantineEvents
            .AsNoTracking()
            .SingleAsync(x => x.Id == outcome.EvidenceId);
        Assert.Equal("Owner", evidence.ActorRole);
        Assert.Equal(fixture.LegalName, evidence.TypedConfirmation);
        Assert.Equal(QuarantineReason, evidence.Reason);
        Assert.True(CompanyQuarantineEvidenceIntegrity.IsValid(evidence));
        Assert.Contains($"\"{tableName}\":{before.TableCounts[tableName]}", evidence.InventoryJson, StringComparison.Ordinal);
        Assert.Contains(await db.AuditLogs.AsNoTracking().ToListAsync(), log =>
            log.CompanyId == fixture.CompanyId
            && log.Action == AuditEventCodes.CompanyQuarantined
            && log.UserId == "user:10");
    }

    [Fact]
    public async Task QuarantineFiltersTheWholeOwnershipGraphAndRecoveryRestoresIt()
    {
        var options = OptionsFor(Guid.NewGuid().ToString("N"));
        Fixture fixture;
        await using (var seed = new AccountsDbContext(options))
        {
            fixture = await SeedBaseAsync(seed);
            var bank = new BankAccount { CompanyId = fixture.CompanyId, Name = "Retained bank", OpeningBalance = 0m };
            seed.BankAccounts.Add(bank);
            await seed.SaveChangesAsync();
            seed.ImportedTransactions.Add(new ImportedTransaction
            {
                BankAccountId = bank.Id,
                PeriodId = fixture.PeriodId,
                Date = new DateOnly(2025, 6, 30),
                Description = "Retained transaction",
                Amount = 25m
            });
            await seed.SaveChangesAsync();
            await new CompanyQuarantineService(seed, new AuditService(seed)).QuarantineAsync(
                fixture.CompanyId,
                new(fixture.LegalName, QuarantineReason),
                Actor(fixture.TenantId));
        }

        await using (var hidden = new AccountsDbContext(options))
        {
            Assert.Empty(await hidden.Companies.ToListAsync());
            Assert.Empty(await hidden.AccountingPeriods.ToListAsync());
            Assert.Empty(await hidden.BankAccounts.ToListAsync());
            Assert.Empty(await hidden.ImportedTransactions.ToListAsync());
            Assert.Single(await hidden.Companies.IgnoreQueryFilters().ToListAsync());
            Assert.Single(await hidden.ImportedTransactions.IgnoreQueryFilters().ToListAsync());

            var outcome = await new CompanyQuarantineService(hidden, new AuditService(hidden)).RecoverAsync(
                fixture.CompanyId,
                new(fixture.LegalName, RecoveryReason),
                Actor(fixture.TenantId),
                "recover-test");
            Assert.Equal("Recovered", outcome.Status);
        }

        await using var restored = new AccountsDbContext(options);
        Assert.Single(await restored.Companies.ToListAsync());
        Assert.Single(await restored.AccountingPeriods.ToListAsync());
        Assert.Single(await restored.BankAccounts.ToListAsync());
        Assert.Single(await restored.ImportedTransactions.ToListAsync());
        var evidence = await restored.CompanyQuarantineEvents.OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, evidence.Count);
        Assert.Equal("Quarantined", evidence[0].EventType);
        Assert.Equal("Recovered", evidence[1].EventType);
        Assert.Equal(evidence[0].EvidenceSha256, evidence[1].PreviousEvidenceSha256);
        Assert.All(evidence, item => Assert.True(CompanyQuarantineEvidenceIntegrity.IsValid(item)));
    }

    [Fact]
    public async Task QuarantineRequiresARealReason()
    {
        await using var db = new AccountsDbContext(OptionsFor(Guid.NewGuid().ToString("N")));
        var fixture = await SeedBaseAsync(db);
        var service = new CompanyQuarantineService(db, new AuditService(db));

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.QuarantineAsync(
            fixture.CompanyId,
            new(fixture.LegalName, "too short"),
            Actor(fixture.TenantId)));
        Assert.False((await db.Companies.IgnoreQueryFilters().SingleAsync()).IsQuarantined);
        Assert.Empty(await db.CompanyQuarantineEvents.ToListAsync());
    }

    [Theory]
    [InlineData(PeriodStatus.Draft, true)]
    [InlineData(PeriodStatus.Finalised, false)]
    [InlineData(PeriodStatus.Filed, false)]
    public async Task QuarantineBlocksEveryLockedOrCompletedPeriodState(PeriodStatus status, bool explicitlyLocked)
    {
        await using var db = new AccountsDbContext(OptionsFor(Guid.NewGuid().ToString("N")));
        var fixture = await SeedBaseAsync(db);
        var service = new CompanyQuarantineService(db, new AuditService(db));
        var period = await db.AccountingPeriods.SingleAsync(x => x.Id == fixture.PeriodId);
        period.Status = status;
        period.LockedAt = explicitlyLocked ? DateTime.UtcNow : null;
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.QuarantineAsync(
            fixture.CompanyId,
            new(fixture.LegalName, QuarantineReason),
            Actor(fixture.TenantId)));
        Assert.Contains("locked or", error.Message);
        Assert.False((await db.Companies.IgnoreQueryFilters().SingleAsync()).IsQuarantined);
        Assert.Empty(await db.CompanyQuarantineEvents.ToListAsync());
    }

    [Fact]
    public async Task EndpointIsOwnerOnlyAndCrossTenantRecoveryIsIndistinguishableFromMissing()
    {
        var options = OptionsFor(Guid.NewGuid().ToString("N"));
        await using var db = new AccountsDbContext(options);
        var fixture = await SeedBaseAsync(db);
        var apiAccess = DisabledApiAccess();

        var reviewerResult = await CompanyDeletionEndpoint.DeleteAsync(
            fixture.CompanyId,
            new(fixture.LegalName, QuarantineReason),
            Request(Actor(fixture.TenantId, "Reviewer"), HttpMethods.Delete, $"/api/companies/{fixture.CompanyId}"),
            apiAccess,
            db,
            new AccountingWriteGuard(db),
            new AuditService(db));
        Assert.Equal(StatusCodes.Status403Forbidden, StatusCode(reviewerResult));

        var ownerResult = await CompanyDeletionEndpoint.DeleteAsync(
            fixture.CompanyId,
            new(fixture.LegalName, QuarantineReason),
            Request(Actor(fixture.TenantId), HttpMethods.Delete, $"/api/companies/{fixture.CompanyId}"),
            apiAccess,
            db,
            new AccountingWriteGuard(db),
            new AuditService(db));
        Assert.Equal(StatusCodes.Status200OK, StatusCode(ownerResult));
        db.ChangeTracker.Clear();

        var reviewerList = await CompanyDeletionEndpoint.ListQuarantinedAsync(
            Request(Actor(fixture.TenantId, "Reviewer"), HttpMethods.Get, "/api/companies/quarantined"),
            apiAccess,
            db,
            new AuditService(db));
        Assert.Equal(StatusCodes.Status403Forbidden, StatusCode(reviewerList));
        var ownerList = await CompanyDeletionEndpoint.ListQuarantinedAsync(
            Request(Actor(fixture.TenantId), HttpMethods.Get, "/api/companies/quarantined"),
            apiAccess,
            db,
            new AuditService(db));
        var summaries = Assert.IsAssignableFrom<IReadOnlyList<QuarantinedCompanySummary>>(
            Assert.IsAssignableFrom<IValueHttpResult>(ownerList).Value);
        Assert.Collection(summaries, summary =>
        {
            Assert.Equal(fixture.CompanyId, summary.CompanyId);
            Assert.Equal(fixture.LegalName, summary.LegalName);
            Assert.Equal(QuarantineReason, summary.Reason);
        });

        var foreignRecovery = await CompanyDeletionEndpoint.RecoverAsync(
            fixture.CompanyId,
            new(fixture.LegalName, RecoveryReason),
            Request(Actor(fixture.TenantId + 1), HttpMethods.Post, $"/api/companies/{fixture.CompanyId}/recover"),
            apiAccess,
            db,
            new AuditService(db));
        Assert.Equal(StatusCodes.Status404NotFound, StatusCode(foreignRecovery));

        var reviewerRecovery = await CompanyDeletionEndpoint.RecoverAsync(
            fixture.CompanyId,
            new(fixture.LegalName, RecoveryReason),
            Request(Actor(fixture.TenantId, "Reviewer"), HttpMethods.Post, $"/api/companies/{fixture.CompanyId}/recover"),
            apiAccess,
            db,
            new AuditService(db));
        Assert.Equal(StatusCodes.Status403Forbidden, StatusCode(reviewerRecovery));

        var ownerRecovery = await CompanyDeletionEndpoint.RecoverAsync(
            fixture.CompanyId,
            new(fixture.LegalName, RecoveryReason),
            Request(Actor(fixture.TenantId), HttpMethods.Post, $"/api/companies/{fixture.CompanyId}/recover"),
            apiAccess,
            db,
            new AuditService(db));
        Assert.Equal(StatusCodes.Status200OK, StatusCode(ownerRecovery));
        db.ChangeTracker.Clear();
        Assert.NotNull(await db.Companies.SingleOrDefaultAsync(company => company.Id == fixture.CompanyId));
    }

    [Fact]
    public async Task QuarantineEvidenceIsAppendOnlyInTheApplicationModel()
    {
        var options = OptionsFor(Guid.NewGuid().ToString("N"));
        await using var db = new AccountsDbContext(options);
        var fixture = await SeedBaseAsync(db);
        await new CompanyQuarantineService(db, new AuditService(db)).QuarantineAsync(
            fixture.CompanyId,
            new(fixture.LegalName, QuarantineReason),
            Actor(fixture.TenantId));

        var evidence = await db.CompanyQuarantineEvents.SingleAsync();
        evidence.Reason = "Attempted evidence rewrite that must never be persisted.";
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.SaveChangesAsync());
        db.Entry(evidence).State = EntityState.Deleted;
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task QuarantineStateCannotBeToggledWithoutItsMatchingImmutableEvidenceEvent()
    {
        var options = OptionsFor(Guid.NewGuid().ToString("N"));
        await using var db = new AccountsDbContext(options);
        var fixture = await SeedBaseAsync(db);
        var company = await db.Companies.SingleAsync(item => item.Id == fixture.CompanyId);
        var occurredAtUtc = DateTime.UtcNow;
        company.IsQuarantined = true;
        company.QuarantinedAtUtc = occurredAtUtc;
        company.QuarantinedByUserId = "rogue@example.ie";
        company.QuarantinedByDisplayName = "Rogue code path";
        company.QuarantineReason = "Attempted quarantine without immutable evidence.";
        company.QuarantineEvidenceSha256 = new string('a', 64);
        company.UpdatedAt = occurredAtUtc;

        var quarantineError = await Assert.ThrowsAsync<BusinessRuleException>(() => db.SaveChangesAsync());
        Assert.Contains("matching immutable evidence event", quarantineError.Message);
        db.ChangeTracker.Clear();

        await new CompanyQuarantineService(db, new AuditService(db)).QuarantineAsync(
            fixture.CompanyId,
            new(fixture.LegalName, QuarantineReason),
            Actor(fixture.TenantId));
        db.ChangeTracker.Clear();

        company = await db.Companies.IgnoreQueryFilters().SingleAsync(item => item.Id == fixture.CompanyId);
        company.IsQuarantined = false;
        company.QuarantinedAtUtc = null;
        company.QuarantinedByUserId = null;
        company.QuarantinedByDisplayName = null;
        company.QuarantineReason = null;
        company.QuarantineEvidenceSha256 = null;
        company.UpdatedAt = DateTime.UtcNow;

        var recoveryError = await Assert.ThrowsAsync<BusinessRuleException>(() => db.SaveChangesAsync());
        Assert.Contains("matching immutable evidence event", recoveryError.Message);
        Assert.Single(await db.CompanyQuarantineEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public void InventoryRegistryMatchesEveryCompanyOrPeriodOwnedDbSetInTheEfModel()
    {
        using var db = new AccountsDbContext(OptionsFor(Guid.NewGuid().ToString("N")));
        var registered = CompanyDependentInventoryService.RequiredTableNames.ToHashSet(StringComparer.Ordinal);
        var ownedTables = db.Model.GetEntityTypes()
            .Where(type => IsCompanyOwned(type.ClrType))
            .Select(type => type.GetTableName())
            .Where(name => name is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        var missing = ownedTables
            .Where(name => !registered.Contains(name))
            .OrderBy(name => name)
            .ToArray();
        var unknown = registered
            .Where(name => !ownedTables.Contains(name))
            .OrderBy(name => name)
            .ToArray();
        Assert.Empty(missing);
        Assert.Empty(unknown);
        Assert.Equal(registered.Count, CompanyDependentInventoryService.RequiredTableNames.Count);
    }

    private static bool IsCompanyOwned(Type type)
    {
        if (type == typeof(Tenant) || type == typeof(UserAccount)) return false;
        return type == typeof(Company)
            || type == typeof(CompanyQuarantineEvent)
            || type.GetProperty("CompanyId") is not null
            || type.GetProperty("PeriodId") is not null
            || type == typeof(SizeClassification)
            || type == typeof(FilingRegime)
            || type == typeof(ImportBatch)
            || type == typeof(ImportedTransaction)
            || type == typeof(LoanBalanceSnapshot)
            || type == typeof(DirectorLoanMovement);
    }

    private static async Task SeedOwnedTableAsync(AccountsDbContext db, Fixture f, string table)
    {
        switch (table)
        {
            case "accounting_periods":
                return;
            case "user_company_accesses":
            {
                var user = new UserAccount
                {
                    TenantId = f.TenantId,
                    Email = "assigned@example.ie",
                    DisplayName = "Assigned User",
                    Role = "Client",
                    PasswordHash = "hash",
                    PasswordSalt = "salt"
                };
                db.UserAccounts.Add(user);
                await db.SaveChangesAsync();
                db.UserCompanyAccesses.Add(new UserCompanyAccess { UserId = user.Id, CompanyId = f.CompanyId });
                break;
            }
            case "company_officers":
                db.CompanyOfficers.Add(new CompanyOfficer { CompanyId = f.CompanyId, Name = "Director One", Role = OfficerRole.Director });
                break;
            case "size_classifications":
                db.SizeClassifications.Add(new SizeClassification { PeriodId = f.PeriodId, Turnover = 1m, BalanceSheetTotal = 1m, AvgEmployees = 1 });
                break;
            case "filing_regimes":
                db.FilingRegimes.Add(new FilingRegime { PeriodId = f.PeriodId, ElectedRegime = ElectedRegime.Full });
                break;
            case "cro_filing_packages":
                db.CroFilingPackages.Add(new CroFilingPackage { PeriodId = f.PeriodId });
                break;
            case "revenue_filing_packages":
                db.RevenueFilingPackages.Add(new RevenueFilingPackage { PeriodId = f.PeriodId });
                break;
            case "charity_filing_packages":
                db.CharityFilingPackages.Add(new CharityFilingPackage { PeriodId = f.PeriodId });
                break;
            case "filing_authority_engagements":
                await EnsureFilingAuthorityAsync(db, f);
                return;
            case "external_filing_handoff_snapshots":
                await EnsureExternalFilingSnapshotAsync(db, f);
                return;
            case "external_filing_outcome_events":
            {
                var snapshot = await EnsureExternalFilingSnapshotAsync(db, f);
                db.ExternalFilingOutcomeEvents.Add(new ExternalFilingOutcomeEvent
                {
                    TenantId = f.TenantId,
                    CompanyId = f.CompanyId,
                    PeriodId = f.PeriodId,
                    SnapshotRecordId = snapshot.Id,
                    SnapshotId = snapshot.SnapshotId,
                    SnapshotArtifactSha256 = snapshot.ArtifactSha256,
                    Sequence = 1,
                    Outcome = "ReadyForManualHandoff",
                    RecordedByUserId = "user:10",
                    RecordedByDisplayName = "Owner User",
                    RecordedByRole = "Owner",
                    RecordedAtUtc = DateTime.UtcNow,
                    EventSha256 = new string('e', 64)
                });
                break;
            }
            case "bank_accounts":
                db.BankAccounts.Add(new BankAccount { CompanyId = f.CompanyId, Name = "Bank", OpeningBalance = 0m });
                break;
            case "import_batches":
            {
                var bank = await EnsureBankAsync(db, f.CompanyId);
                db.ImportBatches.Add(new ImportBatch { BankAccountId = bank.Id, Filename = "bank.csv" });
                break;
            }
            case "imported_transactions":
            {
                var bank = await EnsureBankAsync(db, f.CompanyId);
                db.ImportedTransactions.Add(new ImportedTransaction { BankAccountId = bank.Id, PeriodId = f.PeriodId, Date = new(2025, 1, 2), Description = "Transaction", Amount = 1m });
                break;
            }
            case "transaction_rules":
            {
                var category = await EnsureCategoryAsync(db, f.CompanyId, "1000", AccountCategoryType.Asset);
                db.TransactionRules.Add(new TransactionRule { CompanyId = f.CompanyId, CategoryId = category.Id, Pattern = "test" });
                break;
            }
            case "account_categories":
                db.AccountCategories.Add(new AccountCategory { CompanyId = f.CompanyId, Code = "1000", Name = "Cash", Type = AccountCategoryType.Asset });
                break;
            case "debtors":
                db.Debtors.Add(new Debtor { PeriodId = f.PeriodId, Name = "Debtor", Amount = 1m });
                break;
            case "creditors":
                db.Creditors.Add(new Creditor { PeriodId = f.PeriodId, Name = "Creditor", Amount = 1m });
                break;
            case "fixed_assets":
                db.FixedAssets.Add(NewAsset(f.CompanyId));
                break;
            case "depreciation_entries":
            {
                var asset = await EnsureAssetAsync(db, f.CompanyId);
                db.DepreciationEntries.Add(new DepreciationEntry { AssetId = asset.Id, PeriodId = f.PeriodId, OpeningNbv = 1m, Charge = 1m, ClosingNbv = 0m });
                break;
            }
            case "capital_allowance_claims":
            {
                var asset = await EnsureAssetAsync(db, f.CompanyId);
                db.CapitalAllowanceClaims.Add(new CapitalAllowanceClaim { AssetId = asset.Id, PeriodId = f.PeriodId, Cost = 1m, Claim = 1m });
                break;
            }
            case "inventories":
                db.Inventories.Add(new Inventory { PeriodId = f.PeriodId, Description = "Inventory", Value = 1m });
                break;
            case "loans":
                db.Loans.Add(NewLoan(f.CompanyId));
                break;
            case "loan_balance_snapshots":
            {
                var loan = await EnsureLoanAsync(db, f.CompanyId);
                db.LoanBalanceSnapshots.Add(new LoanBalanceSnapshot { LoanId = loan.Id, PeriodId = f.PeriodId, ClosingBalance = 1m, DueWithinYear = 1m });
                break;
            }
            case "director_loans":
            {
                var director = new CompanyOfficer { CompanyId = f.CompanyId, Name = "Director", Role = OfficerRole.Director };
                db.CompanyOfficers.Add(director);
                await db.SaveChangesAsync();
                db.DirectorLoans.Add(new DirectorLoan { PeriodId = f.PeriodId, DirectorId = director.Id, ClosingBalance = 1m });
                break;
            }
            case "director_loan_movements":
            {
                var director = new CompanyOfficer { CompanyId = f.CompanyId, Name = "Movement Director", Role = OfficerRole.Director };
                db.CompanyOfficers.Add(director);
                await db.SaveChangesAsync();
                var directorLoan = new DirectorLoan { PeriodId = f.PeriodId, DirectorId = director.Id, ClosingBalance = 1m };
                db.DirectorLoans.Add(directorLoan);
                await db.SaveChangesAsync();
                db.DirectorLoanMovements.Add(new DirectorLoanMovement
                {
                    DirectorLoanId = directorLoan.Id,
                    MovementDate = new DateOnly(2025, 6, 30),
                    MovementType = DirectorLoanMovementType.Advance,
                    Amount = 1m,
                    EvidenceReference = "INVENTORY-DIRECTOR-LOAN-MOVEMENT"
                });
                break;
            }
            case "payroll_summaries":
                db.PayrollSummaries.Add(new PayrollSummary { PeriodId = f.PeriodId, GrossWages = 1m, StaffCount = 1 });
                break;
            case "corporation_tax_scope_reviews":
            case "corporation_tax_loss_records":
                await EnsureCorporationTaxScopeAsync(db, f);
                return;
            case "corporation_tax_filing_support_reviews":
                db.CorporationTaxFilingSupportReviews.Add(new CorporationTaxFilingSupportReview
                {
                    PeriodId = f.PeriodId,
                    PreparedBy = "Owner User",
                    PreparedAtUtc = DateTime.UtcNow,
                    EvidenceNote = "Inventory coverage fixture for retained filing support."
                });
                break;
            case "corporation_tax_payment_records":
                db.CorporationTaxPaymentRecords.Add(new CorporationTaxPaymentRecord
                {
                    PeriodId = f.PeriodId,
                    PaymentDate = new DateOnly(2025, 9, 23),
                    Amount = 1m,
                    Kind = CorporationTaxPaymentKind.PreliminarySecondOrSingle,
                    EvidenceReference = "INVENTORY-CT-PAYMENT",
                    RecordedBy = "Owner User",
                    RecordedAtUtc = DateTime.UtcNow
                });
                break;
            case "tax_balances":
                db.TaxBalances.Add(new TaxBalance { PeriodId = f.PeriodId, Liability = 1m, Balance = 1m });
                break;
            case "dividends":
                db.Dividends.Add(new Dividend { PeriodId = f.PeriodId, Amount = 1m });
                break;
            case "opening_balances":
            {
                var category = await EnsureCategoryAsync(db, f.CompanyId, "1000", AccountCategoryType.Asset);
                db.OpeningBalances.Add(new OpeningBalance { PeriodId = f.PeriodId, AccountCategoryId = category.Id, Debit = 1m });
                break;
            }
            case "year_end_review_confirmations":
                db.YearEndReviewConfirmations.Add(new YearEndReviewConfirmation { PeriodId = f.PeriodId, SectionKey = "inventory" });
                break;
            case "adjustments":
            {
                var debit = await EnsureCategoryAsync(db, f.CompanyId, "1000", AccountCategoryType.Asset);
                var credit = await EnsureCategoryAsync(db, f.CompanyId, "2000", AccountCategoryType.Liability);
                db.Adjustments.Add(new Adjustment { PeriodId = f.PeriodId, Description = "Journal", DebitCategoryId = debit.Id, CreditCategoryId = credit.Id, Amount = 1m });
                break;
            }
            case "reports":
                db.Reports.Add(new Report { PeriodId = f.PeriodId, Type = ReportType.TrialBalance });
                break;
            case "notes_disclosures":
                db.NotesDisclosures.Add(new NotesDisclosure { PeriodId = f.PeriodId, NoteNumber = 1, Title = "Note" });
                break;
            case "share_capitals":
                db.ShareCapitals.Add(new ShareCapital { CompanyId = f.CompanyId, IssueDate = new(2025, 1, 1) });
                break;
            case "filing_deadlines":
                db.FilingDeadlines.Add(new FilingDeadline { CompanyId = f.CompanyId, PeriodId = f.PeriodId, DeadlineType = DeadlineType.CRO, CalculatedDueDate = new(2026, 9, 30), DueDate = new(2026, 9, 30) });
                break;
            case "filing_histories":
                db.FilingHistories.Add(new FilingHistory { CompanyId = f.CompanyId, PeriodId = f.PeriodId, DeadlineType = DeadlineType.CRO, DueDate = new(2026, 9, 30), FiledDate = new(2026, 9, 30) });
                break;
            case "deadline_reminder_outbox":
            {
                var deadline = new FilingDeadline
                {
                    CompanyId = f.CompanyId,
                    PeriodId = f.PeriodId,
                    DeadlineType = DeadlineType.CRO,
                    CalculatedDueDate = new(2026, 9, 30),
                    DueDate = new(2026, 9, 30)
                };
                db.FilingDeadlines.Add(deadline);
                await db.SaveChangesAsync();
                db.DeadlineReminderOutbox.Add(new DeadlineReminderOutbox
                {
                    Id = Guid.NewGuid(),
                    TenantId = f.TenantId,
                    CompanyId = f.CompanyId,
                    PeriodId = f.PeriodId,
                    FilingDeadlineId = deadline.Id,
                    DeadlineType = DeadlineType.CRO,
                    ReminderKind = DeadlineReminderKind.DueSoon,
                    State = DeadlineReminderState.Pending,
                    ObservedDueDate = deadline.DueDate,
                    DeduplicationKeySha256 = new string('d', 64),
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                    NextAttemptAtUtc = DateTime.UtcNow
                });
                break;
            }
            case "post_balance_sheet_events":
                db.PostBalanceSheetEvents.Add(new PostBalanceSheetEvent { PeriodId = f.PeriodId, Description = "Event", EventDate = new(2026, 1, 2) });
                break;
            case "related_party_transactions":
                db.RelatedPartyTransactions.Add(new RelatedPartyTransaction { PeriodId = f.PeriodId, PartyName = "Party", Relationship = "Director", TransactionType = "Loan", Amount = 1m });
                break;
            case "contingent_liabilities":
                db.ContingentLiabilities.Add(new ContingentLiability { PeriodId = f.PeriodId, Description = "Claim", Nature = "Legal", Likelihood = "Possible" });
                break;
            case "charity_infos":
                db.CharityInfos.Add(new CharityInfo { CompanyId = f.CompanyId, CharityNumber = "20000000" });
                break;
            case "fund_balances":
                db.FundBalances.Add(new FundBalance { PeriodId = f.PeriodId, FundName = "General", ClosingBalance = 1m });
                break;
            case "audit_logs":
                await new AuditService(db).LogAsync(f.CompanyId, f.PeriodId, "Company", f.CompanyId, "SeedAudit", tenantId: f.TenantId);
                return;
            case "audit_integrity_checkpoints":
            {
                await new AuditService(db).LogAsync(f.CompanyId, f.PeriodId, "Company", f.CompanyId, "SeedAudit", tenantId: f.TenantId);
                var audit = await db.AuditLogs.OrderByDescending(x => x.Id).FirstAsync();
                db.AuditIntegrityCheckpoints.Add(new AuditIntegrityCheckpoint
                {
                    CompanyId = f.CompanyId,
                    TenantId = f.TenantId,
                    LastAuditLogId = audit.Id,
                    LastIntegrityHash = audit.IntegrityHash!,
                    CheckedEntries = 1,
                    KeyId = "test-key",
                    Signature = Guid.NewGuid().ToString("N")
                });
                break;
            }
            case "annual_return_date_records":
            {
                var company = await db.Companies.SingleAsync(candidate => candidate.Id == f.CompanyId);
                var replacement = (company.AnnualReturnDate ?? new DateOnly(2026, 9, 15)).AddDays(1);
                await new AnnualReturnDateService(db, new AuditService(db)).RecordChangeAsync(
                    f.CompanyId,
                    new AnnualReturnDateChangeInput(
                        replacement,
                        replacement,
                        AnnualReturnDateSource.CroRecord,
                        "CRO-INVENTORY-ARD",
                        new string('a', 64),
                        "Exact ARD changed for dependent inventory coverage testing."),
                    Actor(f.TenantId));
                return;
            }
            case "company_quarantine_events":
            {
                var service = new CompanyQuarantineService(db, new AuditService(db));
                await service.QuarantineAsync(f.CompanyId, new(f.LegalName, QuarantineReason), Actor(f.TenantId));
                await service.RecoverAsync(f.CompanyId, new(f.LegalName, RecoveryReason), Actor(f.TenantId));
                return;
            }
            case "company_onboarding_requests":
            {
                var bank = await EnsureBankAsync(db, f.CompanyId);
                var request = new CompanyOnboardingRequest
                {
                    TenantId = f.TenantId,
                    IdempotencyKey = $"inventory-{Guid.NewGuid():N}",
                    RequestSha256 = new string('a', 64),
                    Status = "InProgress",
                    CreatedByUserId = "owner@example.ie",
                    CreatedByDisplayName = "Owner User",
                    StartedAtUtc = DateTime.UtcNow
                };
                db.CompanyOnboardingRequests.Add(request);
                await db.SaveChangesAsync();
                request.Status = "Completed";
                request.CompanyId = f.CompanyId;
                request.PeriodId = f.PeriodId;
                request.BankAccountId = bank.Id;
                request.CategoryCount = 1;
                request.CompletedAtUtc = DateTime.UtcNow;
                request.ResponseJson = "{}";
                request.ResponseSha256 = Convert.ToHexStringLower(
                    System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.ResponseJson)));
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(table), table, "Missing owned-table seeder.");
        }
        await db.SaveChangesAsync();
    }

    private static async Task<BankAccount> EnsureBankAsync(AccountsDbContext db, int companyId)
    {
        var existing = await db.BankAccounts.FirstOrDefaultAsync(x => x.CompanyId == companyId);
        if (existing is not null) return existing;
        var bank = new BankAccount { CompanyId = companyId, Name = "Bank", OpeningBalance = 0m };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        return bank;
    }

    private static async Task<FilingAuthorityEngagement> EnsureFilingAuthorityAsync(
        AccountsDbContext db,
        Fixture fixture)
    {
        var existing = await db.FilingAuthorityEngagements
            .FirstOrDefaultAsync(item => item.CompanyId == fixture.CompanyId);
        if (existing is not null) return existing;

        byte[] artifact = [1, 2, 3, 4];
        var authority = new FilingAuthorityEngagement
        {
            TenantId = fixture.TenantId,
            CompanyId = fixture.CompanyId,
            Version = 1,
            Workflow = "CRO",
            Kind = "Presenter",
            Status = "Active",
            LegalName = fixture.LegalName,
            AuthorityScope = "Manual CRO handoff only",
            EngagementReference = "INVENTORY-AUTHORITY",
            ExternalAuthorityReference = "INVENTORY-EXTERNAL-AUTHORITY",
            EffectiveFromUtc = DateTime.UtcNow,
            AuthorityEvidenceArtifact = artifact,
            AuthorityEvidenceSha256 = Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(artifact)),
            EvidenceMediaType = "application/pdf",
            EvidenceFileName = "authority.pdf",
            ReviewedByUserId = "user:10",
            ReviewedByDisplayName = "Owner User",
            ReviewedByRole = "Owner",
            ReviewedAtUtc = DateTime.UtcNow,
            ReleaseCandidate = "inventory-test",
            RecordSha256 = new string('a', 64),
            CreatedByUserId = "user:10",
            CreatedByDisplayName = "Owner User",
            CreatedByRole = "Owner",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.FilingAuthorityEngagements.Add(authority);
        await db.SaveChangesAsync();
        return authority;
    }

    private static async Task EnsureCorporationTaxScopeAsync(AccountsDbContext db, Fixture fixture)
    {
        if (await db.CorporationTaxScopeReviews.AnyAsync(item => item.PeriodId == fixture.PeriodId)
            && await db.CorporationTaxLossRecords.AnyAsync(item => item.PeriodId == fixture.PeriodId))
        {
            return;
        }

        await new TaxComputationService(db, new FinancialStatementsService(db)).SaveScopeReviewAsync(
            fixture.CompanyId,
            fixture.PeriodId,
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
                EvidenceNote: "Inventory coverage fixture for retained tax scope evidence."),
            "Owner User");
    }

    private static async Task<ExternalFilingHandoffSnapshot> EnsureExternalFilingSnapshotAsync(
        AccountsDbContext db,
        Fixture fixture)
    {
        var existing = await db.ExternalFilingHandoffSnapshots
            .FirstOrDefaultAsync(item => item.CompanyId == fixture.CompanyId);
        if (existing is not null) return existing;

        var authority = await EnsureFilingAuthorityAsync(db, fixture);
        byte[] artifact = [5, 6, 7, 8];
        var snapshot = new ExternalFilingHandoffSnapshot
        {
            SnapshotId = Guid.NewGuid(),
            TenantId = fixture.TenantId,
            CompanyId = fixture.CompanyId,
            PeriodId = fixture.PeriodId,
            Workflow = "CRO",
            Version = 1,
            SchemaVersion = "inventory-test-v1",
            ArtifactBytes = artifact,
            ArtifactSha256 = Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(artifact)),
            SourceFingerprintSha256 = new string('b', 64),
            AuthorityId = authority.Id,
            AuthorityEvidenceSha256 = authority.AuthorityEvidenceSha256,
            QualifiedReviewManifestSha256 = new string('c', 64),
            ReleaseCandidate = "inventory-test",
            DirectSubmissionSupported = false,
            IsCompleteExternalReturn = false,
            ReadyForManualHandoff = true,
            PreparedByUserId = "user:10",
            PreparedByDisplayName = "Owner User",
            PreparedByRole = "Owner",
            PreparedAtUtc = DateTime.UtcNow
        };
        db.ExternalFilingHandoffSnapshots.Add(snapshot);
        await db.SaveChangesAsync();
        return snapshot;
    }

    private static async Task<AccountCategory> EnsureCategoryAsync(AccountsDbContext db, int companyId, string code, AccountCategoryType type)
    {
        var existing = await db.AccountCategories.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Code == code);
        if (existing is not null) return existing;
        var category = new AccountCategory { CompanyId = companyId, Code = code, Name = code, Type = type };
        db.AccountCategories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    private static FixedAsset NewAsset(int companyId) => new()
    {
        CompanyId = companyId,
        Name = "Asset",
        Category = "Equipment",
        Cost = 1m,
        AcquisitionDate = new(2025, 1, 1),
        UsefulLifeYears = 1
    };

    private static async Task<FixedAsset> EnsureAssetAsync(AccountsDbContext db, int companyId)
    {
        var existing = await db.FixedAssets.FirstOrDefaultAsync(x => x.CompanyId == companyId);
        if (existing is not null) return existing;
        var asset = NewAsset(companyId);
        db.FixedAssets.Add(asset);
        await db.SaveChangesAsync();
        return asset;
    }

    private static Loan NewLoan(int companyId) => new()
    {
        CompanyId = companyId,
        Lender = "Lender",
        OriginalAmount = 1m,
        Balance = 1m,
        DueWithinYear = 1m
    };

    private static async Task<Loan> EnsureLoanAsync(AccountsDbContext db, int companyId)
    {
        var existing = await db.Loans.FirstOrDefaultAsync(x => x.CompanyId == companyId);
        if (existing is not null) return existing;
        var loan = NewLoan(companyId);
        db.Loans.Add(loan);
        await db.SaveChangesAsync();
        return loan;
    }

    private static async Task<Fixture> SeedBaseAsync(AccountsDbContext db)
    {
        var tenant = new Tenant { Name = "Quarantine Test Firm", Slug = Guid.NewGuid().ToString("N") };
        var company = new Company
        {
            Tenant = tenant,
            LegalName = "Quarantine Evidence Limited",
            CompanyType = CompanyType.Private,
            IncorporationDate = new(2025, 1, 1),
            AnnualReturnDate = new DateOnly(2024, 9, 15)
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new(2025, 1, 1),
            PeriodEnd = new(2025, 12, 31),
            IsFirstYear = true,
            Status = PeriodStatus.Draft
        };
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();
        return new Fixture(tenant.Id, company.Id, period.Id, company.LegalName);
    }

    private static AuthenticatedUser Actor(int tenantId, string role = "Owner") => new(
        10,
        tenantId,
        "Quarantine Test Firm",
        role == "Owner" ? "owner@example.ie" : "reviewer@example.ie",
        role == "Owner" ? "Owner User" : "Reviewer User",
        role);

    private static DefaultHttpContext Request(AuthenticatedUser user, string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.TraceIdentifier = Guid.NewGuid().ToString("N");
        context.Items[AuthContext.ItemKey] = user;
        return context;
    }

    private static ApiAccessService DisabledApiAccess() =>
        new(Options.Create(new ApiAccessConfig { Enabled = false }), new TestEnvironment());

    private static int StatusCode(IResult result) =>
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode
        ?? StatusCodes.Status200OK;

    private static DbContextOptions<AccountsDbContext> OptionsFor(string databaseName) =>
        new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

    private sealed record Fixture(int TenantId, int CompanyId, int PeriodId, string LegalName);

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Accounts.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
