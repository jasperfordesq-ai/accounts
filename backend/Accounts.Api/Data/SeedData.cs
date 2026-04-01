using Accounts.Api.Entities;

namespace Accounts.Api.Data;

public static class SeedData
{
    public static async Task SeedAsync(AccountsDbContext db)
    {
        if (db.Companies.Any()) return; // Already seeded

        // ===== COMPANY 1: Micro company (CLG, non-trading charity-like) =====
        var micro = new Company
        {
            LegalName = "Green Valley Community Development CLG",
            TradingName = "Green Valley",
            CroNumber = "567890",
            TaxReference = "5678901T",
            CompanyType = CompanyType.CompanyLimitedByGuarantee,
            IncorporationDate = new DateOnly(2019, 3, 15),
            FinancialYearStartMonth = 1,
            ArdMonth = 6,
            RegisteredOfficeAddress1 = "12 Main Street",
            RegisteredOfficeCity = "Castlebar",
            RegisteredOfficeCounty = "Mayo",
            RegisteredOfficeEircode = "F23 AB12",
            IsTrading = true,
            IsDormant = false,
            IsVatRegistered = false,
            IsEmployer = false,
            HasStock = false,
            OwnsAssets = true,
            HasBorrowings = false,
            HasDirectorLoans = false,
            Officers =
            [
                new() { Name = "Mary O'Brien", Role = OfficerRole.Director, AppointedDate = new DateOnly(2019, 3, 15) },
                new() { Name = "Patrick Walsh", Role = OfficerRole.Director, AppointedDate = new DateOnly(2019, 3, 15) },
                new() { Name = "Siobhán Kelly", Role = OfficerRole.Secretary, AppointedDate = new DateOnly(2019, 3, 15) },
            ],
            BankAccounts =
            [
                new() { Name = "AIB Current Account", Iban = "IE12AIBK93115212345678", Currency = "EUR", OpeningBalance = 4500m, OpeningBalanceDate = new DateOnly(2024, 1, 1) }
            ],
            FixedAssets =
            [
                new() { Name = "Laptop — Dell XPS", Category = "Computer Equipment", Cost = 1200m, AcquisitionDate = new DateOnly(2020, 6, 1), UsefulLifeYears = 3, DepreciationMethod = DepreciationMethod.StraightLine },
                new() { Name = "Office Furniture", Category = "Office Equipment", Cost = 800m, AcquisitionDate = new DateOnly(2019, 4, 1), UsefulLifeYears = 10, DepreciationMethod = DepreciationMethod.StraightLine },
            ]
        };
        db.Companies.Add(micro);
        await db.SaveChangesAsync();

        db.ShareCapitals.Add(new ShareCapital { CompanyId = micro.Id, ShareClass = "Guarantee", NominalValue = 1m, NumberIssued = 1, TotalValue = 1m, IsFullyPaid = true });

        var microPeriod = new AccountingPeriod
        {
            CompanyId = micro.Id,
            PeriodStart = new DateOnly(2024, 1, 1),
            PeriodEnd = new DateOnly(2024, 12, 31),
            Status = PeriodStatus.Draft,
            IsFirstYear = false,
        };
        db.AccountingPeriods.Add(microPeriod);
        await db.SaveChangesAsync();

        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = microPeriod.Id,
            Turnover = 45000m,
            BalanceSheetTotal = 12000m,
            AvgEmployees = 0,
            PriorYearClass = CompanySizeClass.Micro,
            CalculatedClass = CompanySizeClass.Micro,
            QualificationNotes = "Qualifies as Micro for both current and prior year."
        });

        db.Debtors.AddRange(
            new Debtor { PeriodId = microPeriod.Id, Name = "SICAP Grant Due", Amount = 2500m, Type = DebtorType.Other },
            new Debtor { PeriodId = microPeriod.Id, Name = "Insurance Prepayment", Amount = 600m, Type = DebtorType.Prepayment }
        );

        db.Creditors.AddRange(
            new Creditor { PeriodId = microPeriod.Id, Name = "Electric Ireland", Amount = 350m, Type = CreditorType.Accrual, DueWithinYear = true },
            new Creditor { PeriodId = microPeriod.Id, Name = "Accountancy Fee", Amount = 750m, Type = CreditorType.Accrual, DueWithinYear = true }
        );

        db.TaxBalances.Add(new TaxBalance
        {
            PeriodId = microPeriod.Id, TaxType = TaxType.CorporationTax, Liability = 250m, Paid = 250m, Balance = 0m
        });

        await db.SaveChangesAsync();

        // ===== COMPANY 2: Small trading company (LTD) =====
        var small = new Company
        {
            LegalName = "Connacht Digital Solutions Limited",
            TradingName = "Connacht Digital",
            CroNumber = "654321",
            TaxReference = "6543210W",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2018, 9, 1),
            FinancialYearStartMonth = 4,
            ArdMonth = 9,
            RegisteredOfficeAddress1 = "Unit 4, Galway Technology Centre",
            RegisteredOfficeAddress2 = "Mervue Business Park",
            RegisteredOfficeCity = "Galway",
            RegisteredOfficeCounty = "Galway",
            RegisteredOfficeEircode = "H91 W6KT",
            IsTrading = true,
            IsDormant = false,
            IsVatRegistered = true,
            IsEmployer = true,
            HasStock = false,
            OwnsAssets = true,
            HasBorrowings = true,
            HasDirectorLoans = true,
            Officers =
            [
                new() { Name = "Aoife Brennan", Role = OfficerRole.Director, AppointedDate = new DateOnly(2018, 9, 1) },
                new() { Name = "Cian Murphy", Role = OfficerRole.Director, AppointedDate = new DateOnly(2018, 9, 1) },
                new() { Name = "Roisín Flaherty", Role = OfficerRole.Secretary, AppointedDate = new DateOnly(2019, 1, 15) },
            ],
            BankAccounts =
            [
                new() { Name = "BOI Business Account", Iban = "IE45BOFI90001712345678", Currency = "EUR", OpeningBalance = 32000m, OpeningBalanceDate = new DateOnly(2024, 4, 1) },
                new() { Name = "Revolut Business", Iban = "IE98REVO99036012345678", Currency = "EUR", OpeningBalance = 5600m, OpeningBalanceDate = new DateOnly(2024, 4, 1) },
            ],
            FixedAssets =
            [
                new() { Name = "MacBook Pro x3", Category = "Computer Equipment", Cost = 8400m, AcquisitionDate = new DateOnly(2021, 1, 15), UsefulLifeYears = 3, DepreciationMethod = DepreciationMethod.StraightLine },
                new() { Name = "Office Desks & Chairs", Category = "Office Equipment", Cost = 3200m, AcquisitionDate = new DateOnly(2018, 10, 1), UsefulLifeYears = 10, DepreciationMethod = DepreciationMethod.StraightLine },
                new() { Name = "Server Infrastructure", Category = "Computer Equipment", Cost = 12000m, AcquisitionDate = new DateOnly(2022, 6, 1), UsefulLifeYears = 5, DepreciationMethod = DepreciationMethod.StraightLine },
                new() { Name = "Company Van — Ford Transit", Category = "Motor Vehicles", Cost = 28000m, AcquisitionDate = new DateOnly(2023, 3, 1), UsefulLifeYears = 5, DepreciationMethod = DepreciationMethod.ReducingBalance },
            ],
            Loans =
            [
                new() { Lender = "Bank of Ireland", OriginalAmount = 50000m, Balance = 35000m, InterestRate = 4.5m, IsDirectorLoan = false, DueWithinYear = 10000m, DueAfterYear = 25000m },
            ]
        };
        db.Companies.Add(small);
        await db.SaveChangesAsync();

        db.ShareCapitals.Add(new ShareCapital { CompanyId = small.Id, ShareClass = "Ordinary", NominalValue = 1m, NumberIssued = 100, TotalValue = 100m, IsFullyPaid = true });

        var smallPeriod = new AccountingPeriod
        {
            CompanyId = small.Id,
            PeriodStart = new DateOnly(2024, 4, 1),
            PeriodEnd = new DateOnly(2025, 3, 31),
            Status = PeriodStatus.Draft,
            IsFirstYear = false,
        };
        db.AccountingPeriods.Add(smallPeriod);
        await db.SaveChangesAsync();

        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = smallPeriod.Id,
            Turnover = 850000m,
            BalanceSheetTotal = 180000m,
            AvgEmployees = 8,
            PriorYearClass = CompanySizeClass.Small,
            CalculatedClass = CompanySizeClass.Small,
            QualificationNotes = "Qualifies as Small for both current and prior year."
        });

        db.Debtors.AddRange(
            new Debtor { PeriodId = smallPeriod.Id, Name = "Medtronic Ireland", Amount = 12500m, Type = DebtorType.Trade },
            new Debtor { PeriodId = smallPeriod.Id, Name = "Fidelity Investments", Amount = 8200m, Type = DebtorType.Trade },
            new Debtor { PeriodId = smallPeriod.Id, Name = "NUI Galway", Amount = 3500m, Type = DebtorType.Trade },
            new Debtor { PeriodId = smallPeriod.Id, Name = "Office Insurance Prepaid", Amount = 1800m, Type = DebtorType.Prepayment },
            new Debtor { PeriodId = smallPeriod.Id, Name = "Software Licences Prepaid", Amount = 2400m, Type = DebtorType.Prepayment }
        );

        db.Creditors.AddRange(
            new Creditor { PeriodId = smallPeriod.Id, Name = "AWS Ireland", Amount = 4200m, Type = CreditorType.Trade, DueWithinYear = true },
            new Creditor { PeriodId = smallPeriod.Id, Name = "Vodafone Business", Amount = 850m, Type = CreditorType.Trade, DueWithinYear = true },
            new Creditor { PeriodId = smallPeriod.Id, Name = "Audit Fee Accrual", Amount = 3500m, Type = CreditorType.Accrual, DueWithinYear = true },
            new Creditor { PeriodId = smallPeriod.Id, Name = "Electricity Accrual", Amount = 420m, Type = CreditorType.Accrual, DueWithinYear = true },
            new Creditor { PeriodId = smallPeriod.Id, Name = "PAYE/PRSI Q4", Amount = 6800m, Type = CreditorType.Tax, DueWithinYear = true },
            new Creditor { PeriodId = smallPeriod.Id, Name = "VAT Return", Amount = 3200m, Type = CreditorType.Tax, DueWithinYear = true }
        );

        var aoife = small.Officers.First(o => o.Name == "Aoife Brennan");
        db.DirectorLoans.Add(new DirectorLoan
        {
            PeriodId = smallPeriod.Id, DirectorId = aoife.Id, OpeningBalance = 15000m, Advances = 5000m, Repayments = 8000m, ClosingBalance = 12000m
        });

        db.PayrollSummaries.Add(new PayrollSummary
        {
            PeriodId = smallPeriod.Id, GrossWages = 320000m, EmployerPrsi = 35200m, PensionContributions = 16000m, StaffCount = 8
        });

        db.TaxBalances.AddRange(
            new TaxBalance { PeriodId = smallPeriod.Id, TaxType = TaxType.CorporationTax, Liability = 18750m, Paid = 15000m, Balance = 3750m },
            new TaxBalance { PeriodId = smallPeriod.Id, TaxType = TaxType.Vat, Liability = 3200m, Paid = 0m, Balance = 3200m },
            new TaxBalance { PeriodId = smallPeriod.Id, TaxType = TaxType.Paye, Liability = 6800m, Paid = 0m, Balance = 6800m }
        );

        db.Dividends.Add(new Dividend
        {
            PeriodId = smallPeriod.Id, Amount = 20000m, DateDeclared = new DateOnly(2025, 1, 15), DatePaid = new DateOnly(2025, 2, 1)
        });

        await db.SaveChangesAsync();

        // ===== COMPANY 3: Medium company (DAC) =====
        var medium = new Company
        {
            LegalName = "Atlantic Manufacturing DAC",
            TradingName = "Atlantic Mfg",
            CroNumber = "789012",
            TaxReference = "7890123K",
            CompanyType = CompanyType.DesignatedActivityCompany,
            IncorporationDate = new DateOnly(2010, 1, 20),
            FinancialYearStartMonth = 1,
            ArdMonth = 4,
            RegisteredOfficeAddress1 = "Industrial Estate",
            RegisteredOfficeAddress2 = "Limerick Road",
            RegisteredOfficeCity = "Shannon",
            RegisteredOfficeCounty = "Clare",
            RegisteredOfficeEircode = "V14 XY99",
            IsTrading = true,
            IsDormant = false,
            IsVatRegistered = true,
            IsEmployer = true,
            HasStock = true,
            OwnsAssets = true,
            HasBorrowings = true,
            HasDirectorLoans = false,
            Officers =
            [
                new() { Name = "Declan Ryan", Role = OfficerRole.Director, AppointedDate = new DateOnly(2010, 1, 20) },
                new() { Name = "Niamh Fitzgerald", Role = OfficerRole.Director, AppointedDate = new DateOnly(2012, 5, 1) },
                new() { Name = "Tomás Ó Sé", Role = OfficerRole.Director, AppointedDate = new DateOnly(2018, 3, 15) },
                new() { Name = "Claire Dunne", Role = OfficerRole.Secretary, AppointedDate = new DateOnly(2015, 7, 1) },
            ],
            BankAccounts =
            [
                new() { Name = "AIB Business Current", Iban = "IE29AIBK93104512345678", Currency = "EUR", OpeningBalance = 245000m, OpeningBalanceDate = new DateOnly(2024, 1, 1) },
                new() { Name = "AIB Deposit Account", Iban = "IE30AIBK93104599999999", Currency = "EUR", OpeningBalance = 150000m, OpeningBalanceDate = new DateOnly(2024, 1, 1) },
            ],
            FixedAssets =
            [
                new() { Name = "Factory Building", Category = "Land & Buildings", Cost = 850000m, AcquisitionDate = new DateOnly(2010, 3, 1), UsefulLifeYears = 50, DepreciationMethod = DepreciationMethod.StraightLine },
                new() { Name = "CNC Machines x4", Category = "Plant & Machinery", Cost = 420000m, AcquisitionDate = new DateOnly(2018, 1, 15), UsefulLifeYears = 10, DepreciationMethod = DepreciationMethod.StraightLine },
                new() { Name = "Forklift Fleet (3)", Category = "Plant & Machinery", Cost = 75000m, AcquisitionDate = new DateOnly(2021, 6, 1), UsefulLifeYears = 7, DepreciationMethod = DepreciationMethod.ReducingBalance },
                new() { Name = "Delivery Trucks x2", Category = "Motor Vehicles", Cost = 120000m, AcquisitionDate = new DateOnly(2022, 9, 1), UsefulLifeYears = 5, DepreciationMethod = DepreciationMethod.ReducingBalance },
                new() { Name = "IT Infrastructure", Category = "Computer Equipment", Cost = 35000m, AcquisitionDate = new DateOnly(2023, 1, 1), UsefulLifeYears = 3, DepreciationMethod = DepreciationMethod.StraightLine },
                new() { Name = "Office Fit-out", Category = "Office Equipment", Cost = 28000m, AcquisitionDate = new DateOnly(2015, 4, 1), UsefulLifeYears = 10, DepreciationMethod = DepreciationMethod.StraightLine },
            ],
            Loans =
            [
                new() { Lender = "AIB Corporate", OriginalAmount = 500000m, Balance = 280000m, InterestRate = 3.8m, IsDirectorLoan = false, DueWithinYear = 60000m, DueAfterYear = 220000m },
                new() { Lender = "Enterprise Ireland", OriginalAmount = 100000m, Balance = 40000m, InterestRate = 0m, IsDirectorLoan = false, DueWithinYear = 20000m, DueAfterYear = 20000m },
            ]
        };
        db.Companies.Add(medium);
        await db.SaveChangesAsync();

        db.ShareCapitals.Add(new ShareCapital { CompanyId = medium.Id, ShareClass = "Ordinary", NominalValue = 1m, NumberIssued = 1000, TotalValue = 1000m, IsFullyPaid = true });

        var mediumPeriod = new AccountingPeriod
        {
            CompanyId = medium.Id,
            PeriodStart = new DateOnly(2024, 1, 1),
            PeriodEnd = new DateOnly(2024, 12, 31),
            Status = PeriodStatus.Draft,
            IsFirstYear = false,
        };
        db.AccountingPeriods.Add(mediumPeriod);
        await db.SaveChangesAsync();

        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = mediumPeriod.Id,
            Turnover = 18500000m,
            BalanceSheetTotal = 9200000m,
            AvgEmployees = 65,
            PriorYearClass = CompanySizeClass.Medium,
            CalculatedClass = CompanySizeClass.Medium,
            QualificationNotes = "Qualifies as Medium. Exceeds small thresholds on turnover and employees."
        });

        db.Debtors.AddRange(
            new Debtor { PeriodId = mediumPeriod.Id, Name = "Boston Scientific", Amount = 185000m, Type = DebtorType.Trade },
            new Debtor { PeriodId = mediumPeriod.Id, Name = "Stryker Ireland", Amount = 142000m, Type = DebtorType.Trade },
            new Debtor { PeriodId = mediumPeriod.Id, Name = "Johnson & Johnson", Amount = 98000m, Type = DebtorType.Trade },
            new Debtor { PeriodId = mediumPeriod.Id, Name = "Zimmer Biomet", Amount = 67000m, Type = DebtorType.Trade },
            new Debtor { PeriodId = mediumPeriod.Id, Name = "Insurance Prepayment", Amount = 18000m, Type = DebtorType.Prepayment },
            new Debtor { PeriodId = mediumPeriod.Id, Name = "Rent Deposit", Amount = 12000m, Type = DebtorType.Other }
        );

        db.Creditors.AddRange(
            new Creditor { PeriodId = mediumPeriod.Id, Name = "Steel Suppliers Ltd", Amount = 45000m, Type = CreditorType.Trade, DueWithinYear = true },
            new Creditor { PeriodId = mediumPeriod.Id, Name = "Tooling Direct", Amount = 23000m, Type = CreditorType.Trade, DueWithinYear = true },
            new Creditor { PeriodId = mediumPeriod.Id, Name = "Audit Fee", Amount = 15000m, Type = CreditorType.Accrual, DueWithinYear = true },
            new Creditor { PeriodId = mediumPeriod.Id, Name = "Holiday Pay Accrual", Amount = 28000m, Type = CreditorType.Accrual, DueWithinYear = true },
            new Creditor { PeriodId = mediumPeriod.Id, Name = "PAYE/PRSI Dec", Amount = 42000m, Type = CreditorType.Tax, DueWithinYear = true },
            new Creditor { PeriodId = mediumPeriod.Id, Name = "VAT Return", Amount = 35000m, Type = CreditorType.Tax, DueWithinYear = true }
        );

        db.Inventories.AddRange(
            new Inventory { PeriodId = mediumPeriod.Id, Description = "Raw Materials", Value = 120000m, ValuationMethod = ValuationMethod.Cost },
            new Inventory { PeriodId = mediumPeriod.Id, Description = "Work in Progress", Value = 85000m, ValuationMethod = ValuationMethod.Cost },
            new Inventory { PeriodId = mediumPeriod.Id, Description = "Finished Goods", Value = 95000m, ValuationMethod = ValuationMethod.LowerOfCostAndNrv }
        );

        db.PayrollSummaries.Add(new PayrollSummary
        {
            PeriodId = mediumPeriod.Id, GrossWages = 2800000m, EmployerPrsi = 308000m, PensionContributions = 140000m, StaffCount = 65
        });

        db.TaxBalances.AddRange(
            new TaxBalance { PeriodId = mediumPeriod.Id, TaxType = TaxType.CorporationTax, Liability = 156000m, Paid = 140000m, Balance = 16000m },
            new TaxBalance { PeriodId = mediumPeriod.Id, TaxType = TaxType.Vat, Liability = 35000m, Paid = 0m, Balance = 35000m },
            new TaxBalance { PeriodId = mediumPeriod.Id, TaxType = TaxType.Paye, Liability = 42000m, Paid = 0m, Balance = 42000m }
        );

        db.Dividends.Add(new Dividend
        {
            PeriodId = mediumPeriod.Id, Amount = 100000m, DateDeclared = new DateOnly(2024, 11, 1), DatePaid = new DateOnly(2024, 12, 1)
        });

        await db.SaveChangesAsync();
    }
}
