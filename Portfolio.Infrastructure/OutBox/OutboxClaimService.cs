using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.Infrastructure.Outbox;

public sealed class OutboxClaimService
{
    private const int MaxAttempts = 5;

    private const int MaxErrorLength = 2000;

    private readonly PortfolioDbContext _dbContext;

    public OutboxClaimService(PortfolioDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> RecordFailureAsync(
        Guid messageId,
        Guid lockToken,
        int attemptCount,
        string error,
        CancellationToken cancellationToken)
    {
        if (attemptCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attemptCount));
        }

        ArgumentNullException.ThrowIfNull(error);

        var now = DateTimeOffset.UtcNow;

        // The count supplied by the Worker represents previous failures.
        var nextAttemptCount = checked(attemptCount + 1);

        var isDeadLettered =
            nextAttemptCount >= MaxAttempts;

        var delaySeconds = Math.Min(
            300,
            Math.Pow(2, Math.Min(nextAttemptCount, 8)));

        var nextAttemptAtUtc = now.AddSeconds(delaySeconds);

        var safeError = error.Length <= MaxErrorLength
            ? error
            : error[..MaxErrorLength];

        var deadLetteredAtUtc = isDeadLettered
            ? now
            : (DateTimeOffset?)null;

        var updatedRows = await _dbContext.OutboxMessages
            .Where(x =>
                x.Id == messageId &&
                x.LockToken == lockToken &&
                x.AttemptCount == attemptCount &&
                x.ProcessedAtUtc == null &&
                x.DeadLetteredAtUtc == null &&
                x.LockedUntilUtc > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        x => x.AttemptCount,
                        nextAttemptCount)
                    .SetProperty(
                        x => x.LastError,
                        safeError)
                    .SetProperty(
                        x => x.NextAttemptAtUtc,
                        nextAttemptAtUtc)
                    .SetProperty(
                        x => x.DeadLetteredAtUtc,
                        deadLetteredAtUtc)
                    .SetProperty(
                        x => x.LockedBy,
                        (string?)null)
                    .SetProperty(
                        x => x.LockToken,
                        (Guid?)null)
                    .SetProperty(
                        x => x.LockedUntilUtc,
                        (DateTimeOffset?)null),
                cancellationToken);

        return updatedRows == 1;
    }

    public async Task<bool> MarkProcessedAsync(
        Guid messageId,
        Guid lockToken,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var updatedRows = await _dbContext.OutboxMessages
            .Where(x =>
                x.Id == messageId &&
                x.LockToken == lockToken &&
                x.ProcessedAtUtc == null &&
                x.DeadLetteredAtUtc == null &&
                x.LockedUntilUtc > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        x => x.ProcessedAtUtc,
                        (DateTimeOffset?)now)
                    .SetProperty(
                        x => x.NextAttemptAtUtc,
                        (DateTimeOffset?)null)
                    .SetProperty(
                        x => x.LockedBy,
                        (string?)null)
                    .SetProperty(
                        x => x.LockToken,
                        (Guid?)null)
                    .SetProperty(
                        x => x.LockedUntilUtc,
                        (DateTimeOffset?)null)
                    .SetProperty(
                        x => x.LastError,
                        (string?)null),
                cancellationToken);

        return updatedRows == 1;
    }

    public async Task<IReadOnlyList<ClaimedOutboxMessage>>
        ClaimBatchAsync(
            string workerId,
            int batchSize,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batchSize));
        }

        if (leaseDuration < TimeSpan.FromSeconds(1) ||
            leaseDuration.TotalSeconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseDuration));
        }

        var leaseSeconds =
            checked((int)Math.Ceiling(
                leaseDuration.TotalSeconds));

        var now = DateTimeOffset.UtcNow;

        var connection =
            _dbContext.Database.GetDbConnection();

        var shouldCloseConnection =
            connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await _dbContext.Database.OpenConnectionAsync(
                cancellationToken);
        }

        try
        {
            await using var command =
                connection.CreateCommand();

            command.CommandText = """
                ;WITH CandidateMessages AS
                (
                    SELECT TOP (@BatchSize) *
                    FROM dbo.OutboxMessages WITH
                        (UPDLOCK, READPAST, ROWLOCK, READCOMMITTEDLOCK)
                    WHERE ProcessedAtUtc IS NULL
                      AND DeadLetteredAtUtc IS NULL
                      AND
                      (
                          NextAttemptAtUtc IS NULL
                          OR NextAttemptAtUtc <= @NowUtc
                      )
                      AND
                      (
                          LockedUntilUtc IS NULL
                          OR LockedUntilUtc <= @NowUtc
                      )
                    ORDER BY OccurredAtUtc, Id
                )
                UPDATE CandidateMessages
                SET
                    LockedBy = @WorkerId,
                    LockToken = NEWID(),
                    LockedUntilUtc = DATEADD(
                        SECOND,
                        @LeaseSeconds,
                        @NowUtc)
                OUTPUT
                    inserted.Id,
                    inserted.LockToken,
                    inserted.MessageType,
                    inserted.Payload,
                    inserted.OccurredAtUtc,
                    inserted.AttemptCount;
                """;

            AddParameter(
                command,
                "@BatchSize",
                DbType.Int32,
                batchSize);

            AddParameter(
                command,
                "@WorkerId",
                DbType.String,
                workerId);

            AddParameter(
                command,
                "@LeaseSeconds",
                DbType.Int32,
                leaseSeconds);

            AddParameter(
                command,
                "@NowUtc",
                DbType.DateTimeOffset,
                now);

            var messages =
                new List<ClaimedOutboxMessage>();

            await using var reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            while (await reader.ReadAsync(
                cancellationToken))
            {
                messages.Add(
                    new ClaimedOutboxMessage(
                        Id: reader.GetGuid(0),
                        LockToken: reader.GetGuid(1),
                        MessageType: reader.GetString(2),
                        Payload: reader.GetString(3),
                        OccurredAtUtc:
                            reader.GetFieldValue<DateTimeOffset>(4),
                        AttemptCount: reader.GetInt32(5)));
            }

            return messages;
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await _dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        DbType dbType,
        object value)
    {
        var parameter = command.CreateParameter();

        parameter.ParameterName = name;
        parameter.DbType = dbType;
        parameter.Value = value;

        command.Parameters.Add(parameter);
    }
}