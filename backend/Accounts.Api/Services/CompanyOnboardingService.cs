using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;

namespace Accounts.Api.Services;

public sealed class CompanyOnboardingInput
{
    public CompanyInput? Company { get; init; }
    public List<CompanyOfficerInput>? Officers { get; init; }
    public AccountingPeriodInput? FirstPeriod { get; init; }
    public BankAccountInput? OpeningBankAccount { get; init; }
}

public sealed record OnboardedOfficer(int Id, string Name, OfficerRole Role);

public sealed record CompanyOnboardingOutcome(
    int CompanyId,
    string CompanyLegalName,
    int FirstPeriodId,
    DateOnly FirstPeriodStart,
    DateOnly FirstPeriodEnd,
    int OpeningBankAccountId,
    string OpeningBankAccountName,
    int CategoryCount,
    IReadOnlyList<OnboardedOfficer> Officers);

public sealed record CompanyOnboardingResult(
    CompanyOnboardingOutcome Outcome,
    bool WasReplay,
    long IdempotencyRecordId,
    DateTime ExpiresAtUtc,
    int HttpStatusCode);

public sealed class CompanyOnboardingValidationException(
    IReadOnlyDictionary<string, string[]> errors) : BusinessRuleException("Company onboarding validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class CompanyOnboardingIdempotencyConflictException(string message) : BusinessRuleException(message);

public static class CompanyOnboardingValidation
{
    public static IReadOnlyDictionary<string, string[]> Validate(
        CompanyOnboardingInput input,
        string? idempotencyKey)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var key = idempotencyKey?.Trim() ?? string.Empty;
        if (key.Length is < 8 or > 128
            || key.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':')))
        {
            errors["idempotencyKey"] = ["Idempotency-Key must contain 8 to 128 letters, digits, dots, colons, underscores, or hyphens."];
        }

        var company = input.Company;
        if (company is null)
        {
            errors["company"] = ["Company details are required."];
        }
        else
        {
            if (string.IsNullOrWhiteSpace(company.LegalName))
                errors["legalName"] = ["Legal name is required."];
            else if (company.LegalName.Trim().Length > 200)
                errors["legalName"] = ["Legal name must be 200 characters or fewer."];
            if (company.IncorporationDate == default)
                errors["incorporationDate"] = ["Incorporation date is required."];
            else if (company.IncorporationDate > DateOnly.FromDateTime(DateTime.UtcNow))
                errors["incorporationDate"] = ["Incorporation date cannot be in the future."];
            if (company.FinancialYearStartMonth is < 1 or > 12)
                errors["financialYearStartMonth"] = ["Financial year start month must be between 1 and 12."];
            foreach (var (field, messages) in AnnualReturnDateService.Validate(
                         null,
                         EndpointInputs.ToAnnualReturnDateChange(company),
                         initial: true))
            {
                errors[field] = messages;
            }
            if (company.AnnualReturnDate is { } annualReturnDate
                && company.IncorporationDate != default
                && annualReturnDate < company.IncorporationDate)
            {
                errors["annualReturnDate"] = ["Annual Return Date cannot be before incorporation."];
            }
            if (!string.IsNullOrWhiteSpace(company.CroNumber) && company.CroNumber.Trim().Length > 20)
                errors["croNumber"] = ["CRO number must be 20 characters or fewer."];
        }

        var officers = input.Officers;
        if (officers is null || officers.Count == 0)
        {
            errors["officers"] = ["At least one officer is required."];
        }
        else
        {
            for (var index = 0; index < officers.Count; index++)
            {
                var officer = officers[index];
                if (string.IsNullOrWhiteSpace(officer.Name))
                    errors[$"officers.{index}.name"] = ["Officer name is required."];
                else if (officer.Name.Trim().Length > 200)
                    errors[$"officers.{index}.name"] = ["Officer name must be 200 characters or fewer."];
                if (!Enum.IsDefined(officer.Role))
                    errors[$"officers.{index}.role"] = ["Officer role is not supported."];
                if (officer.ResignedDate is not null
                    && officer.AppointedDate is not null
                    && officer.ResignedDate < officer.AppointedDate)
                {
                    errors[$"officers.{index}.resignedDate"] = ["Resigned date cannot be before appointed date."];
                }
            }
        }

        var period = input.FirstPeriod;
        if (period is null)
        {
            errors["firstPeriod"] = ["The first accounting period is required."];
        }
        else
        {
            if (period.PeriodStart == default)
                errors["firstPeriod.periodStart"] = ["First-period start is required."];
            if (period.PeriodEnd == default)
                errors["firstPeriod.periodEnd"] = ["First-period end is required."];
            if (period.PeriodStart != default && period.PeriodEnd < period.PeriodStart)
                errors["firstPeriod.periodEnd"] = ["First-period end cannot be before its start."];
            if (period.PeriodStart != default
                && period.PeriodEnd > period.PeriodStart.AddMonths(18).AddDays(-1))
            {
                errors["firstPeriod.periodEnd"] = ["The first accounting period cannot exceed 18 months."];
            }
            if (!period.IsFirstYear)
                errors["firstPeriod.isFirstYear"] = ["The initial accounting period must be marked as the first year."];
            if (company is not null
                && company.IncorporationDate != default
                && period.PeriodStart != company.IncorporationDate)
            {
                errors["firstPeriod.periodStart"] = ["The first accounting period must begin on the incorporation date."];
            }
        }

        var bank = input.OpeningBankAccount;
        if (bank is null)
        {
            errors["openingBankAccount"] = ["An opening bank account is required."];
        }
        else
        {
            if (string.IsNullOrWhiteSpace(bank.Name))
                errors["openingBankAccount.name"] = ["Bank account name is required."];
            else if (bank.Name.Trim().Length > 200)
                errors["openingBankAccount.name"] = ["Bank account name must be 200 characters or fewer."];
            if (!string.IsNullOrWhiteSpace(bank.Iban) && bank.Iban.Trim().Length > 34)
                errors["openingBankAccount.iban"] = ["IBAN must be 34 characters or fewer."];
            if (!string.IsNullOrWhiteSpace(bank.Currency) && bank.Currency.Trim().Length != 3)
                errors["openingBankAccount.currency"] = ["Currency must be a three-letter code."];
            if (bank.OpeningBalance != 0 && bank.OpeningBalanceDate is null)
                errors["openingBankAccount.openingBalanceDate"] = ["Opening balance date is required when an opening balance is entered."];
            if (bank.OpeningBalance != 0
                && period is not null
                && bank.OpeningBalanceDate != period.PeriodStart)
            {
                errors["openingBankAccount.openingBalanceDate"] = ["Opening bank balance must be dated at the first-period start."];
            }
        }

        return errors;
    }
}

