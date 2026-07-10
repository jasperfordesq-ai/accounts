using Accounts.Api.Entities;

namespace Accounts.Api.Endpoints;

// Public write contracts deliberately contain only client-editable scalar fields. Persistence
// identity, route ownership, calculated/audit fields, and EF navigation graphs never cross the
// HTTP binding boundary.
public sealed class BankAccountInput
{
    public string? Name { get; init; }
    public string? Iban { get; init; }
    public string? Currency { get; init; } = "EUR";
    public decimal OpeningBalance { get; init; }
    public DateOnly? OpeningBalanceDate { get; init; }

    internal BankAccount ToEntity(int companyId) => new()
    {
        CompanyId = companyId,
        Name = Name!.Trim(),
        Iban = string.IsNullOrWhiteSpace(Iban) ? null : Iban.Trim(),
        Currency = BankingEndpointInputs.NormalizeCurrency(Currency),
        OpeningBalance = OpeningBalance,
        OpeningBalanceDate = OpeningBalanceDate
    };

    public static implicit operator BankAccountInput(BankAccount value) => new()
    {
        Name = value.Name,
        Iban = value.Iban,
        Currency = value.Currency,
        OpeningBalance = value.OpeningBalance,
        OpeningBalanceDate = value.OpeningBalanceDate
    };
}

public sealed class AccountCategoryInput
{
    public string? Code { get; init; }
    public string? Name { get; init; }
    public AccountCategoryType Type { get; init; }
    public TaxTreatment TaxTreatment { get; init; } = TaxTreatment.Deductible;
    public bool IsNonTradingIncome { get; init; }
    public int? ParentId { get; init; }

    internal AccountCategory ToEntity(int companyId) => new()
    {
        CompanyId = companyId,
        Code = Code!.Trim(),
        Name = Name!.Trim(),
        Type = Type,
        TaxTreatment = TaxTreatment,
        IsNonTradingIncome = IsNonTradingIncome,
        IsSystem = false,
        ParentId = ParentId
    };

    public static implicit operator AccountCategoryInput(AccountCategory value) => new()
    {
        Code = value.Code,
        Name = value.Name,
        Type = value.Type,
        TaxTreatment = value.TaxTreatment,
        IsNonTradingIncome = value.IsNonTradingIncome,
        ParentId = value.ParentId
    };
}

public sealed class CharityInfoInput
{
    public string? CharityNumber { get; init; }
    public string? CharityType { get; init; }
    public decimal GrossIncome { get; init; }
    public string? CharitableObjectives { get; init; }
    public string? PrincipalActivities { get; init; }
    public bool? GovernanceCodeCompliant { get; init; }
    public string? GovernanceCodeNote { get; init; }
    public string? GovernanceEvidenceReference { get; init; }
    public byte[]? GovernanceEvidenceArtifact { get; init; }
    public bool HasInternationalTransfers { get; init; }
    public string? InternationalTransferDetails { get; init; }
    public bool TrusteeRemunerationPaid { get; init; }
    public decimal TrusteeRemunerationAmount { get; init; }
    public string? TrusteeExpensesDetails { get; init; }

    internal CharityInfo ToEntity(int companyId) => new()
    {
        CompanyId = companyId,
        CharityNumber = CharityNumber,
        CharityType = CharityType,
        GrossIncome = GrossIncome,
        CharitableObjectives = CharitableObjectives,
        PrincipalActivities = PrincipalActivities,
        GovernanceCodeCompliant = GovernanceCodeCompliant,
        GovernanceCodeNote = GovernanceCodeNote,
        GovernanceEvidenceReference = GovernanceEvidenceReference,
        GovernanceEvidenceArtifact = GovernanceEvidenceArtifact,
        HasInternationalTransfers = HasInternationalTransfers,
        InternationalTransferDetails = InternationalTransferDetails,
        TrusteeRemunerationPaid = TrusteeRemunerationPaid,
        TrusteeRemunerationAmount = TrusteeRemunerationAmount,
        TrusteeExpensesDetails = TrusteeExpensesDetails,
        CreatedAt = DateTime.UtcNow
    };

