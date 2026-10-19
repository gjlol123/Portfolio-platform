namespace Portfolio.Infrastructure.Outbox;

public sealed record ClaimedOutboxMessage(
    Guid Id,
    Guid LockToken,
    string MessageType,
    string Payload,
    DateTimeOffset OccurredAtUtc,
    int AttemptCount);