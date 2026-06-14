using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Data;

public static class SeedData
{
    private const string MainTenantSlug = "main-demo";
    private const string PasswordAlgorithm = "PBKDF2-SHA256-210000";
    private const int PasswordIterations = 210_000;
    private const string DemoSeedUser = "demo.seed@accounts.local";
    private static readonly string[] DemoUserEmails =
    [
        "owner@accounts-demo.ie",
        "accountant@accounts-demo.ie",
        "reviewer@accounts-demo.ie",
        "client@accounts-demo.ie"
    ];
    private static readonly string[] NonCharitySampleCompanyCroNumbers =
    [
        "654321",
        "789012"
    ];

    public static async Task SeedAsync(
        AccountsDbContext db,
        bool seedDemoUsers = true,
        bool seedSampleCompanies = true)
    {
        var tenant = await EnsureMainTenantAsync(db);

        var micro = await SeedGreenValleyAsync(db, tenant.Id);
        var seededCompanies = new List<Company> { micro };
        if (seedSampleCompanies)
        {
            seededCompanies.Add(await SeedConnachtDigitalAsync(db, tenant.Id));
            seededCompanies.Add(await SeedAtlanticManufacturingAsync(db, tenant.Id));
        }
        else
        {
            await RemoveNonCharitySampleCompaniesAsync(db);
        }

        if (seedDemoUsers)
            await EnsureDemoUsersAsync(db, tenant.Id, micro.Id);
        else
            await RemoveDemoUsersAsync(db);

        await EnsureTenantAuditLogAsync(db, tenant, seededCompanies.ToArray());

        await db.SaveChangesAsync();
    }