    public static implicit operator CharityInfoInput(CharityInfo value) => new()
    {
        CharityNumber = value.CharityNumber,
        CharityType = value.CharityType,
        GrossIncome = value.GrossIncome,
        CharitableObjectives = value.CharitableObjectives,
        PrincipalActivities = value.PrincipalActivities,
        GovernanceCodeCompliant = value.GovernanceCodeCompliant,
        GovernanceCodeNote = value.GovernanceCodeNote,
        GovernanceEvidenceReference = value.GovernanceEvidenceReference,
        GovernanceEvidenceArtifact = value.GovernanceEvidenceArtifact,
        HasInternationalTransfers = value.HasInternationalTransfers,
        InternationalTransferDetails = value.InternationalTransferDetails,
        TrusteeRemunerationPaid = value.TrusteeRemunerationPaid,
        TrusteeRemunerationAmount = value.TrusteeRemunerationAmount,
        TrusteeExpensesDetails = value.TrusteeExpensesDetails
    };
}

public sealed class FundBalanceInput
{
    public string FundName { get; init; } = "";
    public string FundType { get; init; } = "Unrestricted";
    public decimal OpeningBalance { get; init; }
    public decimal IncomingResources { get; init; }
    public decimal ResourcesExpended { get; init; }
    public decimal Transfers { get; init; }
    public decimal GainsLosses { get; init; }
    public string? Notes { get; init; }

    internal FundBalance ToEntity(int periodId) => new()
    {
        PeriodId = periodId,
        FundName = FundName,
        FundType = FundType,
        OpeningBalance = OpeningBalance,
        IncomingResources = IncomingResources,
        ResourcesExpended = ResourcesExpended,
        Transfers = Transfers,
        GainsLosses = GainsLosses,
        Notes = Notes
    };

    public static implicit operator FundBalanceInput(FundBalance value) => new()
    {
        FundName = value.FundName,
        FundType = value.FundType,
        OpeningBalance = value.OpeningBalance,
        IncomingResources = value.IncomingResources,
        ResourcesExpended = value.ResourcesExpended,
        Transfers = value.Transfers,
        GainsLosses = value.GainsLosses,
        Notes = value.Notes
    };
}

public sealed class CharityTrusteeReviewInput
{
    public bool Accepted { get; init; }
    public string? EvidenceReference { get; init; }
    public byte[]? EvidenceArtifact { get; init; }
}

public sealed class DebtorInput
{
    public string? Name { get; init; }
    public decimal Amount { get; init; }
    public DebtorType Type { get; init; }
    public string? Notes { get; init; }

    internal Debtor ToEntity(int periodId) => new()
    {
        PeriodId = periodId,
        Name = Name!,
        Amount = Amount,
        Type = Type,
        Notes = Notes
    };

    public static implicit operator DebtorInput(Debtor value) => new()
    {
        Name = value.Name,
        Amount = value.Amount,
        Type = value.Type,
        Notes = value.Notes
    };
}

public sealed class CreditorInput
{
    public string? Name { get; init; }
    public decimal Amount { get; init; }
    public CreditorType Type { get; init; }
    public bool DueWithinYear { get; init; } = true;
    public string? Notes { get; init; }

    internal Creditor ToEntity(int periodId) => new()
    {
        PeriodId = periodId,
        Name = Name!,
        Amount = Amount,
        Type = Type,
        DueWithinYear = DueWithinYear,
        Notes = Notes
    };

    public static implicit operator CreditorInput(Creditor value) => new()
    {
        Name = value.Name,
        Amount = value.Amount,
        Type = value.Type,
        DueWithinYear = value.DueWithinYear,
        Notes = value.Notes
    };
}

