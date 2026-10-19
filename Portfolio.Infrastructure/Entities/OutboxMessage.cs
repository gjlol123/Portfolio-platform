using System;
using System.Collections.Generic;
using System.Text;

namespace Portfolio.Infrastructure.Entities
{
    public sealed class OutboxMessage
    {
        public Guid Id { get; private set; }
        public string MessageType { get; private set; } = "";
        public string Payload { get; private set; } = "";
        public DateTimeOffset OccurredAtUtc { get; private set; }
        public DateTimeOffset? DeadLetteredAtUtc { get; private set; }

        public DateTimeOffset? OccurredOnUtc { get; private set; }

        public DateTimeOffset? ProcessedOnUtc { get; private set; }


        public DateTimeOffset? ProcessedAtUtc { get; private set; }
        public int Attempts { get; private set; }
        public DateTimeOffset? NextAttemptAtUtc { get; private set; }
        public string? LastError { get; private set; }

        public Guid? LockToken { get; private set; }


        public DateTimeOffset? LockedUntilUtc { get; private set; }

        public int AttemptCount { get; private set; }

        public string? LockedBy { get; private set; }

        public OutboxMessage()
        {
        }

        public OutboxMessage(
            Guid id,
            string messageType,
            string payload,
            DateTimeOffset occurredAtUtc)
        {
            Id = id;
            MessageType = messageType;
            Payload = payload;
            OccurredAtUtc = occurredAtUtc;
        }

        public void MarkProcessed(DateTimeOffset processedAtUtc)
        {
            ProcessedAtUtc = processedAtUtc;
            LastError = null;
            NextAttemptAtUtc = null;
            ReleaseLock();
        }

        public void AcquireLock(
            string workerId,
            Guid lockToken,
            DateTimeOffset lockedUntilUtc)
        {
            LockedBy = workerId;
            LockToken = lockToken;
            LockedUntilUtc = lockedUntilUtc;
        }

        public void RecordFailure(
            string error,
            DateTimeOffset nextAttemptAtUtc)
        {
            AttemptCount++;
            LastError = error;
            NextAttemptAtUtc = nextAttemptAtUtc;
            ReleaseLock();
        }

        public void ReleaseLock()
        {
            LockedBy = null;
            LockToken = null;
            LockedUntilUtc = null;
        }
    }


}