    private static async Task<Tenant> EnsureMainTenantAsync(AccountsDbContext db)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == MainTenantSlug);
        if (tenant is null)
        {
            tenant = new Tenant
            {
                Name = "Accounts v2 Demo Tenant",
                Slug = MainTenantSlug,
                IsMainDemoTenant = true
            };
            db.Tenants.Add(tenant);
        }

        tenant.Name = "Accounts v2 Demo Tenant";
        tenant.IsMainDemoTenant = true;
        tenant.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return tenant;
    }

    private static async Task<Company> SeedGreenValleyAsync(AccountsDbContext db, int tenantId)
    {
        var company = await EnsureCompanyAsync(db, tenantId, "567890", c =>
        {
            c.LegalName = "Green Valley Community Development CLG";
            c.TradingName = "Green Valley";
            c.TaxReference = "5678901T";
            c.CompanyType = CompanyType.CompanyLimitedByGuarantee;
            c.IncorporationDate = new DateOnly(2019, 3, 15);
            c.FinancialYearStartMonth = 1;
            c.ArdMonth = 6;
            c.RegisteredOfficeAddress1 = "12 Main Street";
            c.RegisteredOfficeAddress2 = "Market Square";
            c.RegisteredOfficeCity = "Castlebar";
            c.RegisteredOfficeCounty = "Mayo";
            c.RegisteredOfficeEircode = "F23 AB12";
            c.IsTrading = true;
            c.IsDormant = false;
            c.IsVatRegistered = false;
            c.IsEmployer = true;
            c.HasStock = false;
            c.OwnsAssets = true;
            c.HasBorrowings = false;
            c.HasDirectorLoans = false;
            c.IsCharitableOrganisation = true;
        });

        await EnsureOfficerAsync(db, company.Id, "Mary O'Brien", OfficerRole.Director, new DateOnly(2019, 3, 15), "12 Main Street, Castlebar, Co. Mayo");
        await EnsureOfficerAsync(db, company.Id, "Patrick Walsh", OfficerRole.Director, new DateOnly(2019, 3, 15), "Quay Road, Westport, Co. Mayo");
        await EnsureOfficerAsync(db, company.Id, "Siobhan Kelly", OfficerRole.Secretary, new DateOnly(2019, 3, 15), "Spencer Street, Castlebar, Co. Mayo");
        await EnsureShareCapitalAsync(db, company.Id, "Guarantee", 1m, 1, 1m, company.IncorporationDate);

        var bank = await EnsureBankAccountAsync(db, company.Id, "AIB Current Account", "IE12AIBK93115212345678", 4_500m, new DateOnly(2024, 1, 1));
        await EnsureFixedAssetAsync(db, company.Id, "Laptop - Dell XPS", "Computer Equipment", 1_200m, new DateOnly(2020, 6, 1), 3, DepreciationMethod.StraightLine);
        await EnsureFixedAssetAsync(db, company.Id, "Office Furniture", "Office Equipment", 800m, new DateOnly(2019, 4, 1), 10, DepreciationMethod.StraightLine);
        await EnsureFixedAssetAsync(db, company.Id, "Community Training Projector", "Office Equipment", 1_950m, new DateOnly(2024, 2, 20), 5, DepreciationMethod.StraightLine);

        var period = await EnsurePeriodAsync(db, company.Id, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31), PeriodStatus.Review, false);
        var categories = await EnsureCategoriesAsync(db, company.Id);

        await EnsureSizeClassificationAsync(db, period.Id, 78_000m, 39_500m, 3, CompanySizeClass.Micro, CompanySizeClass.Micro, "Micro entity and charity-style CLG. Prepared with SORP-ready charity data for the v2 showcase.");
        await EnsureFilingRegimeAsync(db, period.Id, true, true, true, ElectedRegime.Micro,
            ["FRS 105 micro accounts", "Directors' advances note", "Approval statement", "Charity SORP schedules"],
            ["Micro balance sheet", "Income and expenditure summary", "Trustees annual report"]);
        await EnsureFilingPackagesAsync(db, period.Id, FilingStatus.ReadyForReview, "GV-2024-CRO-DRAFT", "GV-CT1-2024-DRAFT", true, true, true);
        await EnsureDeadlinesAsync(db, company.Id, period.Id, period.PeriodEnd, includeCharity: true);

        await EnsureImportBatchWithTransactionsAsync(db, bank, period, categories, "green-valley-aib-2024.csv",
        [
            new(new DateOnly(2024, 1, 5), "Mayo County Council grant receipt", 18_000m, "4300", "GRANT-001", 0.98m),
            new(new DateOnly(2024, 2, 7), "Community training workshop fees", 3_250m, "4000", "INV-24007", 0.93m),
            new(new DateOnly(2024, 3, 14), "Department grant tranche 1", 22_500m, "4300", "GRANT-002", 0.98m),
            new(new DateOnly(2024, 4, 3), "Venue hire - family resource centre", -1_200m, "6100", "VENUE-APR", 0.91m),
            new(new DateOnly(2024, 5, 16), "Tutor payments", -4_800m, "6000", "PAY-0524", 0.9m),
            new(new DateOnly(2024, 6, 28), "Insurance renewal", -1_450m, "6200", "INS-2024", 0.9m),
            new(new DateOnly(2024, 7, 19), "Donations - summer programme", 4_950m, "4100", "DON-SUM", 0.84m),
            new(new DateOnly(2024, 9, 10), "Electric Ireland community hub", -920m, "6300", "UTIL-0910", 0.88m),
            new(new DateOnly(2024, 10, 25), "Community training workshop fees", 3_100m, "4000", "INV-24104", 0.93m),
            new(new DateOnly(2024, 12, 20), "Accountancy interim fee", -750m, "6810", "ACC-1220", 0.95m),
        ]);

        await EnsureTransactionRulesAsync(db, company.Id, categories,
        [
            new("grant", "4300", 1),
            new("workshop", "4000", 2),
            new("Electric Ireland", "6300", 3),
            new("Accountancy", "6810", 4),
        ]);

        await EnsureDebtorAsync(db, period.Id, "SICAP grant due", 2_500m, DebtorType.Other, "Final 2024 tranche approved before year end.");
        await EnsureDebtorAsync(db, period.Id, "Insurance prepayment", 600m, DebtorType.Prepayment, "Four months of 2025 cover prepaid.");
        await EnsureCreditorAsync(db, period.Id, "Electric Ireland", 350m, CreditorType.Accrual, true, "December utility estimate.");
        await EnsureCreditorAsync(db, period.Id, "Accountancy fee", 750m, CreditorType.Accrual, true, "Year-end accounts accrual.");
        await EnsurePayrollSummaryAsync(db, period.Id, 38_500m, 4_235m, 1_100m, 3);
        await EnsureTaxBalanceAsync(db, period.Id, TaxType.CorporationTax, 250m, 250m, 0m);
        await EnsureTaxBalanceAsync(db, period.Id, TaxType.Paye, 1_040m, 950m, 90m);
        await EnsureDepreciationEntriesAsync(db, company.Id, period.Id, period.PeriodStart);

        await EnsureOpeningBalancesAsync(db, period.Id, categories,
        [
            new("0050", 400m, 0m, "Prior-year laptop NBV reviewed from signed 2023 accounts."),
            new("0040", 1_620m, 0m, "Furniture and projector opening cost base."),
            new("3100", 0m, 6_520m, "Restricted reserves brought forward."),
        ]);
        await EnsureAdjustmentsAsync(db, period.Id, categories,
        [
            new("Insurance prepayment recognition", "1200", "6200", 600m, AdjustmentSource.Auto, "Recognise prepaid insurance cover after year end.", "FRS 105 accruals concept", 600m, 600m, true),
            new("December electricity accrual", "6300", "2100", 350m, AdjustmentSource.Auto, "Utility incurred before year end.", "FRS 105 accruals concept", -350m, 0m, true),
            new("Restricted grant deferral review", "4300", "2100", 1_500m, AdjustmentSource.Manual, "Unspent programme funding ring-fenced for January workshops.", "Charity SORP fund accounting", -1_500m, 0m, false),
        ]);

        await EnsureCharityInfoAsync(db, company.Id);
        await EnsureFundBalancesAsync(db, period.Id,
        [
            new("General fund", "Unrestricted", 8_000m, 18_300m, 15_950m, -1_000m, 0m, 9_350m, "Core unrestricted operations."),
            new("Community training grant", "Restricted", 6_500m, 35_500m, 32_900m, 1_000m, 0m, 10_100m, "Grant restricted to community training supports."),
            new("Digital inclusion equipment", "Designated", 2_000m, 4_950m, 3_600m, 0m, 0m, 3_350m, "Board-designated equipment replacement fund."),
        ]);
        await EnsureInterrogationDataAsync(db, period.Id,
            postBalanceSheetEvents:
            [
                new("Additional Mayo County Council grant approval received after year end.", new DateOnly(2025, 2, 10), false, 6_500m, "Disclose as non-adjusting post balance sheet event."),
            ],
            relatedParties:
            [
                new("Patrick Walsh", "Director", "Volunteer expense reimbursement", 480m, 0m, "Reimbursed at vouched cost with board approval."),
            ],
            contingencies:
            [
                new("Grant clawback clause on unspent restricted funding.", "Grant", 1_500m, "Remote"),
            ]);

        await EnsureReviewConfirmationsAsync(db, period.Id, "Mary O'Brien",
        [
            ("company-profile", "CLG and charity flags confirmed."),
            ("bank-import", "AIB 2024 import reconciled to bank statement."),
            ("debtors", "Grant debtor agreed to approval letter."),
            ("creditors", "Accruals reviewed against supplier correspondence."),
            ("fixed-assets", "Asset register reviewed by trustees."),
            ("charity-sorp", "Fund accounting schedules prepared for SORP view."),
            ("tax", "No corporation tax balance outstanding."),
            ("filing-review", "Micro and charity filing pack ready for review."),
        ]);
        await EnsureReportsAndNotesAsync(db, period.Id, ElectedRegime.Micro, "Green Valley Community Development CLG", "Charity micro CLG showcase");
        await EnsureAuditLogAsync(db, company.Id, period.Id, "SeedData", period.Id, "SeededV2Demo", "Seeded all Green Valley modules for v2 demo.");

        return company;
    }

    private static async Task<Company> SeedConnachtDigitalAsync(AccountsDbContext db, int tenantId)
    {
        var company = await EnsureCompanyAsync(db, tenantId, "654321", c =>
        {
            c.LegalName = "Connacht Digital Solutions Limited";
            c.TradingName = "Connacht Digital";
            c.TaxReference = "6543210W";
            c.CompanyType = CompanyType.Private;
            c.IncorporationDate = new DateOnly(2018, 9, 1);
            c.FinancialYearStartMonth = 4;
            c.ArdMonth = 9;
            c.RegisteredOfficeAddress1 = "Unit 4, Galway Technology Centre";
            c.RegisteredOfficeAddress2 = "Mervue Business Park";
            c.RegisteredOfficeCity = "Galway";
            c.RegisteredOfficeCounty = "Galway";
            c.RegisteredOfficeEircode = "H91 W6KT";
            c.IsTrading = true;
            c.IsDormant = false;
            c.IsVatRegistered = true;
            c.IsEmployer = true;
            c.HasStock = false;
            c.OwnsAssets = true;
            c.HasBorrowings = true;
            c.HasDirectorLoans = true;
        });

        var aoife = await EnsureOfficerAsync(db, company.Id, "Aoife Brennan", OfficerRole.Director, new DateOnly(2018, 9, 1), "Salthill, Galway");
        await EnsureOfficerAsync(db, company.Id, "Cian Murphy", OfficerRole.Director, new DateOnly(2018, 9, 1), "Oranmore, Co. Galway");
        await EnsureOfficerAsync(db, company.Id, "Roisin Flaherty", OfficerRole.Secretary, new DateOnly(2019, 1, 15), "Claregalway, Co. Galway");
        await EnsureShareCapitalAsync(db, company.Id, "Ordinary", 1m, 100, 100m, company.IncorporationDate);
        await EnsureShareCapitalAsync(db, company.Id, "Growth", 0.01m, 2_500, 25m, new DateOnly(2020, 1, 1));

        var boi = await EnsureBankAccountAsync(db, company.Id, "BOI Business Account", "IE45BOFI90001712345678", 32_000m, new DateOnly(2024, 4, 1));
        var revolut = await EnsureBankAccountAsync(db, company.Id, "Revolut Business", "IE98REVO99036012345678", 5_600m, new DateOnly(2024, 4, 1));
        await EnsureFixedAssetAsync(db, company.Id, "MacBook Pro x3", "Computer Equipment", 8_400m, new DateOnly(2021, 1, 15), 3, DepreciationMethod.StraightLine);
        await EnsureFixedAssetAsync(db, company.Id, "Office Desks and Chairs", "Office Equipment", 3_200m, new DateOnly(2018, 10, 1), 10, DepreciationMethod.StraightLine);
        await EnsureFixedAssetAsync(db, company.Id, "Server Infrastructure", "Computer Equipment", 12_000m, new DateOnly(2022, 6, 1), 5, DepreciationMethod.StraightLine);
        await EnsureFixedAssetAsync(db, company.Id, "Company Van - Ford Transit", "Motor Vehicles", 28_000m, new DateOnly(2023, 3, 1), 5, DepreciationMethod.ReducingBalance);
        await EnsureLoanAsync(db, company.Id, "Bank of Ireland", 50_000m, 35_000m, 4.5m, false, 10_000m, 25_000m, new DateOnly(2022, 4, 1), new DateOnly(2025, 3, 31));

        var period = await EnsurePeriodAsync(db, company.Id, new DateOnly(2024, 4, 1), new DateOnly(2025, 3, 31), PeriodStatus.Review, false);
        var categories = await EnsureCategoriesAsync(db, company.Id);

        await EnsureSizeClassificationAsync(db, period.Id, 850_000m, 180_000m, 8, CompanySizeClass.Small, CompanySizeClass.Small, "Small company. Eligible for small abridged accounts and audit exemption subject to member notice and late filing checks.");
        await EnsureFilingRegimeAsync(db, period.Id, false, true, true, ElectedRegime.SmallAbridged,
            ["FRS 102 Section 1A notes", "Abridgement statement", "Directors' loans", "Employee disclosures", "Taxation"],
            ["Profit and loss", "Abridged balance sheet", "Directors' report", "Notes"]);
        await EnsureFilingPackagesAsync(db, period.Id, FilingStatus.PackageGenerated, "CDS-2025-CRO-PACK", "CDS-CT1-2025-DRAFT", true, true, false);
        await EnsureDeadlinesAsync(db, company.Id, period.Id, period.PeriodEnd, includeCharity: false);

        await EnsureImportBatchWithTransactionsAsync(db, boi, period, categories, "connacht-digital-boi-2025.csv",
        [
            new(new DateOnly(2024, 4, 8), "Stripe payout - SaaS subscriptions", 72_400m, "4000", "STR-APR", 0.97m),
            new(new DateOnly(2024, 5, 8), "Stripe payout - SaaS subscriptions", 74_250m, "4000", "STR-MAY", 0.97m),
            new(new DateOnly(2024, 6, 14), "Medtronic Ireland project milestone", 118_000m, "4000", "MED-0624", 0.98m),
            new(new DateOnly(2024, 7, 2), "AWS Ireland hosting", -12_800m, "7300", "AWS-Q2", 0.95m),
            new(new DateOnly(2024, 7, 25), "Payroll net pay July", -26_400m, "6000", "PAY-0724", 0.96m),
            new(new DateOnly(2024, 8, 15), "Fidelity Investments sprint delivery", 86_000m, "4000", "FID-0824", 0.98m),
            new(new DateOnly(2024, 9, 3), "Office rent quarter", -14_400m, "6100", "RENT-Q3", 0.95m),
            new(new DateOnly(2024, 10, 22), "Vodafone Business fibre", -850m, "6400", "VOD-1024", 0.92m),
            new(new DateOnly(2024, 11, 11), "HubSpot subscription", -2_200m, "7300", "HUB-1124", 0.9m),
            new(new DateOnly(2025, 1, 15), "NUI Galway support retainer", 19_500m, "4000", "NUIG-0125", 0.95m),
            new(new DateOnly(2025, 2, 1), "Dividend payment", -20_000m, "3200", "DIV-0201", 0.99m),
            new(new DateOnly(2025, 3, 20), "Corporation tax preliminary payment", -15_000m, "2400", "CT-PRELIM", 0.99m),
        ]);
        await EnsureImportBatchWithTransactionsAsync(db, revolut, period, categories, "connacht-digital-revolut-2025.csv",
        [
            new(new DateOnly(2024, 4, 18), "Google Workspace", -720m, "7300", "GOOG-0418", 0.87m),
            new(new DateOnly(2024, 6, 1), "Client travel - Dublin", -1_260m, "6700", "TRAVEL-0601", 0.81m),
            new(new DateOnly(2024, 12, 12), "Team Christmas entertainment", -2_100m, "7500", "ENT-1212", 0.9m),
            new(new DateOnly(2025, 3, 29), "Duplicate test - Stripe payout", 74_250m, "4000", "DUP-TEST", 0.61m, true),
        ]);

        await EnsureTransactionRulesAsync(db, company.Id, categories,
        [
            new("Stripe payout", "4000", 1),
            new("AWS Ireland", "7300", 2),
            new("Payroll", "6000", 3),
            new("Vodafone", "6400", 4),
            new("Dividend", "3200", 5),
        ]);

        await EnsureDebtorAsync(db, period.Id, "Medtronic Ireland", 12_500m, DebtorType.Trade, "March sprint invoice unpaid at year end.");
        await EnsureDebtorAsync(db, period.Id, "Fidelity Investments", 8_200m, DebtorType.Trade, "Retainer invoice raised before year end.");
        await EnsureDebtorAsync(db, period.Id, "NUI Galway", 3_500m, DebtorType.Trade, "Support balance due.");
        await EnsureDebtorAsync(db, period.Id, "Office insurance prepaid", 1_800m, DebtorType.Prepayment, "Nine months prepaid at 31 March.");
        await EnsureDebtorAsync(db, period.Id, "Software licences prepaid", 2_400m, DebtorType.Prepayment, "Annual licences spanning the next period.");
        await EnsureCreditorAsync(db, period.Id, "AWS Ireland", 4_200m, CreditorType.Trade, true, "March hosting invoice.");
        await EnsureCreditorAsync(db, period.Id, "Vodafone Business", 850m, CreditorType.Trade, true, "March fibre and mobile invoice.");
        await EnsureCreditorAsync(db, period.Id, "Audit fee accrual", 3_500m, CreditorType.Accrual, true, "Audit exemption review and accounts compilation.");
        await EnsureCreditorAsync(db, period.Id, "Electricity accrual", 420m, CreditorType.Accrual, true, "Metered office estimate.");
        await EnsureCreditorAsync(db, period.Id, "PAYE/PRSI Q4", 6_800m, CreditorType.Tax, true, "PAYE Modernisation balance.");
        await EnsureCreditorAsync(db, period.Id, "VAT return", 3_200m, CreditorType.Tax, true, "Jan-Feb VAT liability.");
        await EnsureDirectorLoanAsync(db, period.Id, aoife.Id, 15_000m, 5_000m, 8_000m, 12_000m, 5m, 600m, 17_000m, "Repayable on demand; reviewed for s.239 connected lending limits.");
        await EnsurePayrollSummaryAsync(db, period.Id, 320_000m, 35_200m, 16_000m, 8);
        await EnsureTaxBalanceAsync(db, period.Id, TaxType.CorporationTax, 18_750m, 15_000m, 3_750m);
        await EnsureTaxBalanceAsync(db, period.Id, TaxType.Vat, 3_200m, 0m, 3_200m);
        await EnsureTaxBalanceAsync(db, period.Id, TaxType.Paye, 6_800m, 0m, 6_800m);
        await EnsureDividendAsync(db, period.Id, 20_000m, new DateOnly(2025, 1, 15), new DateOnly(2025, 2, 1));
        await EnsureDepreciationEntriesAsync(db, company.Id, period.Id, period.PeriodStart);

        await EnsureOpeningBalancesAsync(db, period.Id, categories,
        [
            new("0050", 9_600m, 0m, "Computer equipment opening NBV from 2024 signed accounts."),
            new("0030", 22_400m, 0m, "Van opening NBV from asset register."),
            new("2600", 0m, 10_000m, "Current loan tranche due within one year."),
            new("2700", 0m, 25_000m, "Long-term bank loan tranche."),
            new("3000", 0m, 125m, "Issued ordinary and growth share capital."),
            new("3100", 0m, 24_875m, "Retained earnings brought forward."),
        ]);
        await EnsureAdjustmentsAsync(db, period.Id, categories,
        [
            new("Depreciation - server and laptop estate", "7000", "0050", 5_600m, AdjustmentSource.Auto, "Annual depreciation for owned computer equipment.", "FRS 102 Section 17", -5_600m, -5_600m, true),
            new("Office insurance prepayment", "1200", "6200", 1_800m, AdjustmentSource.Auto, "Prepaid office insurance recognised as current asset.", "FRS 102 accruals concept", 1_800m, 1_800m, true),
            new("Audit and accounts accrual", "6810", "2100", 3_500m, AdjustmentSource.Auto, "Year-end professional fee accrual.", "FRS 102 accruals concept", -3_500m, 0m, true),
            new("Entertainment add-back marker", null, null, 2_100m, AdjustmentSource.Manual, "Flag non-deductible entertainment for CT computation.", "TCA 1997 Case I principles", -2_100m, 0m, false),
        ]);
        await EnsureInterrogationDataAsync(db, period.Id,
            postBalanceSheetEvents:
            [
                new("Three-year SaaS support contract signed with a medical device client.", new DateOnly(2025, 4, 12), false, 240_000m, "Disclose as non-adjusting commercial event."),
            ],
            relatedParties:
            [
                new("Aoife Brennan", "Director", "Director loan", 5_000m, 12_000m, "Repayable on demand with interest charged at 5%."),
                new("Brennan UX Limited", "Connected company", "Design subcontracting", 18_400m, 2_100m, "Services at market rate and approved by non-conflicted director."),
            ],
            contingencies:
            [
                new("Customer service-credit claim under one implementation contract.", "Warranty", 8_000m, "Possible"),
            ]);
        await EnsureReviewConfirmationsAsync(db, period.Id, "Roisin Flaherty",
        [
            ("company-profile", "Company flags and addresses checked."),
            ("bank-import", "BOI and Revolut imports loaded and duplicate test row flagged."),
            ("categorisation", "Automation rules reviewed and key transactions sampled."),
            ("debtors", "Aged debtor list agreed to invoices."),
            ("creditors", "Trade creditors and accruals matched to supplier statements."),
            ("fixed-assets", "Asset register agreed to depreciation schedules."),
            ("director-loans", "Director loan disclosure prepared."),
            ("payroll", "Payroll summary agreed to payroll year-end report."),
            ("tax", "VAT, PAYE and corporation tax balances captured."),
            ("filing-review", "CRO pack generated; payment pending."),
        ]);
        await EnsureReportsAndNotesAsync(db, period.Id, ElectedRegime.SmallAbridged, "Connacht Digital Solutions Limited", "Small company workflow showcase");
        await EnsureAuditLogAsync(db, company.Id, period.Id, "SeedData", period.Id, "SeededV2Demo", "Seeded all Connacht Digital modules for v2 demo.");

        return company;
    }

    private static async Task<Company> SeedAtlanticManufacturingAsync(AccountsDbContext db, int tenantId)
    {
        var company = await EnsureCompanyAsync(db, tenantId, "789012", c =>
        {
            c.LegalName = "Atlantic Manufacturing DAC";
            c.TradingName = "Atlantic Mfg";
            c.TaxReference = "7890123K";
            c.CompanyType = CompanyType.DesignatedActivityCompany;
            c.IncorporationDate = new DateOnly(2010, 1, 20);
            c.FinancialYearStartMonth = 1;
            c.ArdMonth = 4;
            c.RegisteredOfficeAddress1 = "Industrial Estate";
            c.RegisteredOfficeAddress2 = "Limerick Road";
            c.RegisteredOfficeCity = "Shannon";
            c.RegisteredOfficeCounty = "Clare";
            c.RegisteredOfficeEircode = "V14 XY99";
            c.IsTrading = true;
            c.IsDormant = false;
            c.IsVatRegistered = true;
            c.IsEmployer = true;
            c.HasStock = true;
            c.OwnsAssets = true;
            c.HasBorrowings = true;
            c.HasDirectorLoans = false;
        });

        await EnsureOfficerAsync(db, company.Id, "Declan Ryan", OfficerRole.Director, new DateOnly(2010, 1, 20), "Ennis Road, Limerick");
        await EnsureOfficerAsync(db, company.Id, "Niamh Fitzgerald", OfficerRole.Director, new DateOnly(2012, 5, 1), "Newmarket-on-Fergus, Co. Clare");
        await EnsureOfficerAsync(db, company.Id, "Tomas O Se", OfficerRole.Director, new DateOnly(2018, 3, 15), "Adare, Co. Limerick");
        await EnsureOfficerAsync(db, company.Id, "Claire Dunne", OfficerRole.Secretary, new DateOnly(2015, 7, 1), "Shannon, Co. Clare");
        await EnsureShareCapitalAsync(db, company.Id, "Ordinary", 1m, 1_000, 1_000m, company.IncorporationDate);
        await EnsureShareCapitalAsync(db, company.Id, "Redeemable preference", 1m, 50_000, 50_000m, new DateOnly(2015, 1, 1));

        var current = await EnsureBankAccountAsync(db, company.Id, "AIB Business Current", "IE29AIBK93104512345678", 245_000m, new DateOnly(2024, 1, 1));
        var deposit = await EnsureBankAccountAsync(db, company.Id, "AIB Deposit Account", "IE30AIBK93104599999999", 150_000m, new DateOnly(2024, 1, 1));
        await EnsureFixedAssetAsync(db, company.Id, "Factory Building", "Land & Buildings", 850_000m, new DateOnly(2010, 3, 1), 50, DepreciationMethod.StraightLine);
        await EnsureFixedAssetAsync(db, company.Id, "CNC Machines x4", "Plant & Machinery", 420_000m, new DateOnly(2018, 1, 15), 10, DepreciationMethod.StraightLine);
        await EnsureFixedAssetAsync(db, company.Id, "Forklift Fleet (3)", "Plant & Machinery", 75_000m, new DateOnly(2021, 6, 1), 7, DepreciationMethod.ReducingBalance);
        await EnsureFixedAssetAsync(db, company.Id, "Delivery Trucks x2", "Motor Vehicles", 120_000m, new DateOnly(2022, 9, 1), 5, DepreciationMethod.ReducingBalance);
        await EnsureFixedAssetAsync(db, company.Id, "IT Infrastructure", "Computer Equipment", 35_000m, new DateOnly(2023, 1, 1), 3, DepreciationMethod.StraightLine);
        await EnsureFixedAssetAsync(db, company.Id, "Office Fit-out", "Office Equipment", 28_000m, new DateOnly(2015, 4, 1), 10, DepreciationMethod.StraightLine);
        await EnsureLoanAsync(db, company.Id, "AIB Corporate", 500_000m, 280_000m, 3.8m, false, 60_000m, 220_000m, new DateOnly(2020, 1, 1), new DateOnly(2024, 12, 31));
        await EnsureLoanAsync(db, company.Id, "Enterprise Ireland", 100_000m, 40_000m, 0m, false, 20_000m, 20_000m, new DateOnly(2021, 6, 1), new DateOnly(2024, 12, 31));

        var period = await EnsurePeriodAsync(db, company.Id, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31), PeriodStatus.Finalised, false, "Claire Dunne");
        var categories = await EnsureCategoriesAsync(db, company.Id);

        await EnsureSizeClassificationAsync(db, period.Id, 18_500_000m, 9_200_000m, 65, CompanySizeClass.Medium, CompanySizeClass.Medium, "Medium company. Exceeds small thresholds on turnover and employees.");
        await EnsureFilingRegimeAsync(db, period.Id, false, false, false, ElectedRegime.Medium,
            ["Full FRS 102 notes", "Directors' report", "Employee disclosures", "Related parties", "Post balance sheet events", "Contingencies"],
            ["Profit and loss", "Balance sheet", "Cash flow statement", "Statement of changes in equity", "Directors' report", "Notes"]);
        await EnsureFilingPackagesAsync(db, period.Id, FilingStatus.Accepted, "AM-2024-CRO-ACCEPTED", "AM-CT1-2024-ROS", true, true, true);
        await EnsureDeadlinesAsync(db, company.Id, period.Id, period.PeriodEnd, includeCharity: false, filed: true);

        await EnsureImportBatchWithTransactionsAsync(db, current, period, categories, "atlantic-manufacturing-aib-current-2024.csv",
        [
            new(new DateOnly(2024, 1, 18), "Boston Scientific production run", 1_850_000m, "4000", "BS-0118", 0.99m),
            new(new DateOnly(2024, 2, 2), "Steel Suppliers Ltd", -420_000m, "5100", "STEEL-Q1", 0.98m),
            new(new DateOnly(2024, 3, 8), "Stryker Ireland tooling order", 1_420_000m, "4000", "STRY-0308", 0.99m),
            new(new DateOnly(2024, 4, 25), "Payroll April", -238_000m, "6000", "PAY-0424", 0.98m),
            new(new DateOnly(2024, 5, 16), "Factory electricity", -38_000m, "6300", "UTIL-0516", 0.96m),
            new(new DateOnly(2024, 6, 10), "Johnson and Johnson fulfilment", 980_000m, "4000", "JNJ-0610", 0.99m),
            new(new DateOnly(2024, 7, 12), "Tooling Direct", -180_000m, "5000", "TOOL-Q2", 0.96m),
            new(new DateOnly(2024, 8, 30), "Delivery fleet maintenance", -26_000m, "7100", "FLEET-0830", 0.93m),
            new(new DateOnly(2024, 9, 5), "Zimmer Biomet annual order", 1_060_000m, "4000", "ZIM-0905", 0.99m),
            new(new DateOnly(2024, 10, 21), "AIB loan repayment", -80_000m, "2600", "AIB-REPAY", 0.99m),
            new(new DateOnly(2024, 11, 1), "Dividend declared and paid", -100_000m, "3200", "DIV-1101", 0.99m),
            new(new DateOnly(2024, 12, 18), "Corporation tax preliminary payment", -140_000m, "2400", "CT-PRELIM", 0.99m),
        ]);
        await EnsureImportBatchWithTransactionsAsync(db, deposit, period, categories, "atlantic-manufacturing-aib-deposit-2024.csv",
        [
            new(new DateOnly(2024, 3, 31), "Deposit interest", 4_200m, "4200", "INT-Q1", 0.98m),
            new(new DateOnly(2024, 6, 30), "Deposit interest", 4_450m, "4200", "INT-Q2", 0.98m),
            new(new DateOnly(2024, 9, 30), "Deposit interest", 4_600m, "4200", "INT-Q3", 0.98m),
            new(new DateOnly(2024, 12, 31), "Deposit interest", 4_750m, "4200", "INT-Q4", 0.98m),
        ]);

        await EnsureTransactionRulesAsync(db, company.Id, categories,
        [
            new("Boston Scientific", "4000", 1),
            new("Stryker", "4000", 2),
            new("Steel Suppliers", "5100", 3),
            new("Payroll", "6000", 4),
            new("loan repayment", "2600", 5),
        ]);

        await EnsureDebtorAsync(db, period.Id, "Boston Scientific", 185_000m, DebtorType.Trade, "December delivery balance.");
        await EnsureDebtorAsync(db, period.Id, "Stryker Ireland", 142_000m, DebtorType.Trade, "December invoice outstanding.");
        await EnsureDebtorAsync(db, period.Id, "Johnson and Johnson", 98_000m, DebtorType.Trade, "Approved invoice pending payment.");
        await EnsureDebtorAsync(db, period.Id, "Zimmer Biomet", 67_000m, DebtorType.Trade, "Credit-controlled customer balance.");
        await EnsureDebtorAsync(db, period.Id, "Insurance prepayment", 18_000m, DebtorType.Prepayment, "Six months factory cover prepaid.");
        await EnsureDebtorAsync(db, period.Id, "Rent deposit", 12_000m, DebtorType.Other, "Long-term deposit held by landlord.");
        await EnsureCreditorAsync(db, period.Id, "Steel Suppliers Ltd", 45_000m, CreditorType.Trade, true, "December steel invoice.");
        await EnsureCreditorAsync(db, period.Id, "Tooling Direct", 23_000m, CreditorType.Trade, true, "Tooling supplies invoice.");
        await EnsureCreditorAsync(db, period.Id, "Audit fee", 15_000m, CreditorType.Accrual, true, "Medium company audit accrual.");
        await EnsureCreditorAsync(db, period.Id, "Holiday pay accrual", 28_000m, CreditorType.Accrual, true, "Accrued staff holiday entitlement.");
        await EnsureCreditorAsync(db, period.Id, "PAYE/PRSI December", 42_000m, CreditorType.Tax, true, "December payroll taxes.");
        await EnsureCreditorAsync(db, period.Id, "VAT return", 35_000m, CreditorType.Tax, true, "Nov-Dec VAT liability.");
        await EnsureInventoryAsync(db, period.Id, "Raw materials", 120_000m, ValuationMethod.Cost);
        await EnsureInventoryAsync(db, period.Id, "Work in progress", 85_000m, ValuationMethod.Cost);
        await EnsureInventoryAsync(db, period.Id, "Finished goods", 95_000m, ValuationMethod.LowerOfCostAndNrv);
        await EnsurePayrollSummaryAsync(db, period.Id, 2_800_000m, 308_000m, 140_000m, 65);
        await EnsureTaxBalanceAsync(db, period.Id, TaxType.CorporationTax, 156_000m, 140_000m, 16_000m);
        await EnsureTaxBalanceAsync(db, period.Id, TaxType.Vat, 35_000m, 0m, 35_000m);
        await EnsureTaxBalanceAsync(db, period.Id, TaxType.Paye, 42_000m, 0m, 42_000m);
        await EnsureDividendAsync(db, period.Id, 100_000m, new DateOnly(2024, 11, 1), new DateOnly(2024, 12, 1));
        await EnsureDepreciationEntriesAsync(db, company.Id, period.Id, period.PeriodStart);

        await EnsureOpeningBalancesAsync(db, period.Id, categories,
        [
            new("0010", 714_000m, 0m, "Factory building opening NBV from 2023 audited accounts."),
            new("0020", 342_000m, 0m, "Plant and machinery opening NBV from fixed asset register."),
            new("0030", 86_500m, 0m, "Motor vehicles opening NBV."),
            new("1000", 272_500m, 0m, "Opening inventory by stock count."),
            new("2600", 0m, 80_000m, "Loan balances due within one year."),
            new("2700", 0m, 240_000m, "Loan balances due after one year."),
            new("3000", 0m, 51_000m, "Issued share capital."),
            new("3100", 0m, 1_044_000m, "Retained earnings brought forward."),
        ]);
        await EnsureAdjustmentsAsync(db, period.Id, categories,
        [
            new("Closing stock and WIP recognition", "1000", "5000", 300_000m, AdjustmentSource.Auto, "Closing inventory valued at lower of cost and NRV.", "FRS 102 Section 13", 300_000m, 300_000m, true),
            new("Factory insurance prepayment", "1200", "6200", 18_000m, AdjustmentSource.Auto, "Prepaid factory insurance recognised as current asset.", "FRS 102 accruals concept", 18_000m, 18_000m, true),
            new("Holiday pay accrual", "6000", "2100", 28_000m, AdjustmentSource.Manual, "Accrued employee holiday pay at year end.", "FRS 102 accruals concept", -28_000m, 0m, true),
            new("Corporation tax provision", "8000", "2400", 156_000m, AdjustmentSource.Auto, "Corporation tax liability recognised.", "FRS 102 Section 29", -156_000m, 0m, true),
        ]);
        await EnsureInterrogationDataAsync(db, period.Id,
            postBalanceSheetEvents:
            [
                new("New CNC line approved by the board after year end.", new DateOnly(2025, 1, 22), false, 640_000m, "Disclose as non-adjusting capital commitment."),
                new("Credit note agreed with tooling supplier for defective parts received before year end.", new DateOnly(2025, 2, 4), true, 12_500m, "Adjust trade creditors before approval."),
            ],
            relatedParties:
            [
                new("Atlantic Engineering Holdings Limited", "Parent company", "Management fee", 95_000m, 18_000m, "Charged under group services agreement."),
                new("Ryan Property Partnership", "Director-connected entity", "Factory lease", 144_000m, 0m, "Lease renewed at independently benchmarked market rent."),
            ],
            contingencies:
            [
                new("Performance bond on export customer framework.", "Guarantee", 250_000m, "Possible"),
                new("Product warranty exposure on 2024 production runs.", "Warranty", 75_000m, "Possible"),
            ]);
        await EnsureReviewConfirmationsAsync(db, period.Id, "Claire Dunne",
        [
            ("company-profile", "DAC details and director slate confirmed."),
            ("bank-import", "AIB current and deposit accounts imported and reconciled."),
            ("categorisation", "High-value transactions reviewed by finance manager."),
            ("debtors", "Trade debtor ledger agreed to customer statements."),
            ("creditors", "Supplier statement recs complete."),
            ("fixed-assets", "Asset register and depreciation reviewed."),
            ("stock", "Stock count, WIP and NRV review complete."),
            ("loans", "Bank and Enterprise Ireland loans agreed to statements."),
            ("payroll", "Payroll costs agreed to annual payroll report."),
            ("tax", "VAT, PAYE and CT balances entered."),
            ("interrogation", "Related parties, contingencies and post balance sheet events reviewed."),
            ("filing-review", "Final accounts and CRO/Revenue packages accepted in demo workflow."),
        ]);
        await EnsureReportsAndNotesAsync(db, period.Id, ElectedRegime.Medium, "Atlantic Manufacturing DAC", "Medium company full compliance showcase");
        await EnsureAuditLogAsync(db, company.Id, period.Id, "SeedData", period.Id, "SeededV2Demo", "Seeded all Atlantic Manufacturing modules for v2 demo.");

        return company;
    }

    private static async Task<Company> EnsureCompanyAsync(AccountsDbContext db, int tenantId, string croNumber, Action<Company> configure)
    {
        var company = await db.Companies
            .Include(c => c.Officers)
            .Include(c => c.FixedAssets)
            .FirstOrDefaultAsync(c => c.CroNumber == croNumber);

        if (company is null)
        {
            company = new Company
            {
                TenantId = tenantId,
                LegalName = "Pending Demo Company",
                CroNumber = croNumber,
                IncorporationDate = new DateOnly(2020, 1, 1),
                ArdMonth = 1
            };
            db.Companies.Add(company);
        }

        company.TenantId = tenantId;
        company.CroNumber = croNumber;
        configure(company);
        company.UpdatedAt = DateTime.UtcNow;
        FixLegacyDemoEncoding(company);
        await db.SaveChangesAsync();
        return company;
    }

    private static void FixLegacyDemoEncoding(Company company)
    {
        foreach (var officer in company.Officers)
        {
            officer.Name = ToAscii(CleanLegacyText(officer.Name));
            officer.Address = officer.Address is null ? null : ToAscii(CleanLegacyText(officer.Address));
        }

        foreach (var asset in company.FixedAssets)
        {
            asset.Name = ToAscii(CleanLegacyText(asset.Name));
            asset.Category = ToAscii(CleanLegacyText(asset.Category));
        }
    }

    private static string CleanLegacyText(string value) => value
        .Replace("SiobhÃ¡n", "Siobhan", StringComparison.Ordinal)
        .Replace("RoisÃ­n", "Roisin", StringComparison.Ordinal)
        .Replace("TomÃ¡s Ã“ SÃ©", "Tomas O Se", StringComparison.Ordinal)
        .Replace("â€”", "-", StringComparison.Ordinal)
        .Replace("—", "-", StringComparison.Ordinal)
        .Replace("–", "-", StringComparison.Ordinal)
        .Replace("â‚¬", "EUR", StringComparison.Ordinal);

    private static string ToAscii(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(capacity: value.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string NormalizeDemoKey(string value)
    {
        var ascii = ToAscii(CleanLegacyText(value)).ToLowerInvariant().Replace("&", "and", StringComparison.Ordinal);
        return string.Join(" ", ascii.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static async Task<CompanyOfficer> EnsureOfficerAsync(AccountsDbContext db, int companyId, string name, OfficerRole role, DateOnly appointedDate, string address)
    {
        var normalizedName = NormalizeDemoKey(name);
        var existingOfficers = await db.CompanyOfficers.Where(o => o.CompanyId == companyId && o.Role == role).ToListAsync();
        var officer = existingOfficers.FirstOrDefault(o => NormalizeDemoKey(o.Name) == normalizedName);
        if (officer is null)
        {
            officer = new CompanyOfficer { CompanyId = companyId, Name = name, Role = role };
            db.CompanyOfficers.Add(officer);
        }

        foreach (var duplicate in existingOfficers.Where(o => o.Id != officer.Id && NormalizeDemoKey(o.Name) == normalizedName))
            db.CompanyOfficers.Remove(duplicate);

        officer.AppointedDate = appointedDate;
        officer.Name = name;
        officer.Address = address;
        officer.ResignedDate = null;
        await db.SaveChangesAsync();
        return officer;
    }

    private static async Task<BankAccount> EnsureBankAccountAsync(AccountsDbContext db, int companyId, string name, string iban, decimal openingBalance, DateOnly openingDate)
    {
        var account = await db.BankAccounts.FirstOrDefaultAsync(a => a.CompanyId == companyId && a.Name == name);
        if (account is null)
        {
            account = new BankAccount { CompanyId = companyId, Name = name };
            db.BankAccounts.Add(account);
        }

        account.Iban = iban;
        account.Currency = "EUR";
        account.OpeningBalance = openingBalance;
        account.OpeningBalanceDate = openingDate;
        await db.SaveChangesAsync();
        return account;
    }

    private static async Task EnsureShareCapitalAsync(AccountsDbContext db, int companyId, string shareClass, decimal nominalValue, int numberIssued, decimal totalValue, DateOnly issueDate)
    {
        var share = await db.ShareCapitals.FirstOrDefaultAsync(s => s.CompanyId == companyId && s.ShareClass == shareClass);
        if (share is null)
        {
            share = new ShareCapital { CompanyId = companyId, ShareClass = shareClass };
            db.ShareCapitals.Add(share);
        }

        share.NominalValue = nominalValue;
        share.NumberIssued = numberIssued;
        share.TotalValue = totalValue;
        share.IsFullyPaid = true;
        share.IssueDate = issueDate;
        share.CancelledDate = null;
        await db.SaveChangesAsync();
    }

    private static async Task EnsureFixedAssetAsync(AccountsDbContext db, int companyId, string name, string category, decimal cost, DateOnly acquisitionDate, int usefulLifeYears, DepreciationMethod method)
    {
        var normalizedName = NormalizeDemoKey(name);
        var existingAssets = await db.FixedAssets.Where(a => a.CompanyId == companyId).ToListAsync();
        var asset = existingAssets.FirstOrDefault(a => NormalizeDemoKey(a.Name) == normalizedName);
        if (asset is null)
        {
            asset = new FixedAsset { CompanyId = companyId, Name = name, Category = category };
            db.FixedAssets.Add(asset);
        }

        foreach (var duplicate in existingAssets.Where(a => a.Id != asset.Id && NormalizeDemoKey(a.Name) == normalizedName))
            db.FixedAssets.Remove(duplicate);

        asset.Name = name;
        asset.Category = category;
        asset.Cost = cost;
        asset.AcquisitionDate = acquisitionDate;
        asset.UsefulLifeYears = usefulLifeYears;
        asset.DepreciationMethod = method;
        asset.DisposalDate = null;
        asset.DisposalProceeds = null;
        await db.SaveChangesAsync();
    }

    private static async Task EnsureLoanAsync(AccountsDbContext db, int companyId, string lender, decimal originalAmount, decimal balance, decimal interestRate, bool isDirectorLoan, decimal dueWithinYear, decimal dueAfterYear, DateOnly drawdownDate, DateOnly balanceAsOfDate)
    {
        var loan = await db.Loans.FirstOrDefaultAsync(l => l.CompanyId == companyId && l.Lender == lender);
        if (loan is null)
        {
            loan = new Loan { CompanyId = companyId, Lender = lender };
            db.Loans.Add(loan);
        }

        loan.OriginalAmount = originalAmount;
        loan.Balance = balance;
        loan.DrawdownDate = drawdownDate;
        loan.BalanceAsOfDate = balanceAsOfDate;
        loan.InterestRate = interestRate;
        loan.IsDirectorLoan = isDirectorLoan;
        loan.DueWithinYear = dueWithinYear;
        loan.DueAfterYear = dueAfterYear;
        await db.SaveChangesAsync();
    }

    private static async Task<AccountingPeriod> EnsurePeriodAsync(AccountsDbContext db, int companyId, DateOnly start, DateOnly end, PeriodStatus status, bool isFirstYear, string? lockedBy = null)
    {
        var period = await db.AccountingPeriods.FirstOrDefaultAsync(p => p.CompanyId == companyId && p.PeriodEnd == end);
        if (period is null)
        {
            period = new AccountingPeriod { CompanyId = companyId, PeriodStart = start, PeriodEnd = end };
            db.AccountingPeriods.Add(period);
        }

        period.PeriodStart = start;
        period.PeriodEnd = end;
        period.Status = status;
        period.IsFirstYear = isFirstYear;
        period.GoingConcernConfirmed = true;
        period.GoingConcernNote = "Directors have assessed budgets, cash flow and filing obligations for at least twelve months from approval.";
        period.MemberAuditNoticeReceived = false;
        period.MemberAuditNoticeDate = null;
        if (status is PeriodStatus.Finalised or PeriodStatus.Filed)
        {
            period.LockedAt ??= DateTime.UtcNow.AddDays(-8);
            period.LockedBy = lockedBy ?? DemoSeedUser;
        }
        else
        {
            period.LockedAt = null;
            period.LockedBy = null;
        }

        await db.SaveChangesAsync();
        return period;
    }

    private static async Task<Dictionary<string, AccountCategory>> EnsureCategoriesAsync(AccountsDbContext db, int companyId)
    {
        var existing = await db.AccountCategories.Where(c => c.CompanyId == companyId).ToListAsync();
        var byCode = existing.GroupBy(c => c.Code).ToDictionary(g => g.Key, g => g.First());

        foreach (var seed in CategorySeeds)
        {
            if (!byCode.TryGetValue(seed.Code, out var category))
            {
                category = new AccountCategory { CompanyId = companyId, Code = seed.Code, Name = seed.Name, Type = seed.Type };
                db.AccountCategories.Add(category);
                byCode[seed.Code] = category;
            }

            category.Name = seed.Name;
            category.Type = seed.Type;
            category.TaxTreatment = seed.TaxTreatment;
            category.IsSystem = true;
        }

        await db.SaveChangesAsync();
        return (await db.AccountCategories.Where(c => c.CompanyId == companyId).ToListAsync())
            .GroupBy(c => c.Code)
            .ToDictionary(g => g.Key, g => g.First());
    }

    private static readonly CategorySeed[] CategorySeeds =
    [
        new("4000", "Sales / Revenue", AccountCategoryType.Income, TaxTreatment.Deductible),
        new("4100", "Other Income", AccountCategoryType.Income, TaxTreatment.Deductible),
        new("4200", "Interest Received", AccountCategoryType.Income, TaxTreatment.Deductible),
        new("4300", "Grants Received", AccountCategoryType.Income, TaxTreatment.Deductible),
        new("5000", "Cost of Sales", AccountCategoryType.Expense, TaxTreatment.Deductible),
        new("5100", "Direct Materials", AccountCategoryType.Expense, TaxTreatment.Deductible),
        new("5200", "Direct Labour", AccountCategoryType.Expense, TaxTreatment.Deductible),
        new("6000", "Wages & Salaries", AccountCategoryType.Expense, TaxTreatment.Deductible),
        new("6010", "Employer PRSI", AccountCategoryType.Expense, TaxTreatment.Deductible),
        new("6020", "Pension Contributions", AccountCategoryType.Expense, TaxTreatment.Deductible),
        new("6100", "Rent", AccountCategoryType.Expense, TaxTreatment.Deductible),
        new("6110", "Rates", AccountCategoryType.Expense, TaxTreatment.Deductible),
        new("6200", "Insurance", AccountCategoryType.Expense, TaxTreatment.Deductible),
        new("6300", "Light & Heat", AccountCategoryType.Expense, TaxTreatment.Deductible),
        new("6400", "Telephone & Internet", AccountCategoryType.Expense, TaxTreatment.Deductible),
        new("6500", "Office Supplies & Stationery", AccountCategoryType.Expense, TaxTreatment.Deductible),
        new("6600", "Motor Expenses", AccountCategoryType.Expense, TaxTreatment.Deductible),
        new("6700", "Travel & Subsistence", AccountCategoryType.Expense, TaxTreatment.Deductible),
        new("6800", "Professional Fees", AccountCategoryType.Expense, TaxTreatment.Deductible),
        new("6810", "Accountancy Fees", AccountCategoryType.Expense, TaxTreatment.Deductible),
        new("6820", "Legal Fees", AccountCategoryType.Expense, TaxTreatment.Deductible),
        new("6900", "Bank Charges & Interest", AccountCategoryType.Expense, TaxTreatment.Deductible),
        new("7000", "Depreciation", AccountCategoryType.Expense, TaxTreatment.NonDeductible),
        new("7100", "Repairs & Maintenance", AccountCategoryType.Expense, TaxTreatment.Deductible),
        new("7200", "Advertising & Marketing", AccountCategoryType.Expense, TaxTreatment.Deductible),
        new("7300", "Software & Subscriptions", AccountCategoryType.Expense, TaxTreatment.Deductible),
        new("7400", "Training & Development", AccountCategoryType.Expense, TaxTreatment.Deductible),
        new("7500", "Entertainment", AccountCategoryType.Expense, TaxTreatment.NonDeductible),
        new("7900", "Sundry Expenses", AccountCategoryType.Expense, TaxTreatment.Deductible),
        new("8000", "Corporation Tax Charge", AccountCategoryType.Expense, TaxTreatment.NonDeductible),
        new("0010", "Land & Buildings", AccountCategoryType.Asset, TaxTreatment.CapitalAllowance),
        new("0020", "Plant & Machinery", AccountCategoryType.Asset, TaxTreatment.CapitalAllowance),
        new("0030", "Motor Vehicles", AccountCategoryType.Asset, TaxTreatment.CapitalAllowance),
        new("0040", "Office Equipment", AccountCategoryType.Asset, TaxTreatment.CapitalAllowance),
        new("0050", "Computer Equipment", AccountCategoryType.Asset, TaxTreatment.CapitalAllowance),
        new("1000", "Stock / Inventory", AccountCategoryType.Asset, TaxTreatment.Other),
        new("1100", "Trade Debtors", AccountCategoryType.Asset, TaxTreatment.Other),
        new("1200", "Prepayments", AccountCategoryType.Asset, TaxTreatment.Other),
        new("1300", "VAT Receivable", AccountCategoryType.Asset, TaxTreatment.Other),
        new("1400", "Bank Current Account", AccountCategoryType.Asset, TaxTreatment.Other),
        new("1410", "Petty Cash", AccountCategoryType.Asset, TaxTreatment.Other),
        new("2000", "Trade Creditors", AccountCategoryType.Liability, TaxTreatment.Other),
        new("2100", "Accruals", AccountCategoryType.Liability, TaxTreatment.Other),
        new("2200", "VAT Payable", AccountCategoryType.Liability, TaxTreatment.Other),
        new("2300", "PAYE / PRSI Payable", AccountCategoryType.Liability, TaxTreatment.Other),
        new("2400", "Corporation Tax Payable", AccountCategoryType.Liability, TaxTreatment.Other),
        new("2500", "Director Loan Account", AccountCategoryType.Liability, TaxTreatment.Other),
        new("2600", "Bank Loan (< 1 year)", AccountCategoryType.Liability, TaxTreatment.Other),
        new("2700", "Bank Loan (> 1 year)", AccountCategoryType.Liability, TaxTreatment.Other),
        new("3000", "Share Capital", AccountCategoryType.Equity, TaxTreatment.Other),
        new("3100", "Retained Earnings", AccountCategoryType.Equity, TaxTreatment.Other),
        new("3200", "Dividends Paid", AccountCategoryType.Equity, TaxTreatment.Other),
    ];

    private static async Task EnsureImportBatchWithTransactionsAsync(AccountsDbContext db, BankAccount bank, AccountingPeriod period, Dictionary<string, AccountCategory> categories, string filename, TransactionSeed[] transactions)
    {
        var batch = await db.ImportBatches.FirstOrDefaultAsync(b => b.BankAccountId == bank.Id && b.Filename == filename);
        if (batch is null)
        {
            batch = new ImportBatch { BankAccountId = bank.Id, Filename = filename };
            db.ImportBatches.Add(batch);
            await db.SaveChangesAsync();
        }

        batch.RowCount = transactions.Length;
        batch.MatchedCount = transactions.Count(t => t.CategoryCode is not null && !t.IsDuplicate);
        batch.ImportedAt = DateTime.UtcNow.AddDays(-15);

        foreach (var seed in transactions)
        {
            var tx = await db.ImportedTransactions.FirstOrDefaultAsync(t =>
                t.BankAccountId == bank.Id &&
                t.Date == seed.Date &&
                t.Amount == seed.Amount &&
                t.Description == seed.Description);

            if (tx is null)
            {
                tx = new ImportedTransaction
                {
                    BankAccountId = bank.Id,
                    Description = seed.Description,
                    Date = seed.Date,
                    Amount = seed.Amount
                };
                db.ImportedTransactions.Add(tx);
            }

            tx.PeriodId = period.Id;
            tx.ImportBatchId = batch.Id;
            tx.Reference = seed.Reference;
            tx.CategoryId = seed.CategoryCode is not null && categories.TryGetValue(seed.CategoryCode, out var category) ? category.Id : null;
            tx.ConfidenceScore = seed.ConfidenceScore;
            tx.IsDuplicate = seed.IsDuplicate;
            tx.ManualOverride = seed.IsDuplicate;
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureTransactionRulesAsync(AccountsDbContext db, int companyId, Dictionary<string, AccountCategory> categories, TransactionRuleSeed[] rules)
    {
        foreach (var seed in rules)
        {
            if (!categories.TryGetValue(seed.CategoryCode, out var category)) continue;
            var rule = await db.TransactionRules.FirstOrDefaultAsync(r => r.CompanyId == companyId && r.Pattern == seed.Pattern);
            if (rule is null)
            {
                rule = new TransactionRule { CompanyId = companyId, Pattern = seed.Pattern, CategoryId = category.Id };
                db.TransactionRules.Add(rule);
            }

            rule.CategoryId = category.Id;
            rule.Priority = seed.Priority;
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureSizeClassificationAsync(AccountsDbContext db, int periodId, decimal turnover, decimal balanceSheetTotal, int avgEmployees, CompanySizeClass priorYearClass, CompanySizeClass calculatedClass, string notes)
    {
        var classification = await db.SizeClassifications.FirstOrDefaultAsync(s => s.PeriodId == periodId);
        if (classification is null)
        {
            classification = new SizeClassification { PeriodId = periodId };
            db.SizeClassifications.Add(classification);
        }

        classification.Turnover = turnover;
        classification.BalanceSheetTotal = balanceSheetTotal;
        classification.AvgEmployees = avgEmployees;
        classification.PriorYearClass = priorYearClass;
        classification.CalculatedClass = calculatedClass;
        classification.QualificationNotes = notes;
        await db.SaveChangesAsync();
    }

    private static async Task EnsureFilingRegimeAsync(AccountsDbContext db, int periodId, bool canUseMicro, bool canFileAbridged, bool auditExempt, ElectedRegime electedRegime, string[] notes, string[] statements)
    {
        var regime = await db.FilingRegimes.FirstOrDefaultAsync(f => f.PeriodId == periodId);
        if (regime is null)
        {
            regime = new FilingRegime { PeriodId = periodId };
            db.FilingRegimes.Add(regime);
        }

        regime.CanUseMicro = canUseMicro;
        regime.CanFileAbridged = canFileAbridged;
        regime.AuditExempt = auditExempt;
        regime.ElectedRegime = electedRegime;
        regime.RequiredNotesJson = JsonSerializer.Serialize(notes);
        regime.RequiredStatementsJson = JsonSerializer.Serialize(statements);
        regime.DeterminedAt = DateTime.UtcNow.AddDays(-12);
        await db.SaveChangesAsync();
    }

    private static async Task EnsureFilingPackagesAsync(AccountsDbContext db, int periodId, FilingStatus filingStatus, string croReference, string ct1Reference, bool accountsPdfGenerated, bool signaturePageGenerated, bool paymentCompleted)
    {
        var cro = await db.CroFilingPackages.FirstOrDefaultAsync(c => c.PeriodId == periodId);
        if (cro is null)
        {
            cro = new CroFilingPackage { PeriodId = periodId };
            db.CroFilingPackages.Add(cro);
        }

        cro.Status = filingStatus is FilingStatus.Accepted or FilingStatus.Submitted ? FilingPackageStatus.Submitted : FilingPackageStatus.Generated;
        cro.FilingStatus = filingStatus;
        cro.PdfPath = $"/demo/filings/{croReference}.pdf";
        cro.CroSubmissionReference = filingStatus is FilingStatus.Submitted or FilingStatus.Accepted ? croReference : null;
        cro.AccountsPdfGenerated = accountsPdfGenerated;
        cro.SignaturePageGenerated = signaturePageGenerated;
        cro.PaymentCompleted = paymentCompleted;
        cro.ApprovedBy = filingStatus >= FilingStatus.Approved ? "demo.reviewer@accounts.local" : null;
        cro.ApprovedAt = filingStatus >= FilingStatus.Approved ? DateTime.UtcNow.AddDays(-6) : null;
        cro.SubmittedBy = filingStatus is FilingStatus.Submitted or FilingStatus.Accepted ? "demo.owner@accounts.local" : null;
        cro.SubmittedAt = filingStatus is FilingStatus.Submitted or FilingStatus.Accepted ? DateTime.UtcNow.AddDays(-3) : null;
        cro.GeneratedAt = DateTime.UtcNow.AddDays(-7);

        var revenue = await db.RevenueFilingPackages.FirstOrDefaultAsync(r => r.PeriodId == periodId);
        if (revenue is null)
        {
            revenue = new RevenueFilingPackage { PeriodId = periodId };
            db.RevenueFilingPackages.Add(revenue);
        }

        revenue.Status = FilingPackageStatus.Generated;
        revenue.FilingStatus = filingStatus is FilingStatus.Accepted ? FilingStatus.Accepted : FilingStatus.InProgress;
        revenue.Ct1Reference = ct1Reference;
        revenue.Ct1DataJson = JsonSerializer.Serialize(new { ct1Reference, generatedBy = DemoSeedUser, demo = true });
        revenue.IxbrlPath = $"/demo/revenue/{ct1Reference}.xhtml";
        revenue.IxbrlGenerated = true;
        revenue.IxbrlValidated = false;
        revenue.IxbrlValidationErrors = filingStatus is FilingStatus.Accepted or FilingStatus.ReadyForReview or FilingStatus.PackageGenerated
            ? "Internal checks passed. External ROS/iXBRL validation remains required."
            : "Draft data awaiting final review.";
        revenue.ApprovedBy = filingStatus is FilingStatus.Accepted ? "demo.reviewer@accounts.local" : null;
        revenue.ApprovedAt = filingStatus is FilingStatus.Accepted ? DateTime.UtcNow.AddDays(-5) : null;
        revenue.GeneratedAt = DateTime.UtcNow.AddDays(-7);
        await db.SaveChangesAsync();
    }

    private static async Task EnsureDeadlinesAsync(AccountsDbContext db, int companyId, int periodId, DateOnly periodEnd, bool includeCharity, bool filed = false)
    {
        await EnsureDeadlineAsync(db, companyId, periodId, DeadlineType.CRO, periodEnd.AddMonths(9), filed ? periodEnd.AddMonths(8).AddDays(18) : null, "Annual return and accounts filing deadline.");
        await EnsureDeadlineAsync(db, companyId, periodId, DeadlineType.Revenue, periodEnd.AddMonths(9).AddDays(23), filed ? periodEnd.AddMonths(9).AddDays(10) : null, "CT1 and iXBRL Revenue deadline.");
        if (includeCharity)
            await EnsureDeadlineAsync(db, companyId, periodId, DeadlineType.Charity, periodEnd.AddMonths(10), null, "Charities Regulator annual report deadline.");

        await EnsureFilingHistoryAsync(db, companyId, periodId, DeadlineType.CRO, periodEnd.AddYears(-1).AddMonths(9), periodEnd.AddYears(-1).AddMonths(9).AddDays(companyId % 2 == 0 ? 4 : -12));
    }

    private static async Task EnsureDeadlineAsync(AccountsDbContext db, int companyId, int periodId, DeadlineType type, DateOnly dueDate, DateOnly? filedDate, string notes)
    {
        var deadline = await db.FilingDeadlines.FirstOrDefaultAsync(d => d.CompanyId == companyId && d.PeriodId == periodId && d.DeadlineType == type);
        if (deadline is null)
        {
            deadline = new FilingDeadline { CompanyId = companyId, PeriodId = periodId, DeadlineType = type };
            db.FilingDeadlines.Add(deadline);
        }

        deadline.DueDate = dueDate;
        deadline.FiledDate = filedDate;
        deadline.IsLate = filedDate.HasValue && filedDate > dueDate;
        deadline.PenaltyAmount = deadline.IsLate ? 100m + ((filedDate!.Value.DayNumber - dueDate.DayNumber) * 3m) : 0m;
        deadline.Notes = notes;
        await db.SaveChangesAsync();
    }

    private static async Task EnsureFilingHistoryAsync(AccountsDbContext db, int companyId, int periodId, DeadlineType type, DateOnly dueDate, DateOnly filedDate)
    {
        var history = await db.FilingHistories.FirstOrDefaultAsync(h => h.CompanyId == companyId && h.DeadlineType == type && h.DueDate == dueDate);
        if (history is null)
        {
            history = new FilingHistory { CompanyId = companyId, DeadlineType = type, DueDate = dueDate, FiledDate = filedDate };
            db.FilingHistories.Add(history);
        }

        history.PeriodId = periodId;
        history.FiledDate = filedDate;
        history.DaysLate = Math.Max(0, filedDate.DayNumber - dueDate.DayNumber);
        history.PenaltyAmount = history.DaysLate == 0 ? 0m : 100m + (history.DaysLate * 3m);
        await db.SaveChangesAsync();
    }

    private static async Task EnsureDebtorAsync(AccountsDbContext db, int periodId, string name, decimal amount, DebtorType type, string notes)
    {
        var debtor = await db.Debtors.FirstOrDefaultAsync(d => d.PeriodId == periodId && d.Name == name);
        if (debtor is null)
        {
            debtor = new Debtor { PeriodId = periodId, Name = name };
            db.Debtors.Add(debtor);
        }

        debtor.Amount = amount;
        debtor.Type = type;
        debtor.Notes = notes;
        await db.SaveChangesAsync();
    }

    private static async Task EnsureCreditorAsync(AccountsDbContext db, int periodId, string name, decimal amount, CreditorType type, bool dueWithinYear, string notes)
    {
        var creditor = await db.Creditors.FirstOrDefaultAsync(c => c.PeriodId == periodId && c.Name == name);
        if (creditor is null)
        {
            creditor = new Creditor { PeriodId = periodId, Name = name };
            db.Creditors.Add(creditor);
        }

        creditor.Amount = amount;
        creditor.Type = type;
        creditor.DueWithinYear = dueWithinYear;
        creditor.Notes = notes;
        await db.SaveChangesAsync();
    }

    private static async Task EnsureInventoryAsync(AccountsDbContext db, int periodId, string description, decimal value, ValuationMethod method)
    {
        var inventory = await db.Inventories.FirstOrDefaultAsync(i => i.PeriodId == periodId && i.Description == description);
        if (inventory is null)
        {
            inventory = new Inventory { PeriodId = periodId, Description = description };
            db.Inventories.Add(inventory);
        }

        inventory.Value = value;
        inventory.ValuationMethod = method;
        await db.SaveChangesAsync();
    }

    private static async Task EnsureDirectorLoanAsync(AccountsDbContext db, int periodId, int directorId, decimal opening, decimal advances, decimal repayments, decimal closing, decimal interestRate, decimal interestCharged, decimal maxBalance, string terms)
    {
        var loan = await db.DirectorLoans.FirstOrDefaultAsync(d => d.PeriodId == periodId && d.DirectorId == directorId);
        if (loan is null)
        {
            loan = new DirectorLoan { PeriodId = periodId, DirectorId = directorId };
            db.DirectorLoans.Add(loan);
        }

        loan.OpeningBalance = opening;
        loan.Advances = advances;
        loan.Repayments = repayments;
        loan.ClosingBalance = closing;
        loan.InterestRate = interestRate;
        loan.InterestCharged = interestCharged;
        loan.MaxBalanceDuringYear = maxBalance;
        loan.IsDocumented = true;
        loan.LoanTerms = terms;
        await db.SaveChangesAsync();
    }

    private static async Task EnsurePayrollSummaryAsync(AccountsDbContext db, int periodId, decimal grossWages, decimal employerPrsi, decimal pension, int staffCount)
    {
        var payroll = await db.PayrollSummaries.FirstOrDefaultAsync(p => p.PeriodId == periodId);
        if (payroll is null)
        {
            payroll = new PayrollSummary { PeriodId = periodId };
            db.PayrollSummaries.Add(payroll);
        }

        payroll.GrossWages = grossWages;
        payroll.EmployerPrsi = employerPrsi;
        payroll.PensionContributions = pension;
        payroll.StaffCount = staffCount;
        await db.SaveChangesAsync();
    }

    private static async Task EnsureTaxBalanceAsync(AccountsDbContext db, int periodId, TaxType type, decimal liability, decimal paid, decimal balance)
    {
        var tax = await db.TaxBalances.FirstOrDefaultAsync(t => t.PeriodId == periodId && t.TaxType == type);
        if (tax is null)
        {
            tax = new TaxBalance { PeriodId = periodId, TaxType = type };
            db.TaxBalances.Add(tax);
        }

        tax.Liability = liability;
        tax.Paid = paid;
        tax.Balance = balance;
        await db.SaveChangesAsync();
    }

    private static async Task EnsureDividendAsync(AccountsDbContext db, int periodId, decimal amount, DateOnly declared, DateOnly paid)
    {
        var dividend = await db.Dividends.FirstOrDefaultAsync(d => d.PeriodId == periodId && d.Amount == amount && d.DateDeclared == declared);
        if (dividend is null)
        {
            dividend = new Dividend { PeriodId = periodId, Amount = amount };
            db.Dividends.Add(dividend);
        }

        dividend.DateDeclared = declared;
        dividend.DatePaid = paid;
        await db.SaveChangesAsync();
    }

    private static async Task EnsureDepreciationEntriesAsync(AccountsDbContext db, int companyId, int periodId, DateOnly periodStart)
    {
        var assets = await db.FixedAssets.Where(a => a.CompanyId == companyId && a.DisposalDate == null).ToListAsync();
        foreach (var asset in assets.Where(a => a.UsefulLifeYears > 0))
        {
            var entry = await db.DepreciationEntries.FirstOrDefaultAsync(d => d.AssetId == asset.Id && d.PeriodId == periodId);
            if (entry is null)
            {
                entry = new DepreciationEntry { AssetId = asset.Id, PeriodId = periodId };
                db.DepreciationEntries.Add(entry);
            }

            var priorYears = Math.Max(0, periodStart.Year - asset.AcquisitionDate.Year);
            decimal opening = asset.Cost;
            decimal annualCharge;
            if (asset.DepreciationMethod == DepreciationMethod.StraightLine)
            {
                annualCharge = Math.Round(asset.Cost / asset.UsefulLifeYears, 2);
                opening = Math.Max(0, asset.Cost - (annualCharge * Math.Min(priorYears, asset.UsefulLifeYears)));
            }
            else
            {
                var rate = 1m / asset.UsefulLifeYears;
                for (var i = 0; i < priorYears; i++)
                    opening -= Math.Round(opening * rate, 2);
                annualCharge = Math.Round(opening * rate, 2);
            }

            annualCharge = Math.Min(opening, annualCharge);
            entry.OpeningNbv = Math.Round(opening, 2);
            entry.Charge = Math.Round(annualCharge, 2);
            entry.ClosingNbv = Math.Round(opening - annualCharge, 2);
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureOpeningBalancesAsync(AccountsDbContext db, int periodId, Dictionary<string, AccountCategory> categories, OpeningBalanceSeed[] balances)
    {
        foreach (var seed in balances)
        {
            if (!categories.TryGetValue(seed.CategoryCode, out var category)) continue;
            var balance = await db.OpeningBalances.FirstOrDefaultAsync(o => o.PeriodId == periodId && o.AccountCategoryId == category.Id);
            if (balance is null)
            {
                balance = new OpeningBalance { PeriodId = periodId, AccountCategoryId = category.Id };
                db.OpeningBalances.Add(balance);
            }

            balance.Debit = seed.Debit;
            balance.Credit = seed.Credit;
            balance.SourceNote = seed.SourceNote;
            balance.EnteredBy = DemoSeedUser;
            balance.EnteredAt = DateTime.UtcNow.AddDays(-14);
            balance.Reviewed = true;
            balance.ReviewedBy = "demo.reviewer@accounts.local";
            balance.ReviewedAt = DateTime.UtcNow.AddDays(-12);
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureAdjustmentsAsync(AccountsDbContext db, int periodId, Dictionary<string, AccountCategory> categories, AdjustmentSeed[] adjustments)
    {
        foreach (var seed in adjustments)
        {
            var adjustment = await db.Adjustments.FirstOrDefaultAsync(a => a.PeriodId == periodId && a.Description == seed.Description);
            if (adjustment is null)
            {
                adjustment = new Adjustment { PeriodId = periodId, Description = seed.Description };
                db.Adjustments.Add(adjustment);
            }

            adjustment.DebitCategoryId = seed.DebitCode is not null && categories.TryGetValue(seed.DebitCode, out var debit) ? debit.Id : null;
            adjustment.CreditCategoryId = seed.CreditCode is not null && categories.TryGetValue(seed.CreditCode, out var credit) ? credit.Id : null;
            adjustment.Amount = seed.Amount;
            adjustment.Source = seed.Source;
            adjustment.Reason = seed.Reason;
            adjustment.LegalBasis = seed.LegalBasis;
            adjustment.ImpactOnProfit = seed.ImpactOnProfit;
            adjustment.ImpactOnAssets = seed.ImpactOnAssets;
            adjustment.CreatedBy = DemoSeedUser;
            adjustment.IsAuto = seed.Source == AdjustmentSource.Auto;
            adjustment.ApprovedBy = seed.Approved ? "demo.reviewer@accounts.local" : null;
            adjustment.ApprovedAt = seed.Approved ? DateTime.UtcNow.AddDays(-9) : null;
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureCharityInfoAsync(AccountsDbContext db, int companyId)
    {
        var charity = await db.CharityInfos.FirstOrDefaultAsync(c => c.CompanyId == companyId);
        if (charity is null)
        {
            charity = new CharityInfo { CompanyId = companyId };
            db.CharityInfos.Add(charity);
        }

        charity.CharityNumber = "CHY-22881";
        charity.CharityType = "CLG";
        charity.GrossIncome = 78_000m;
        charity.SorpTier = 1;
        charity.CharitableObjectives = "Advance community development, training access and digital inclusion in west Mayo.";
        charity.PrincipalActivities = "Community workshops, training grants, digital inclusion supports and volunteer-led outreach.";
        charity.GovernanceCodeCompliant = true;
        charity.GovernanceCodeNote = "Board self-assessment completed and minuted in December 2024.";
        charity.HasInternationalTransfers = false;
        charity.InternationalTransferDetails = null;
        charity.TrusteeRemunerationPaid = false;
        charity.TrusteeRemunerationAmount = 0m;
        charity.TrusteeExpensesDetails = "Trustee expenses reimbursed at vouched cost only.";
        await db.SaveChangesAsync();
    }

    private static async Task EnsureFundBalancesAsync(AccountsDbContext db, int periodId, FundBalanceSeed[] funds)
    {
        foreach (var seed in funds)
        {
            var fund = await db.FundBalances.FirstOrDefaultAsync(f => f.PeriodId == periodId && f.FundName == seed.FundName);
            if (fund is null)
            {
                fund = new FundBalance { PeriodId = periodId, FundName = seed.FundName };
                db.FundBalances.Add(fund);
            }

            fund.FundType = seed.FundType;
            fund.OpeningBalance = seed.OpeningBalance;
            fund.IncomingResources = seed.IncomingResources;
            fund.ResourcesExpended = seed.ResourcesExpended;
            fund.Transfers = seed.Transfers;
            fund.GainsLosses = seed.GainsLosses;
            fund.ClosingBalance = seed.ClosingBalance;
            fund.Notes = seed.Notes;
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureInterrogationDataAsync(AccountsDbContext db, int periodId, PostBalanceSheetEventSeed[] postBalanceSheetEvents, RelatedPartySeed[] relatedParties, ContingentLiabilitySeed[] contingencies)
    {
        foreach (var seed in postBalanceSheetEvents)
        {
            var item = await db.PostBalanceSheetEvents.FirstOrDefaultAsync(e => e.PeriodId == periodId && e.Description == seed.Description);
            if (item is null)
            {
                item = new PostBalanceSheetEvent { PeriodId = periodId, Description = seed.Description };
                db.PostBalanceSheetEvents.Add(item);
            }

            item.EventDate = seed.EventDate;
            item.IsAdjusting = seed.IsAdjusting;
            item.FinancialImpact = seed.FinancialImpact;
            item.ActionRequired = seed.ActionRequired;
        }

        foreach (var seed in relatedParties)
        {
            var item = await db.RelatedPartyTransactions.FirstOrDefaultAsync(r => r.PeriodId == periodId && r.PartyName == seed.PartyName && r.TransactionType == seed.TransactionType);
            if (item is null)
            {
                item = new RelatedPartyTransaction { PeriodId = periodId, PartyName = seed.PartyName, TransactionType = seed.TransactionType };
                db.RelatedPartyTransactions.Add(item);
            }

            item.Relationship = seed.Relationship;
            item.Amount = seed.Amount;
            item.BalanceOwed = seed.BalanceOwed;
            item.Terms = seed.Terms;
        }

        foreach (var seed in contingencies)
        {
            var item = await db.ContingentLiabilities.FirstOrDefaultAsync(c => c.PeriodId == periodId && c.Description == seed.Description);
            if (item is null)
            {
                item = new ContingentLiability { PeriodId = periodId, Description = seed.Description };
                db.ContingentLiabilities.Add(item);
            }

            item.Nature = seed.Nature;
            item.EstimatedAmount = seed.EstimatedAmount;
            item.Likelihood = seed.Likelihood;
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureReviewConfirmationsAsync(AccountsDbContext db, int periodId, string confirmedBy, (string SectionKey, string Note)[] confirmations)
    {
        foreach (var seed in confirmations)
        {
            var confirmation = await db.YearEndReviewConfirmations.FirstOrDefaultAsync(c => c.PeriodId == periodId && c.SectionKey == seed.SectionKey);
            if (confirmation is null)
            {
                confirmation = new YearEndReviewConfirmation { PeriodId = periodId, SectionKey = seed.SectionKey };
                db.YearEndReviewConfirmations.Add(confirmation);
            }

            confirmation.Confirmed = true;
            confirmation.ConfirmedBy = confirmedBy;
            confirmation.ConfirmedAt = DateTime.UtcNow.AddDays(-5);
            confirmation.Note = seed.Note;
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureReportsAndNotesAsync(AccountsDbContext db, int periodId, ElectedRegime regime, string companyName, string showcaseLabel)
    {
        foreach (var type in Enum.GetValues<ReportType>())
        {
            var report = await db.Reports.FirstOrDefaultAsync(r => r.PeriodId == periodId && r.Type == type);
            if (report is null)
            {
                report = new Report { PeriodId = periodId, Type = type };
                db.Reports.Add(report);
            }

            report.GeneratedAt = DateTime.UtcNow.AddDays(-4);
            report.DataJson = JsonSerializer.Serialize(new
            {
                companyName,
                showcaseLabel,
                reportType = type.ToString(),
                periodId,
                generatedFor = "Accounts v2 demo"
            });
        }

        var noteSeeds = new[]
        {
            new NoteSeed(1, "Accounting Policies", $"Prepared under {regime} demo assumptions for {companyName}. The seed includes bank import, classification, tax, filing, notes and review data.", true),
            new NoteSeed(2, "Tangible Fixed Assets", "Fixed asset additions, depreciation entries and NBV roll-forward are populated for preview and iXBRL support.", true),
            new NoteSeed(3, "Debtors", "Trade debtors, other debtors and prepayments are populated for year-end disclosure.", true),
            new NoteSeed(4, "Creditors", "Trade creditors, accruals, tax balances and loan maturity splits are populated.", true),
            new NoteSeed(5, "Filing Workflow", "CRO and Revenue package state is pre-seeded to demonstrate the version two workflow.", true),
            new NoteSeed(6, "Review Notes", "The year-end review confirmations are complete enough for a demo walkthrough.", false),
        };

        foreach (var seed in noteSeeds)
        {
            var note = await db.NotesDisclosures.FirstOrDefaultAsync(n => n.PeriodId == periodId && n.NoteNumber == seed.NoteNumber);
            if (note is null)
            {
                note = new NotesDisclosure { PeriodId = periodId, NoteNumber = seed.NoteNumber, Title = seed.Title };
                db.NotesDisclosures.Add(note);
            }

            note.Title = seed.Title;
            note.Content = seed.Content;
            note.IsRequired = seed.IsRequired;
            note.IsIncluded = true;
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureAuditLogAsync(AccountsDbContext db, int companyId, int periodId, string entityType, int entityId, string action, string message)
    {
        var exists = await db.AuditLogs.AnyAsync(a => a.CompanyId == companyId && a.PeriodId == periodId && a.EntityType == entityType && a.Action == action);
        if (exists) return;

        db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            PeriodId = periodId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            UserId = DemoSeedUser,
            NewValueJson = JsonSerializer.Serialize(new { message, generatedAt = DateTime.UtcNow, version = "v2-demo" }),
            Timestamp = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task EnsureTenantAuditLogAsync(AccountsDbContext db, Tenant tenant, Company[] companies)
    {
        var exists = await db.AuditLogs.AnyAsync(a => a.EntityType == "Tenant" && a.EntityId == tenant.Id && a.Action == "SeededMainTenant");
        if (exists) return;

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = "Tenant",
            EntityId = tenant.Id,
            Action = "SeededMainTenant",
            UserId = DemoSeedUser,
            NewValueJson = JsonSerializer.Serialize(new
            {
                tenant = tenant.Name,
                companies = companies.Select(c => c.LegalName).ToArray(),
                modules = "All EF modules seeded for Accounts v2 demo"
            }),
            Timestamp = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task EnsureDemoUsersAsync(AccountsDbContext db, int tenantId, int demoClientCompanyId)
    {
        var users = new DemoUserSeed[]
        {
            new("owner@accounts-demo.ie", "Orla Byrne", "Owner", "Harbour!Ledger-V2-Owner-2026-84Qm"),
            new("accountant@accounts-demo.ie", "Eoin Gallagher", "Accountant", "Liffey#Accounts-V2-Prep-2026-63Tx"),
            new("reviewer@accounts-demo.ie", "Maeve Collins", "Reviewer", "Cedar%Review-V2-Approve-2026-17Np"),
            new("client@accounts-demo.ie", "Nora Walsh", "Client", "Quartz&Client-V2-Filing-2026-52Kd"),
        };

        foreach (var seed in users)
        {
            if (!MeetsStrongPasswordPolicy(seed.Password))
                throw new InvalidOperationException($"Demo password for {seed.Email} does not meet the strong password policy.");

            var user = await db.UserAccounts.FirstOrDefaultAsync(u => u.Email == seed.Email);
            if (user is null)
            {
                user = new UserAccount
                {
                    TenantId = tenantId,
                    Email = seed.Email,
                    DisplayName = seed.DisplayName,
                    Role = seed.Role,
                    PasswordHash = "",
                    PasswordSalt = ""
                };
                db.UserAccounts.Add(user);
            }

            user.TenantId = tenantId;
            user.DisplayName = seed.DisplayName;
            user.Role = seed.Role;
            user.IsActive = true;
            user.MustChangePassword = false;
            SetPassword(user, seed.Password);
            user.UpdatedAt = DateTime.UtcNow;

            if (seed.Role.Equals("Client", StringComparison.OrdinalIgnoreCase))
                await EnsureUserCompanyAccessAsync(db, user, demoClientCompanyId);
        }

        await db.SaveChangesAsync();
    }

    private static async Task RemoveDemoUsersAsync(AccountsDbContext db)
    {
        var demoUsers = await db.UserAccounts
            .Where(u => DemoUserEmails.Contains(u.Email))
            .ToListAsync();
        if (demoUsers.Count == 0)
            return;

        db.UserAccounts.RemoveRange(demoUsers);
        await db.SaveChangesAsync();
    }

    private static async Task RemoveNonCharitySampleCompaniesAsync(AccountsDbContext db)
    {
        var sampleCompanies = await db.Companies
            .Where(c => c.CroNumber != null && NonCharitySampleCompanyCroNumbers.Contains(c.CroNumber))
            .ToListAsync();
        if (sampleCompanies.Count == 0)
            return;

        var sampleCompanyIds = sampleCompanies.Select(c => c.Id).ToArray();
        var samplePeriodIds = await db.AccountingPeriods
            .Where(p => sampleCompanyIds.Contains(p.CompanyId))
            .Select(p => p.Id)
            .ToArrayAsync();
        var sampleCategoryIds = await db.AccountCategories
            .Where(c => c.CompanyId.HasValue && sampleCompanyIds.Contains(c.CompanyId.Value))
            .Select(c => c.Id)
            .ToArrayAsync();
        var openingBalances = await db.OpeningBalances
            .Where(o => samplePeriodIds.Contains(o.PeriodId) || sampleCategoryIds.Contains(o.AccountCategoryId))
            .ToListAsync();
        if (openingBalances.Count > 0)
        {
            db.OpeningBalances.RemoveRange(openingBalances);
            await db.SaveChangesAsync();
        }

        db.Companies.RemoveRange(sampleCompanies);
        await db.SaveChangesAsync();
    }

    private static async Task EnsureUserCompanyAccessAsync(AccountsDbContext db, UserAccount user, int companyId)
    {
        await db.SaveChangesAsync();

        var exists = await db.UserCompanyAccesses
            .AnyAsync(a => a.UserId == user.Id && a.CompanyId == companyId);
        if (exists) return;

        db.UserCompanyAccesses.Add(new UserCompanyAccess
        {
            UserId = user.Id,
            CompanyId = companyId
        });
    }

    private static void SetPassword(UserAccount user, string password)
    {
        if (!MeetsStrongPasswordPolicy(password))
            throw new InvalidOperationException($"Password for {user.Email} does not meet the strong password policy.");

        var salt = RandomNumberGenerator.GetBytes(32);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordIterations, HashAlgorithmName.SHA256, 32);
        user.PasswordSalt = Convert.ToBase64String(salt);
        user.PasswordHash = Convert.ToBase64String(hash);
        user.PasswordAlgorithm = PasswordAlgorithm;
        user.PasswordStrengthScore = 5;
        user.PasswordLastChangedAt = DateTime.UtcNow;
    }

    private static bool MeetsStrongPasswordPolicy(string password) =>
        password.Length >= 20
        && password.Any(char.IsUpper)
        && password.Any(char.IsLower)
        && password.Any(char.IsDigit)
        && password.Any(ch => !char.IsLetterOrDigit(ch));

    private sealed record CategorySeed(string Code, string Name, AccountCategoryType Type, TaxTreatment TaxTreatment);
    private sealed record TransactionSeed(DateOnly Date, string Description, decimal Amount, string? CategoryCode, string Reference, decimal ConfidenceScore, bool IsDuplicate = false);
    private sealed record TransactionRuleSeed(string Pattern, string CategoryCode, int Priority);
    private sealed record OpeningBalanceSeed(string CategoryCode, decimal Debit, decimal Credit, string SourceNote);
    private sealed record AdjustmentSeed(string Description, string? DebitCode, string? CreditCode, decimal Amount, AdjustmentSource Source, string Reason, string LegalBasis, decimal ImpactOnProfit, decimal ImpactOnAssets, bool Approved);
    private sealed record FundBalanceSeed(string FundName, string FundType, decimal OpeningBalance, decimal IncomingResources, decimal ResourcesExpended, decimal Transfers, decimal GainsLosses, decimal ClosingBalance, string Notes);
    private sealed record PostBalanceSheetEventSeed(string Description, DateOnly EventDate, bool IsAdjusting, decimal? FinancialImpact, string ActionRequired);
    private sealed record RelatedPartySeed(string PartyName, string Relationship, string TransactionType, decimal Amount, decimal? BalanceOwed, string Terms);
    private sealed record ContingentLiabilitySeed(string Description, string Nature, decimal? EstimatedAmount, string Likelihood);
    private sealed record NoteSeed(int NoteNumber, string Title, string Content, bool IsRequired);
    private sealed record DemoUserSeed(string Email, string DisplayName, string Role, string Password);
}