public sealed class InventoryInput
{
    public string? Description { get; init; }
    public decimal Value { get; init; }
    public ValuationMethod ValuationMethod { get; init; } = ValuationMethod.LowerOfCostAndNrv;

    internal Inventory ToEntity(int periodId) => new()
    {
        PeriodId = periodId,
        Description = Description!,
        Value = Value,
        ValuationMethod = ValuationMethod
    };

    public static implicit operator InventoryInput(Inventory value) => new()
    {
        Description = value.Description,
        Value = value.Value,
        ValuationMethod = value.ValuationMethod
    };
}

public sealed class FixedAssetInput
{
    public string? Name { get; init; }
    public string? Category { get; init; }
    public decimal Cost { get; init; }
    public decimal ResidualValue { get; init; }
    public DateOnly AcquisitionDate { get; init; }
    public DateOnly? DisposalDate { get; init; }
    public decimal? DisposalProceeds { get; init; }
    public int UsefulLifeYears { get; init; }
    public DepreciationMethod DepreciationMethod { get; init; } = DepreciationMethod.StraightLine;
    public CapitalAllowanceTreatment CapitalAllowanceTreatment { get; init; } = CapitalAllowanceTreatment.Unreviewed;
    public string? CapitalAllowanceEvidence { get; init; }

    internal FixedAsset ToEntity(int companyId) => new()
    {
        CompanyId = companyId,
        Name = Name!,
        Category = Category!,
        Cost = Cost,
        ResidualValue = ResidualValue,
        AcquisitionDate = AcquisitionDate,
        DisposalDate = DisposalDate,
        DisposalProceeds = DisposalProceeds,
        UsefulLifeYears = UsefulLifeYears,
        DepreciationMethod = DepreciationMethod,
        CapitalAllowanceTreatment = CapitalAllowanceTreatment,
        CapitalAllowanceEvidence = CapitalAllowanceEvidence
    };

    public static implicit operator FixedAssetInput(FixedAsset value) => new()
    {
        Name = value.Name,
        Category = value.Category,
        Cost = value.Cost,
        ResidualValue = value.ResidualValue,
        AcquisitionDate = value.AcquisitionDate,
        DisposalDate = value.DisposalDate,
        DisposalProceeds = value.DisposalProceeds,
        UsefulLifeYears = value.UsefulLifeYears,
        DepreciationMethod = value.DepreciationMethod,
        CapitalAllowanceTreatment = value.CapitalAllowanceTreatment,
        CapitalAllowanceEvidence = value.CapitalAllowanceEvidence
    };
}

public sealed class LoanInput
{
    public string? Lender { get; init; }
    public decimal OriginalAmount { get; init; }
    public decimal Balance { get; init; }
    public DateOnly? DrawdownDate { get; init; }
    public DateOnly? BalanceAsOfDate { get; init; }
    public decimal InterestRate { get; init; }
    public bool IsDirectorLoan { get; init; }
    public decimal DueWithinYear { get; init; }
    public decimal DueAfterYear { get; init; }

    internal Loan ToEntity(int companyId) => new()
    {
        CompanyId = companyId,
        Lender = Lender!,
        OriginalAmount = OriginalAmount,
        Balance = Balance,
        DrawdownDate = DrawdownDate,
        BalanceAsOfDate = BalanceAsOfDate,
        InterestRate = InterestRate,
        IsDirectorLoan = IsDirectorLoan,
        DueWithinYear = DueWithinYear,
        DueAfterYear = DueAfterYear
    };

    public static implicit operator LoanInput(Loan value) => new()
    {
        Lender = value.Lender,
        OriginalAmount = value.OriginalAmount,
        Balance = value.Balance,
        DrawdownDate = value.DrawdownDate,
        BalanceAsOfDate = value.BalanceAsOfDate,
        InterestRate = value.InterestRate,
        IsDirectorLoan = value.IsDirectorLoan,
        DueWithinYear = value.DueWithinYear,
        DueAfterYear = value.DueAfterYear
    };
}

