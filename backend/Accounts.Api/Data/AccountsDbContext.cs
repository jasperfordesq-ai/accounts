using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using System.Text.Json;
using System.Text;

namespace Accounts.Api.Data;

public class AccountsDbContext : DbContext
{
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly DatabaseTenantContext? _databaseTenantContext;

    public AccountsDbContext(DbContextOptions<AccountsDbContext> options)
        : this(options, null, null)
    {
    }

    public AccountsDbContext(DbContextOptions<AccountsDbContext> options, IHttpContextAccessor? httpContextAccessor)
        : this(options, httpContextAccessor, null)
    {
    }

    public AccountsDbContext(
        DbContextOptions<AccountsDbContext> options,
        IHttpContextAccessor? httpContextAccessor,
        DatabaseTenantContext? databaseTenantContext)
        : base(options)
    {
        _httpContextAccessor = httpContextAccessor;
        _databaseTenantContext = databaseTenantContext;
    }

    public int? CurrentTenantId => _databaseTenantContext?.TenantId
        ?? (_httpContextAccessor?.HttpContext is { } context
            ? AuthContext.GetUser(context)?.TenantId
            : null);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateCompanyQuarantineEvidenceChanges();
        ValidateCompanyOnboardingRequestChanges();
        ValidateIdempotencyRecordChanges();
        ValidateExternalFilingHandoffChanges();
        ValidateAnnualReturnDateEvidenceChanges();
        ValidateDuplicateReviewChanges();
        ValidateIdentityLifecycleEvidenceChanges();
        PersistenceOwnershipInvariantValidator.ValidateAsync(this, CurrentTenantId, CancellationToken.None)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ValidateCompanyQuarantineEvidenceChanges();
        ValidateCompanyOnboardingRequestChanges();
        ValidateIdempotencyRecordChanges();
        ValidateExternalFilingHandoffChanges();
        ValidateAnnualReturnDateEvidenceChanges();
        ValidateDuplicateReviewChanges();
        ValidateIdentityLifecycleEvidenceChanges();
        await PersistenceOwnershipInvariantValidator.ValidateAsync(this, CurrentTenantId, cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // Tenancy & Users
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<UserCompanyAccess> UserCompanyAccesses => Set<UserCompanyAccess>();
    public DbSet<UserActionToken> UserActionTokens => Set<UserActionToken>();
    public DbSet<UserMfaCredential> UserMfaCredentials => Set<UserMfaCredential>();
    public DbSet<UserMfaRecoveryCode> UserMfaRecoveryCodes => Set<UserMfaRecoveryCode>();
    public DbSet<UserMfaChallenge> UserMfaChallenges => Set<UserMfaChallenge>();
    public DbSet<UserLifecycleEvent> UserLifecycleEvents => Set<UserLifecycleEvent>();

    // Company & Officers
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyOfficer> CompanyOfficers => Set<CompanyOfficer>();

    // Accounting Periods
    public DbSet<AccountingPeriod> AccountingPeriods => Set<AccountingPeriod>();
    public DbSet<SizeClassification> SizeClassifications => Set<SizeClassification>();

    // Filing
    public DbSet<FilingRegime> FilingRegimes => Set<FilingRegime>();
    public DbSet<CroFilingPackage> CroFilingPackages => Set<CroFilingPackage>();
    public DbSet<RevenueFilingPackage> RevenueFilingPackages => Set<RevenueFilingPackage>();
    public DbSet<CharityFilingPackage> CharityFilingPackages => Set<CharityFilingPackage>();
    public DbSet<FilingAuthorityEngagement> FilingAuthorityEngagements => Set<FilingAuthorityEngagement>();
    public DbSet<ExternalFilingHandoffSnapshot> ExternalFilingHandoffSnapshots => Set<ExternalFilingHandoffSnapshot>();
    public DbSet<ExternalFilingOutcomeEvent> ExternalFilingOutcomeEvents => Set<ExternalFilingOutcomeEvent>();

    // Banking & Transactions
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportedTransaction> ImportedTransactions => Set<ImportedTransaction>();
    public DbSet<TransactionRule> TransactionRules => Set<TransactionRule>();
    public DbSet<AccountCategory> AccountCategories => Set<AccountCategory>();

    // Year-End Items
    public DbSet<Debtor> Debtors => Set<Debtor>();
    public DbSet<Creditor> Creditors => Set<Creditor>();
    public DbSet<FixedAsset> FixedAssets => Set<FixedAsset>();
    public DbSet<DepreciationEntry> DepreciationEntries => Set<DepreciationEntry>();
    public DbSet<CapitalAllowanceClaim> CapitalAllowanceClaims => Set<CapitalAllowanceClaim>();
    public DbSet<Inventory> Inventories => Set<Inventory>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<LoanBalanceSnapshot> LoanBalanceSnapshots => Set<LoanBalanceSnapshot>();
    public DbSet<DirectorLoan> DirectorLoans => Set<DirectorLoan>();
    public DbSet<DirectorLoanMovement> DirectorLoanMovements => Set<DirectorLoanMovement>();
    public DbSet<PayrollSummary> PayrollSummaries => Set<PayrollSummary>();
    public DbSet<CorporationTaxScopeReview> CorporationTaxScopeReviews => Set<CorporationTaxScopeReview>();
    public DbSet<CorporationTaxLossRecord> CorporationTaxLossRecords => Set<CorporationTaxLossRecord>();
    public DbSet<CorporationTaxFilingSupportReview> CorporationTaxFilingSupportReviews => Set<CorporationTaxFilingSupportReview>();
    public DbSet<CorporationTaxPaymentRecord> CorporationTaxPaymentRecords => Set<CorporationTaxPaymentRecord>();
    public DbSet<TaxBalance> TaxBalances => Set<TaxBalance>();
    public DbSet<Dividend> Dividends => Set<Dividend>();
    public DbSet<OpeningBalance> OpeningBalances => Set<OpeningBalance>();
    public DbSet<YearEndReviewConfirmation> YearEndReviewConfirmations => Set<YearEndReviewConfirmation>();

    // Adjustments
    public DbSet<Adjustment> Adjustments => Set<Adjustment>();

    // Reports & Output
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<NotesDisclosure> NotesDisclosures => Set<NotesDisclosure>();

    // Share Capital
    public DbSet<ShareCapital> ShareCapitals => Set<ShareCapital>();

    // Filing Compliance
    public DbSet<FilingDeadline> FilingDeadlines => Set<FilingDeadline>();
    public DbSet<FilingHistory> FilingHistories => Set<FilingHistory>();

    // Interrogation Engine (Phase 2)
    public DbSet<PostBalanceSheetEvent> PostBalanceSheetEvents => Set<PostBalanceSheetEvent>();
    public DbSet<RelatedPartyTransaction> RelatedPartyTransactions => Set<RelatedPartyTransaction>();
    public DbSet<ContingentLiability> ContingentLiabilities => Set<ContingentLiability>();

    // Charity / SORP
    public DbSet<CharityInfo> CharityInfos => Set<CharityInfo>();
    public DbSet<FundBalance> FundBalances => Set<FundBalance>();

    // Audit
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AuditIntegrityCheckpoint> AuditIntegrityCheckpoints => Set<AuditIntegrityCheckpoint>();
    public DbSet<CompanyQuarantineEvent> CompanyQuarantineEvents => Set<CompanyQuarantineEvent>();
    public DbSet<CompanyOnboardingRequest> CompanyOnboardingRequests => Set<CompanyOnboardingRequest>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<AnnualReturnDateRecord> AnnualReturnDateRecords => Set<AnnualReturnDateRecord>();

    // Privacy governance
    public DbSet<LoginSecurityEvent> LoginSecurityEvents => Set<LoginSecurityEvent>();
    public DbSet<PrivacySubjectRequest> PrivacySubjectRequests => Set<PrivacySubjectRequest>();
    public DbSet<PrivacyIncidentExercise> PrivacyIncidentExercises => Set<PrivacyIncidentExercise>();

    // Scheduled deadline delivery and platform operations
    public DbSet<DeadlineReminderOutbox> DeadlineReminderOutbox => Set<DeadlineReminderOutbox>();
    public DbSet<PlatformJobRun> PlatformJobRuns => Set<PlatformJobRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tenant
        modelBuilder.Entity<Tenant>(e =>
        {
            e.ToTable("tenants");
            e.HasKey(t => t.Id);
            e.Property(t => t.Name).HasMaxLength(200).IsRequired();
            e.Property(t => t.Slug).HasMaxLength(120).IsRequired();
            e.HasIndex(t => t.Slug).IsUnique();
        });

        // UserAccount
        modelBuilder.Entity<UserAccount>(e =>
        {
            e.ToTable("user_accounts");
            e.HasKey(u => u.Id);
            e.HasQueryFilter(u => CurrentTenantId == null || u.TenantId == CurrentTenantId);
            e.Property(u => u.Email).HasMaxLength(320).IsRequired();
            e.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
            e.Property(u => u.Role).HasMaxLength(80).IsRequired();
            e.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();
            e.Property(u => u.PasswordSalt).HasMaxLength(256).IsRequired();
            e.Property(u => u.PasswordAlgorithm).HasMaxLength(80).IsRequired();
            e.Property(u => u.SessionVersion).HasDefaultValue(1);
            e.Property(u => u.FailedLoginCount).HasDefaultValue(0);
            e.HasOne(u => u.Tenant).WithMany(t => t.Users).HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(u => u.Email).IsUnique();
            e.HasIndex(u => new { u.TenantId, u.Role });
        });

        // UserCompanyAccess
        modelBuilder.Entity<UserCompanyAccess>(e =>
        {
            e.ToTable("user_company_accesses");
            e.HasKey(a => a.Id);
            e.HasQueryFilter(a => CurrentTenantId == null || a.Company.TenantId == CurrentTenantId);
            e.HasOne(a => a.User).WithMany(u => u.CompanyAccesses).HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Company).WithMany(c => c.UserAccesses).HasForeignKey(a => a.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => new { a.UserId, a.CompanyId }).IsUnique();
            e.HasIndex(a => a.CompanyId);
        });

