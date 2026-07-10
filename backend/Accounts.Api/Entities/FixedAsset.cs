using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public class FixedAsset
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public required string Name { get; set; }
    public required string Category { get; set; }
    public decimal Cost { get; set; }
    public decimal ResidualValue { get; set; }
    public DateOnly AcquisitionDate { get; set; }
    public DateOnly? DisposalDate { get; set; }
    public decimal? DisposalProceeds { get; set; }
    public int UsefulLifeYears { get; set; }
    public DepreciationMethod DepreciationMethod { get; set; } = DepreciationMethod.StraightLine;

    // Corporation-tax treatment is never inferred from the accounting category. A named workflow
    // actor must select and evidence the treatment before automated wear-and-tear support is allowed.
    public CapitalAllowanceTreatment CapitalAllowanceTreatment { get; set; } = CapitalAllowanceTreatment.Unreviewed;
    public string? CapitalAllowanceEvidence { get; set; }
    public string? CapitalAllowanceReviewedBy { get; set; }
    public DateTime? CapitalAllowanceReviewedAtUtc { get; set; }

    // Navigation
    [JsonIgnore]
    public Company Company { get; set; } = null!;
    public List<DepreciationEntry> DepreciationEntries { get; set; } = [];
}
