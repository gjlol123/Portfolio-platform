using Microsoft.EntityFrameworkCore;
using Portfolio.Infrastructure.Entities;
using Portfolio.Infrastructure.Outbox;
using Portfolio.Infrastructure.Persistence;
using Xunit;

namespace Portfolio.Infrastructure.Tests;

public sealed class OutboxLeaseExpirationTests
{
    private static string GetConnectionString()
    {
        var connectionString = @"Server=SSNC-PF66AM5D;Database=PortfolioDb;User ID=sa;Password=Advent.sa;TrustServerCertificate=True";

        return connectionString;
    }

    private static PortfolioDbContext CreateDbContext(
        string connectionString)
    {
        var options =
            new DbContextOptionsBuilder<PortfolioDbContext>()
                .UseSqlServer(connectionString)
                .Options;

        return new PortfolioDbContext(options);
    }

    [Fact]
    public async Task ExpiredLease_OldWorkerCannotChangeMessageState()
    {
        var connectionString = GetConnectionString();

        var messageId = Guid.NewGuid();

        var workerAId = $"worker-A-{Guid.NewGuid():N}";
        var workerBId = $"worker-B-{Guid.NewGuid():N}";

        try
        {
            // Arrange: create one message in an isolated test queue.
            await using (var setup =
                CreateDbContext(connectionString))
            {
                await setup.Database.MigrateAsync();

                var hasUnfinishedMessages =
                    await setup.OutboxMessages.AnyAsync(x =>
                        x.ProcessedAtUtc == null &&
                        x.DeadLetteredAtUtc == null);

                Assert.False(
                    hasUnfinishedMessages,
                    "The test requires a dedicated queue with no " +
                    "other unfinished Outbox messages.");

                setup.OutboxMessages.Add(
                    new OutboxMessage(
                        messageId,
                        "LeaseExpirationTest",
                        """{"portfolioId":1001}""",
                        DateTimeOffset.UtcNow.AddMinutes(-1)));

                await setup.SaveChangesAsync();
            }

            // Act 1: Worker A claims the message.
            await using var dbA =
                CreateDbContext(connectionString);

            var serviceA = new OutboxClaimService(dbA);

            var batchA = await serviceA.ClaimBatchAsync(
                workerId: workerAId,
                batchSize: 1,
                leaseDuration: TimeSpan.FromMinutes(2),
                cancellationToken: CancellationToken.None);

            var firstClaim = Assert.Single(batchA);

            Assert.Equal(messageId, firstClaim.Id);
            Assert.NotEqual(Guid.Empty, firstClaim.LockToken);

            var lockTokenA = firstClaim.LockToken;

            // Act 2: simulate that Worker A's lease has expired.
            await using (var adminDb =
                CreateDbContext(connectionString))
            {
                var expiredAtUtc =
                    DateTimeOffset.UtcNow.AddSeconds(-10);

                var updatedRows = await adminDb.OutboxMessages
                    .Where(x =>
                        x.Id == messageId &&
                        x.LockToken == lockTokenA)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(
                            x => x.LockedUntilUtc,
                            (DateTimeOffset?)expiredAtUtc));

                Assert.Equal(1, updatedRows);
            }

            // Act 3: Worker B reclaims the expired message.
            await using var dbB =
                CreateDbContext(connectionString);

            var serviceB = new OutboxClaimService(dbB);

            var batchB = await serviceB.ClaimBatchAsync(
                workerId: workerBId,
                batchSize: 1,
                leaseDuration: TimeSpan.FromMinutes(2),
                cancellationToken: CancellationToken.None);

            var secondClaim = Assert.Single(batchB);

            var lockTokenB = secondClaim.LockToken;

            Assert.Equal(messageId, secondClaim.Id);
            Assert.NotEqual(Guid.Empty, lockTokenB);
            Assert.NotEqual(lockTokenA, lockTokenB);

            // Verify that the database now records Worker B's claim.
            await using (var verifyClaimDb =
                CreateDbContext(connectionString))
            {
                var saved = await verifyClaimDb.OutboxMessages
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == messageId);

                Assert.Equal(workerBId, saved.LockedBy);
                Assert.Equal(lockTokenB, saved.LockToken);
                Assert.Null(saved.ProcessedAtUtc);
                Assert.Equal(0, saved.AttemptCount);
            }

            // Act 4: Worker A attempts to record a failure using
            // its stale lock token. The update must affect no rows.
            var oldFailureRecorded =
                await serviceA.RecordFailureAsync(
                    messageId,
                    lockTokenA,
                    firstClaim.AttemptCount,
                    "Failure reported by stale Worker A",
                    CancellationToken.None);

            Assert.False(oldFailureRecorded);

            // Worker A also attempts to mark the message processed.
            var oldCompletionRecorded =
                await serviceA.MarkProcessedAsync(
                    messageId,
                    lockTokenA,
                    CancellationToken.None);

            Assert.False(oldCompletionRecorded);

            // Assert: Worker A must not have modified Worker B's claim.
            await using (var verifyStaleWorkerDb =
                CreateDbContext(connectionString))
            {
                var saved = await verifyStaleWorkerDb.OutboxMessages
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == messageId);

                Assert.Equal(workerBId, saved.LockedBy);
                Assert.Equal(lockTokenB, saved.LockToken);

                Assert.Equal(0, saved.AttemptCount);
                Assert.Null(saved.LastError);
                Assert.Null(saved.ProcessedAtUtc);
            }

            // Act 5: Worker B completes the message with its current token.
            var completed = await serviceB.MarkProcessedAsync(
                messageId,
                lockTokenB,
                CancellationToken.None);

            Assert.True(completed);

            // Assert the final persisted state through a fresh DbContext.
            await using (var finalDb =
                CreateDbContext(connectionString))
            {
                var saved = await finalDb.OutboxMessages
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == messageId);

                Assert.NotNull(saved.ProcessedAtUtc);

                Assert.Null(saved.LockedBy);
                Assert.Null(saved.LockToken);
                Assert.Null(saved.LockedUntilUtc);

                Assert.Equal(0, saved.AttemptCount);
                Assert.Null(saved.LastError);
            }
        }
        finally
        {
            // Remove only the message created by this test.
            await using var cleanup =
                CreateDbContext(connectionString);

            await cleanup.OutboxMessages
                .Where(x => x.Id == messageId)
                .ExecuteDeleteAsync();
        }
    }
}