        modelBuilder.Entity<UserActionToken>(e =>
        {
            e.ToTable("user_action_tokens", table => table.HasCheckConstraint(
                "CK_user_action_tokens_purpose",
                "\"Purpose\" IN ('Invitation', 'PasswordReset')"));
            e.HasKey(token => token.Id);
            e.HasQueryFilter(token => CurrentTenantId == null || token.TenantId == CurrentTenantId);
            e.Property(token => token.Purpose).HasMaxLength(40).IsRequired();
            e.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
            e.HasOne(token => token.User).WithMany(user => user.ActionTokens).HasForeignKey(token => token.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(token => token.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(token => token.TokenHash).IsUnique();
            e.HasIndex(token => new { token.TenantId, token.UserId, token.Purpose, token.ExpiresAtUtc });
        });

        modelBuilder.Entity<UserMfaCredential>(e =>
        {
            e.ToTable("user_mfa_credentials");
            e.HasKey(credential => credential.Id);
            e.HasQueryFilter(credential => CurrentTenantId == null || credential.TenantId == CurrentTenantId);
            e.Property(credential => credential.EncryptedSecret).HasMaxLength(1024).IsRequired();
            e.Property(credential => credential.LastAcceptedTotpCounter)
                .IsConcurrencyToken()
                .HasDefaultValue(-1L);
            e.HasOne(credential => credential.User).WithOne(user => user.MfaCredential).HasForeignKey<UserMfaCredential>(credential => credential.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(credential => credential.UserId).IsUnique();
            e.HasIndex(credential => new { credential.TenantId, credential.EnabledAtUtc });
        });

        modelBuilder.Entity<UserMfaRecoveryCode>(e =>
        {
            e.ToTable("user_mfa_recovery_codes");
            e.HasKey(code => code.Id);
            e.HasQueryFilter(code => CurrentTenantId == null || code.TenantId == CurrentTenantId);
            e.Property(code => code.CodeHash).HasMaxLength(64).IsRequired();
            e.HasOne(code => code.User).WithMany(user => user.MfaRecoveryCodes).HasForeignKey(code => code.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(code => code.CodeHash).IsUnique();
            e.HasIndex(code => new { code.TenantId, code.UserId, code.UsedAtUtc });
        });

        modelBuilder.Entity<UserMfaChallenge>(e =>
        {
            e.ToTable("user_mfa_challenges", table =>
            {
                table.HasCheckConstraint("CK_user_mfa_challenges_purpose", "\"Purpose\" IN ('MfaEnrollment', 'MfaLogin')");
                table.HasCheckConstraint("CK_user_mfa_challenges_attempts", "\"FailedAttempts\" >= 0 AND \"FailedAttempts\" <= 5");
            });
            e.HasKey(challenge => challenge.Id);
            e.HasQueryFilter(challenge => CurrentTenantId == null || challenge.TenantId == CurrentTenantId);
            e.Property(challenge => challenge.Purpose).HasMaxLength(40).IsRequired();
            e.Property(challenge => challenge.TokenHash).HasMaxLength(64).IsRequired();
            e.HasOne(challenge => challenge.User).WithMany(user => user.MfaChallenges).HasForeignKey(challenge => challenge.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(challenge => challenge.TokenHash).IsUnique();
            e.HasIndex(challenge => new { challenge.TenantId, challenge.UserId, challenge.ExpiresAtUtc });
        });

        modelBuilder.Entity<UserLifecycleEvent>(e =>
        {
            e.ToTable("user_lifecycle_events");
            e.HasKey(entry => entry.Id);
            e.HasQueryFilter(entry => CurrentTenantId == null || entry.TenantId == CurrentTenantId);
            e.Property(entry => entry.EventType).HasMaxLength(80).IsRequired();
            e.Property(entry => entry.DetailsJson).HasColumnType("jsonb").IsRequired();
            e.HasOne<UserAccount>().WithMany().HasForeignKey(entry => entry.TargetUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(entry => entry.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(entry => new { entry.TenantId, entry.TargetUserId, entry.OccurredAtUtc });
            e.HasIndex(entry => new { entry.TenantId, entry.ActorUserId, entry.OccurredAtUtc });
        });

        // Company
        modelBuilder.Entity<Company>(e =>
        {
            e.ToTable("companies");
            e.ToTable(t => t.HasTrigger("TR_companies_tenant_immutable"));
            e.ToTable(t => t.HasCheckConstraint(
                "CK_companies_quarantine_evidence",
                "(NOT \"IsQuarantined\" AND \"QuarantinedAtUtc\" IS NULL AND \"QuarantinedByUserId\" IS NULL AND \"QuarantinedByDisplayName\" IS NULL AND \"QuarantineReason\" IS NULL AND \"QuarantineEvidenceSha256\" IS NULL) OR (\"IsQuarantined\" AND \"QuarantinedAtUtc\" IS NOT NULL AND \"QuarantinedByUserId\" IS NOT NULL AND \"QuarantinedByDisplayName\" IS NOT NULL AND \"QuarantineReason\" IS NOT NULL AND \"QuarantineEvidenceSha256\" IS NOT NULL)"));
            e.HasKey(c => c.Id);
            e.HasQueryFilter(c => (CurrentTenantId == null || c.TenantId == CurrentTenantId) && !c.IsQuarantined);
            e.Property(c => c.QuarantinedByUserId).HasMaxLength(320);
            e.Property(c => c.QuarantinedByDisplayName).HasMaxLength(200);
            e.Property(c => c.QuarantineReason).HasMaxLength(2000);
            e.Property(c => c.QuarantineEvidenceSha256).HasMaxLength(64);
            e.Property(c => c.LegalName).HasMaxLength(500).IsRequired();
            e.Property(c => c.TradingName).HasMaxLength(500);
            e.Property(c => c.CroNumber).HasMaxLength(20);
            e.Property(c => c.TaxReference).HasMaxLength(20);
            e.HasOne(c => c.Tenant).WithMany(t => t.Companies).HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(c => c.CroNumber).IsUnique().HasFilter("\"CroNumber\" IS NOT NULL");
            e.HasIndex(c => c.TenantId);
        });

        modelBuilder.Entity<AnnualReturnDateRecord>(e =>
        {
            e.ToTable("annual_return_date_records", table =>
            {
                table.HasCheckConstraint(
                    "CK_annual_return_date_records_effective_date",
                    "\"EffectiveFrom\" <= \"AnnualReturnDate\"");
                table.HasCheckConstraint(
                    "CK_annual_return_date_records_sha256",
                    "length(\"RecordSha256\") = 64 AND (\"EvidenceSha256\" IS NULL OR length(\"EvidenceSha256\") = 64)");
                table.HasCheckConstraint(
                    "CK_annual_return_date_records_change",
                    "\"PreviousAnnualReturnDate\" IS NULL OR \"PreviousAnnualReturnDate\" <> \"AnnualReturnDate\"");
                table.HasCheckConstraint(
                    "CK_annual_return_date_records_manual_override",
                    "\"Source\" <> 'ManualOverride' OR (\"EvidenceSha256\" IS NOT NULL AND length(\"ChangeReason\") >= 20)");
            });
            e.HasKey(record => record.Id);
            e.HasQueryFilter(record => CurrentTenantId == null || record.Company.TenantId == CurrentTenantId);
            e.Property(record => record.EvidenceReference).HasMaxLength(300).IsRequired();
            e.Property(record => record.EvidenceSha256).HasMaxLength(64);
            e.Property(record => record.ChangeReason).HasMaxLength(1000);
            e.Property(record => record.RecordedByUserId).HasMaxLength(320).IsRequired();
            e.Property(record => record.RecordedByDisplayName).HasMaxLength(200).IsRequired();
            e.Property(record => record.RecordSha256).HasMaxLength(64).IsRequired();
            e.HasOne(record => record.Company)
                .WithMany(company => company.AnnualReturnDateHistory)
                .HasForeignKey(record => record.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(record => new { record.CompanyId, record.RecordedAtUtc }).IsUnique();
            e.HasIndex(record => new { record.CompanyId, record.AnnualReturnDate });
        });

        // CompanyOfficer
        modelBuilder.Entity<CompanyOfficer>(e =>
        {
            e.ToTable("company_officers");
            e.HasKey(o => o.Id);
            e.HasQueryFilter(o => CurrentTenantId == null || o.Company.TenantId == CurrentTenantId);
            e.Property(o => o.Name).HasMaxLength(300).IsRequired();
            e.HasOne(o => o.Company).WithMany(c => c.Officers).HasForeignKey(o => o.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        // AccountingPeriod
        modelBuilder.Entity<AccountingPeriod>(e =>
        {
            e.ToTable("accounting_periods");
            e.ToTable(t => t.HasTrigger("TR_accounting_periods_company_immutable"));
            e.HasKey(p => p.Id);
            e.HasQueryFilter(p => CurrentTenantId == null || p.Company.TenantId == CurrentTenantId);
            e.Property(p => p.LockedBy).HasMaxLength(200);
            e.Property(p => p.ReopenedBy).HasMaxLength(200);
            e.Property(p => p.ReopenReason).HasMaxLength(1000);
            e.Property(p => p.AuditorsReportFirmName).HasMaxLength(200);
            e.Property(p => p.AuditorsReportSignerName).HasMaxLength(200);
            e.Property(p => p.AuditorsReportProfessionalBody).HasMaxLength(200);
            e.Property(p => p.AuditorsReportMembershipNumber).HasMaxLength(100);
            e.Property(p => p.AuditorsReportReviewedBy).HasMaxLength(200);
            e.Property(p => p.AuditorsReportReviewDecision).HasMaxLength(50);
            e.Property(p => p.AuditorsReportSha256).HasMaxLength(64);
            e.HasOne(p => p.Company).WithMany(c => c.Periods).HasForeignKey(p => p.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.CompanyId, p.PeriodEnd }).IsUnique();
        });

        // SizeClassification (1:1 with AccountingPeriod)
        modelBuilder.Entity<SizeClassification>(e =>
        {
            e.ToTable("size_classifications");
            e.HasKey(s => s.Id);
            e.HasQueryFilter(s => CurrentTenantId == null || s.Period.Company.TenantId == CurrentTenantId);
            e.Property(s => s.Turnover).HasColumnType("decimal(18,2)");
            e.Property(s => s.BalanceSheetTotal).HasColumnType("decimal(18,2)");
            e.Property(s => s.AnnualisedTurnover).HasColumnType("decimal(18,2)");
            e.Property(s => s.PeriodLengthInYears).HasColumnType("decimal(10,6)");
            e.Property(s => s.ThresholdScheduleCode).HasMaxLength(100);
            e.Property(s => s.DecisionInputFingerprintSha256).HasMaxLength(64);
            e.Property(s => s.OverrideAuthorityRole).HasMaxLength(50);
            e.Property(s => s.OverrideApprovedBy).HasMaxLength(200);
            e.Property(s => s.OverrideEvidenceSha256).HasMaxLength(64);
            e.Property(s => s.OverrideInputFingerprintSha256).HasMaxLength(64);
            e.HasOne(s => s.Period).WithOne(p => p.SizeClassification).HasForeignKey<SizeClassification>(s => s.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // FilingRegime (1:1 with AccountingPeriod)
        modelBuilder.Entity<FilingRegime>(e =>
        {
            e.ToTable("filing_regimes");
            e.HasKey(f => f.Id);
            e.HasQueryFilter(f => CurrentTenantId == null || f.Period.Company.TenantId == CurrentTenantId);
            e.HasOne(f => f.Period).WithOne(p => p.FilingRegime).HasForeignKey<FilingRegime>(f => f.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // CroFilingPackage (1:1 with AccountingPeriod)
        modelBuilder.Entity<CroFilingPackage>(e =>
        {
            e.ToTable("cro_filing_packages");
            e.ToTable(t => t.HasTrigger("TR_cro_filing_packages_period_immutable"));
            e.HasKey(c => c.Id);
            e.HasQueryFilter(c => CurrentTenantId == null || c.Period.Company.TenantId == CurrentTenantId);
            e.Property(c => c.AccountsPdfSha256).HasMaxLength(64);
            e.Property(c => c.SignaturePageSha256).HasMaxLength(64);
            e.Property(c => c.ArtifactReleaseCandidate).HasMaxLength(200);
            e.Property(c => c.ArtifactSourceFingerprintSha256).HasMaxLength(64);
            e.Property(c => c.AttachedAuditorReportSha256).HasMaxLength(64);
            e.Property(c => c.ApprovedArtifactManifestSha256).HasMaxLength(64);
            e.Property(c => c.ApprovedReleaseCandidate).HasMaxLength(200);
            e.Property(c => c.ApproverProfessionalBody).HasMaxLength(200);
            e.Property(c => c.ApproverMembershipNumber).HasMaxLength(100);
            e.Property(c => c.ApprovalScope).HasMaxLength(100);
            e.Property(c => c.ApprovalCapacity).HasMaxLength(100);
            e.Property(c => c.ApprovalDecision).HasMaxLength(50);
            e.Property(c => c.ApproverVerificationReference).HasMaxLength(200);
            e.Property(c => c.ApproverVerificationArtifactSha256).HasMaxLength(64);
            e.Property(c => c.SignedPdfSha256).HasMaxLength(64);
            e.HasOne(c => c.Period).WithOne(p => p.CroFilingPackage).HasForeignKey<CroFilingPackage>(c => c.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // RevenueFilingPackage (1:1 with AccountingPeriod)
        modelBuilder.Entity<RevenueFilingPackage>(e =>
        {
            e.ToTable("revenue_filing_packages");
            e.ToTable(t => t.HasTrigger("TR_revenue_filing_packages_period_immutable"));
            e.HasKey(r => r.Id);
            e.HasQueryFilter(r => CurrentTenantId == null || r.Period.Company.TenantId == CurrentTenantId);
            e.Property(r => r.IxbrlSha256).HasMaxLength(64);
            e.Property(r => r.ArtifactReleaseCandidate).HasMaxLength(200);
            e.Property(r => r.ApprovedArtifactManifestSha256).HasMaxLength(64);
            e.Property(r => r.ApprovedReleaseCandidate).HasMaxLength(200);
            e.Property(r => r.ApproverProfessionalBody).HasMaxLength(200);
            e.Property(r => r.ApproverMembershipNumber).HasMaxLength(100);
            e.Property(r => r.ApprovalScope).HasMaxLength(100);
            e.Property(r => r.ApprovalCapacity).HasMaxLength(100);
            e.Property(r => r.ApprovalDecision).HasMaxLength(50);
            e.Property(r => r.ApproverVerificationReference).HasMaxLength(200);
            e.Property(r => r.ApproverVerificationArtifactSha256).HasMaxLength(64);
            e.Property(r => r.ExternalValidationArtifactSha256).HasMaxLength(64);
            e.Property(r => r.ExternalValidationReference).HasMaxLength(200);
            e.Property(r => r.ExternalValidatorProvider).HasMaxLength(200);
            e.Property(r => r.ExternalValidatorVersion).HasMaxLength(100);
            e.Property(r => r.ExternalTaxonomyPackageSha256).HasMaxLength(64);
            e.Property(r => r.ExternalValidationWarningDisposition).HasMaxLength(50);
            e.Property(r => r.ExternalValidationResponseSha256).HasMaxLength(64);
            e.HasOne(r => r.Period).WithOne(p => p.RevenueFilingPackage).HasForeignKey<RevenueFilingPackage>(r => r.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // CharityFilingPackage (1:1 with AccountingPeriod)
        modelBuilder.Entity<CharityFilingPackage>(e =>
        {
            e.ToTable("charity_filing_packages");
            e.ToTable(t => t.HasTrigger("TR_charity_filing_packages_period_immutable"));
            e.HasKey(c => c.Id);
            e.HasQueryFilter(c => CurrentTenantId == null || c.Period.Company.TenantId == CurrentTenantId);
            e.Property(c => c.ApprovedBy).HasMaxLength(200);
            e.Property(c => c.SubmittedBy).HasMaxLength(200);
            e.Property(c => c.AcceptedBy).HasMaxLength(200);
            e.Property(c => c.AnnualReturnReference).HasMaxLength(200);
            e.Property(c => c.RejectionReason).HasMaxLength(1000);
            e.Property(c => c.SofaSha256).HasMaxLength(64);
            e.Property(c => c.TrusteesReportSha256).HasMaxLength(64);
            e.Property(c => c.ArtifactReleaseCandidate).HasMaxLength(200);
            e.Property(c => c.ArtifactSourceFingerprintSha256).HasMaxLength(64);
            e.Property(c => c.SorpFrameworkCode).HasMaxLength(50);
            e.Property(c => c.SofaBasis).HasMaxLength(50);
            e.Property(c => c.SorpDecisionSha256).HasMaxLength(64);
            e.Property(c => c.CharityNumberSnapshot).HasMaxLength(20);
            e.Property(c => c.SofaClosingFunds).HasColumnType("decimal(18,2)");
            e.Property(c => c.BalanceSheetNetAssets).HasColumnType("decimal(18,2)");
            e.Property(c => c.ReconciliationDifference).HasColumnType("decimal(18,2)");
            e.Property(c => c.TrusteeReviewReference).HasMaxLength(300);
            e.Property(c => c.TrusteeReviewedBy).HasMaxLength(200);
            e.Property(c => c.TrusteeReviewArtifactSha256).HasMaxLength(64);
            e.Property(c => c.TrusteePopulationSha256).HasMaxLength(64);
            e.Property(c => c.ManualProfessionalHandoffReason).HasMaxLength(1000);
            e.Property(c => c.ApprovedArtifactManifestSha256).HasMaxLength(64);
            e.Property(c => c.ApprovedReleaseCandidate).HasMaxLength(200);
            e.Property(c => c.ApproverProfessionalBody).HasMaxLength(200);
            e.Property(c => c.ApproverMembershipNumber).HasMaxLength(100);
            e.Property(c => c.ApprovalScope).HasMaxLength(100);
            e.Property(c => c.ApprovalCapacity).HasMaxLength(100);
            e.Property(c => c.ApprovalDecision).HasMaxLength(50);
            e.Property(c => c.ApproverVerificationReference).HasMaxLength(200);
            e.Property(c => c.ApproverVerificationArtifactSha256).HasMaxLength(64);
            e.HasOne(c => c.Period).WithOne(p => p.CharityFilingPackage).HasForeignKey<CharityFilingPackage>(c => c.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FilingAuthorityEngagement>(e =>
        {
            e.ToTable("filing_authority_engagements", table =>
            {
                table.HasTrigger("TR_filing_authority_engagements_immutable");
                table.HasCheckConstraint("CK_filing_authority_engagements_sha256", "length(\"AuthorityEvidenceSha256\") = 64 AND length(\"RecordSha256\") = 64");
                table.HasCheckConstraint("CK_filing_authority_engagements_version_chain", "(\"Version\" = 1 AND \"SupersedesAuthorityId\" IS NULL) OR (\"Version\" > 1 AND \"SupersedesAuthorityId\" IS NOT NULL)");
                table.HasCheckConstraint("CK_filing_authority_engagements_status_dates", "(\"Status\" = 'Active' AND \"RevokedAtUtc\" IS NULL) OR (\"Status\" = 'Revoked' AND \"RevokedAtUtc\" IS NOT NULL) OR (\"Status\" = 'Expired' AND \"EffectiveUntilUtc\" IS NOT NULL) OR \"Status\" IN ('Draft', 'Pending')");
            });
            e.HasKey(item => item.Id);
            e.HasQueryFilter(item => CurrentTenantId == null || item.TenantId == CurrentTenantId);
            e.Property(item => item.Workflow).HasMaxLength(40).IsRequired();
            e.Property(item => item.Kind).HasMaxLength(50).IsRequired();
            e.Property(item => item.Status).HasMaxLength(30).IsRequired();
            e.Property(item => item.LegalName).HasMaxLength(300).IsRequired();
            e.Property(item => item.PracticeName).HasMaxLength(300);
            e.Property(item => item.MaskedPresenterOrTain).HasMaxLength(100);
            e.Property(item => item.AuthorityScope).HasMaxLength(1000).IsRequired();
            e.Property(item => item.EngagementReference).HasMaxLength(500).IsRequired();
            e.Property(item => item.ExternalAuthorityReference).HasMaxLength(500).IsRequired();
            e.Property(item => item.AuthorityEvidenceArtifact).IsRequired();
            e.Property(item => item.AuthorityEvidenceSha256).HasMaxLength(64).IsRequired();
            e.Property(item => item.EvidenceMediaType).HasMaxLength(100).IsRequired();
            e.Property(item => item.EvidenceFileName).HasMaxLength(255).IsRequired();
            e.Property(item => item.ReviewedByUserId).HasMaxLength(320).IsRequired();
            e.Property(item => item.ReviewedByDisplayName).HasMaxLength(200).IsRequired();
            e.Property(item => item.ReviewedByRole).HasMaxLength(100).IsRequired();
            e.Property(item => item.ReleaseCandidate).HasMaxLength(200).IsRequired();
            e.Property(item => item.RecordSha256).HasMaxLength(64).IsRequired();
            e.Property(item => item.CreatedByUserId).HasMaxLength(320).IsRequired();
            e.Property(item => item.CreatedByDisplayName).HasMaxLength(200).IsRequired();
            e.Property(item => item.CreatedByRole).HasMaxLength(100).IsRequired();
            e.HasOne(item => item.Tenant).WithMany().HasForeignKey(item => item.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(item => item.Company).WithMany().HasForeignKey(item => item.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(item => item.SupersedesAuthority).WithMany().HasForeignKey(item => item.SupersedesAuthorityId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(item => new { item.CompanyId, item.Workflow, item.Version }).IsUnique();
            e.HasIndex(item => new { item.TenantId, item.CompanyId, item.Workflow, item.CreatedAtUtc });
        });

        modelBuilder.Entity<ExternalFilingHandoffSnapshot>(e =>
        {
            e.ToTable("external_filing_handoff_snapshots", table =>
            {
                table.HasTrigger("TR_external_filing_handoff_snapshots_immutable");
                table.HasCheckConstraint("CK_external_filing_handoff_snapshots_sha256", "length(\"ArtifactSha256\") = 64 AND length(\"SourceFingerprintSha256\") = 64 AND length(\"AuthorityEvidenceSha256\") = 64 AND length(\"QualifiedReviewManifestSha256\") = 64");
                table.HasCheckConstraint("CK_external_filing_handoff_snapshots_manual_only", "NOT \"DirectSubmissionSupported\" AND NOT \"IsCompleteExternalReturn\"");
                table.HasCheckConstraint("CK_external_filing_handoff_snapshots_version_chain", "(\"Version\" = 1 AND \"SupersedesSnapshotRecordId\" IS NULL AND \"SupersedesSnapshotId\" IS NULL AND \"SupersedesArtifactSha256\" IS NULL AND \"AmendmentReason\" IS NULL) OR (\"Version\" > 1 AND \"SupersedesSnapshotRecordId\" IS NOT NULL AND \"SupersedesSnapshotId\" IS NOT NULL AND length(\"SupersedesArtifactSha256\") = 64 AND length(\"AmendmentReason\") >= 10)");
            });
            e.HasKey(item => item.Id);
            e.HasQueryFilter(item => CurrentTenantId == null || item.TenantId == CurrentTenantId);
            e.Property(item => item.Workflow).HasMaxLength(40).IsRequired();
            e.Property(item => item.AmendmentReason).HasMaxLength(2000);
            e.Property(item => item.SchemaVersion).HasMaxLength(100).IsRequired();
            e.Property(item => item.ArtifactBytes).IsRequired();
            e.Property(item => item.ArtifactSha256).HasMaxLength(64).IsRequired();
            e.Property(item => item.SourceFingerprintSha256).HasMaxLength(64).IsRequired();
            e.Property(item => item.AuthorityEvidenceSha256).HasMaxLength(64).IsRequired();
            e.Property(item => item.QualifiedReviewManifestSha256).HasMaxLength(64).IsRequired();
            e.Property(item => item.ReleaseCandidate).HasMaxLength(200).IsRequired();
            e.Property(item => item.PreparedByUserId).HasMaxLength(320).IsRequired();
            e.Property(item => item.PreparedByDisplayName).HasMaxLength(200).IsRequired();
            e.Property(item => item.PreparedByRole).HasMaxLength(100).IsRequired();
            e.HasOne(item => item.Tenant).WithMany().HasForeignKey(item => item.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(item => item.Company).WithMany().HasForeignKey(item => item.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(item => item.Period).WithMany().HasForeignKey(item => item.PeriodId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(item => item.Authority).WithMany().HasForeignKey(item => item.AuthorityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(item => item.SupersedesSnapshot).WithMany().HasForeignKey(item => item.SupersedesSnapshotRecordId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(item => item.SnapshotId).IsUnique();
            e.HasIndex(item => new { item.CompanyId, item.PeriodId, item.Workflow, item.Version }).IsUnique();
            e.HasIndex(item => new { item.TenantId, item.CompanyId, item.PeriodId, item.PreparedAtUtc });
        });

        modelBuilder.Entity<ExternalFilingOutcomeEvent>(e =>
        {
            e.ToTable("external_filing_outcome_events", table =>
            {
                table.HasTrigger("TR_external_filing_outcome_events_immutable");
                table.HasCheckConstraint("CK_external_filing_outcome_events_hashes", "length(\"SnapshotArtifactSha256\") = 64 AND length(\"EventSha256\") = 64 AND (\"EvidenceSha256\" IS NULL OR length(\"EvidenceSha256\") = 64) AND (\"SupersedingSnapshotArtifactSha256\" IS NULL OR length(\"SupersedingSnapshotArtifactSha256\") = 64)");
                table.HasCheckConstraint("CK_external_filing_outcome_events_evidence_shape", "(\"Outcome\" = 'ReadyForManualHandoff' AND \"ExternalReference\" IS NULL AND \"ExternalOccurredAtUtc\" IS NULL AND \"EvidenceReference\" IS NULL AND \"EvidenceArtifact\" IS NULL AND \"EvidenceSha256\" IS NULL AND \"CorrectionDeadlineUtc\" IS NULL AND \"SupersedingSnapshotRecordId\" IS NULL AND \"SupersedingSnapshotId\" IS NULL AND \"SupersedingSnapshotArtifactSha256\" IS NULL) OR (\"Outcome\" = 'SupersededByAmendment' AND \"ExternalReference\" IS NULL AND \"ExternalOccurredAtUtc\" IS NULL AND \"EvidenceReference\" IS NULL AND \"EvidenceArtifact\" IS NULL AND \"EvidenceSha256\" IS NULL AND \"CorrectionDeadlineUtc\" IS NULL AND \"SupersedingSnapshotRecordId\" IS NOT NULL AND \"SupersedingSnapshotId\" IS NOT NULL AND \"SupersedingSnapshotArtifactSha256\" IS NOT NULL) OR (\"Outcome\" IN ('ExternallySubmittedRecorded', 'CorrectionRequired', 'ExternallyRejected', 'ExternallyAcceptedRecorded') AND \"ExternalReference\" IS NOT NULL AND \"ExternalOccurredAtUtc\" IS NOT NULL AND \"EvidenceReference\" IS NOT NULL AND \"EvidenceArtifact\" IS NOT NULL AND \"EvidenceSha256\" IS NOT NULL AND \"SupersedingSnapshotRecordId\" IS NULL AND \"SupersedingSnapshotId\" IS NULL AND \"SupersedingSnapshotArtifactSha256\" IS NULL)");
                table.HasCheckConstraint("CK_external_filing_outcome_events_correction", "(\"Outcome\" = 'CorrectionRequired' AND length(\"Reason\") >= 5 AND \"CorrectionDeadlineUtc\" > \"ExternalOccurredAtUtc\") OR (\"Outcome\" = 'ExternallyRejected' AND length(\"Reason\") >= 5 AND \"CorrectionDeadlineUtc\" IS NULL) OR (\"Outcome\" NOT IN ('CorrectionRequired', 'ExternallyRejected') AND \"CorrectionDeadlineUtc\" IS NULL)");
            });
            e.HasKey(item => item.Id);
            e.HasQueryFilter(item => CurrentTenantId == null || item.TenantId == CurrentTenantId);
            e.Property(item => item.SnapshotArtifactSha256).HasMaxLength(64).IsRequired();
            e.Property(item => item.Outcome).HasMaxLength(50).IsRequired();
            e.Property(item => item.ExternalReference).HasMaxLength(500);
            e.Property(item => item.Reason).HasMaxLength(2000);
            e.Property(item => item.EvidenceReference).HasMaxLength(1000);
            e.Property(item => item.EvidenceSha256).HasMaxLength(64);
            e.Property(item => item.SupersedingSnapshotArtifactSha256).HasMaxLength(64);
            e.Property(item => item.RecordedByUserId).HasMaxLength(320).IsRequired();
            e.Property(item => item.RecordedByDisplayName).HasMaxLength(200).IsRequired();
            e.Property(item => item.RecordedByRole).HasMaxLength(100).IsRequired();
            e.Property(item => item.EventSha256).HasMaxLength(64).IsRequired();
            e.HasOne(item => item.Tenant).WithMany().HasForeignKey(item => item.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(item => item.Company).WithMany().HasForeignKey(item => item.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(item => item.Period).WithMany().HasForeignKey(item => item.PeriodId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(item => item.Snapshot).WithMany().HasForeignKey(item => item.SnapshotRecordId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(item => item.SupersedingSnapshot).WithMany().HasForeignKey(item => item.SupersedingSnapshotRecordId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(item => new { item.SnapshotRecordId, item.Sequence }).IsUnique();
            e.HasIndex(item => new { item.TenantId, item.CompanyId, item.PeriodId, item.RecordedAtUtc });
        });

        // BankAccount
        modelBuilder.Entity<BankAccount>(e =>
        {
            e.ToTable("bank_accounts");
            e.ToTable(t => t.HasTrigger("TR_bank_accounts_company_immutable"));
            e.ToTable(t => t.HasTrigger("TR_bank_accounts_import_identity_immutable"));
            e.HasKey(b => b.Id);
            e.HasQueryFilter(b => CurrentTenantId == null || b.Company.TenantId == CurrentTenantId);
            e.Property(b => b.Name).HasMaxLength(200).IsRequired();
            e.Property(b => b.Iban).HasMaxLength(34);
            e.Property(b => b.Currency).HasMaxLength(3);
            e.Property(b => b.OpeningBalance).HasColumnType("decimal(18,2)");
            e.HasOne(b => b.Company).WithMany(c => c.BankAccounts).HasForeignKey(b => b.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint(
                "CK_bank_accounts_opening_balance_date_required",
                "\"OpeningBalance\" = 0 OR \"OpeningBalanceDate\" IS NOT NULL"));
            e.ToTable(t => t.HasCheckConstraint(
                "CK_bank_accounts_currency_code",
                "\"Currency\" ~ '^[A-Z]{3}$'"));
        });

        // ImportBatch
        modelBuilder.Entity<ImportBatch>(e =>
        {
            e.ToTable("import_batches");
            e.ToTable(t => t.HasTrigger("TR_import_batches_bank_immutable"));
            e.ToTable(t => t.HasTrigger("TR_import_batches_source_immutable"));
            e.HasKey(b => b.Id);
            e.HasQueryFilter(b => CurrentTenantId == null || b.BankAccount.Company.TenantId == CurrentTenantId);
            e.Property(b => b.Filename).HasMaxLength(500).IsRequired();
            e.Property(b => b.SourceFileSha256).HasMaxLength(64);
            e.Property(b => b.SourceHeaderJson).HasColumnType("jsonb");
            e.Property(b => b.ImportWarningsJson).HasColumnType("jsonb");
            e.HasOne(b => b.BankAccount).WithMany(a => a.ImportBatches).HasForeignKey(b => b.BankAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(b => new { b.BankAccountId, b.SourceFileSha256 });
            e.ToTable(t => t.HasCheckConstraint(
                "CK_import_batches_source_evidence",
                "(\"SourceFileSha256\" IS NULL AND \"SourceFileBytes\" IS NULL AND \"SourceHeaderJson\" IS NULL AND \"ImportWarningsJson\" IS NULL) OR (\"SourceFileSha256\" ~ '^[0-9a-f]{64}$' AND \"SourceFileBytes\" > 0 AND jsonb_typeof(\"SourceHeaderJson\") = 'array' AND jsonb_typeof(\"ImportWarningsJson\") = 'array')"));
        });

        // ImportedTransaction
        modelBuilder.Entity<ImportedTransaction>(e =>
        {
            e.ToTable("imported_transactions");
            e.ToTable(t => t.HasTrigger("TR_imported_transactions_ownership"));
            e.ToTable(t => t.HasTrigger("TR_imported_transactions_bank_immutable"));
            e.ToTable(t => t.HasTrigger("TR_imported_transactions_source_immutable"));
            e.HasKey(t => t.Id);
            e.HasQueryFilter(t => CurrentTenantId == null || t.BankAccount.Company.TenantId == CurrentTenantId);
            e.Property(t => t.Description).HasMaxLength(1000).IsRequired();
            e.Property(t => t.Reference).HasMaxLength(200);
            e.Property(t => t.Amount).HasColumnType("decimal(18,2)");
            e.Property(t => t.Balance).HasColumnType("decimal(18,2)");
            e.Property(t => t.ConfidenceScore).HasColumnType("decimal(5,4)");
            e.Property(t => t.SourceRowSha256).HasMaxLength(64);
            e.Property(t => t.SourceRowJson).HasColumnType("jsonb");
            e.Property(t => t.DuplicateConfidence).HasColumnType("decimal(5,4)");
            e.Property(t => t.DuplicateCandidateReasonsJson).HasColumnType("jsonb");
            e.Property(t => t.DuplicateMatchedSourceRowSha256).HasMaxLength(64);
            e.Property(t => t.DuplicateDecisionByUserId).HasMaxLength(320);
            e.Property(t => t.DuplicateDecisionByDisplayName).HasMaxLength(200);
            e.Property(t => t.DuplicateDecisionReason).HasMaxLength(1000);
            e.HasOne(t => t.BankAccount).WithMany(a => a.Transactions).HasForeignKey(t => t.BankAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.Period).WithMany(p => p.Transactions).HasForeignKey(t => t.PeriodId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.ImportBatch).WithMany(b => b.Transactions).HasForeignKey(t => t.ImportBatchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.Category).WithMany(c => c.Transactions).HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(t => new { t.BankAccountId, t.Date, t.Amount, t.Description }).HasDatabaseName("ix_transaction_duplicate_check");
            e.HasIndex(t => new { t.BankAccountId, t.SourceRowSha256 });
            e.HasIndex(t => new { t.PeriodId, t.DuplicateReviewStatus });
            e.ToTable(t => t.HasCheckConstraint(
                "CK_imported_transactions_duplicate_hashes",
                "(\"SourceRowSha256\" IS NULL OR \"SourceRowSha256\" ~ '^[0-9a-f]{64}$') AND (\"DuplicateMatchedSourceRowSha256\" IS NULL OR \"DuplicateMatchedSourceRowSha256\" ~ '^[0-9a-f]{64}$') AND (\"DuplicateMatchedTransactionId\" IS NULL OR \"DuplicateMatchedTransactionId\" > 0)"));
            e.ToTable(t => t.HasCheckConstraint(
                "CK_imported_transactions_source_evidence",
                "(\"SourceRowNumber\" IS NULL AND \"SourceRowSha256\" IS NULL AND \"SourceRowJson\" IS NULL) OR (\"SourceRowNumber\" > 0 AND \"SourceRowSha256\" ~ '^[0-9a-f]{64}$' AND jsonb_typeof(\"SourceRowJson\") = 'array' AND \"ImportBatchId\" IS NOT NULL)"));
            e.ToTable(t => t.HasCheckConstraint(
                "CK_imported_transactions_duplicate_candidate",
                "(\"DuplicateReviewStatus\" = 'NotCandidate' AND \"DuplicateCandidateKind\" IS NULL AND \"DuplicateConfidence\" IS NULL AND \"DuplicateCandidateReasonsJson\" IS NULL AND \"DuplicateMatchedTransactionId\" IS NULL AND \"DuplicateMatchedSourceRowSha256\" IS NULL) OR (\"DuplicateReviewStatus\" IN ('Pending', 'LegacyLockedUnverified', 'Retained', 'Discarded') AND \"DuplicateCandidateKind\" IS NOT NULL AND \"DuplicateConfidence\" BETWEEN 0 AND 1 AND \"DuplicateCandidateReasonsJson\" IS NOT NULL AND (\"DuplicateCandidateKind\" = 'LegacyUnverified' OR (\"SourceRowNumber\" > 0 AND \"SourceRowSha256\" IS NOT NULL AND \"SourceRowJson\" IS NOT NULL AND (\"DuplicateMatchedTransactionId\" > 0 OR \"DuplicateMatchedSourceRowSha256\" IS NOT NULL))))"));
            e.ToTable(t => t.HasCheckConstraint(
                "CK_imported_transactions_duplicate_decision",
                "(\"DuplicateDecisionVersion\" = 0 AND \"DuplicateReviewStatus\" IN ('NotCandidate', 'Pending', 'LegacyLockedUnverified') AND \"DuplicateDecisionByUserId\" IS NULL AND \"DuplicateDecisionByDisplayName\" IS NULL AND \"DuplicateDecisionAtUtc\" IS NULL AND \"DuplicateDecisionReason\" IS NULL) OR (\"DuplicateDecisionVersion\" > 0 AND \"DuplicateReviewStatus\" IN ('Pending', 'Retained', 'Discarded') AND \"DuplicateDecisionByUserId\" IS NOT NULL AND \"DuplicateDecisionByDisplayName\" IS NOT NULL AND \"DuplicateDecisionAtUtc\" IS NOT NULL AND char_length(\"DuplicateDecisionReason\") BETWEEN 20 AND 1000)"));
            e.ToTable(t => t.HasCheckConstraint(
                "CK_imported_transactions_duplicate_ledger_state",
                "\"IsDuplicate\" = (\"DuplicateReviewStatus\" IN ('Discarded', 'LegacyLockedUnverified'))"));
        });

        // TransactionRule
        modelBuilder.Entity<TransactionRule>(e =>
        {
            e.ToTable("transaction_rules");
            e.ToTable(t => t.HasTrigger("TR_transaction_rules_ownership"));
            e.ToTable(t => t.HasTrigger("TR_transaction_rules_company_immutable"));
            e.HasKey(r => r.Id);
            e.HasQueryFilter(r => CurrentTenantId == null || r.Company.TenantId == CurrentTenantId);
            e.Property(r => r.Pattern).HasMaxLength(500).IsRequired();
            e.HasOne(r => r.Company).WithMany(c => c.TransactionRules).HasForeignKey(r => r.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Category).WithMany(c => c.Rules).HasForeignKey(r => r.CategoryId).OnDelete(DeleteBehavior.Cascade);
        });

        // AccountCategory (self-referencing hierarchy)
        modelBuilder.Entity<AccountCategory>(e =>
        {
            e.ToTable("account_categories");
            e.ToTable(t => t.HasTrigger("TR_account_categories_ownership"));
            e.ToTable(t => t.HasTrigger("TR_account_categories_company_immutable"));
            e.HasKey(c => c.Id);
            e.HasQueryFilter(c => CurrentTenantId == null || c.CompanyId == null || c.Company!.TenantId == CurrentTenantId);
            e.Property(c => c.Code).HasMaxLength(20).IsRequired();
            e.Property(c => c.Name).HasMaxLength(200).IsRequired();
            e.HasOne(c => c.Company).WithMany(co => co.Categories).HasForeignKey(c => c.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Parent).WithMany(c => c.Children).HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint(
                "CK_account_categories_global_requires_system",
                "\"CompanyId\" IS NOT NULL OR \"IsSystem\""));
        });

        // Debtor
        modelBuilder.Entity<Debtor>(e =>
        {
            e.ToTable("debtors");
            e.HasKey(d => d.Id);
            e.HasQueryFilter(d => CurrentTenantId == null || d.Period.Company.TenantId == CurrentTenantId);
            e.Property(d => d.Name).HasMaxLength(300).IsRequired();
            e.Property(d => d.Amount).HasColumnType("decimal(18,2)");
            e.HasOne(d => d.Period).WithMany(p => p.Debtors).HasForeignKey(d => d.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // Creditor
        modelBuilder.Entity<Creditor>(e =>
        {
            e.ToTable("creditors");
            e.HasKey(c => c.Id);
            e.HasQueryFilter(c => CurrentTenantId == null || c.Period.Company.TenantId == CurrentTenantId);
            e.Property(c => c.Name).HasMaxLength(300).IsRequired();
            e.Property(c => c.Amount).HasColumnType("decimal(18,2)");
            e.HasOne(c => c.Period).WithMany(p => p.Creditors).HasForeignKey(c => c.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // FixedAsset
        modelBuilder.Entity<FixedAsset>(e =>
        {
            e.ToTable("fixed_assets");
            e.HasKey(a => a.Id);
            e.HasQueryFilter(a => CurrentTenantId == null || a.Company.TenantId == CurrentTenantId);
            e.Property(a => a.Name).HasMaxLength(300).IsRequired();
            e.Property(a => a.Category).HasMaxLength(100).IsRequired();
            e.Property(a => a.Cost).HasColumnType("decimal(18,2)");
            e.Property(a => a.ResidualValue).HasColumnType("decimal(18,2)");
            e.Property(a => a.DisposalProceeds).HasColumnType("decimal(18,2)");
            e.Property(a => a.CapitalAllowanceTreatment).HasConversion<string>().HasMaxLength(40);
            e.Property(a => a.CapitalAllowanceEvidence).HasMaxLength(1000);
            e.Property(a => a.CapitalAllowanceReviewedBy).HasMaxLength(200);
            e.ToTable(t => t.HasCheckConstraint(
                "CK_fixed_assets_residual_value",
                "\"ResidualValue\" >= 0 AND \"ResidualValue\" <= \"Cost\""));
            e.HasOne(a => a.Company).WithMany(c => c.FixedAssets).HasForeignKey(a => a.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        // DepreciationEntry
        modelBuilder.Entity<DepreciationEntry>(e =>
        {
            e.ToTable("depreciation_entries");
            e.HasKey(d => d.Id);
            e.HasQueryFilter(d => CurrentTenantId == null || d.Period.Company.TenantId == CurrentTenantId);
            e.Property(d => d.OpeningNbv).HasColumnType("decimal(18,2)");
            e.Property(d => d.Charge).HasColumnType("decimal(18,2)");
            e.Property(d => d.ClosingNbv).HasColumnType("decimal(18,2)");
            e.HasOne(d => d.Asset).WithMany(a => a.DepreciationEntries).HasForeignKey(d => d.AssetId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.Period).WithMany(p => p.DepreciationEntries).HasForeignKey(d => d.PeriodId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(d => new { d.AssetId, d.PeriodId }).IsUnique();
        });

        // CapitalAllowanceClaim
        modelBuilder.Entity<CapitalAllowanceClaim>(e =>
        {
            e.ToTable("capital_allowance_claims");
            e.HasKey(c => c.Id);
            e.HasQueryFilter(c => CurrentTenantId == null || c.Period.Company.TenantId == CurrentTenantId);
            e.Property(c => c.Cost).HasColumnType("decimal(18,2)");
            e.Property(c => c.Claim).HasColumnType("decimal(18,2)");
            e.HasOne(c => c.Asset).WithMany().HasForeignKey(c => c.AssetId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Period).WithMany().HasForeignKey(c => c.PeriodId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => new { c.AssetId, c.PeriodId }).IsUnique();
        });

        // Inventory
        modelBuilder.Entity<Inventory>(e =>
        {
            e.ToTable("inventories");
            e.HasKey(i => i.Id);
            e.HasQueryFilter(i => CurrentTenantId == null || i.Period.Company.TenantId == CurrentTenantId);
            e.Property(i => i.Description).HasMaxLength(500).IsRequired();
            e.Property(i => i.Value).HasColumnType("decimal(18,2)");
            e.HasOne(i => i.Period).WithMany(p => p.Inventories).HasForeignKey(i => i.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // Opening balances
        modelBuilder.Entity<OpeningBalance>(e =>
        {
            e.ToTable("opening_balances");
            e.HasKey(o => o.Id);
            e.HasQueryFilter(o => CurrentTenantId == null || o.Period.Company.TenantId == CurrentTenantId);
            e.Property(o => o.Debit).HasColumnType("decimal(18,2)");
            e.Property(o => o.Credit).HasColumnType("decimal(18,2)");
            e.Property(o => o.SourceNote).HasMaxLength(1000);
            e.Property(o => o.EnteredBy).HasMaxLength(200);
            e.Property(o => o.ReviewedBy).HasMaxLength(200);
            e.HasOne(o => o.Period).WithMany(p => p.OpeningBalances).HasForeignKey(o => o.PeriodId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(o => o.AccountCategory).WithMany().HasForeignKey(o => o.AccountCategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(o => new { o.PeriodId, o.AccountCategoryId }).IsUnique();
        });

        // Year-end review confirmations
        modelBuilder.Entity<YearEndReviewConfirmation>(e =>
        {
            e.ToTable("year_end_review_confirmations");
            e.HasKey(r => r.Id);
            e.HasQueryFilter(r => CurrentTenantId == null || r.Period.Company.TenantId == CurrentTenantId);
            e.Property(r => r.SectionKey).HasMaxLength(80).IsRequired();
            e.Property(r => r.ConfirmedBy).HasMaxLength(200);
            e.Property(r => r.Note).HasMaxLength(1000);
            e.HasOne(r => r.Period).WithMany(p => p.YearEndReviewConfirmations).HasForeignKey(r => r.PeriodId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => new { r.PeriodId, r.SectionKey }).IsUnique();
        });

        // Loan
        modelBuilder.Entity<Loan>(e =>
        {
            e.ToTable("loans");
            e.HasKey(l => l.Id);
            e.HasQueryFilter(l => CurrentTenantId == null || l.Company.TenantId == CurrentTenantId);
            e.Property(l => l.Lender).HasMaxLength(300).IsRequired();
            e.Property(l => l.OriginalAmount).HasColumnType("decimal(18,2)");
            e.Property(l => l.Balance).HasColumnType("decimal(18,2)");
            e.Property(l => l.InterestRate).HasColumnType("decimal(5,2)");
            e.Property(l => l.DueWithinYear).HasColumnType("decimal(18,2)");
            e.Property(l => l.DueAfterYear).HasColumnType("decimal(18,2)");
            e.HasOne(l => l.Company).WithMany(c => c.Loans).HasForeignKey(l => l.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(l => new { l.CompanyId, l.DrawdownDate });
            e.HasIndex(l => new { l.CompanyId, l.BalanceAsOfDate });
            e.ToTable(t => t.HasCheckConstraint(
                "CK_loans_period_effective_dates_required",
                "\"DrawdownDate\" IS NOT NULL AND \"BalanceAsOfDate\" IS NOT NULL"));
        });

        // LoanBalanceSnapshot
        modelBuilder.Entity<LoanBalanceSnapshot>(e =>
        {
            e.ToTable("loan_balance_snapshots");
            e.HasKey(s => s.Id);
            e.HasQueryFilter(s => CurrentTenantId == null || s.Period.Company.TenantId == CurrentTenantId);
            e.Property(s => s.OpeningBalance).HasColumnType("decimal(18,2)");
            e.Property(s => s.Drawdowns).HasColumnType("decimal(18,2)");
            e.Property(s => s.Repayments).HasColumnType("decimal(18,2)");
            e.Property(s => s.ClosingBalance).HasColumnType("decimal(18,2)");
            e.Property(s => s.DueWithinYear).HasColumnType("decimal(18,2)");
            e.Property(s => s.DueAfterYear).HasColumnType("decimal(18,2)");
            e.Property(s => s.Notes).HasMaxLength(1000);
            e.Property(s => s.EnteredBy).HasMaxLength(200);
            e.HasOne(s => s.Loan).WithMany().HasForeignKey(s => s.LoanId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Period).WithMany().HasForeignKey(s => s.PeriodId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => new { s.LoanId, s.PeriodId }).IsUnique();
            e.HasIndex(s => s.PeriodId);
        });

        // DirectorLoan
        modelBuilder.Entity<DirectorLoan>(e =>
        {
            e.ToTable("director_loans");
            e.HasKey(d => d.Id);
            e.HasQueryFilter(d => CurrentTenantId == null || d.Period.Company.TenantId == CurrentTenantId);
            e.Property(d => d.OpeningBalance).HasColumnType("decimal(18,2)");
            e.Property(d => d.Advances).HasColumnType("decimal(18,2)");
            e.Property(d => d.Repayments).HasColumnType("decimal(18,2)");
            e.Property(d => d.ClosingBalance).HasColumnType("decimal(18,2)");
            e.Property(d => d.InterestRate).HasColumnType("decimal(5,2)");
            e.Property(d => d.InterestCharged).HasColumnType("decimal(18,2)");
            e.Property(d => d.AllowanceMade).HasColumnType("decimal(18,2)");
            e.Property(d => d.Section236PresumptionEvidenceReference).HasMaxLength(1000);
            e.Property(d => d.MaxBalanceDuringYear).HasColumnType("decimal(18,2)");
            e.Property(d => d.LoanTerms).HasMaxLength(1000);
            e.Property(d => d.CounterpartyName).HasMaxLength(300);
            e.Property(d => d.RelevantAssetsAmount).HasColumnType("decimal(18,2)");
            e.Property(d => d.RelevantAssetsReference).HasMaxLength(1000);
            e.Property(d => d.TermsAmendmentEvidenceReference).HasMaxLength(1000);
            e.Property(d => d.ExceptionEvidenceReference).HasMaxLength(1000);
            e.Property(d => d.SapDeclarationReference).HasMaxLength(1000);
            e.Property(d => d.SapResolutionReference).HasMaxLength(1000);
            e.Property(d => d.SapCroFilingReference).HasMaxLength(1000);
            e.Property(d => d.ReviewNote).HasMaxLength(2000);
            e.Property(d => d.ReviewedBy).HasMaxLength(200);
            e.Property(d => d.ReviewerRole).HasMaxLength(100);
            e.HasOne(d => d.Period).WithMany(p => p.DirectorLoans).HasForeignKey(d => d.PeriodId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.Director).WithMany().HasForeignKey(d => d.DirectorId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(d => d.PeriodId);
        });

        modelBuilder.Entity<DirectorLoanMovement>(e =>
        {
            e.ToTable("director_loan_movements");
            e.HasKey(m => m.Id);
            e.HasQueryFilter(m => CurrentTenantId == null || m.DirectorLoan.Period.Company.TenantId == CurrentTenantId);
            e.Property(m => m.Amount).HasColumnType("decimal(18,2)");
            e.Property(m => m.EvidenceReference).HasMaxLength(1000);
            e.HasOne(m => m.DirectorLoan)
                .WithMany(d => d.BalanceMovements)
                .HasForeignKey(m => m.DirectorLoanId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(m => new { m.DirectorLoanId, m.MovementDate, m.Id });
        });

        // PayrollSummary (1:1 with AccountingPeriod)
        modelBuilder.Entity<PayrollSummary>(e =>
        {
            e.ToTable("payroll_summaries");
            e.HasKey(p => p.Id);
            e.HasQueryFilter(p => CurrentTenantId == null || p.Period.Company.TenantId == CurrentTenantId);
            e.Property(p => p.GrossWages).HasColumnType("decimal(18,2)");
            e.Property(p => p.DirectorsFees).HasColumnType("decimal(18,2)");
            e.Property(p => p.EmployerPrsi).HasColumnType("decimal(18,2)");
            e.Property(p => p.PensionContributions).HasColumnType("decimal(18,2)");
            e.HasOne(p => p.Period).WithOne(ap => ap.PayrollSummary).HasForeignKey<PayrollSummary>(p => p.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // CorporationTaxScopeReview (1:1 with AccountingPeriod)
        modelBuilder.Entity<CorporationTaxScopeReview>(e =>
        {
            e.ToTable("corporation_tax_scope_reviews");
            e.HasKey(review => review.Id);
            e.HasQueryFilter(review => CurrentTenantId == null || review.Period.Company.TenantId == CurrentTenantId);
            e.Property(review => review.LossTreatment).HasConversion<string>().HasMaxLength(40);
            e.Property(review => review.BroughtForwardTradingLoss).HasColumnType("decimal(18,2)");
            e.Property(review => review.BroughtForwardLossEvidence).HasMaxLength(1000);
            e.Property(review => review.PreparedBy).HasMaxLength(200).IsRequired();
            e.Property(review => review.EvidenceNote).HasMaxLength(2000).IsRequired();
            e.HasOne(review => review.Period)
                .WithOne(period => period.CorporationTaxScopeReview)
                .HasForeignKey<CorporationTaxScopeReview>(review => review.PeriodId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(review => review.PeriodId).IsUnique();
            e.ToTable(table => table.HasCheckConstraint(
                "CK_corporation_tax_scope_reviews_brought_forward_loss",
                "\"BroughtForwardTradingLoss\" >= 0"));
        });

        // CorporationTaxLossRecord (1:1 with AccountingPeriod)
        modelBuilder.Entity<CorporationTaxLossRecord>(e =>
        {
            e.ToTable("corporation_tax_loss_records");
            e.HasKey(record => record.Id);
            e.HasQueryFilter(record => CurrentTenantId == null || record.Period.Company.TenantId == CurrentTenantId);
            e.Property(record => record.OpeningTradingLoss).HasColumnType("decimal(18,2)");
            e.Property(record => record.CurrentPeriodTradingLoss).HasColumnType("decimal(18,2)");
            e.Property(record => record.TradingLossUsed).HasColumnType("decimal(18,2)");
            e.Property(record => record.ClosingTradingLoss).HasColumnType("decimal(18,2)");
            e.Property(record => record.Treatment).HasConversion<string>().HasMaxLength(40);
            e.Property(record => record.CalculationSha256).HasMaxLength(64).IsFixedLength().IsRequired();
            e.Property(record => record.RecordedBy).HasMaxLength(200).IsRequired();
            e.HasOne(record => record.Period)
                .WithOne(period => period.CorporationTaxLossRecord)
                .HasForeignKey<CorporationTaxLossRecord>(record => record.PeriodId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(record => record.PeriodId).IsUnique();
            e.ToTable(table => table.HasCheckConstraint(
                "CK_corporation_tax_loss_records_nonnegative",
                "\"OpeningTradingLoss\" >= 0 AND \"CurrentPeriodTradingLoss\" >= 0 AND \"TradingLossUsed\" >= 0 AND \"ClosingTradingLoss\" >= 0"));
        });

        modelBuilder.Entity<CorporationTaxFilingSupportReview>(e =>
        {
            e.ToTable("corporation_tax_filing_support_reviews");
            e.HasKey(review => review.Id);
            e.HasQueryFilter(review => CurrentTenantId == null || review.Period.Company.TenantId == CurrentTenantId);
            e.Property(review => review.PriorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239).HasColumnType("decimal(18,2)");
            e.Property(review => review.PriorPeriodSection239IncomeTax).HasColumnType("decimal(18,2)");
            e.Property(review => review.CurrentPeriodSection239IncomeTax).HasColumnType("decimal(18,2)");
            e.Property(review => review.PriorLiabilityEvidenceReference).HasMaxLength(1000);
            e.Property(review => review.PreparedBy).HasMaxLength(200).IsRequired();
            e.Property(review => review.EvidenceNote).HasMaxLength(2000).IsRequired();
            e.HasOne(review => review.Period)
                .WithOne(period => period.CorporationTaxFilingSupportReview)
                .HasForeignKey<CorporationTaxFilingSupportReview>(review => review.PeriodId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(review => review.PeriodId).IsUnique();
            e.ToTable(table => table.HasCheckConstraint(
                "CK_corporation_tax_filing_support_reviews_nonnegative",
                "\"CurrentPeriodSection239IncomeTax\" >= 0 AND (\"PriorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239\" IS NULL OR \"PriorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239\" >= 0) AND (\"PriorPeriodSection239IncomeTax\" IS NULL OR \"PriorPeriodSection239IncomeTax\" >= 0)"));
            e.ToTable(table => table.HasCheckConstraint(
                "CK_corporation_tax_filing_support_reviews_prior_pair",
                "((\"PriorPeriodStart\" IS NULL AND \"PriorPeriodEnd\" IS NULL AND \"PriorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239\" IS NULL AND \"PriorPeriodSection239IncomeTax\" IS NULL AND \"PriorLiabilityEvidenceReference\" IS NULL) OR (\"PriorPeriodStart\" IS NOT NULL AND \"PriorPeriodEnd\" IS NOT NULL AND \"PriorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239\" IS NOT NULL AND \"PriorPeriodSection239IncomeTax\" IS NOT NULL AND char_length(btrim(COALESCE(\"PriorLiabilityEvidenceReference\", ''))) >= 20))"));
            e.ToTable(table => table.HasCheckConstraint(
                "CK_corporation_tax_filing_support_reviews_prior_dates",
                "\"PriorPeriodEnd\" IS NULL OR (\"PriorPeriodEnd\" >= \"PriorPeriodStart\" AND \"PriorPeriodEnd\" <= (\"PriorPeriodStart\" + INTERVAL '1 year' - INTERVAL '1 day')::date)"));
        });

        modelBuilder.Entity<CorporationTaxPaymentRecord>(e =>
        {
            e.ToTable("corporation_tax_payment_records");
            e.HasKey(payment => payment.Id);
            e.HasQueryFilter(payment => CurrentTenantId == null || payment.Period.Company.TenantId == CurrentTenantId);
            e.Property(payment => payment.Amount).HasColumnType("decimal(18,2)");
            e.Property(payment => payment.Kind).HasConversion<string>().HasMaxLength(40);
            e.Property(payment => payment.EvidenceReference).HasMaxLength(1000).IsRequired();
            e.Property(payment => payment.ExternalPaymentReference).HasMaxLength(200);
            e.Property(payment => payment.RecordedBy).HasMaxLength(200).IsRequired();
            e.Property(payment => payment.VoidedBy).HasMaxLength(200);
            e.Property(payment => payment.VoidReason).HasMaxLength(1000);
            e.HasOne(payment => payment.Period)
                .WithMany(period => period.CorporationTaxPaymentRecords)
                .HasForeignKey(payment => payment.PeriodId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(payment => new { payment.PeriodId, payment.PaymentDate, payment.Id });
            e.HasIndex(payment => new
            {
                payment.PeriodId,
                payment.PaymentDate,
                payment.Amount,
                payment.Kind,
                payment.EvidenceReference
            }).IsUnique().HasFilter("\"IsVoided\" = FALSE");
            e.ToTable(table => table.HasCheckConstraint(
                "CK_corporation_tax_payment_records_positive_amount",
                "\"Amount\" > 0"));
            e.ToTable(table => table.HasCheckConstraint(
                "CK_corporation_tax_payment_records_evidence",
                "char_length(btrim(\"EvidenceReference\")) >= 20"));
            e.ToTable(table => table.HasCheckConstraint(
                "CK_corporation_tax_payment_records_void_state",
                "(\"IsVoided\" = FALSE AND \"VoidedBy\" IS NULL AND \"VoidedAtUtc\" IS NULL AND \"VoidReason\" IS NULL) OR (\"IsVoided\" = TRUE AND char_length(btrim(COALESCE(\"VoidedBy\", ''))) > 0 AND \"VoidedAtUtc\" IS NOT NULL AND char_length(btrim(COALESCE(\"VoidReason\", ''))) >= 20)"));
        });

        // TaxBalance
        modelBuilder.Entity<TaxBalance>(e =>
        {
            e.ToTable("tax_balances");
            e.HasKey(t => t.Id);
            e.HasQueryFilter(t => CurrentTenantId == null || t.Period.Company.TenantId == CurrentTenantId);
            e.Property(t => t.Liability).HasColumnType("decimal(18,2)");
            e.Property(t => t.Paid).HasColumnType("decimal(18,2)");
            e.Property(t => t.Balance).HasColumnType("decimal(18,2)");
            e.HasOne(t => t.Period).WithMany(p => p.TaxBalances).HasForeignKey(t => t.PeriodId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(t => new { t.PeriodId, t.TaxType }).IsUnique();
        });

        // Dividend
        modelBuilder.Entity<Dividend>(e =>
        {
            e.ToTable("dividends");
            e.HasKey(d => d.Id);
            e.HasQueryFilter(d => CurrentTenantId == null || d.Period.Company.TenantId == CurrentTenantId);
            e.Property(d => d.Amount).HasColumnType("decimal(18,2)");
            e.HasOne(d => d.Period).WithMany(p => p.Dividends).HasForeignKey(d => d.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // Adjustment
        modelBuilder.Entity<Adjustment>(e =>
        {
            e.ToTable("adjustments");
            e.HasKey(a => a.Id);
            e.HasQueryFilter(a => CurrentTenantId == null || a.Period.Company.TenantId == CurrentTenantId);
            e.Property(a => a.Description).HasMaxLength(500).IsRequired();
            e.Property(a => a.Amount).HasColumnType("decimal(18,2)");
            e.Property(a => a.ImpactOnProfit).HasColumnType("decimal(18,2)");
            e.Property(a => a.ImpactOnAssets).HasColumnType("decimal(18,2)");
            e.Property(a => a.DebitCategoryId).IsRequired();
            e.Property(a => a.CreditCategoryId).IsRequired();
            e.ToTable(t => t.HasCheckConstraint("CK_adjustments_positive_amount", "\"Amount\" > 0"));
            e.ToTable(t => t.HasCheckConstraint(
                "CK_adjustments_distinct_accounts",
                "\"DebitCategoryId\" <> \"CreditCategoryId\""));
            e.HasOne(a => a.Period).WithMany(p => p.Adjustments).HasForeignKey(a => a.PeriodId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.DebitCategory).WithMany().HasForeignKey(a => a.DebitCategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.CreditCategory).WithMany().HasForeignKey(a => a.CreditCategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        // Report
        modelBuilder.Entity<Report>(e =>
        {
            e.ToTable("reports");
            e.HasKey(r => r.Id);
            e.HasQueryFilter(r => CurrentTenantId == null || r.Period.Company.TenantId == CurrentTenantId);
            e.HasOne(r => r.Period).WithMany(p => p.Reports).HasForeignKey(r => r.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // NotesDisclosure
        modelBuilder.Entity<NotesDisclosure>(e =>
        {
            e.ToTable("notes_disclosures");
            e.HasKey(n => n.Id);
            e.HasQueryFilter(n => CurrentTenantId == null || n.Period.Company.TenantId == CurrentTenantId);
            e.Property(n => n.Title).HasMaxLength(300).IsRequired();
            e.Property(n => n.Code).HasMaxLength(80);
            e.Property(n => n.ChecklistState).HasConversion<string>().HasMaxLength(30);
            e.Property(n => n.ReviewEvidence).HasMaxLength(20_000);
            e.Property(n => n.ReviewedBy).HasMaxLength(300);
            e.HasOne(n => n.Period).WithMany(p => p.NotesDisclosures).HasForeignKey(n => n.PeriodId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(n => new { n.PeriodId, n.NoteNumber }).IsUnique();
            e.HasIndex(n => new { n.PeriodId, n.Code })
                .IsUnique()
                .HasFilter("\"Code\" IS NOT NULL");
        });

        // ShareCapital
        modelBuilder.Entity<ShareCapital>(e =>
        {
            e.ToTable("share_capitals");
            e.HasKey(s => s.Id);
            e.HasQueryFilter(s => CurrentTenantId == null || s.Company.TenantId == CurrentTenantId);
            e.Property(s => s.ShareClass).HasMaxLength(100).IsRequired();
            e.Property(s => s.NominalValue).HasColumnType("decimal(18,2)");
            e.Property(s => s.TotalValue).HasColumnType("decimal(18,2)");
            e.HasOne(s => s.Company).WithMany(c => c.ShareCapitals).HasForeignKey(s => s.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => new { s.CompanyId, s.IssueDate });
            e.ToTable(t =>
            {
                t.HasCheckConstraint(
                    "CK_share_capitals_issue_date_required",
                    "\"IssueDate\" IS NOT NULL");
                t.HasCheckConstraint(
                    "CK_share_capitals_cancelled_after_issue",
                    "\"CancelledDate\" IS NULL OR \"CancelledDate\" >= \"IssueDate\"");
            });
        });

        // FilingDeadline
        modelBuilder.Entity<FilingDeadline>(e =>
        {
            e.ToTable("filing_deadlines", table =>
            {
                table.HasCheckConstraint(
                    "CK_filing_deadlines_effective_due_date",
                    "(\"ManualOverrideStatus\" = 'Active' AND \"ManualOverrideDueDate\" = \"DueDate\") OR (COALESCE(\"ManualOverrideStatus\", '') <> 'Active' AND \"CalculatedDueDate\" = \"DueDate\")");
                table.HasCheckConstraint(
                    "CK_filing_deadlines_manual_override_evidence",
                    "\"ManualOverrideStatus\" IS NULL OR (\"ManualOverrideDueDate\" IS NOT NULL AND \"ManualOverrideReason\" IS NOT NULL AND \"ManualOverrideEvidenceReference\" IS NOT NULL AND \"ManualOverrideEvidenceSha256\" IS NOT NULL AND \"ManualOverrideByUserId\" IS NOT NULL AND \"ManualOverrideByDisplayName\" IS NOT NULL AND \"ManualOverrideAtUtc\" IS NOT NULL AND \"ManualOverrideCalculationFingerprintSha256\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_filing_deadlines_manual_override_status",
                    "\"ManualOverrideStatus\" IS NULL OR \"ManualOverrideStatus\" IN ('Active', 'NeedsReview')");
            });
            e.ToTable(t => t.HasTrigger("TR_filing_deadlines_ownership"));
            e.HasKey(f => f.Id);
            e.HasQueryFilter(f => CurrentTenantId == null || f.Company.TenantId == CurrentTenantId);
            e.Property(f => f.PenaltyAmount).HasColumnType("decimal(18,2)");
            e.Property(f => f.FilingReference).HasMaxLength(200);
            e.Property(f => f.Notes).HasMaxLength(1000);
            e.Property(f => f.CalculationRuleVersion).HasMaxLength(100);
            e.Property(f => f.CalculationSourceUrl).HasMaxLength(500);
            e.Property(f => f.CalculationFingerprintSha256).HasMaxLength(64);
            e.Property(f => f.ManualOverrideStatus).HasMaxLength(30);
            e.Property(f => f.ManualOverrideReason).HasMaxLength(1000);
            e.Property(f => f.ManualOverrideEvidenceReference).HasMaxLength(300);
            e.Property(f => f.ManualOverrideEvidenceSha256).HasMaxLength(64);
            e.Property(f => f.ManualOverrideByUserId).HasMaxLength(320);
            e.Property(f => f.ManualOverrideByDisplayName).HasMaxLength(200);
            e.Property(f => f.ManualOverrideCalculationFingerprintSha256).HasMaxLength(64);
            e.HasOne(f => f.Company).WithMany(c => c.FilingDeadlines).HasForeignKey(f => f.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(f => f.Period).WithMany(p => p.FilingDeadlines).HasForeignKey(f => f.PeriodId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(f => new { f.CompanyId, f.PeriodId, f.DeadlineType }).IsUnique();
        });

        // FilingHistory
        modelBuilder.Entity<FilingHistory>(e =>
        {
            e.ToTable("filing_histories");
            e.ToTable(t => t.HasTrigger("TR_filing_histories_ownership"));
            e.HasKey(f => f.Id);
            e.HasQueryFilter(f => CurrentTenantId == null || f.Company.TenantId == CurrentTenantId);
            e.Property(f => f.PenaltyAmount).HasColumnType("decimal(18,2)");
            e.Property(f => f.FilingReference).HasMaxLength(200);
            e.HasOne(f => f.Company).WithMany(c => c.FilingHistories).HasForeignKey(f => f.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(f => f.Period).WithMany().HasForeignKey(f => f.PeriodId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(f => new { f.CompanyId, f.DueDate });
            e.HasIndex(f => new { f.CompanyId, f.PeriodId, f.DeadlineType }).IsUnique();
        });

        // PostBalanceSheetEvent
        modelBuilder.Entity<PostBalanceSheetEvent>(e =>
        {
            e.ToTable("post_balance_sheet_events");
            e.HasKey(x => x.Id);
            e.HasQueryFilter(x => CurrentTenantId == null || x.Period.Company.TenantId == CurrentTenantId);
            e.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            e.Property(x => x.FinancialImpact).HasColumnType("decimal(18,2)");
            e.HasOne(x => x.Period).WithMany(p => p.PostBalanceSheetEvents).HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // RelatedPartyTransaction
        modelBuilder.Entity<RelatedPartyTransaction>(e =>
        {
            e.ToTable("related_party_transactions");
            e.HasKey(x => x.Id);
            e.HasQueryFilter(x => CurrentTenantId == null || x.Period.Company.TenantId == CurrentTenantId);
            e.Property(x => x.PartyName).HasMaxLength(300).IsRequired();
            e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            e.Property(x => x.BalanceOwed).HasColumnType("decimal(18,2)");
            e.HasOne(x => x.Period).WithMany(p => p.RelatedPartyTransactions).HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // ContingentLiability
        modelBuilder.Entity<ContingentLiability>(e =>
        {
            e.ToTable("contingent_liabilities");
            e.HasKey(x => x.Id);
            e.HasQueryFilter(x => CurrentTenantId == null || x.Period.Company.TenantId == CurrentTenantId);
            e.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            e.Property(x => x.EstimatedAmount).HasColumnType("decimal(18,2)");
            e.HasOne(x => x.Period).WithMany(p => p.ContingentLiabilities).HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // CharityInfo (1:1 with Company)
        modelBuilder.Entity<CharityInfo>(e =>
        {
            e.ToTable("charity_infos");
            e.HasKey(x => x.Id);
            e.HasQueryFilter(x => CurrentTenantId == null || x.Company.TenantId == CurrentTenantId);
            e.Property(x => x.CharityNumber).HasMaxLength(20);
            e.Property(x => x.GrossIncome).HasColumnType("decimal(18,2)");
            e.Property(x => x.TrusteeRemunerationAmount).HasColumnType("decimal(18,2)");
            e.Property(x => x.GovernanceEvidenceReference).HasMaxLength(300);
            e.Property(x => x.GovernanceReviewedBy).HasMaxLength(200);
            e.Property(x => x.GovernanceEvidenceArtifactSha256).HasMaxLength(64);
            e.HasOne(x => x.Company).WithOne(c => c.CharityInfo).HasForeignKey<CharityInfo>(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        // FundBalance
        modelBuilder.Entity<FundBalance>(e =>
        {
            e.ToTable("fund_balances");
            e.HasKey(x => x.Id);
            e.HasQueryFilter(x => CurrentTenantId == null || x.Period.Company.TenantId == CurrentTenantId);
            e.Property(x => x.FundName).HasMaxLength(300).IsRequired();
            e.Property(x => x.OpeningBalance).HasColumnType("decimal(18,2)");
            e.Property(x => x.IncomingResources).HasColumnType("decimal(18,2)");
            e.Property(x => x.ResourcesExpended).HasColumnType("decimal(18,2)");
            e.Property(x => x.Transfers).HasColumnType("decimal(18,2)");
            e.Property(x => x.GainsLosses).HasColumnType("decimal(18,2)");
            e.Property(x => x.ClosingBalance).HasColumnType("decimal(18,2)");
            e.HasOne(x => x.Period).WithMany(p => p.FundBalances).HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // AuditLog (no foreign keys — records may outlive referenced entities)
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs");
            e.ToTable(t => t.HasTrigger("TR_audit_logs_ownership"));
            e.ToTable(t => t.HasTrigger("TR_audit_logs_scope_immutable"));
            e.HasKey(a => a.Id);
            e.HasQueryFilter(a => CurrentTenantId == null || a.TenantId == CurrentTenantId);
            e.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
            e.Property(a => a.Action).HasMaxLength(50).IsRequired();
            e.Property(a => a.RequestId).HasMaxLength(128);
            e.Property(a => a.ActorDisplayName).HasMaxLength(200);
            e.Property(a => a.PreviousIntegrityHash).HasMaxLength(64);
            e.Property(a => a.IntegrityHash).HasMaxLength(64);
            e.HasIndex(a => a.Timestamp);
            e.HasIndex(a => new { a.CompanyId, a.Timestamp });
            e.HasIndex(a => new { a.TenantId, a.Timestamp });
            e.HasIndex(a => a.RequestId);
            e.HasIndex(a => a.IntegrityHash).IsUnique();
            e.HasIndex(a => a.PreviousIntegrityHash).IsUnique();
            e.HasIndex(a => new { a.CompanyId, a.PeriodId, a.Timestamp });
        });

        modelBuilder.Entity<AuditIntegrityCheckpoint>(e =>
        {
            e.ToTable("audit_integrity_checkpoints");
            e.ToTable(t => t.HasTrigger("TR_audit_integrity_checkpoints_ownership"));
            e.ToTable(t => t.HasTrigger("TR_audit_integrity_checkpoints_scope_immutable"));
            e.HasKey(c => c.Id);
            e.HasQueryFilter(c => CurrentTenantId == null || c.TenantId == CurrentTenantId);
            e.Property(c => c.LastIntegrityHash).HasMaxLength(64).IsRequired();
            e.Property(c => c.CreatedByUserId).HasMaxLength(320);
            e.Property(c => c.CreatedByDisplayName).HasMaxLength(200);
            e.Property(c => c.RequestId).HasMaxLength(128);
            e.Property(c => c.KeyId).HasMaxLength(120).IsRequired();
            e.Property(c => c.Signature).HasMaxLength(64).IsRequired();
            e.HasIndex(c => new { c.CompanyId, c.Id });
            e.HasIndex(c => new { c.CompanyId, c.LastAuditLogId });
            e.HasIndex(c => new { c.TenantId, c.CreatedAtUtc });
            e.HasIndex(c => c.Signature).IsUnique();
        });

        modelBuilder.Entity<CompanyQuarantineEvent>(e =>
        {
            e.ToTable("company_quarantine_events");
            e.ToTable(t => t.HasTrigger("TR_company_quarantine_events_immutable"));
            e.ToTable(t => t.HasCheckConstraint(
                "CK_company_quarantine_events_type",
                "\"EventType\" IN ('Quarantined', 'Recovered')"));
            e.ToTable(t => t.HasCheckConstraint(
                "CK_company_quarantine_events_hashes",
                "char_length(\"InventorySha256\") = 64 AND char_length(\"EvidenceSha256\") = 64 AND (\"PreviousEvidenceSha256\" IS NULL OR char_length(\"PreviousEvidenceSha256\") = 64)"));
            e.ToTable(t => t.HasCheckConstraint(
                "CK_company_quarantine_events_reason",
                "char_length(\"Reason\") BETWEEN 20 AND 2000"));
            e.HasKey(item => item.Id);
            e.HasQueryFilter(item => CurrentTenantId == null || item.TenantId == CurrentTenantId);
            e.Property(item => item.CompanyLegalName).HasMaxLength(500).IsRequired();
            e.Property(item => item.EventType).HasMaxLength(20).IsRequired();
            e.Property(item => item.ActorUserId).HasMaxLength(320).IsRequired();
            e.Property(item => item.ActorDisplayName).HasMaxLength(200).IsRequired();
            e.Property(item => item.ActorRole).HasMaxLength(50).IsRequired();
            e.Property(item => item.Reason).HasMaxLength(2000).IsRequired();
            e.Property(item => item.TypedConfirmation).HasMaxLength(500).IsRequired();
            e.Property(item => item.InventoryJson).HasColumnType("jsonb").IsRequired();
            e.Property(item => item.InventorySha256).HasMaxLength(64).IsRequired();
            e.Property(item => item.PreviousEvidenceSha256).HasMaxLength(64);
            e.Property(item => item.EvidenceSha256).HasMaxLength(64).IsRequired();
            e.Property(item => item.RequestId).HasMaxLength(128);
            e.HasIndex(item => new { item.TenantId, item.OccurredAtUtc });
            e.HasIndex(item => new { item.CompanyId, item.Id });
            e.HasIndex(item => item.EvidenceSha256).IsUnique();
        });

        modelBuilder.Entity<CompanyOnboardingRequest>(e =>
        {
            e.ToTable("company_onboarding_requests");
            e.ToTable(t => t.HasTrigger("TR_company_onboarding_requests_immutable"));
            e.ToTable(t => t.HasCheckConstraint(
                "CK_company_onboarding_requests_status",
                "(\"Status\" = 'InProgress' AND \"CompanyId\" IS NULL AND \"PeriodId\" IS NULL AND \"BankAccountId\" IS NULL AND \"CompletedAtUtc\" IS NULL AND \"ResponseJson\" IS NULL AND \"ResponseSha256\" IS NULL) OR (\"Status\" = 'Completed' AND \"CompanyId\" IS NOT NULL AND \"PeriodId\" IS NOT NULL AND \"BankAccountId\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL AND \"ResponseJson\" IS NOT NULL AND \"ResponseSha256\" IS NOT NULL)"));
            e.ToTable(t => t.HasCheckConstraint(
                "CK_company_onboarding_requests_hashes",
                "char_length(\"RequestSha256\") = 64 AND (\"ResponseSha256\" IS NULL OR char_length(\"ResponseSha256\") = 64)"));
            e.ToTable(t => t.HasCheckConstraint(
                "CK_company_onboarding_requests_category_count",
                "\"CategoryCount\" >= 0"));
            e.HasKey(request => request.Id);
            e.HasQueryFilter(request => CurrentTenantId == null || request.TenantId == CurrentTenantId);
            e.Property(request => request.IdempotencyKey).HasMaxLength(128).IsRequired();
            e.Property(request => request.RequestSha256).HasMaxLength(64).IsRequired();
            e.Property(request => request.Status).HasMaxLength(20).IsRequired();
            e.Property(request => request.CreatedByUserId).HasMaxLength(320).IsRequired();
            e.Property(request => request.CreatedByDisplayName).HasMaxLength(200).IsRequired();
            e.Property(request => request.ResponseJson).HasColumnType("text");
            e.Property(request => request.ResponseSha256).HasMaxLength(64);
            e.HasIndex(request => new { request.TenantId, request.IdempotencyKey }).IsUnique();
            e.HasIndex(request => request.CompanyId);
            e.HasIndex(request => request.RequestSha256);
        });

        modelBuilder.Entity<IdempotencyRecord>(e =>
        {
            e.ToTable("idempotency_records");
            e.ToTable(t => t.HasTrigger("TR_idempotency_records_immutable"));
            e.ToTable(t => t.HasCheckConstraint(
                "CK_idempotency_records_status",
                "(\"Status\" = 'InProgress' AND \"CompletedAtUtc\" IS NULL AND \"ResultResourceType\" IS NULL AND \"ResultResourceId\" IS NULL AND \"ResultHttpStatusCode\" IS NULL AND \"ResponseJson\" IS NULL AND \"ResponseSha256\" IS NULL) OR (\"Status\" = 'Completed' AND \"CompletedAtUtc\" IS NOT NULL AND \"ResultResourceType\" IS NOT NULL AND \"ResultResourceId\" IS NOT NULL AND \"ResultHttpStatusCode\" BETWEEN 100 AND 599 AND \"ResponseJson\" IS NOT NULL AND \"ResponseSha256\" IS NOT NULL)"));
            e.ToTable(t => t.HasCheckConstraint(
                "CK_idempotency_records_hashes",
                "\"RequestFingerprintSha256\" ~ '^[0-9a-f]{64}$' AND (\"ResponseSha256\" IS NULL OR \"ResponseSha256\" ~ '^[0-9a-f]{64}$')"));
            e.ToTable(t => t.HasCheckConstraint(
                "CK_idempotency_records_key",
                "char_length(\"IdempotencyKey\") BETWEEN 8 AND 128 AND \"IdempotencyKey\" ~ '^[A-Za-z0-9._:-]+$'"));
            e.ToTable(t => t.HasCheckConstraint(
                "CK_idempotency_records_expiry",
                "\"ExpiresAtUtc\" > \"StartedAtUtc\" AND (\"CompletedAtUtc\" IS NULL OR \"ExpiresAtUtc\" > \"CompletedAtUtc\")"));
            e.HasKey(record => record.Id);
            e.HasQueryFilter(record => CurrentTenantId == null || record.TenantId == CurrentTenantId);
            e.HasOne<Tenant>().WithMany().HasForeignKey(record => record.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.Property(record => record.IdempotencyKey).HasMaxLength(128).IsRequired();
            e.Property(record => record.Operation).HasMaxLength(160).IsRequired();
            e.Property(record => record.RequestFingerprintSha256).HasMaxLength(64).IsRequired();
            e.Property(record => record.Status).HasMaxLength(20).IsRequired();
            e.Property(record => record.CreatedByUserId).HasMaxLength(320).IsRequired();
            e.Property(record => record.CreatedByDisplayName).HasMaxLength(200).IsRequired();
            e.Property(record => record.ResultResourceType).HasMaxLength(160);
            e.Property(record => record.ResultResourceId).HasMaxLength(200);
            e.Property(record => record.ResponseJson).HasColumnType("text");
            e.Property(record => record.ResponseSha256).HasMaxLength(64);
            e.HasIndex(record => new { record.TenantId, record.IdempotencyKey }).IsUnique();
            e.HasIndex(record => record.ExpiresAtUtc);
            e.HasIndex(record => new { record.TenantId, record.Operation, record.CompletedAtUtc });
        });

        modelBuilder.ConfigurePrivacyGovernance();
        modelBuilder.Entity<LoginSecurityEvent>()
            .HasQueryFilter(item => CurrentTenantId == null || item.TenantId == CurrentTenantId);
        modelBuilder.Entity<PrivacySubjectRequest>()
            .HasQueryFilter(item => CurrentTenantId == null || item.TenantId == CurrentTenantId);
        modelBuilder.Entity<PrivacyIncidentExercise>()
            .HasQueryFilter(item => CurrentTenantId == null || item.TenantId == CurrentTenantId);

        modelBuilder.ConfigureDeadlineDelivery();
        modelBuilder.Entity<DeadlineReminderOutbox>()
            .HasQueryFilter(item => CurrentTenantId == null || item.TenantId == CurrentTenantId);
        modelBuilder.Entity<PlatformJobRun>()
            .HasQueryFilter(item => CurrentTenantId == null || item.TenantId == CurrentTenantId);

        // Store all enums as strings for readability
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType.IsEnum || (Nullable.GetUnderlyingType(property.ClrType)?.IsEnum ?? false))
                {
                    property.SetColumnType("text");
                }
            }
        }
    }

    private void ValidateCompanyQuarantineEvidenceChanges()
    {
        ChangeTracker.DetectChanges();
        var evidenceEntries = ChangeTracker.Entries<CompanyQuarantineEvent>().ToArray();
        foreach (var entry in evidenceEntries)
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new BusinessRuleException("Company quarantine evidence is append-only and cannot be changed or deleted.");
            if (entry.State == EntityState.Added
                && !CompanyQuarantineEvidenceIntegrity.IsValid(entry.Entity))
            {
                throw new BusinessRuleException("Company quarantine evidence failed its SHA-256 integrity check.");
            }
        }

        var addedEvidence = evidenceEntries
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToArray();
        foreach (var entry in ChangeTracker.Entries<Company>()
                     .Where(entry => entry.State == EntityState.Modified))
        {
            var quarantineStateChanged = entry.Property(nameof(Company.IsQuarantined)).IsModified;
            var quarantineEvidenceChanged = quarantineStateChanged
                || entry.Property(nameof(Company.QuarantinedAtUtc)).IsModified
                || entry.Property(nameof(Company.QuarantinedByUserId)).IsModified
                || entry.Property(nameof(Company.QuarantinedByDisplayName)).IsModified
                || entry.Property(nameof(Company.QuarantineReason)).IsModified
                || entry.Property(nameof(Company.QuarantineEvidenceSha256)).IsModified;
            if (!quarantineEvidenceChanged)
                continue;

            var company = entry.Entity;
            var expectedEventType = company.IsQuarantined ? "Quarantined" : "Recovered";
            var matchingEvidence = addedEvidence.Where(evidence =>
                    evidence.CompanyId == company.Id
                    && evidence.TenantId == company.TenantId
                    && string.Equals(evidence.CompanyLegalName, company.LegalName, StringComparison.Ordinal)
                    && string.Equals(evidence.EventType, expectedEventType, StringComparison.Ordinal)
                    && string.Equals(evidence.TypedConfirmation, company.LegalName, StringComparison.Ordinal)
                    && evidence.OccurredAtUtc == company.UpdatedAt)
                .ToArray();
            var matchesCurrentState = matchingEvidence.Length == 1
                && (company.IsQuarantined
                    ? string.Equals(matchingEvidence[0].EvidenceSha256, company.QuarantineEvidenceSha256, StringComparison.Ordinal)
                      && string.Equals(matchingEvidence[0].ActorUserId, company.QuarantinedByUserId, StringComparison.Ordinal)
                      && string.Equals(matchingEvidence[0].ActorDisplayName, company.QuarantinedByDisplayName, StringComparison.Ordinal)
                      && string.Equals(matchingEvidence[0].Reason, company.QuarantineReason, StringComparison.Ordinal)
                      && matchingEvidence[0].OccurredAtUtc == company.QuarantinedAtUtc
                    : company.QuarantinedAtUtc is null
                      && company.QuarantinedByUserId is null
                      && company.QuarantinedByDisplayName is null
                      && company.QuarantineReason is null
                      && company.QuarantineEvidenceSha256 is null);
            if (!quarantineStateChanged || !matchesCurrentState)
            {
                throw new BusinessRuleException(
                    "A company quarantine or recovery transition requires one matching immutable evidence event.");
            }
        }
    }

    private void ValidateCompanyOnboardingRequestChanges()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<CompanyOnboardingRequest>())
        {
            var request = entry.Entity;
            if (entry.State == EntityState.Deleted)
            {
                throw new BusinessRuleException(
                    "Company onboarding idempotency evidence cannot be deleted.");
            }
            if (entry.State == EntityState.Added)
            {
                var validReservation = string.Equals(request.Status, "InProgress", StringComparison.Ordinal)
                    && request.CompanyId is null
                    && request.PeriodId is null
                    && request.BankAccountId is null
                    && request.CompletedAtUtc is null
                    && request.ResponseJson is null
                    && request.ResponseSha256 is null
                    && request.RequestSha256 is { Length: 64 };
                if (!validReservation)
                {
                    throw new BusinessRuleException(
                        "Company onboarding must begin with a valid in-progress idempotency reservation.");
                }
                continue;
            }
            if (entry.State != EntityState.Modified)
                continue;

            var originalStatus = entry.Property(nameof(CompanyOnboardingRequest.Status)).OriginalValue as string;
            var immutableChanged = entry.Property(nameof(CompanyOnboardingRequest.TenantId)).IsModified
                || entry.Property(nameof(CompanyOnboardingRequest.IdempotencyKey)).IsModified
                || entry.Property(nameof(CompanyOnboardingRequest.RequestSha256)).IsModified
                || entry.Property(nameof(CompanyOnboardingRequest.CreatedByUserId)).IsModified
                || entry.Property(nameof(CompanyOnboardingRequest.CreatedByDisplayName)).IsModified
                || entry.Property(nameof(CompanyOnboardingRequest.StartedAtUtc)).IsModified;
            var responseHash = request.ResponseJson is null
                ? null
                : Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(request.ResponseJson)));
            var validCompletion = string.Equals(originalStatus, "InProgress", StringComparison.Ordinal)
                && string.Equals(request.Status, "Completed", StringComparison.Ordinal)
                && !immutableChanged
                && request.CompanyId is > 0
                && request.PeriodId is > 0
                && request.BankAccountId is > 0
                && request.CategoryCount > 0
                && request.CompletedAtUtc is not null
                && request.ResponseSha256 is { Length: 64 }
                && string.Equals(responseHash, request.ResponseSha256, StringComparison.OrdinalIgnoreCase);
            if (!validCompletion)
            {
                throw new BusinessRuleException(
                    "Company onboarding idempotency evidence is immutable after its single completion transition.");
            }
        }
    }

    private void ValidateIdempotencyRecordChanges()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<IdempotencyRecord>())
        {
            var record = entry.Entity;
            if (entry.State == EntityState.Deleted)
            {
                if (record.ExpiresAtUtc > DateTime.UtcNow)
                    throw new BusinessRuleException("Unexpired idempotency evidence cannot be deleted.");
                continue;
            }

            if (entry.State == EntityState.Added)
            {
                var validReservation = string.Equals(record.Status, IdempotencyService.InProgressStatus, StringComparison.Ordinal)
                    && IdempotencyService.IsValidKey(record.IdempotencyKey)
                    && record.RequestFingerprintSha256 is { Length: 64 }
                    && record.CompletedAtUtc is null
                    && record.ResultResourceType is null
                    && record.ResultResourceId is null
                    && record.ResultHttpStatusCode is null
                    && record.ResponseJson is null
                    && record.ResponseSha256 is null
                    && record.ExpiresAtUtc > record.StartedAtUtc;
                if (!validReservation)
                    throw new BusinessRuleException("Idempotency evidence must begin as a valid in-progress reservation.");
                continue;
            }

            if (entry.State != EntityState.Modified)
                continue;

            var originalStatus = entry.Property(nameof(IdempotencyRecord.Status)).OriginalValue as string;
            var immutableChanged = entry.Property(nameof(IdempotencyRecord.TenantId)).IsModified
                || entry.Property(nameof(IdempotencyRecord.IdempotencyKey)).IsModified
                || entry.Property(nameof(IdempotencyRecord.Operation)).IsModified
                || entry.Property(nameof(IdempotencyRecord.RequestFingerprintSha256)).IsModified
                || entry.Property(nameof(IdempotencyRecord.CreatedByUserId)).IsModified
                || entry.Property(nameof(IdempotencyRecord.CreatedByDisplayName)).IsModified
                || entry.Property(nameof(IdempotencyRecord.StartedAtUtc)).IsModified;
            var responseHash = record.ResponseJson is null ? null : IdempotencyService.Hash(record.ResponseJson);
            var validCompletion = string.Equals(originalStatus, IdempotencyService.InProgressStatus, StringComparison.Ordinal)
                && string.Equals(record.Status, IdempotencyService.CompletedStatus, StringComparison.Ordinal)
                && !immutableChanged
                && record.CompletedAtUtc is not null
                && record.ExpiresAtUtc > record.CompletedAtUtc
                && !string.IsNullOrWhiteSpace(record.ResultResourceType)
                && !string.IsNullOrWhiteSpace(record.ResultResourceId)
                && record.ResultHttpStatusCode is >= 100 and <= 599
                && record.ResponseSha256 is { Length: 64 }
                && string.Equals(responseHash, record.ResponseSha256, StringComparison.OrdinalIgnoreCase);
            if (!validCompletion)
                throw new BusinessRuleException("Idempotency evidence is immutable after its single completion transition.");
        }
    }

    private void ValidateExternalFilingHandoffChanges()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<FilingAuthorityEngagement>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new BusinessRuleException("Filing authority evidence is append-only and cannot be changed or deleted.");
            if (entry.State != EntityState.Added)
                continue;
            var authority = entry.Entity;
            var evidenceHash = authority.AuthorityEvidenceArtifact is { Length: > 0 }
                ? Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(authority.AuthorityEvidenceArtifact))
                : null;
            if (authority.Version <= 0
                || authority.AuthorityEvidenceSha256 is not { Length: 64 }
                || !string.Equals(evidenceHash, authority.AuthorityEvidenceSha256, StringComparison.OrdinalIgnoreCase)
                || authority.RecordSha256 is not { Length: 64 })
            {
                throw new BusinessRuleException("Filing authority evidence failed its append-only integrity checks.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<ExternalFilingHandoffSnapshot>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new BusinessRuleException("External filing handoff snapshots are append-only and cannot be changed or deleted.");
            if (entry.State != EntityState.Added)
                continue;
            var snapshot = entry.Entity;
            var artifactHash = snapshot.ArtifactBytes is { Length: > 0 }
                ? Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(snapshot.ArtifactBytes))
                : null;
            if (snapshot.DirectSubmissionSupported
                || snapshot.IsCompleteExternalReturn
                || snapshot.Version <= 0
                || !string.Equals(artifactHash, snapshot.ArtifactSha256, StringComparison.OrdinalIgnoreCase)
                || snapshot.SourceFingerprintSha256 is not { Length: 64 }
                || snapshot.AuthorityEvidenceSha256 is not { Length: 64 }
                || snapshot.QualifiedReviewManifestSha256 is not { Length: 64 })
            {
                throw new BusinessRuleException("External filing handoff snapshot failed its immutable artifact integrity checks.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<ExternalFilingOutcomeEvent>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new BusinessRuleException("External filing outcome evidence is append-only and cannot be changed or deleted.");
            if (entry.State != EntityState.Added)
                continue;
            var outcome = entry.Entity;
            var evidenceHash = outcome.EvidenceArtifact is { Length: > 0 }
                ? Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(outcome.EvidenceArtifact))
                : null;
            if (outcome.Sequence <= 0
                || outcome.SnapshotArtifactSha256 is not { Length: 64 }
                || outcome.EventSha256 is not { Length: 64 }
                || outcome.EvidenceArtifact is not null
                    && !string.Equals(evidenceHash, outcome.EvidenceSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessRuleException("External filing outcome failed its append-only evidence integrity checks.");
            }
        }
    }

    private void ValidateAnnualReturnDateEvidenceChanges()
    {
        ChangeTracker.DetectChanges();
        var records = ChangeTracker.Entries<AnnualReturnDateRecord>().ToArray();
        foreach (var entry in records)
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new BusinessRuleException(
                    "Annual Return Date evidence is append-only and cannot be changed or deleted.");
            }
            if (entry.State == EntityState.Added
                && !AnnualReturnDateEvidenceIntegrity.IsValid(entry.Entity))
            {
                throw new BusinessRuleException(
                    "Annual Return Date evidence failed its SHA-256 integrity check.");
            }
        }

        var added = records
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToArray();
        foreach (var companyEntry in ChangeTracker.Entries<Company>()
                     .Where(entry => entry.State == EntityState.Modified
                         && entry.Property(nameof(Company.AnnualReturnDate)).IsModified))
        {
            var company = companyEntry.Entity;
            var previous = (DateOnly?)companyEntry.Property(nameof(Company.AnnualReturnDate)).OriginalValue;
            var matching = added.Count(record => record.CompanyId == company.Id
                && record.PreviousAnnualReturnDate == previous
                && record.AnnualReturnDate == company.AnnualReturnDate);
            if (matching != 1)
            {
                throw new BusinessRuleException(
                    "Changing a company's Annual Return Date requires one matching immutable evidence record.");
            }
        }
    }

    private void ValidateDuplicateReviewChanges()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<ImportBatch>())
        {
            var hasRetainedSource = entry.Entity.SourceFileSha256 is not null;
            if (entry.State == EntityState.Deleted && hasRetainedSource)
            {
                throw new BusinessRuleException(
                    "A retained import batch cannot be deleted because its source and warning evidence is auditable.");
            }
            if (entry.State == EntityState.Modified && hasRetainedSource)
            {
                var immutableBatchChanged =
                    entry.Property(nameof(ImportBatch.BankAccountId)).IsModified
                    || entry.Property(nameof(ImportBatch.Filename)).IsModified
                    || entry.Property(nameof(ImportBatch.ImportedAt)).IsModified
                    || entry.Property(nameof(ImportBatch.RowCount)).IsModified
                    || entry.Property(nameof(ImportBatch.MatchedCount)).IsModified
                    || entry.Property(nameof(ImportBatch.SourceFileSha256)).IsModified
                    || entry.Property(nameof(ImportBatch.SourceFileBytes)).IsModified
                    || entry.Property(nameof(ImportBatch.SourceHeaderJson)).IsModified
                    || entry.Property(nameof(ImportBatch.ImportWarningsJson)).IsModified;
                if (immutableBatchChanged)
                {
                    throw new BusinessRuleException(
                        "Import batch source, processing and warning evidence is immutable after import.");
                }
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                var batch = entry.Entity;
                var hasAnyEvidence = batch.SourceFileSha256 is not null
                    || batch.SourceFileBytes is not null
                    || batch.SourceHeaderJson is not null
                    || batch.ImportWarningsJson is not null;
                var completeEvidence = IsSha256(batch.SourceFileSha256)
                    && batch.SourceFileBytes is > 0
                    && IsJsonArray(batch.SourceHeaderJson)
                    && IsJsonStringArray(batch.ImportWarningsJson);
                if (hasAnyEvidence && !completeEvidence)
                    throw new BusinessRuleException("Import batch source and warning evidence is incomplete or invalid.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<ImportedTransaction>())
        {
            var transaction = entry.Entity;
            if (entry.State == EntityState.Deleted && transaction.SourceRowSha256 is not null)
            {
                throw new BusinessRuleException(
                    "Retained import source rows cannot be deleted directly. Use the audited duplicate-review decision to exclude a row from the ledger.");
            }

            if (entry.State == EntityState.Modified)
            {
                var originalSourceHash = entry.Property(nameof(ImportedTransaction.SourceRowSha256)).OriginalValue as string;
                if (originalSourceHash is not null)
                {
                    var sourceFactsChanged =
                        entry.Property(nameof(ImportedTransaction.BankAccountId)).IsModified
                        || entry.Property(nameof(ImportedTransaction.PeriodId)).IsModified
                        || entry.Property(nameof(ImportedTransaction.ImportBatchId)).IsModified
                        || entry.Property(nameof(ImportedTransaction.Date)).IsModified
                        || entry.Property(nameof(ImportedTransaction.Description)).IsModified
                        || entry.Property(nameof(ImportedTransaction.Amount)).IsModified
                        || entry.Property(nameof(ImportedTransaction.Balance)).IsModified
                        || entry.Property(nameof(ImportedTransaction.Reference)).IsModified;
                    if (sourceFactsChanged)
                    {
                        throw new BusinessRuleException(
                            "Imported transaction source facts and ownership are immutable after import.");
                    }
                }

                var immutableEvidenceChanged =
                    entry.Property(nameof(ImportedTransaction.SourceRowNumber)).IsModified
                    || entry.Property(nameof(ImportedTransaction.SourceRowSha256)).IsModified
                    || entry.Property(nameof(ImportedTransaction.SourceRowJson)).IsModified
                    || entry.Property(nameof(ImportedTransaction.DuplicateCandidateKind)).IsModified
                    || entry.Property(nameof(ImportedTransaction.DuplicateConfidence)).IsModified
                    || entry.Property(nameof(ImportedTransaction.DuplicateCandidateReasonsJson)).IsModified
                    || entry.Property(nameof(ImportedTransaction.DuplicateMatchedTransactionId)).IsModified
                    || entry.Property(nameof(ImportedTransaction.DuplicateMatchedSourceRowSha256)).IsModified;
                if (immutableEvidenceChanged)
                {
                    throw new BusinessRuleException(
                        "Duplicate candidate source and match evidence is immutable after import.");
                }

                var reviewChanged =
                    entry.Property(nameof(ImportedTransaction.DuplicateReviewStatus)).IsModified
                    || entry.Property(nameof(ImportedTransaction.IsDuplicate)).IsModified
                    || entry.Property(nameof(ImportedTransaction.DuplicateDecisionByUserId)).IsModified
                    || entry.Property(nameof(ImportedTransaction.DuplicateDecisionByDisplayName)).IsModified
                    || entry.Property(nameof(ImportedTransaction.DuplicateDecisionAtUtc)).IsModified
                    || entry.Property(nameof(ImportedTransaction.DuplicateDecisionReason)).IsModified
                    || entry.Property(nameof(ImportedTransaction.DuplicateDecisionVersion)).IsModified;
                var originalVersion = (int)(entry.Property(nameof(ImportedTransaction.DuplicateDecisionVersion)).OriginalValue ?? 0);
                if (reviewChanged && transaction.DuplicateDecisionVersion != originalVersion + 1)
                {
                    throw new BusinessRuleException(
                        "A duplicate review transition must increment its decision version exactly once.");
                }
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
                ValidateDuplicateReviewState(transaction);
        }
    }

    private static void ValidateDuplicateReviewState(ImportedTransaction transaction)
    {
        var hasAnySourceEvidence = transaction.SourceRowNumber is not null
            || transaction.SourceRowSha256 is not null
            || transaction.SourceRowJson is not null;
        var hasCompleteSourceEvidence = transaction.SourceRowNumber is > 0
            && IsSha256(transaction.SourceRowSha256)
            && IsJsonArray(transaction.SourceRowJson);
        if (hasAnySourceEvidence && !hasCompleteSourceEvidence)
            throw new BusinessRuleException("Imported transaction source-row evidence is incomplete.");

        var candidate = transaction.DuplicateReviewStatus != DuplicateReviewStatus.NotCandidate;
        if (!candidate)
        {
            if (transaction.DuplicateCandidateKind is not null
                || transaction.DuplicateConfidence is not null
                || transaction.DuplicateCandidateReasonsJson is not null
                || transaction.DuplicateMatchedTransactionId is not null
                || transaction.DuplicateMatchedSourceRowSha256 is not null)
            {
                throw new BusinessRuleException("A non-candidate transaction cannot carry duplicate-match evidence.");
            }
        }
        else
        {
            var legacy = transaction.DuplicateCandidateKind == DuplicateCandidateKind.LegacyUnverified;
            if (transaction.DuplicateCandidateKind is null
                || transaction.DuplicateConfidence is not (>= 0m and <= 1m)
                || !IsNonEmptyJsonStringArray(transaction.DuplicateCandidateReasonsJson)
                || transaction.DuplicateMatchedTransactionId is <= 0
                || !legacy && (!hasCompleteSourceEvidence
                    || transaction.DuplicateMatchedTransactionId is not > 0
                        && !IsSha256(transaction.DuplicateMatchedSourceRowSha256)))
            {
                throw new BusinessRuleException("Duplicate candidate evidence is incomplete or invalid.");
            }
        }

        var excludedFromLedger = transaction.DuplicateReviewStatus is
            DuplicateReviewStatus.Discarded or DuplicateReviewStatus.LegacyLockedUnverified;
        if (transaction.IsDuplicate != excludedFromLedger)
            throw new BusinessRuleException("Only an explicit discarded duplicate decision may exclude an imported row from the ledger.");

        var decisionEvidenceComplete = !string.IsNullOrWhiteSpace(transaction.DuplicateDecisionByUserId)
            && !string.IsNullOrWhiteSpace(transaction.DuplicateDecisionByDisplayName)
            && transaction.DuplicateDecisionAtUtc is not null
            && transaction.DuplicateDecisionReason?.Trim().EnumerateRunes().Count() is >= 20 and <= 1000;
        if (transaction.DuplicateDecisionVersion == 0)
        {
            if (transaction.DuplicateReviewStatus is DuplicateReviewStatus.Retained or DuplicateReviewStatus.Discarded
                || transaction.DuplicateDecisionByUserId is not null
                || transaction.DuplicateDecisionByDisplayName is not null
                || transaction.DuplicateDecisionAtUtc is not null
                || transaction.DuplicateDecisionReason is not null)
            {
                throw new BusinessRuleException("Resolved duplicate review requires retained reviewer evidence.");
            }
        }
        else if (transaction.DuplicateDecisionVersion < 0
            || transaction.DuplicateReviewStatus is DuplicateReviewStatus.NotCandidate or DuplicateReviewStatus.LegacyLockedUnverified
            || !decisionEvidenceComplete)
        {
            throw new BusinessRuleException("Duplicate decision evidence is incomplete or invalid.");
        }
    }

    private void ValidateIdentityLifecycleEvidenceChanges()
    {
        foreach (var entry in ChangeTracker.Entries<UserLifecycleEvent>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new BusinessRuleException("User lifecycle evidence is append-only and cannot be changed or deleted.");

            if (entry.State == EntityState.Added
                && (entry.Entity.TenantId <= 0
                    || entry.Entity.TargetUserId <= 0
                    || entry.Entity.ActorUserId <= 0
                    || string.IsNullOrWhiteSpace(entry.Entity.EventType)
                    || !IsJsonObject(entry.Entity.DetailsJson)))
            {
                throw new BusinessRuleException("User lifecycle evidence must identify tenant, actor, target, event and structured details.");
            }
        }
    }

    private static bool IsJsonObject(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsJsonArray(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsJsonStringArray(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Array
                && document.RootElement.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsSha256(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsNonEmptyJsonStringArray(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Array
                && document.RootElement.GetArrayLength() > 0
                && document.RootElement.EnumerateArray().All(item =>
                    item.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(item.GetString()));
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
