using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Claims.Api.Services;

public sealed class ServiceBusEventPublisher(IConfiguration configuration, ILogger<ServiceBusEventPublisher> logger) : IEventPublisher
{
    private readonly string _workflowQueue = configuration["ServiceBus:WorkflowQueue"] ?? "claim-workflow";
    private readonly string _notificationQueue = configuration["ServiceBus:NotificationQueue"] ?? "claim-notifications";
    private readonly ServiceBusClient? _client = CreateClient(configuration, logger);

    public bool IsConfigured => _client is not null;

    public async Task PublishSerializedAsync(string subject, string serializedPayload, CancellationToken cancellationToken = default)
    {
        if (_client is null)
        {
            logger.LogDebug("Service Bus is not configured. Outbox message remains pending: {Subject}", subject);
            return;
        }

        var queue = subject.Equals("claim.updated", StringComparison.OrdinalIgnoreCase)
            ? _notificationQueue
            : _workflowQueue;

        await using var sender = _client.CreateSender(queue);
        var message = new ServiceBusMessage(serializedPayload)
        {
            Subject = subject,
            ContentType = "application/json",
            MessageId = Guid.NewGuid().ToString("N")
        };
        message.ApplicationProperties["eventType"] = subject;
        await sender.SendMessageAsync(message, cancellationToken);
        logger.LogInformation("Published {Subject} to {Queue}.", subject, queue);
    }

    private static ServiceBusClient? CreateClient(IConfiguration configuration, ILogger logger)
    {
        var connectionString = configuration["ServiceBus:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning("Service Bus connection is not configured. Outbox messages will remain pending until a connection is supplied.");
            return null;
        }

        return new ServiceBusClient(connectionString);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();
    }
}
