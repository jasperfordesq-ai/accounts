namespace Accounts.Api.Entities;

public class CompanyOfficer
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public required string Name { get; set; }
    public OfficerRole Role { get; set; }
    public DateOnly? AppointedDate { get; set; }
    public DateOnly? ResignedDate { get; set; }
    public string? Address { get; set; }

    // Navigation
    public Company Company { get; set; } = null!;
}
