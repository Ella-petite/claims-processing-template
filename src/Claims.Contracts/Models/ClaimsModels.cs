namespace Claims.Contracts.Models;

public enum ClaimStatus
{
    Submitted,
    ValidatingCustomer,
    ValidatingPolicy,
    CheckingRules,
    CheckingFraud,
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
    string ClaimReference,
    string ClientId,
    string PolicyId,
    string ClaimType,
    decimal Amount,
    DateTime IncidentDate,
    string Description,
    ClaimStatus Status,
    string? PaymentReference,
    string? WorkflowInstanceId,
    IReadOnlyCollection<ClaimHistoryDto> History,
    IReadOnlyCollection<ClaimDocumentDto> Documents);

public sealed record ClaimHistoryDto(DateTimeOffset At, ClaimStatus Status, string Source, string? Note);

public sealed record ClaimDocumentDto(
    Guid Id,
    string FileName,
    string ContentType,
    long Size,
    DateTimeOffset UploadedAt,
    string ProcessingStatus,
    IReadOnlyDictionary<string, string> ExtractedFields);

public sealed record ClaimSubmittedEvent(
    Guid ClaimId,
    string ClientId,
    string PolicyId,
    string ClaimType,
    decimal Amount,
    DateTimeOffset SubmittedAt,
    string CorrelationId);

public sealed record ClaimUpdatedEvent(
    Guid ClaimId,
    string ClaimReference,
    ClaimStatus Status,
    string Source,
    string? Note,
    DateTimeOffset At,
    string CorrelationId,
    string? PaymentReference = null);

public sealed record ValidationResult(bool Success, string? Reason = null);

public sealed record ClientValidationResult(
    bool Success,
    string? ClientName,
    string? ClientTier,
    string? Reason = null);

public sealed record PolicyValidationResult(
    bool Success,
    string? PolicyNumber,
    string? Plan,
    decimal CoverageLimit,
    decimal Excess,
    string? Reason = null);

public sealed record RulesResult(
    bool Passed,
    bool RequiresManualReview,
    string? RuleTriggered,
    string? Reason = null);

public sealed record FraudResult(
    bool Passed,
    int RiskScore,
    bool RequiresManualReview,
    string? Reason = null);

public sealed record PaymentRequest(
    Guid ClaimId,
    decimal Amount,
    string Currency,
    string IdempotencyKey,
    string WorkflowInstanceId);

public sealed record PaymentResponse(
    string PaymentReference,
    string Status,
    string ProviderReference,
    Guid ClaimId,
    decimal Amount);

public sealed record PaymentCompletedEvent(
    Guid ClaimId,
    string PaymentReference,
    string Status,
    DateTimeOffset CompletedAt,
    string ProviderReference);

public sealed record NotificationEvent(
    Guid ClaimId,
    string ClaimReference,
    string NotificationType,
    string Message,
    DateTimeOffset At);

public sealed record DocumentExtractionResult(
    bool Success,
    string DocumentType,
    IReadOnlyDictionary<string, string> ExtractedFields,
    string? Reason = null);

public sealed record AnalystDecisionRequest(bool Approved, string? Note = null);

public sealed record ClientReferenceDto(
    string ClientId,
    string ClientName,
    string Tier);

public sealed record PolicyReferenceDto(
    string PolicyId,
    string Number,
    string Plan,
    decimal CoverageLimit,
    decimal Excess);

public sealed record ReferenceDataDto(
    IReadOnlyCollection<ClientReferenceDto> Clients,
    IReadOnlyCollection<PolicyReferenceDto> Policies);