public sealed class CompanyOnboardingService(
    AccountsDbContext db,
    PeriodChronologyService chronology,
    CategoryService categoryService,
    AnnualReturnDateService annualReturnDateService,
    AuditService audit,
    IdempotencyService? configuredIdempotency = null)
{
    private readonly IdempotencyService idempotency = configuredIdempotency ?? new IdempotencyService(db);

    public async Task<CompanyOnboardingResult> CreateAsync(
        CompanyOnboardingInput input,
        string idempotencyKey,
        AuthenticatedUser actor,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(actor.Role, "Owner", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Only an Owner may onboard a company.");

        var errors = CompanyOnboardingValidation.Validate(input, idempotencyKey);
        if (errors.Count > 0)
            throw new CompanyOnboardingValidationException(errors);

        try
        {
            var normalizedKey = idempotencyKey.Trim();
            var execution = await idempotency.ExecuteAsync(
                actor.TenantId,
                normalizedKey,
                IdempotencyOperations.CompanyOnboard,
                input,
                actor,
                async token =>
                {
                    var company = EndpointInputs.ToCompany(input.Company!);
                    company.TenantId = actor.TenantId;
                    db.Companies.Add(company);
                    annualReturnDateService.PrepareInitial(
                        company,
                        EndpointInputs.ToAnnualReturnDateChange(input.Company!),
                        actor);
                    await db.SaveChangesAsync(token);

                    var period = EndpointInputs.ToPeriod(company.Id, input.FirstPeriod!);
                    await chronology.CreateAsync(period, token);

                    var bankInput = input.OpeningBankAccount!;
                    var bank = new BankAccount
                    {
                        CompanyId = company.Id,
                        Name = bankInput.Name!.Trim(),
                        Iban = TrimToNull(bankInput.Iban)?.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant(),
                        Currency = string.IsNullOrWhiteSpace(bankInput.Currency)
                            ? "EUR"
                            : bankInput.Currency.Trim().ToUpperInvariant(),
                        OpeningBalance = bankInput.OpeningBalance,
                        OpeningBalanceDate = bankInput.OpeningBalance == 0 ? null : bankInput.OpeningBalanceDate
                    };
                    db.BankAccounts.Add(bank);
                    await db.SaveChangesAsync(token);

                    var categories = await categoryService.SeedDefaultCategoriesAsync(company.Id);
                    var officers = input.Officers!
                        .Select(officerInput => EndpointInputs.ToOfficer(company.Id, officerInput))
                        .ToList();
                    db.CompanyOfficers.AddRange(officers);
                    await db.SaveChangesAsync(token);

                    var outcome = new CompanyOnboardingOutcome(
                        company.Id,
                        company.LegalName,
                        period.Id,
                        period.PeriodStart,
                        period.PeriodEnd,
                        bank.Id,
                        bank.Name,
                        categories.Count,
                        officers.Select(officer => new OnboardedOfficer(officer.Id, officer.Name, officer.Role)).ToArray());
                    await audit.LogAsync(
                        company.Id,
                        period.Id,
                        "Company",
                        company.Id,
                        AuditEventCodes.CompanyOnboarded,
                        null,
                        new
                        {
                            IdempotencyKeySha256 = IdempotencyService.Hash(normalizedKey),
                            RequestFingerprintSha256 = IdempotencyService.RequestFingerprint(IdempotencyOperations.CompanyOnboard, input),
                            company.Id,
                            PeriodId = period.Id,
                            BankAccountId = bank.Id,
                            CategoryCount = categories.Count,
                            OfficerIds = officers.Select(officer => officer.Id).ToArray()
                        },
                        AuthenticatedIdentity.AuditUserId(actor),
                        actor.TenantId,
                        normalizedKey,
                        AuthenticatedIdentity.ReviewerDisplayName(actor),
                        cancellationToken: token);
                    return new IdempotencyOperationOutcome<CompanyOnboardingOutcome>(
                        outcome,
                        "Company",
                        company.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        StatusCodes.Status201Created);
                },
                cancellationToken);
            return new CompanyOnboardingResult(
                execution.Result,
                execution.WasReplay,
                execution.RecordId,
                execution.ExpiresAtUtc,
                execution.HttpStatusCode);
        }
        catch (IdempotencyConflictException ex)
        {
            throw new CompanyOnboardingIdempotencyConflictException(ex.Message);
        }
    }

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

}
