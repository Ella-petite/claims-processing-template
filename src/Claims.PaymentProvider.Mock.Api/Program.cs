using System.Collections.Concurrent;
using Claims.Contracts.Models;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
var app = builder.Build();
var store = new ProviderPaymentStore();
app.MapPost("/api/provider/payments", async (ProviderPaymentRequest request, IConfiguration config, IHttpClientFactory clients) =>
{
    var existing = store.GetByGatewayReference(request.GatewayReference);
    if (existing is not null) return Results.Ok(existing);
    var payment = new ProviderPayment(
        Guid.NewGuid().ToString("N"), request.GatewayReference, request.ClaimId, request.Amount,
        "pending", DateTimeOffset.UtcNow, request.CallbackUrl);
    store.Add(payment);
    var autoComplete = !bool.TryParse(config["Demo:AutoCompletePayments"], out var enabled) || enabled;
    if (autoComplete)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(4));
            await CompleteAsync(payment.ProviderReference, store, clients, CancellationToken.None);
        });
    }
    return Results.Accepted($"/api/provider/payments/{payment.ProviderReference}", payment);
});
app.MapGet("/api/provider/payments/{reference}", (string reference) =>
    store.TryGet(reference, out var payment) ? Results.Ok(payment) : Results.NotFound());
app.MapPost("/api/provider/payments/{reference}/complete", async (string reference, IHttpClientFactory clients, CancellationToken ct) =>
    await CompleteAsync(reference, store, clients, ct) is { } result ? Results.Ok(result) : Results.NotFound());
app.MapGet("/api/provider/payments", () => Results.Ok(store.All()));
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "Payment Provider (Mock)" }));
app.Run();
static async Task<ProviderPayment?> CompleteAsync(string reference, ProviderPaymentStore store, IHttpClientFactory clients, CancellationToken ct)
{
    if (!store.TryGet(reference, out var payment)) return null;
    if (payment.Status.Equals("completed", StringComparison.OrdinalIgnoreCase)) return payment;
    var completed = payment with { Status = "completed", CompletedAt = DateTimeOffset.UtcNow };
    store.Update(completed);
    try { await clients.CreateClient().PostAsJsonAsync(completed.CallbackUrl, new PaymentProviderCallback(
        completed.ProviderReference, completed.GatewayReference, completed.ClaimId, "completed", completed.CompletedAt.Value), ct); }
    catch { /* Demo callback can be retried manually from the gateway. */ }
    return completed;
}
public sealed record ProviderPaymentRequest(Guid ClaimId, decimal Amount, string Currency, string GatewayReference, string CallbackUrl);
public sealed record PaymentProviderCallback(string ProviderReference, string GatewayReference, Guid ClaimId, string Status, DateTimeOffset CompletedAt);
public sealed record ProviderPayment(string ProviderReference, string GatewayReference, Guid ClaimId, decimal Amount, string Status, DateTimeOffset CreatedAt, string CallbackUrl, DateTimeOffset? CompletedAt = null);
public sealed class ProviderPaymentStore
{
    private readonly ConcurrentDictionary<string, ProviderPayment> items = new();
    public void Add(ProviderPayment item) => items[item.ProviderReference] = item;
    public void Update(ProviderPayment item) => items[item.ProviderReference] = item;
    public bool TryGet(string key, out ProviderPayment item) => items.TryGetValue(key, out item!);
    public ProviderPayment? GetByGatewayReference(string gatewayReference) => items.Values.FirstOrDefault(x => x.GatewayReference == gatewayReference);
    public IEnumerable<ProviderPayment> All() => items.Values.OrderByDescending(x => x.CreatedAt);
}