public sealed class LoanBalanceSnapshotInput
{
    public int LoanId { get; init; }
    public decimal OpeningBalance { get; init; }
    public decimal Drawdowns { get; init; }
    public decimal Repayments { get; init; }
    public decimal ClosingBalance { get; init; }
    public decimal DueWithinYear { get; init; }
    public decimal DueAfterYear { get; init; }
    public string? Notes { get; init; }

    internal LoanBalanceSnapshot ToEntity(int periodId) => new()
    {
        LoanId = LoanId,
        PeriodId = periodId,
        OpeningBalance = OpeningBalance,
        Drawdowns = Drawdowns,
        Repayments = Repayments,
        ClosingBalance = ClosingBalance,
        DueWithinYear = DueWithinYear,
        DueAfterYear = DueAfterYear,
        Notes = Notes
    };

    public static implicit operator LoanBalanceSnapshotInput(LoanBalanceSnapshot value) => new()
    {
        LoanId = value.LoanId,
        OpeningBalance = value.OpeningBalance,
        Drawdowns = value.Drawdowns,
        Repayments = value.Repayments,
        ClosingBalance = value.ClosingBalance,
        DueWithinYear = value.DueWithinYear,
        DueAfterYear = value.DueAfterYear,
        Notes = value.Notes
    };
}

public sealed class DividendInput
{
    public decimal Amount { get; init; }
    public DateOnly? DateDeclared { get; init; }
    public DateOnly? DatePaid { get; init; }

    internal Dividend ToEntity(int periodId) => new()
    {
        PeriodId = periodId,
        Amount = Amount,
        DateDeclared = DateDeclared,
        DatePaid = DatePaid
    };

    public static implicit operator DividendInput(Dividend value) => new()
    {
        Amount = value.Amount,
        DateDeclared = value.DateDeclared,
        DatePaid = value.DatePaid
    };
}

public sealed class ShareCapitalInput
{
    public string ShareClass { get; init; } = "Ordinary";
    public decimal NominalValue { get; init; } = 1m;
    public int NumberIssued { get; init; } = 1;
    public bool IsFullyPaid { get; init; } = true;
    public DateOnly? IssueDate { get; init; }
    public DateOnly? CancelledDate { get; init; }

    internal ShareCapital ToEntity(int companyId) => new()
    {
        CompanyId = companyId,
        ShareClass = ShareClass,
        NominalValue = NominalValue,
        NumberIssued = NumberIssued,
        TotalValue = NominalValue * NumberIssued,
        IsFullyPaid = IsFullyPaid,
        IssueDate = IssueDate,
        CancelledDate = CancelledDate
    };

    public static implicit operator ShareCapitalInput(ShareCapital value) => new()
    {
        ShareClass = value.ShareClass,
        NominalValue = value.NominalValue,
        NumberIssued = value.NumberIssued,
        IsFullyPaid = value.IsFullyPaid,
        IssueDate = value.IssueDate,
        CancelledDate = value.CancelledDate
    };
}

public sealed class PayrollSummaryInput
{
    public decimal GrossWages { get; init; }
    public decimal DirectorsFees { get; init; }
    public decimal EmployerPrsi { get; init; }
    public decimal PensionContributions { get; init; }
    public int StaffCount { get; init; }

    internal PayrollSummary ToEntity(int periodId) => new()
    {
        PeriodId = periodId,
        GrossWages = GrossWages,
        DirectorsFees = DirectorsFees,
        EmployerPrsi = EmployerPrsi,
        PensionContributions = PensionContributions,
        StaffCount = StaffCount
    };

    public static implicit operator PayrollSummaryInput(PayrollSummary value) => new()
    {
        GrossWages = value.GrossWages,
        DirectorsFees = value.DirectorsFees,
        EmployerPrsi = value.EmployerPrsi,
        PensionContributions = value.PensionContributions,
        StaffCount = value.StaffCount
    };
}

