namespace Claims.Api.Services;

public interface IEventPublisher : IAsyncDisposable
{
    bool IsConfigured { get; }
    Task PublishSerializedAsync(string subject, string serializedPayload, CancellationToken cancellationToken = default);
}
