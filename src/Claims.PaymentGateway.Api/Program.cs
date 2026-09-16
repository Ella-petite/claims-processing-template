using Claims.Contracts.Models;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var payments = new ConcurrentDictionary<string, PaymentRecord>();

app.MapPost("/api/payments", (PaymentRequest request) =>
{
    var existing = payments.Values.FirstOrDefault(x => x.IdempotencyKey == request.IdempotencyKey);
    if (existing is not null) return Results.Ok(new PaymentResponse(existing.PaymentReference, existing.Status, existing.ProviderReference));

    var paymentReference = $"PAY-{Guid.NewGuid():N}";
    var providerReference = $"PROV-{Random.Shared.Next(100000, 999999)}";
    var record = new PaymentRecord(request.ClaimId, request.Amount, request.IdempotencyKey, paymentReference, providerReference, "pending");
    payments[paymentReference] = record;
    return Results.Accepted($"/api/payments/{paymentReference}", new PaymentResponse(paymentReference, record.Status, providerReference));
});

app.MapGet("/api/payments/{reference}", (string reference) =>
    payments.TryGetValue(reference, out var payment)
        ? Results.Ok(new PaymentResponse(payment.PaymentReference, payment.Status, payment.ProviderReference))
        : Results.NotFound());

// Dummy third-party callback simulator. In production this would be a signed provider webhook.
app.MapPost("/api/payments/{reference}/complete", (string reference) =>
{
    if (!payments.TryGetValue(reference, out var payment)) return Results.NotFound();
    payments[reference] = payment with { Status = "completed" };
    return Results.Ok(new PaymentCompletedEvent(payment.ClaimId, payment.PaymentReference, "completed", DateTimeOffset.UtcNow));
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();

public sealed record PaymentRecord(Guid ClaimId, decimal Amount, string IdempotencyKey, string PaymentReference, string ProviderReference, string Status);