public sealed class TaxBalanceInput
{
    public decimal Liability { get; init; }
    public decimal Paid { get; init; }
    public decimal Balance { get; init; }

    internal TaxBalance ToEntity(int periodId, TaxType taxType) => new()
    {
        PeriodId = periodId,
        TaxType = taxType,
        Liability = Liability,
        Paid = Paid,
        Balance = Balance
    };

    public static implicit operator TaxBalanceInput(TaxBalance value) => new()
    {
        Liability = value.Liability,
        Paid = value.Paid,
        Balance = value.Balance
    };
}

public sealed class NotesDisclosureInput
{
    public string? Title { get; init; }
    public string? Content { get; init; }
    public bool IsIncluded { get; init; } = true;

    internal NotesDisclosure ToEntity(int periodId, int noteNumber) => new()
    {
        PeriodId = periodId,
        NoteNumber = noteNumber,
        Title = Title!,
        Content = Content,
        IsRequired = false,
        IsIncluded = IsIncluded
    };

    public static implicit operator NotesDisclosureInput(NotesDisclosure value) => new()
    {
        Title = value.Title,
        Content = value.Content,
        IsIncluded = value.IsIncluded
    };
}

public sealed class PostBalanceSheetEventInput
{
    public string Description { get; init; } = "";
    public DateOnly EventDate { get; init; }
    public bool IsAdjusting { get; init; }
    public decimal? FinancialImpact { get; init; }
    public string? ActionRequired { get; init; }

    internal PostBalanceSheetEvent ToEntity(int periodId) => new()
    {
        PeriodId = periodId,
        Description = Description,
        EventDate = EventDate,
        IsAdjusting = IsAdjusting,
        FinancialImpact = FinancialImpact,
        ActionRequired = ActionRequired,
        CreatedAt = DateTime.UtcNow
    };

    public static implicit operator PostBalanceSheetEventInput(PostBalanceSheetEvent value) => new()
    {
        Description = value.Description,
        EventDate = value.EventDate,
        IsAdjusting = value.IsAdjusting,
        FinancialImpact = value.FinancialImpact,
        ActionRequired = value.ActionRequired
    };
}

public sealed class RelatedPartyTransactionInput
{
    public string PartyName { get; init; } = "";
    public string Relationship { get; init; } = "";
    public string TransactionType { get; init; } = "";
    public decimal Amount { get; init; }
    public decimal? BalanceOwed { get; init; }
    public string? Terms { get; init; }

    internal RelatedPartyTransaction ToEntity(int periodId) => new()
    {
        PeriodId = periodId,
        PartyName = PartyName,
        Relationship = Relationship,
        TransactionType = TransactionType,
        Amount = Amount,
        BalanceOwed = BalanceOwed,
        Terms = Terms,
        CreatedAt = DateTime.UtcNow
    };

    public static implicit operator RelatedPartyTransactionInput(RelatedPartyTransaction value) => new()
    {
        PartyName = value.PartyName,
        Relationship = value.Relationship,
        TransactionType = value.TransactionType,
        Amount = value.Amount,
        BalanceOwed = value.BalanceOwed,
        Terms = value.Terms
    };
}

public sealed class ContingentLiabilityInput
{
    public string Description { get; init; } = "";
    public string Nature { get; init; } = "";
    public decimal? EstimatedAmount { get; init; }
    public string Likelihood { get; init; } = "Possible";

    internal ContingentLiability ToEntity(int periodId) => new()
    {
        PeriodId = periodId,
        Description = Description,
        Nature = Nature,
        EstimatedAmount = EstimatedAmount,
        Likelihood = Likelihood,
        CreatedAt = DateTime.UtcNow
    };

    public static implicit operator ContingentLiabilityInput(ContingentLiability value) => new()
    {
        Description = value.Description,
        Nature = value.Nature,
        EstimatedAmount = value.EstimatedAmount,
        Likelihood = value.Likelihood
    };
}
