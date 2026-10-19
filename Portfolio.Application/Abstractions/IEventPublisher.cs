namespace Portfolio.Application.Abstractions;

public interface IEventPublisher
{
    Task PublishAsync(
        Guid eventId,
        string eventType,
        string payload,
        CancellationToken cancellationToken);
}