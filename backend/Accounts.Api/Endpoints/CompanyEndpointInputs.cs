using Accounts.Api.Services;

public record PeriodStatusUpdate(Accounts.Api.Entities.PeriodStatus Status, string? LockedBy, string? ReopenReason, DateOnly? ApprovalDate = null);

public class CompanyInput
{
    public string? LegalName { get; set; }
    public string? TradingName { get; set; }
    public string? CroNumber { get; set; }
    public string? TaxReference { get; set; }
    public Accounts.Api.Entities.CompanyType CompanyType { get; set; }
    public DateOnly IncorporationDate { get; set; }
    public int FinancialYearStartMonth { get; set; } = 1;
    public int ArdMonth { get; set; }
    public string? RegisteredOfficeAddress1 { get; set; }
    public string? RegisteredOfficeAddress2 { get; set; }
    public string? RegisteredOfficeCity { get; set; }
    public string? RegisteredOfficeCounty { get; set; }
    public string? RegisteredOfficeEircode { get; set; }
    public bool IsGroupMember { get; set; }
    public bool IsHolding { get; set; }
    public bool IsInvestment { get; set; }
    public bool IsSubsidiary { get; set; }
    public bool IsDormant { get; set; }
    public bool IsTrading { get; set; }
    public bool IsVatRegistered { get; set; }
    public bool IsEmployer { get; set; }
    public bool HasStock { get; set; }
    public bool OwnsAssets { get; set; }
    public bool HasBorrowings { get; set; }
    public bool HasDirectorLoans { get; set; }
    public bool IsListedSecurities { get; set; }
    public bool IsCreditInstitution { get; set; }
    public bool IsInsuranceUndertaking { get; set; }
    public bool IsPensionFund { get; set; }
    public bool IsCharitableOrganisation { get; set; }
}

public class CompanyOfficerInput
{
    public string? Name { get; set; }
    public Accounts.Api.Entities.OfficerRole Role { get; set; }
    public DateOnly? AppointedDate { get; set; }
    public DateOnly? ResignedDate { get; set; }
    public string? Address { get; set; }
}

public class AccountingPeriodInput
{
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public bool IsFirstYear { get; set; }
    public bool MemberAuditNoticeReceived { get; set; }
    public DateOnly? MemberAuditNoticeDate { get; set; }
    public bool GoingConcernConfirmed { get; set; } = true;
    public string? GoingConcernNote { get; set; }
}

