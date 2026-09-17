using System.Collections.Concurrent;
using System.Net.Http.Json;
using Claims.Contracts.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
builder.Services.AddSingleton<PaymentStore>();
var app = builder.Build();

app.MapPost("/api/payments", async (PaymentRequest request, PaymentStore store, IConfiguration config, IHttpClientFactory clients, CancellationToken ct) =>
{
    if (request.Amount <= 0) return Results.BadRequest(new { error = "Payment amount must be positive." });
    if (store.TryGetByIdempotency(request.IdempotencyKey, out var existing)) return Results.Ok(existing.ToResponse());

    var gatewayReference = $"GW-{Guid.NewGuid():N}"[..15];
    var callbackBaseUrl = config["PublicBaseUrl"] ?? "http://localhost:5104";
    var callbackUrl = $"{callbackBaseUrl.TrimEnd('/')}/api/provider/callback";
    var providerBaseUrl = config["Provider:BaseUrl"] ?? "http://localhost:5112";

    var providerRequest = new ProviderPaymentRequest(request.ClaimId, request.Amount, request.Currency, gatewayReference, callbackUrl);
    var providerResponse = await clients.CreateClient().PostAsJsonAsync($"{providerBaseUrl}/api/provider/payments", providerRequest, ct);
    providerResponse.EnsureSuccessStatusCode();
    var providerPayment = await providerResponse.Content.ReadFromJsonAsync<ProviderPayment>(cancellationToken: ct)
        ?? throw new InvalidOperationException("Payment provider returned no payment.");

    var payment = new PaymentRecord(request.ClaimId, request.Amount, request.IdempotencyKey, gatewayReference, providerPayment.ProviderReference,
        providerPayment.Status, request.WorkflowInstanceId);
    store.Add(payment);
    return Results.Accepted($"/api/payments/{payment.PaymentReference}", payment.ToResponse());
});

app.MapPost("/api/provider/callback", async (PaymentProviderCallback callback, PaymentStore store, IHttpClientFactory clients, IConfiguration config, CancellationToken ct) =>
{
    if (!store.TryGet(callback.GatewayReference, out var payment)) return Results.NotFound(new { error = "Gateway payment reference not found." });
    var updated = payment with { Status = callback.Status, CompletedAt = callback.CompletedAt };
    store.Update(updated);

    if (!string.IsNullOrWhiteSpace(updated.WorkflowInstanceId))
    {
        var workflowBaseUrl = config["Workflow:BaseUrl"] ?? "http://localhost:5103";
        await clients.CreateClient().PostAsJsonAsync(
            $"{workflowBaseUrl}/api/workflows/{updated.WorkflowInstanceId}/payment-completed",
            new PaymentCompletedEvent(updated.ClaimId, updated.PaymentReference, updated.Status, callback.CompletedAt, callback.ProviderReference), ct);
    }
    return Results.Ok(updated.ToResponse());
});

app.MapGet("/api/payments/{reference}", (string reference, PaymentStore store) =>
    store.TryGet(reference, out var payment) ? Results.Ok(payment.ToResponse()) : Results.NotFound());

app.MapPost("/api/payments/{reference}/complete", async (string reference, PaymentStore store, IConfiguration config, IHttpClientFactory clients, CancellationToken ct) =>
{
    if (!store.TryGet(reference, out var payment)) return Results.NotFound();
    if (payment.Status.Equals("completed", StringComparison.OrdinalIgnoreCase)) return Results.Ok(payment.ToResponse());
    var providerBaseUrl = config["Provider:BaseUrl"] ?? "http://localhost:5112";
    var providerResult = await clients.CreateClient().PostAsync($"{providerBaseUrl}/api/provider/payments/{payment.ProviderReference}/complete", null, ct);
    if (!providerResult.IsSuccessStatusCode) return Results.StatusCode((int)providerResult.StatusCode);
    return Results.Accepted();
});

app.MapGet("/api/mock/provider/payments", (PaymentStore store) => Results.Ok(store.All().Select(x => new
{
    x.PaymentReference, x.ProviderReference, x.ClaimId, x.Amount, x.Status
})));
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "Payment Gateway" }));
app.MapGet("/", () => Results.Ok(new { service = "Claims.PaymentGateway.Api", role = "Payment abstraction / integration boundary" }));
app.Run();

public sealed record ProviderPaymentRequest(Guid ClaimId, decimal Amount, string Currency, string GatewayReference, string CallbackUrl);
public sealed record PaymentProviderCallback(string ProviderReference, string GatewayReference, Guid ClaimId, string Status, DateTimeOffset CompletedAt);
public sealed record ProviderPayment(string ProviderReference, string GatewayReference, Guid ClaimId, decimal Amount, string Status, DateTimeOffset CreatedAt, string CallbackUrl, DateTimeOffset? CompletedAt = null);

public sealed record PaymentRecord(Guid ClaimId, decimal Amount, string IdempotencyKey, string PaymentReference, string ProviderReference, string Status, string? WorkflowInstanceId, DateTimeOffset? CompletedAt = null)
{
    public PaymentResponse ToResponse() => new(PaymentReference, Status, ProviderReference, ClaimId, Amount);
}

public sealed class PaymentStore
{
    private readonly ConcurrentDictionary<string, PaymentRecord> payments = new();
    public void Add(PaymentRecord record) => payments[record.PaymentReference] = record;
    public void Update(PaymentRecord record) => payments[record.PaymentReference] = record;
    public bool TryGet(string reference, out PaymentRecord record) => payments.TryGetValue(reference, out record!);
    public IEnumerable<PaymentRecord> All() => payments.Values.OrderByDescending(x => x.CompletedAt ?? DateTimeOffset.MinValue);
    public bool TryGetByIdempotency(string key, out PaymentRecord record)
    {
        record = payments.Values.FirstOrDefault(x => x.IdempotencyKey == key)!;
        return record is not null;
    }
}
