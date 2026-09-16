using System.Text.Json;
using Azure.Messaging.ServiceBus;

namespace Claims.Api.Services;

public sealed class ServiceBusEventPublisher(IConfiguration configuration, ILogger<ServiceBusEventPublisher> logger) : IEventPublisher, IAsyncDisposable
{
    private readonly ServiceBusClient? _client = CreateClient(configuration);
    private readonly string _queueName = configuration["ServiceBus:QueueName"] ?? "claims";

    private static ServiceBusClient? CreateClient(IConfiguration configuration)
    {
        var ns = configuration["ServiceBus:FullyQualifiedNamespace"];
        return string.IsNullOrWhiteSpace(ns) ? null : new ServiceBusClient(ns);
    }

    public async Task PublishAsync<T>(string subject, T payload, CancellationToken cancellationToken = default)
    {
        if (_client is null)
        {
            logger.LogInformation("[LOCAL MODE] Event {Subject}: {Payload}", subject, JsonSerializer.Serialize(payload));
            return;
        }

        await using var sender = _client.CreateSender(_queueName);
        var message = new ServiceBusMessage(JsonSerializer.Serialize(payload))
        {
            Subject = subject,
            ContentType = "application/json",
            MessageId = Guid.NewGuid().ToString()
        };
        await sender.SendMessageAsync(message, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
    }
}
