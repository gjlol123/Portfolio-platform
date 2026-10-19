using Portfolio.Application.Abstractions;

namespace Portfolio.Infrastructure.Tests;

public sealed class FakeEventPublisher : IEventPublisher
{
    public int CallCount { get; private set; }

    public Func<Guid, string, string, CancellationToken, Task>?
        OnPublish
    { get; set; }

    public Task PublishAsync(
        Guid messageId,
        string eventType,
        string payload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;

        return OnPublish?.Invoke(
            messageId,
            eventType,
            payload,
            cancellationToken) ?? Task.CompletedTask;
    }
}