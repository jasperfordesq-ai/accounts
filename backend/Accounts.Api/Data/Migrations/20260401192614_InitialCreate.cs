using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Accounts.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: true),
                    PeriodId = table.Column<int>(type: "integer", nullable: true),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OldValueJson = table.Column<string>(type: "text", nullable: true),
                    NewValueJson = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "companies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LegalName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TradingName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CroNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    TaxReference = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CompanyType = table.Column<string>(type: "text", nullable: false),
                    IncorporationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FinancialYearStartMonth = table.Column<int>(type: "integer", nullable: false),
                    ArdMonth = table.Column<int>(type: "integer", nullable: false),
                    RegisteredOfficeAddress1 = table.Column<string>(type: "text", nullable: true),
                    RegisteredOfficeAddress2 = table.Column<string>(type: "text", nullable: true),
                    RegisteredOfficeCity = table.Column<string>(type: "text", nullable: true),
                    RegisteredOfficeCounty = table.Column<string>(type: "text", nullable: true),
                    RegisteredOfficeEircode = table.Column<string>(type: "text", nullable: true),
                    IsGroupMember = table.Column<bool>(type: "boolean", nullable: false),
                    IsHolding = table.Column<bool>(type: "boolean", nullable: false),
                    IsInvestment = table.Column<bool>(type: "boolean", nullable: false),
                    IsSubsidiary = table.Column<bool>(type: "boolean", nullable: false),
                    IsDormant = table.Column<bool>(type: "boolean", nullable: false),
                    IsTrading = table.Column<bool>(type: "boolean", nullable: false),
                    IsVatRegistered = table.Column<bool>(type: "boolean", nullable: false),
                    IsEmployer = table.Column<bool>(type: "boolean", nullable: false),
                    HasStock = table.Column<bool>(type: "boolean", nullable: false),
                    OwnsAssets = table.Column<bool>(type: "boolean", nullable: false),
                    HasBorrowings = table.Column<bool>(type: "boolean", nullable: false),
                    HasDirectorLoans = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "account_categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: true),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    TaxTreatment = table.Column<string>(type: "text", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    ParentId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_account_categories_account_categories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "account_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_categories_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_periods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    IsFirstYear = table.Column<bool>(type: "boolean", nullable: false),
                    LockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LockedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounting_periods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_accounting_periods_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bank_accounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    OpeningBalanceDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bank_accounts_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "company_officers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    AppointedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ResignedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Address = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_officers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_company_officers_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fixed_assets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AcquisitionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DisposalDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DisposalProceeds = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    UsefulLifeYears = table.Column<int>(type: "integer", nullable: false),
                    DepreciationMethod = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fixed_assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fixed_assets_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "loans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Lender = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    InterestRate = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    IsDirectorLoan = table.Column<bool>(type: "boolean", nullable: false),
                    DueWithinYear = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DueAfterYear = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_loans_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transaction_rules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Pattern = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CategoryId = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transaction_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_transaction_rules_account_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "account_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_transaction_rules_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "adjustments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DebitCategoryId = table.Column<int>(type: "integer", nullable: true),
                    CreditCategoryId = table.Column<int>(type: "integer", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    LegalBasis = table.Column<string>(type: "text", nullable: true),
                    ImpactOnProfit = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ImpactOnAssets = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    ApprovedBy = table.Column<string>(type: "text", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsAuto = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_adjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_adjustments_account_categories_CreditCategoryId",
                        column: x => x.CreditCategoryId,
                        principalTable: "account_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_adjustments_account_categories_DebitCategoryId",
                        column: x => x.DebitCategoryId,
                        principalTable: "account_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_adjustments_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "creditors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    DueWithinYear = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_creditors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_creditors_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cro_filing_packages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PdfPath = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cro_filing_packages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cro_filing_packages_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "debtors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_debtors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_debtors_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dividends",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DateDeclared = table.Column<DateOnly>(type: "date", nullable: true),
                    DatePaid = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dividends", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dividends_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "filing_regimes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    CanUseMicro = table.Column<bool>(type: "boolean", nullable: false),
                    CanFileAbridged = table.Column<bool>(type: "boolean", nullable: false),
                    AuditExempt = table.Column<bool>(type: "boolean", nullable: false),
                    ElectedRegime = table.Column<string>(type: "text", nullable: false),
                    RequiredNotesJson = table.Column<string>(type: "text", nullable: true),
                    RequiredStatementsJson = table.Column<string>(type: "text", nullable: true),
                    DeterminedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_filing_regimes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_filing_regimes_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ValuationMethod = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inventories_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notes_disclosures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    NoteNumber = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: true),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    IsIncluded = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notes_disclosures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notes_disclosures_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payroll_summaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    GrossWages = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    EmployerPrsi = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PensionContributions = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    StaffCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payroll_summaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payroll_summaries_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    DataJson = table.Column<string>(type: "text", nullable: true),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reports_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "revenue_filing_packages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    Ct1DataJson = table.Column<string>(type: "text", nullable: true),
                    IxbrlPath = table.Column<string>(type: "text", nullable: true),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_revenue_filing_packages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_revenue_filing_packages_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "size_classifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    Turnover = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    BalanceSheetTotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    AvgEmployees = table.Column<int>(type: "integer", nullable: false),
                    PriorYearClass = table.Column<string>(type: "text", nullable: true),
                    CalculatedClass = table.Column<string>(type: "text", nullable: false),
                    OverrideClass = table.Column<string>(type: "text", nullable: true),
                    OverrideReason = table.Column<string>(type: "text", nullable: true),
                    ExclusionFlagsJson = table.Column<string>(type: "text", nullable: true),
                    QualificationNotes = table.Column<string>(type: "text", nullable: true),
                    CalculatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_size_classifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_size_classifications_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tax_balances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    TaxType = table.Column<string>(type: "text", nullable: false),
                    Liability = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Paid = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_balances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tax_balances_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_batches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BankAccountId = table.Column<int>(type: "integer", nullable: false),
                    Filename = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowCount = table.Column<int>(type: "integer", nullable: false),
                    MatchedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_batches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_import_batches_bank_accounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "bank_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "director_loans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    DirectorId = table.Column<int>(type: "integer", nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Advances = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Repayments = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ClosingBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_director_loans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_director_loans_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_director_loans_company_officers_DirectorId",
                        column: x => x.DirectorId,
                        principalTable: "company_officers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "depreciation_entries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AssetId = table.Column<int>(type: "integer", nullable: false),
                    PeriodId = table.Column<int>(type: "integer", nullable: false),
                    OpeningNbv = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Charge = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ClosingNbv = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_depreciation_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_depreciation_entries_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_depreciation_entries_fixed_assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "fixed_assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "imported_transactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BankAccountId = table.Column<int>(type: "integer", nullable: false),
                    PeriodId = table.Column<int>(type: "integer", nullable: true),
                    ImportBatchId = table.Column<int>(type: "integer", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CategoryId = table.Column<int>(type: "integer", nullable: true),
                    ConfidenceScore = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    IsDuplicate = table.Column<bool>(type: "boolean", nullable: false),
                    ManualOverride = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_imported_transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_imported_transactions_account_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "account_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_imported_transactions_accounting_periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "accounting_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_imported_transactions_bank_accounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "bank_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_imported_transactions_import_batches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "import_batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_categories_CompanyId",
                table: "account_categories",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_account_categories_ParentId",
                table: "account_categories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_periods_CompanyId_PeriodEnd",
                table: "accounting_periods",
                columns: new[] { "CompanyId", "PeriodEnd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_adjustments_CreditCategoryId",
                table: "adjustments",
                column: "CreditCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_adjustments_DebitCategoryId",
                table: "adjustments",
                column: "DebitCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_adjustments_PeriodId",
                table: "adjustments",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_CompanyId_Timestamp",
                table: "audit_logs",
                columns: new[] { "CompanyId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_Timestamp",
                table: "audit_logs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_bank_accounts_CompanyId",
                table: "bank_accounts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_companies_CroNumber",
                table: "companies",
                column: "CroNumber",
                unique: true,
                filter: "\"CroNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_company_officers_CompanyId",
                table: "company_officers",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_creditors_PeriodId",
                table: "creditors",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_cro_filing_packages_PeriodId",
                table: "cro_filing_packages",
                column: "PeriodId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_debtors_PeriodId",
                table: "debtors",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_depreciation_entries_AssetId_PeriodId",
                table: "depreciation_entries",
                columns: new[] { "AssetId", "PeriodId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_depreciation_entries_PeriodId",
                table: "depreciation_entries",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_director_loans_DirectorId",
                table: "director_loans",
                column: "DirectorId");

            migrationBuilder.CreateIndex(
                name: "IX_director_loans_PeriodId",
                table: "director_loans",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_dividends_PeriodId",
                table: "dividends",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_filing_regimes_PeriodId",
                table: "filing_regimes",
                column: "PeriodId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fixed_assets_CompanyId",
                table: "fixed_assets",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_import_batches_BankAccountId",
                table: "import_batches",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_imported_transactions_CategoryId",
                table: "imported_transactions",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_imported_transactions_ImportBatchId",
                table: "imported_transactions",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_imported_transactions_PeriodId",
                table: "imported_transactions",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "ix_transaction_duplicate_check",
                table: "imported_transactions",
                columns: new[] { "BankAccountId", "Date", "Amount", "Description" });

            migrationBuilder.CreateIndex(
                name: "IX_inventories_PeriodId",
                table: "inventories",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_loans_CompanyId",
                table: "loans",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_notes_disclosures_PeriodId_NoteNumber",
                table: "notes_disclosures",
                columns: new[] { "PeriodId", "NoteNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payroll_summaries_PeriodId",
                table: "payroll_summaries",
                column: "PeriodId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reports_PeriodId",
                table: "reports",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_revenue_filing_packages_PeriodId",
                table: "revenue_filing_packages",
                column: "PeriodId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_size_classifications_PeriodId",
                table: "size_classifications",
                column: "PeriodId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tax_balances_PeriodId_TaxType",
                table: "tax_balances",
                columns: new[] { "PeriodId", "TaxType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transaction_rules_CategoryId",
                table: "transaction_rules",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_transaction_rules_CompanyId",
                table: "transaction_rules",
                column: "CompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "adjustments");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "creditors");

            migrationBuilder.DropTable(
                name: "cro_filing_packages");

            migrationBuilder.DropTable(
                name: "debtors");

            migrationBuilder.DropTable(
                name: "depreciation_entries");

            migrationBuilder.DropTable(
                name: "director_loans");

            migrationBuilder.DropTable(
                name: "dividends");

            migrationBuilder.DropTable(
                name: "filing_regimes");

            migrationBuilder.DropTable(
                name: "imported_transactions");

            migrationBuilder.DropTable(
                name: "inventories");

            migrationBuilder.DropTable(
                name: "loans");

            migrationBuilder.DropTable(
                name: "notes_disclosures");

            migrationBuilder.DropTable(
                name: "payroll_summaries");

            migrationBuilder.DropTable(
                name: "reports");

            migrationBuilder.DropTable(
                name: "revenue_filing_packages");

            migrationBuilder.DropTable(
                name: "size_classifications");

            migrationBuilder.DropTable(
                name: "tax_balances");

            migrationBuilder.DropTable(
                name: "transaction_rules");

            migrationBuilder.DropTable(
                name: "fixed_assets");

            migrationBuilder.DropTable(
                name: "company_officers");

            migrationBuilder.DropTable(
                name: "import_batches");

            migrationBuilder.DropTable(
                name: "accounting_periods");

            migrationBuilder.DropTable(
                name: "account_categories");

            migrationBuilder.DropTable(
                name: "bank_accounts");

            migrationBuilder.DropTable(
                name: "companies");
        }
    }
}
