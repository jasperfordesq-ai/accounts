using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public class CharityReportingService(AccountsDbContext db)
{
    /// <summary>
    /// Determine SORP tier based on gross income.
    /// Tier 1: ≤ €500k, Tier 2: €500k-€15m, Tier 3: > €15m
    /// </summary>
    public static int DetermineSorpTier(decimal grossIncome) =>
        grossIncome switch
        {
            <= 500_000m => 1,
            <= 15_000_000m => 2,
            _ => 3
        };

    /// <summary>
    /// Generate Statement of Financial Activities (SoFA) data.
    /// Replaces P&L for charities with fund accounting columns.
    /// </summary>
    public async Task<SofaData> GenerateSofaAsync(int companyId, int periodId)
    {
        var periodExists = await db.AccountingPeriods
            .AsNoTracking()
            .AnyAsync(p => p.Id == periodId && p.CompanyId == companyId);
        if (!periodExists)
            throw new ResourceNotFoundException($"Period {periodId} not found");

        var funds = await db.FundBalances
            .Where(f => f.PeriodId == periodId)
            .OrderBy(f => f.FundType).ThenBy(f => f.FundName)
            .ToListAsync();

        var unrestricted = funds.Where(f => f.FundType == "Unrestricted" || f.FundType == "Designated").ToList();
        var restricted = funds.Where(f => f.FundType == "Restricted").ToList();
        var endowment = funds.Where(f => f.FundType == "Endowment").ToList();

        return new SofaData(
            UnrestrictedFunds: unrestricted.Select(f => new FundLine(f.FundName, f.FundType, f.OpeningBalance, f.IncomingResources, f.ResourcesExpended, f.Transfers, f.GainsLosses, f.ClosingBalance)).ToList(),
            RestrictedFunds: restricted.Select(f => new FundLine(f.FundName, f.FundType, f.OpeningBalance, f.IncomingResources, f.ResourcesExpended, f.Transfers, f.GainsLosses, f.ClosingBalance)).ToList(),
            EndowmentFunds: endowment.Select(f => new FundLine(f.FundName, f.FundType, f.OpeningBalance, f.IncomingResources, f.ResourcesExpended, f.Transfers, f.GainsLosses, f.ClosingBalance)).ToList(),
            TotalIncoming: funds.Sum(f => f.IncomingResources),
            TotalExpended: funds.Sum(f => f.ResourcesExpended),
            TotalTransfers: funds.Sum(f => f.Transfers),
            TotalGainsLosses: funds.Sum(f => f.GainsLosses),
            NetMovement: funds.Sum(f => f.IncomingResources - f.ResourcesExpended + f.Transfers + f.GainsLosses),
            TotalOpeningFunds: funds.Sum(f => f.OpeningBalance),
            TotalClosingFunds: funds.Sum(f => f.ClosingBalance)
        );
    }

    /// <summary>
    /// Generate Trustees' Annual Report (TAR) data.
    /// </summary>
    public async Task<TrusteesReportData> GenerateTarAsync(int companyId, int periodId)
    {
        var period = await db.AccountingPeriods
            .Include(p => p.Company).ThenInclude(c => c.Officers)
            .Include(p => p.Company).ThenInclude(c => c.CharityInfo)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        var company = period.Company;
        var charityInfo = company.CharityInfo;
        var trustees = company.Officers.Where(o => o.Role == OfficerRole.Director).Select(o => o.Name).ToList();

        var sofa = await GenerateSofaAsync(companyId, periodId);

        return new TrusteesReportData(
            CharityName: company.LegalName,
            CharityNumber: charityInfo?.CharityNumber ?? "",
            CroNumber: company.CroNumber ?? "",
            PeriodStart: period.PeriodStart.ToString("dd MMMM yyyy"),
            PeriodEnd: period.PeriodEnd.ToString("dd MMMM yyyy"),
            TrusteeNames: trustees,
            CharitableObjectives: charityInfo?.CharitableObjectives ?? "Not specified",
            PrincipalActivities: charityInfo?.PrincipalActivities ?? "Not specified",
            TotalIncome: sofa.TotalIncoming,
            TotalExpenditure: sofa.TotalExpended,
            NetMovement: sofa.NetMovement,
            ClosingFunds: sofa.TotalClosingFunds,
            GovernanceCodeCompliant: charityInfo?.GovernanceCodeCompliant ?? true,
            GovernanceCodeNote: charityInfo?.GovernanceCodeNote,
            TrusteeRemunerationPaid: charityInfo?.TrusteeRemunerationPaid ?? false,
            TrusteeRemunerationAmount: charityInfo?.TrusteeRemunerationAmount ?? 0,
            TrusteeExpensesDetails: charityInfo?.TrusteeExpensesDetails,
            HasInternationalTransfers: charityInfo?.HasInternationalTransfers ?? false,
            InternationalTransferDetails: charityInfo?.InternationalTransferDetails,
            SorpTier: charityInfo?.SorpTier ?? 1,
            FilingDeadline: period.PeriodEnd.AddMonths(10).ToString("dd MMMM yyyy")
        );
    }

    /// <summary>
    /// Save or update charity info for a company.
    /// </summary>
    public async Task<CharityInfo> SaveCharityInfoAsync(int companyId, CharityInfo input)
    {
        var existing = await db.CharityInfos.FirstOrDefaultAsync(c => c.CompanyId == companyId);
        if (existing == null)
        {
            input.CompanyId = companyId;
            input.SorpTier = DetermineSorpTier(input.GrossIncome);
            db.CharityInfos.Add(input);
            existing = input;
        }
        else
        {
            existing.CharityNumber = input.CharityNumber;
            existing.CharityType = input.CharityType;
            existing.GrossIncome = input.GrossIncome;
            existing.SorpTier = DetermineSorpTier(input.GrossIncome);
            existing.CharitableObjectives = input.CharitableObjectives;
            existing.PrincipalActivities = input.PrincipalActivities;
            existing.GovernanceCodeCompliant = input.GovernanceCodeCompliant;
            existing.GovernanceCodeNote = input.GovernanceCodeNote;
            existing.HasInternationalTransfers = input.HasInternationalTransfers;
            existing.InternationalTransferDetails = input.InternationalTransferDetails;
            existing.TrusteeRemunerationPaid = input.TrusteeRemunerationPaid;
            existing.TrusteeRemunerationAmount = input.TrusteeRemunerationAmount;
            existing.TrusteeExpensesDetails = input.TrusteeExpensesDetails;
        }
        await db.SaveChangesAsync();
        return existing;
    }
}

