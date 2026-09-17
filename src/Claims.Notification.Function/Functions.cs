using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Claims.Contracts.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Claims.Notification.Function;

public sealed class NotificationFunctions(NotificationStore store, ILogger<NotificationFunctions> logger)
{
    [Function("ClaimUpdatedNotification")]
    public Task ProcessClaimUpdated(
        [ServiceBusTrigger("claim-notifications", Connection = "NotificationServiceBusConnection")] string body,
        CancellationToken ct)
    {
        var updated = JsonSerializer.Deserialize<ClaimUpdatedEvent>(body)
            ?? throw new InvalidOperationException("Invalid claim.updated message.");

        var notification = new NotificationEvent(
            updated.ClaimId,
            updated.ClaimReference,
            "ClaimUpdated",
            $"Claim {updated.ClaimReference} is now {updated.Status}.",
            updated.At);

        store.Add(notification);
        logger.LogInformation("[MOCK EMAIL/SMS/IN-APP] {Message}", notification.Message);
        return Task.CompletedTask;
    }

    [Function("NotificationHistory")]
    public async Task<HttpResponseData> GetHistory([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "notifications")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(store.All());
        return response;
    }

    [Function("NotificationHealth")]
    public async Task<HttpResponseData> Health([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { status = "ok", service = "Claims.Notification.Function" });
        return response;
    }
}

public sealed class NotificationStore
{
    private readonly ConcurrentQueue<NotificationEvent> _items = new();
    public void Add(NotificationEvent notification) => _items.Enqueue(notification);
    public IReadOnlyCollection<NotificationEvent> All() => _items.Reverse().ToArray();
}
