namespace Claims.Contracts.Models;

public class ClaimAuditDto
{
    public Guid ClaimId { get; set; }
    public string ClaimReference { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string? WorkflowInstanceId { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    public List<ClaimHistoryDto> History { get; set; } = new();
}

public class ClaimHistoryDto
{
    public DateTimeOffset At { get; set; }
    public string Status { get; set; } = "";
    public string Source { get; set; } = "";
    public string? Note { get; set; }
    public string CorrelationId { get; set; } = "";
}
