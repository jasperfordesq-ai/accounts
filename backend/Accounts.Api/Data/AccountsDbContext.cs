using Microsoft.EntityFrameworkCore;
using Accounts.Api.Entities;

namespace Accounts.Api.Data;

public class AccountsDbContext(DbContextOptions<AccountsDbContext> options) : DbContext(options)
{
    // Tenancy & Users
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<UserCompanyAccess> UserCompanyAccesses => Set<UserCompanyAccess>();

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
    public DbSet<PayrollSummary> PayrollSummaries => Set<PayrollSummary>();
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
            e.HasOne(a => a.User).WithMany(u => u.CompanyAccesses).HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Company).WithMany(c => c.UserAccesses).HasForeignKey(a => a.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => new { a.UserId, a.CompanyId }).IsUnique();
            e.HasIndex(a => a.CompanyId);
        });

        // Company
        modelBuilder.Entity<Company>(e =>
        {
            e.ToTable("companies");
            e.HasKey(c => c.Id);
            e.Property(c => c.LegalName).HasMaxLength(500).IsRequired();
            e.Property(c => c.TradingName).HasMaxLength(500);
            e.Property(c => c.CroNumber).HasMaxLength(20);
            e.Property(c => c.TaxReference).HasMaxLength(20);
            e.HasOne(c => c.Tenant).WithMany(t => t.Companies).HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(c => c.CroNumber).IsUnique().HasFilter("\"CroNumber\" IS NOT NULL");
            e.HasIndex(c => c.TenantId);
        });

        // CompanyOfficer
        modelBuilder.Entity<CompanyOfficer>(e =>
        {
            e.ToTable("company_officers");
            e.HasKey(o => o.Id);
            e.Property(o => o.Name).HasMaxLength(300).IsRequired();
            e.HasOne(o => o.Company).WithMany(c => c.Officers).HasForeignKey(o => o.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        // AccountingPeriod
        modelBuilder.Entity<AccountingPeriod>(e =>
        {
            e.ToTable("accounting_periods");
            e.HasKey(p => p.Id);
            e.Property(p => p.LockedBy).HasMaxLength(200);
            e.Property(p => p.ReopenedBy).HasMaxLength(200);
            e.Property(p => p.ReopenReason).HasMaxLength(1000);
            e.HasOne(p => p.Company).WithMany(c => c.Periods).HasForeignKey(p => p.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.CompanyId, p.PeriodEnd }).IsUnique();
        });

        // SizeClassification (1:1 with AccountingPeriod)
        modelBuilder.Entity<SizeClassification>(e =>
        {
            e.ToTable("size_classifications");
            e.HasKey(s => s.Id);
            e.Property(s => s.Turnover).HasColumnType("decimal(18,2)");
            e.Property(s => s.BalanceSheetTotal).HasColumnType("decimal(18,2)");
            e.HasOne(s => s.Period).WithOne(p => p.SizeClassification).HasForeignKey<SizeClassification>(s => s.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // FilingRegime (1:1 with AccountingPeriod)
        modelBuilder.Entity<FilingRegime>(e =>
        {
            e.ToTable("filing_regimes");
            e.HasKey(f => f.Id);
            e.HasOne(f => f.Period).WithOne(p => p.FilingRegime).HasForeignKey<FilingRegime>(f => f.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // CroFilingPackage (1:1 with AccountingPeriod)
        modelBuilder.Entity<CroFilingPackage>(e =>
        {
            e.ToTable("cro_filing_packages");
            e.HasKey(c => c.Id);
            e.HasOne(c => c.Period).WithOne(p => p.CroFilingPackage).HasForeignKey<CroFilingPackage>(c => c.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // RevenueFilingPackage (1:1 with AccountingPeriod)
        modelBuilder.Entity<RevenueFilingPackage>(e =>
        {
            e.ToTable("revenue_filing_packages");
            e.HasKey(r => r.Id);
            e.HasOne(r => r.Period).WithOne(p => p.RevenueFilingPackage).HasForeignKey<RevenueFilingPackage>(r => r.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // CharityFilingPackage (1:1 with AccountingPeriod)
        modelBuilder.Entity<CharityFilingPackage>(e =>
        {
            e.ToTable("charity_filing_packages");
            e.HasKey(c => c.Id);
            e.Property(c => c.ApprovedBy).HasMaxLength(200);
            e.Property(c => c.SubmittedBy).HasMaxLength(200);
            e.Property(c => c.AcceptedBy).HasMaxLength(200);
            e.Property(c => c.AnnualReturnReference).HasMaxLength(200);
            e.Property(c => c.RejectionReason).HasMaxLength(1000);
            e.HasOne(c => c.Period).WithOne(p => p.CharityFilingPackage).HasForeignKey<CharityFilingPackage>(c => c.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // BankAccount
        modelBuilder.Entity<BankAccount>(e =>
        {
            e.ToTable("bank_accounts");
            e.HasKey(b => b.Id);
            e.Property(b => b.Name).HasMaxLength(200).IsRequired();
            e.Property(b => b.Iban).HasMaxLength(34);
            e.Property(b => b.Currency).HasMaxLength(3);
            e.Property(b => b.OpeningBalance).HasColumnType("decimal(18,2)");
            e.HasOne(b => b.Company).WithMany(c => c.BankAccounts).HasForeignKey(b => b.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint(
                "CK_bank_accounts_opening_balance_date_required",
                "\"OpeningBalance\" = 0 OR \"OpeningBalanceDate\" IS NOT NULL"));
        });

        // ImportBatch
        modelBuilder.Entity<ImportBatch>(e =>
        {
            e.ToTable("import_batches");
            e.HasKey(b => b.Id);
            e.Property(b => b.Filename).HasMaxLength(500).IsRequired();
            e.HasOne(b => b.BankAccount).WithMany(a => a.ImportBatches).HasForeignKey(b => b.BankAccountId).OnDelete(DeleteBehavior.Cascade);
        });

        // ImportedTransaction
        modelBuilder.Entity<ImportedTransaction>(e =>
        {
            e.ToTable("imported_transactions");
            e.HasKey(t => t.Id);
            e.Property(t => t.Description).HasMaxLength(1000).IsRequired();
            e.Property(t => t.Reference).HasMaxLength(200);
            e.Property(t => t.Amount).HasColumnType("decimal(18,2)");
            e.Property(t => t.Balance).HasColumnType("decimal(18,2)");
            e.Property(t => t.ConfidenceScore).HasColumnType("decimal(5,4)");
            e.HasOne(t => t.BankAccount).WithMany(a => a.Transactions).HasForeignKey(t => t.BankAccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.Period).WithMany(p => p.Transactions).HasForeignKey(t => t.PeriodId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.ImportBatch).WithMany(b => b.Transactions).HasForeignKey(t => t.ImportBatchId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.Category).WithMany(c => c.Transactions).HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(t => new { t.BankAccountId, t.Date, t.Amount, t.Description }).HasDatabaseName("ix_transaction_duplicate_check");
        });

        // TransactionRule
        modelBuilder.Entity<TransactionRule>(e =>
        {
            e.ToTable("transaction_rules");
            e.HasKey(r => r.Id);
            e.Property(r => r.Pattern).HasMaxLength(500).IsRequired();
            e.HasOne(r => r.Company).WithMany(c => c.TransactionRules).HasForeignKey(r => r.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Category).WithMany(c => c.Rules).HasForeignKey(r => r.CategoryId).OnDelete(DeleteBehavior.Cascade);
        });

        // AccountCategory (self-referencing hierarchy)
        modelBuilder.Entity<AccountCategory>(e =>
        {
            e.ToTable("account_categories");
            e.HasKey(c => c.Id);
            e.Property(c => c.Code).HasMaxLength(20).IsRequired();
            e.Property(c => c.Name).HasMaxLength(200).IsRequired();
            e.HasOne(c => c.Company).WithMany(co => co.Categories).HasForeignKey(c => c.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Parent).WithMany(c => c.Children).HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        // Debtor
        modelBuilder.Entity<Debtor>(e =>
        {
            e.ToTable("debtors");
            e.HasKey(d => d.Id);
            e.Property(d => d.Name).HasMaxLength(300).IsRequired();
            e.Property(d => d.Amount).HasColumnType("decimal(18,2)");
            e.HasOne(d => d.Period).WithMany(p => p.Debtors).HasForeignKey(d => d.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // Creditor
        modelBuilder.Entity<Creditor>(e =>
        {
            e.ToTable("creditors");
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(300).IsRequired();
            e.Property(c => c.Amount).HasColumnType("decimal(18,2)");
            e.HasOne(c => c.Period).WithMany(p => p.Creditors).HasForeignKey(c => c.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // FixedAsset
        modelBuilder.Entity<FixedAsset>(e =>
        {
            e.ToTable("fixed_assets");
            e.HasKey(a => a.Id);
            e.Property(a => a.Name).HasMaxLength(300).IsRequired();
            e.Property(a => a.Category).HasMaxLength(100).IsRequired();
            e.Property(a => a.Cost).HasColumnType("decimal(18,2)");
            e.Property(a => a.DisposalProceeds).HasColumnType("decimal(18,2)");
            e.HasOne(a => a.Company).WithMany(c => c.FixedAssets).HasForeignKey(a => a.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        // DepreciationEntry
        modelBuilder.Entity<DepreciationEntry>(e =>
        {
            e.ToTable("depreciation_entries");
            e.HasKey(d => d.Id);
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
            e.Property(i => i.Description).HasMaxLength(500).IsRequired();
            e.Property(i => i.Value).HasColumnType("decimal(18,2)");
            e.HasOne(i => i.Period).WithMany(p => p.Inventories).HasForeignKey(i => i.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // Opening balances
        modelBuilder.Entity<OpeningBalance>(e =>
        {
            e.ToTable("opening_balances");
            e.HasKey(o => o.Id);
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
            e.Property(d => d.OpeningBalance).HasColumnType("decimal(18,2)");
            e.Property(d => d.Advances).HasColumnType("decimal(18,2)");
            e.Property(d => d.Repayments).HasColumnType("decimal(18,2)");
            e.Property(d => d.ClosingBalance).HasColumnType("decimal(18,2)");
            e.Property(d => d.InterestRate).HasColumnType("decimal(5,2)");
            e.Property(d => d.InterestCharged).HasColumnType("decimal(18,2)");
            e.Property(d => d.MaxBalanceDuringYear).HasColumnType("decimal(18,2)");
            e.Property(d => d.LoanTerms).HasMaxLength(1000);
            e.HasOne(d => d.Period).WithMany(p => p.DirectorLoans).HasForeignKey(d => d.PeriodId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.Director).WithMany().HasForeignKey(d => d.DirectorId).OnDelete(DeleteBehavior.Restrict);
        });

        // PayrollSummary (1:1 with AccountingPeriod)
        modelBuilder.Entity<PayrollSummary>(e =>
        {
            e.ToTable("payroll_summaries");
            e.HasKey(p => p.Id);
            e.Property(p => p.GrossWages).HasColumnType("decimal(18,2)");
            e.Property(p => p.EmployerPrsi).HasColumnType("decimal(18,2)");
            e.Property(p => p.PensionContributions).HasColumnType("decimal(18,2)");
            e.HasOne(p => p.Period).WithOne(ap => ap.PayrollSummary).HasForeignKey<PayrollSummary>(p => p.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // TaxBalance
        modelBuilder.Entity<TaxBalance>(e =>
        {
            e.ToTable("tax_balances");
            e.HasKey(t => t.Id);
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
            e.Property(d => d.Amount).HasColumnType("decimal(18,2)");
            e.HasOne(d => d.Period).WithMany(p => p.Dividends).HasForeignKey(d => d.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // Adjustment
        modelBuilder.Entity<Adjustment>(e =>
        {
            e.ToTable("adjustments");
            e.HasKey(a => a.Id);
            e.Property(a => a.Description).HasMaxLength(500).IsRequired();
            e.Property(a => a.Amount).HasColumnType("decimal(18,2)");
            e.Property(a => a.ImpactOnProfit).HasColumnType("decimal(18,2)");
            e.Property(a => a.ImpactOnAssets).HasColumnType("decimal(18,2)");
            e.HasOne(a => a.Period).WithMany(p => p.Adjustments).HasForeignKey(a => a.PeriodId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.DebitCategory).WithMany().HasForeignKey(a => a.DebitCategoryId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(a => a.CreditCategory).WithMany().HasForeignKey(a => a.CreditCategoryId).OnDelete(DeleteBehavior.SetNull);
        });

        // Report
        modelBuilder.Entity<Report>(e =>
        {
            e.ToTable("reports");
            e.HasKey(r => r.Id);
            e.HasOne(r => r.Period).WithMany(p => p.Reports).HasForeignKey(r => r.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // NotesDisclosure
        modelBuilder.Entity<NotesDisclosure>(e =>
        {
            e.ToTable("notes_disclosures");
            e.HasKey(n => n.Id);
            e.Property(n => n.Title).HasMaxLength(300).IsRequired();
            e.HasOne(n => n.Period).WithMany(p => p.NotesDisclosures).HasForeignKey(n => n.PeriodId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(n => new { n.PeriodId, n.NoteNumber }).IsUnique();
        });

        // ShareCapital
        modelBuilder.Entity<ShareCapital>(e =>
        {
            e.ToTable("share_capitals");
            e.HasKey(s => s.Id);
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
            e.ToTable("filing_deadlines");
            e.HasKey(f => f.Id);
            e.Property(f => f.PenaltyAmount).HasColumnType("decimal(18,2)");
            e.Property(f => f.FilingReference).HasMaxLength(200);
            e.Property(f => f.Notes).HasMaxLength(1000);
            e.HasOne(f => f.Company).WithMany(c => c.FilingDeadlines).HasForeignKey(f => f.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(f => f.Period).WithMany(p => p.FilingDeadlines).HasForeignKey(f => f.PeriodId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(f => new { f.CompanyId, f.PeriodId, f.DeadlineType }).IsUnique();
        });

        // FilingHistory
        modelBuilder.Entity<FilingHistory>(e =>
        {
            e.ToTable("filing_histories");
            e.HasKey(f => f.Id);
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
            e.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            e.Property(x => x.FinancialImpact).HasColumnType("decimal(18,2)");
            e.HasOne(x => x.Period).WithMany(p => p.PostBalanceSheetEvents).HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // RelatedPartyTransaction
        modelBuilder.Entity<RelatedPartyTransaction>(e =>
        {
            e.ToTable("related_party_transactions");
            e.HasKey(x => x.Id);
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
            e.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            e.Property(x => x.EstimatedAmount).HasColumnType("decimal(18,2)");
            e.HasOne(x => x.Period).WithMany(p => p.ContingentLiabilities).HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Cascade);
        });

        // CharityInfo (1:1 with Company)
        modelBuilder.Entity<CharityInfo>(e =>
        {
            e.ToTable("charity_infos");
            e.HasKey(x => x.Id);
            e.Property(x => x.CharityNumber).HasMaxLength(20);
            e.Property(x => x.GrossIncome).HasColumnType("decimal(18,2)");
            e.Property(x => x.TrusteeRemunerationAmount).HasColumnType("decimal(18,2)");
            e.HasOne(x => x.Company).WithOne(c => c.CharityInfo).HasForeignKey<CharityInfo>(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        // FundBalance
        modelBuilder.Entity<FundBalance>(e =>
        {
            e.ToTable("fund_balances");
            e.HasKey(x => x.Id);
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
            e.HasKey(a => a.Id);
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
            e.HasKey(c => c.Id);
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
}
