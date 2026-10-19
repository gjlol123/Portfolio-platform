using Portfolio.Application.Abstractions;

namespace Portfolio.Infrastructure.Tests;

public sealed class FailFirstThenSucceedEventPublisher(
    Guid targetMessageId) : IEventPublisher
{
    private int _attemptCount;

    public int AttemptCount => Volatile.Read(ref _attemptCount);

    public TaskCompletionSource<bool> SucceededOnSecondAttempt { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task PublishAsync(
        Guid messageId,
        string eventType,
        string payload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (messageId != targetMessageId)
        {
            return Task.FromException(
                new InvalidOperationException(
                    $"Unexpected MessageId: {messageId}"));
        }

        var attempt = Interlocked.Increment(ref _attemptCount);

        if (attempt == 1)
        {
            return Task.FromException(
                new InvalidOperationException(
                    "Simulated transient broker failure."));
        }

        if (attempt == 2)
        {
            SucceededOnSecondAttempt.TrySetResult(true);
        }

        return Task.CompletedTask;
    }
}