namespace Claims.Contracts.Models;

public enum ClaimStatus
{
    Submitted,
    ValidatingCustomer,
    ValidatingPolicy,
    UnderReview,
    Approved,
    PaymentPending,
    Paid,
    Rejected,
    Failed
}

public sealed record CreateClaimRequest(
    string ClientId,
    string PolicyId,
    string ClaimType,
    decimal Amount,
    DateTime IncidentDate,
    string Description,
    IReadOnlyCollection<string>? DocumentReferences = null);

public sealed record ClaimDto(
    Guid Id,
    string ClientId,
    string PolicyId,
    string ClaimType,
    decimal Amount,
    DateTime IncidentDate,
    string Description,
    ClaimStatus Status,
    string? PaymentReference,
    IReadOnlyCollection<ClaimHistoryDto> History,
    IReadOnlyCollection<string> DocumentReferences,
    string ClaimReference = "");

public sealed record ClaimHistoryDto(DateTimeOffset At, ClaimStatus Status, string Source, string? Note);
public sealed record ClaimSubmittedEvent(Guid ClaimId, string ClientId, string PolicyId, string ClaimType, decimal Amount, DateTimeOffset SubmittedAt);
public sealed record ValidationResult(bool Success, string? Reason = null);
public sealed record FraudResult(bool Passed, int RiskScore, bool RequiresManualReview, string? Reason = null);
public sealed record PaymentRequest(Guid ClaimId, decimal Amount, string Currency, string IdempotencyKey);
public sealed record PaymentResponse(string PaymentReference, string Status, string ProviderReference);
public sealed record PaymentCompletedEvent(Guid ClaimId, string PaymentReference, string Status, DateTimeOffset CompletedAt);
public sealed record NotificationEvent(Guid ClaimId, string NotificationType, string Message);
public sealed record WorkflowContextInput(Guid ClaimId, string ClientId, string PolicyId, string ClaimType, decimal Amount);