public static class EndpointInputs
{
    public static IResult? ValidateCompany(CompanyInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(input.LegalName))
            errors["legalName"] = ["Legal name is required."];
        if (input.LegalName?.Length > 200)
            errors["legalName"] = ["Legal name must be 200 characters or fewer."];
        if (input.IncorporationDate == default)
            errors["incorporationDate"] = ["Incorporation date is required."];
        if (input.FinancialYearStartMonth is < 1 or > 12)
            errors["financialYearStartMonth"] = ["Financial year start month must be between 1 and 12."];
        if (input.ArdMonth is < 1 or > 12)
            errors["ardMonth"] = ["Annual return date month must be between 1 and 12."];
        if (!string.IsNullOrWhiteSpace(input.CroNumber) && input.CroNumber.Length > 20)
            errors["croNumber"] = ["CRO number must be 20 characters or fewer."];

        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }

    public static IResult? ValidateOfficer(CompanyOfficerInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(input.Name))
            errors["name"] = ["Officer name is required."];
        if (input.Name?.Length > 200)
            errors["name"] = ["Officer name must be 200 characters or fewer."];
        if (input.ResignedDate is not null && input.AppointedDate is not null && input.ResignedDate < input.AppointedDate)
            errors["resignedDate"] = ["Resigned date cannot be before appointed date."];

        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }

    public static IResult? ValidatePeriod(AccountingPeriodInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (input.PeriodStart == default)
            errors["periodStart"] = ["Period start is required."];
        if (input.PeriodEnd == default)
            errors["periodEnd"] = ["Period end is required."];
        if (input.PeriodEnd < input.PeriodStart)
            errors["periodEnd"] = ["Period end cannot be before period start."];
        if (input.PeriodStart != default && input.PeriodEnd > input.PeriodStart.AddMonths(18).AddDays(-1))
            errors["periodEnd"] = ["Accounting period cannot exceed 18 months."];
        if (input.MemberAuditNoticeReceived && input.MemberAuditNoticeDate is null)
            errors["memberAuditNoticeDate"] = ["Member audit notice date is required when notice was received."];

        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }

    public static IResult? ValidatePeriodStatusUpdate(
        Accounts.Api.Entities.AccountingPeriod period,
        PeriodStatusUpdate update,
        AuthenticatedUser user)
    {
        var errors = new Dictionary<string, string[]>();
        var locking = update.Status is Accounts.Api.Entities.PeriodStatus.Finalised or Accounts.Api.Entities.PeriodStatus.Filed;
        var reopening = (period.Status is Accounts.Api.Entities.PeriodStatus.Finalised or Accounts.Api.Entities.PeriodStatus.Filed || period.LockedAt is not null) && !locking;

        if (reopening && (string.IsNullOrWhiteSpace(update.ReopenReason) || update.ReopenReason.Trim().Length < 10))
            errors["reopenReason"] = ["A reopen reason of at least 10 characters is required."];
        if (reopening && !user.Role.Trim().Equals("Owner", StringComparison.OrdinalIgnoreCase))
            errors["status"] = ["Only owner users can reopen a locked accounting period."];

        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }

    public static void ApplyPeriodStatusUpdate(
        Accounts.Api.Entities.AccountingPeriod period,
        PeriodStatusUpdate update,
        AuthenticatedUser user,
        DateTime now)
    {
        var locking = update.Status is Accounts.Api.Entities.PeriodStatus.Finalised or Accounts.Api.Entities.PeriodStatus.Filed;
        var wasLocked = period.Status is Accounts.Api.Entities.PeriodStatus.Finalised or Accounts.Api.Entities.PeriodStatus.Filed || period.LockedAt is not null;

        period.Status = update.Status;
        if (locking)
        {
            period.LockedAt ??= now;
            period.LockedBy = AuthenticatedIdentity.ReviewerDisplayName(user);
        }
        else if (wasLocked)
        {
            period.LockedAt = null;
            period.LockedBy = null;
            period.ReopenedAt = now;
            period.ReopenedBy = AuthenticatedIdentity.ReviewerDisplayName(user);
            period.ReopenReason = update.ReopenReason?.Trim();
        }
    }

    public static Accounts.Api.Entities.Company ToCompany(CompanyInput input)
    {
        var company = new Accounts.Api.Entities.Company { LegalName = input.LegalName!.Trim() };
        ApplyCompany(company, input);
        return company;
    }

    public static void ApplyCompany(Accounts.Api.Entities.Company company, CompanyInput input)
    {
        company.LegalName = input.LegalName!.Trim();
        company.TradingName = TrimToNull(input.TradingName);
        company.CroNumber = TrimToNull(input.CroNumber);
        company.TaxReference = TrimToNull(input.TaxReference);
        company.CompanyType = input.CompanyType;
        company.IncorporationDate = input.IncorporationDate;
        company.FinancialYearStartMonth = input.FinancialYearStartMonth;
        company.ArdMonth = input.ArdMonth;
        company.RegisteredOfficeAddress1 = TrimToNull(input.RegisteredOfficeAddress1);
        company.RegisteredOfficeAddress2 = TrimToNull(input.RegisteredOfficeAddress2);
        company.RegisteredOfficeCity = TrimToNull(input.RegisteredOfficeCity);
        company.RegisteredOfficeCounty = TrimToNull(input.RegisteredOfficeCounty);
        company.RegisteredOfficeEircode = TrimToNull(input.RegisteredOfficeEircode);
        company.IsGroupMember = input.IsGroupMember;
        company.IsHolding = input.IsHolding;
        company.IsInvestment = input.IsInvestment;
        company.IsSubsidiary = input.IsSubsidiary;
        company.IsDormant = input.IsDormant;
        company.IsTrading = input.IsTrading;
        company.IsVatRegistered = input.IsVatRegistered;
        company.IsEmployer = input.IsEmployer;
        company.HasStock = input.HasStock;
        company.OwnsAssets = input.OwnsAssets;
        company.HasBorrowings = input.HasBorrowings;
        company.HasDirectorLoans = input.HasDirectorLoans;
        company.IsListedSecurities = input.IsListedSecurities;
        company.IsCreditInstitution = input.IsCreditInstitution;
        company.IsInsuranceUndertaking = input.IsInsuranceUndertaking;
        company.IsPensionFund = input.IsPensionFund;
        company.IsCharitableOrganisation = input.IsCharitableOrganisation;
        company.UpdatedAt = DateTime.UtcNow;
    }

    public static Accounts.Api.Entities.CompanyOfficer ToOfficer(int companyId, CompanyOfficerInput input)
    {
        var officer = new Accounts.Api.Entities.CompanyOfficer { CompanyId = companyId, Name = input.Name!.Trim() };
        ApplyOfficer(officer, input);
        return officer;
    }

    public static void ApplyOfficer(Accounts.Api.Entities.CompanyOfficer officer, CompanyOfficerInput input)
    {
        officer.Name = input.Name!.Trim();
        officer.Role = input.Role;
        officer.AppointedDate = input.AppointedDate;
        officer.ResignedDate = input.ResignedDate;
        officer.Address = TrimToNull(input.Address);
    }

    public static Accounts.Api.Entities.AccountingPeriod ToPeriod(int companyId, AccountingPeriodInput input) => new()
    {
        CompanyId = companyId,
        PeriodStart = input.PeriodStart,
        PeriodEnd = input.PeriodEnd,
        IsFirstYear = input.IsFirstYear,
        MemberAuditNoticeReceived = input.MemberAuditNoticeReceived,
        MemberAuditNoticeDate = input.MemberAuditNoticeDate,
        GoingConcernConfirmed = input.GoingConcernConfirmed,
        GoingConcernNote = TrimToNull(input.GoingConcernNote)
    };

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
