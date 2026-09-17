namespace Claims.Contracts.Models;

public class ClaimAuditFilter
{
    public string? Status { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}
