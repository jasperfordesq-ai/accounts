namespace Accounts.Api.Entities;

public class FixedAsset
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public required string Name { get; set; }
    public required string Category { get; set; }
    public decimal Cost { get; set; }
    public DateOnly AcquisitionDate { get; set; }
    public DateOnly? DisposalDate { get; set; }
    public decimal? DisposalProceeds { get; set; }
    public int UsefulLifeYears { get; set; }
    public DepreciationMethod DepreciationMethod { get; set; } = DepreciationMethod.StraightLine;

    // Navigation
    public Company Company { get; set; } = null!;
    public List<DepreciationEntry> DepreciationEntries { get; set; } = [];
}
