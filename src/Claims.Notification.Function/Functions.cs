using Claims.Contracts.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace Claims.Notification.Function;

public sealed class NotificationFunctions(ILogger<NotificationFunctions> logger)
{
    [Function("SendNotification")]
    public async Task<HttpResponseData> SendNotification([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "notifications")] HttpRequestData req)
    {
        var notification = await req.ReadFromJsonAsync<NotificationEvent>();
        if (notification is null) return req.CreateResponse(HttpStatusCode.BadRequest);

        logger.LogInformation("[DUMMY EMAIL/SMS/IN-APP] {Type} | Claim: {ClaimId} | {Message}", notification.NotificationType, notification.ClaimId, notification.Message);
        return req.CreateResponse(HttpStatusCode.Accepted);
    }
}