public record FundLine(string FundName, string FundType, decimal OpeningBalance, decimal IncomingResources, decimal ResourcesExpended, decimal Transfers, decimal GainsLosses, decimal ClosingBalance);

public record SofaData(
    List<FundLine> UnrestrictedFunds,
    List<FundLine> RestrictedFunds,
    List<FundLine> EndowmentFunds,
    decimal TotalIncoming,
    decimal TotalExpended,
    decimal TotalTransfers,
    decimal TotalGainsLosses,
    decimal NetMovement,
    decimal TotalOpeningFunds,
    decimal TotalClosingFunds
);

public record TrusteesReportData(
    string CharityName,
    string CharityNumber,
    string CroNumber,
    string PeriodStart,
    string PeriodEnd,
    List<string> TrusteeNames,
    string CharitableObjectives,
    string PrincipalActivities,
    decimal TotalIncome,
    decimal TotalExpenditure,
    decimal NetMovement,
    decimal ClosingFunds,
    bool GovernanceCodeCompliant,
    string? GovernanceCodeNote,
    bool TrusteeRemunerationPaid,
    decimal TrusteeRemunerationAmount,
    string? TrusteeExpensesDetails,
    bool HasInternationalTransfers,
    string? InternationalTransferDetails,
    int SorpTier,
    string FilingDeadline
);
