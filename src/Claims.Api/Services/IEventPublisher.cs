namespace Claims.Api.Services;

public interface IEventPublisher
{
    Task PublishAsync<T>(string subject, T payload, CancellationToken cancellationToken = default);
}
