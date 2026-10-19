using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Portfolio.Application.Abstractions;
using Portfolio.Infrastructure.Background;
using Portfolio.Infrastructure.Entities;
using Portfolio.Infrastructure.Outbox;
using Portfolio.Infrastructure.Persistence;
using Xunit;

namespace Portfolio.Infrastructure.Tests;

public sealed class OutboxRetryAndDeadLetterTests
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

    private static async Task AddMessageAsync(
        string connectionString,
        Guid messageId,
        string messageType)
    {
        await using var db =
            CreateDbContext(connectionString);

        await db.Database.MigrateAsync();

        var hasPendingMessages =
            await db.OutboxMessages.AnyAsync(x =>
                x.ProcessedAtUtc == null &&
                x.DeadLetteredAtUtc == null);

        Assert.False(
            hasPendingMessages,
            "Use an isolated test database with no unfinished Outbox messages. " +
            "Stop the API Worker before running these tests.");

        db.OutboxMessages.Add(
            new OutboxMessage(
                messageId,
                messageType,
                """{"portfolioId":1001}""",
                DateTimeOffset.UtcNow.AddDays(-1)));

        await db.SaveChangesAsync();
    }

    private static async Task DeleteMessageAsync(
        string connectionString,
        Guid messageId)
    {
        await using var db =
            CreateDbContext(connectionString);

        await db.OutboxMessages
            .Where(x => x.Id == messageId)
            .ExecuteDeleteAsync();
    }

    private static async Task<OutboxMessage> WaitForProcessedAsync(
        string connectionString,
        Guid messageId,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var db =
                CreateDbContext(connectionString);

            var message = await db.OutboxMessages
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == messageId);

            if (message?.ProcessedAtUtc is not null)
            {
                return message;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException(
            $"Outbox message {messageId} was not processed in time.");
    }

    [Fact]
    public async Task RepeatedFailures_IncreaseAttempts_AndDeadLetterMessage()
    {
        var connectionString = GetConnectionString();
        var messageId = Guid.NewGuid();

        try
        {
            await AddMessageAsync(
                connectionString,
                messageId,
                "RetryPolicyTest");

            for (var expectedAttempt = 1;
                 expectedAttempt <= 5;
                 expectedAttempt++)
            {
                // Simulate that the retry delay has elapsed.
                // For the first attempt, the new message is already eligible.
                if (expectedAttempt > 1)
                {
                    await using var adminDb =
                        CreateDbContext(connectionString);

                    var updatedRows = await adminDb.OutboxMessages
                        .Where(x => x.Id == messageId)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(
                                x => x.NextAttemptAtUtc,
                                (DateTimeOffset?)
                                    DateTimeOffset.UtcNow.AddSeconds(-1)));

                    Assert.Equal(1, updatedRows);
                }

                await using var attemptDb =
                    CreateDbContext(connectionString);

                var service =
                    new OutboxClaimService(attemptDb);

                var claimed = await service.ClaimBatchAsync(
                    workerId: $"retry-worker-{expectedAttempt}-{Guid.NewGuid():N}",
                    batchSize: 1,
                    leaseDuration: TimeSpan.FromMinutes(2),
                    cancellationToken: CancellationToken.None);

                var claim = Assert.Single(claimed);

                Assert.Equal(messageId, claim.Id);

                Assert.Equal(
                    expectedAttempt - 1,
                    claim.AttemptCount);

                var failureStartedAtUtc =
                    DateTimeOffset.UtcNow;

                var failureRecorded =
                    await service.RecordFailureAsync(
                        messageId,
                        claim.LockToken,
                        claim.AttemptCount,
                        $"Simulated failure #{expectedAttempt}",
                        CancellationToken.None);

                Assert.True(failureRecorded);

                // Verify the persisted database state.
                await using var verifyDb =
                    CreateDbContext(connectionString);

                var saved = await verifyDb.OutboxMessages
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == messageId);

                Assert.Equal(
                    expectedAttempt,
                    saved.AttemptCount);

                Assert.Equal(
                    $"Simulated failure #{expectedAttempt}",
                    saved.LastError);

                Assert.NotNull(saved.NextAttemptAtUtc);

                Assert.Null(saved.ProcessedAtUtc);
                Assert.Null(saved.LockedBy);
                Assert.Null(saved.LockToken);
                Assert.Null(saved.LockedUntilUtc);

                var expectedDelaySeconds =
                    Math.Pow(2, expectedAttempt);

                var actualDelaySeconds =
                    (saved.NextAttemptAtUtc!.Value -
                     failureStartedAtUtc).TotalSeconds;

                Assert.InRange(
                    actualDelaySeconds,
                    expectedDelaySeconds - 0.5,
                    expectedDelaySeconds + 2.0);

                Assert.Equal(
                    expectedAttempt == 5,
                    saved.DeadLetteredAtUtc is not null);
            }

            // A dead-letter message must remain excluded even if its
            // next-attempt time is moved into the past.
            await using (var adminDb =
                CreateDbContext(connectionString))
            {
                await adminDb.OutboxMessages
                    .Where(x => x.Id == messageId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(
                            x => x.NextAttemptAtUtc,
                            (DateTimeOffset?)
                                DateTimeOffset.UtcNow.AddSeconds(-1)));
            }

            await using var claimDb =
                CreateDbContext(connectionString);

            var postDeadLetterBatch =
                await new OutboxClaimService(claimDb)
                    .ClaimBatchAsync(
                        workerId: $"after-dead-letter-{Guid.NewGuid():N}",
                        batchSize: 10,
                        leaseDuration: TimeSpan.FromMinutes(2),
                        cancellationToken: CancellationToken.None);

            Assert.Empty(postDeadLetterBatch);
        }
        finally
        {
            await DeleteMessageAsync(
                connectionString,
                messageId);
        }
    }

    [Fact]
    public async Task OutboxWorker_RetriesAfterTransientFailure_ThenMarksProcessed()
    {
        var connectionString = GetConnectionString();
        var messageId = Guid.NewGuid();

        await AddMessageAsync(
            connectionString,
            messageId,
            "WorkerRetryTest");

        var publisher =
            new FailFirstThenSucceedEventPublisher(messageId);

        var services = new ServiceCollection();

        services.AddLogging();

        services.AddDbContext<PortfolioDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<OutboxClaimService>();

        services.AddSingleton<IEventPublisher>(publisher);

        await using var provider =
            services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateScopes = true,
                    ValidateOnBuild = true
                });

        var worker = new OutboxWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OutboxWorker>.Instance);

        var started = false;

        try
        {
            // Run the real Worker polling loop.
            await worker.StartAsync(CancellationToken.None);

            started = true;

            // The publisher completes this signal when attempt two starts
            // succeeding. The Worker still needs to save the final state.
            await publisher.SucceededOnSecondAttempt.Task
                .WaitAsync(TimeSpan.FromSeconds(20));

            var saved = await WaitForProcessedAsync(
                connectionString,
                messageId,
                TimeSpan.FromSeconds(5));

            Assert.Equal(2, publisher.AttemptCount);

            Assert.Equal(1, saved.AttemptCount);

            Assert.NotNull(saved.ProcessedAtUtc);

            Assert.Null(saved.LastError);
            Assert.Null(saved.NextAttemptAtUtc);

            Assert.Null(saved.LockedBy);
            Assert.Null(saved.LockToken);
            Assert.Null(saved.LockedUntilUtc);
        }
        finally
        {
            if (started)
            {
                await worker.StopAsync(CancellationToken.None);
            }

            await DeleteMessageAsync(
                connectionString,
                messageId);
        }
    }

    [Fact]
    public async Task ExpiredLease_CanBeReclaimedByFreshWorkerInstance()
    {
        var connectionString = GetConnectionString();
        var messageId = Guid.NewGuid();

        var workerAId = $"worker-A-{Guid.NewGuid():N}";
        var workerBId = $"worker-B-{Guid.NewGuid():N}";

        try
        {
            await AddMessageAsync(
                connectionString,
                messageId,
                "RestartRecoveryTest");

            Guid lockTokenA;

            // Worker A claims the message, then its context is disposed.
            await using (var dbA =
                CreateDbContext(connectionString))
            {
                var claimA = await new OutboxClaimService(dbA)
                    .ClaimBatchAsync(
                        workerAId,
                        batchSize: 1,
                        leaseDuration: TimeSpan.FromMinutes(2),
                        cancellationToken: CancellationToken.None);

                var firstClaim = Assert.Single(claimA);

                Assert.Equal(messageId, firstClaim.Id);

                lockTokenA = firstClaim.LockToken;
            }

            // Simulate the lease expiring while the application is down.
            await using (var adminDb =
                CreateDbContext(connectionString))
            {
                var updatedRows = await adminDb.OutboxMessages
                    .Where(x =>
                        x.Id == messageId &&
                        x.LockToken == lockTokenA)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(
                            x => x.LockedUntilUtc,
                            (DateTimeOffset?)
                                DateTimeOffset.UtcNow.AddSeconds(-10)));

                Assert.Equal(1, updatedRows);
            }

            // A fresh context and service represent a new Worker instance.
            await using var dbB =
                CreateDbContext(connectionString);

            var claimB = await new OutboxClaimService(dbB)
                .ClaimBatchAsync(
                    workerBId,
                    batchSize: 1,
                    leaseDuration: TimeSpan.FromMinutes(2),
                    cancellationToken: CancellationToken.None);

            var secondClaim = Assert.Single(claimB);

            Assert.Equal(messageId, secondClaim.Id);

            Assert.NotEqual(
                lockTokenA,
                secondClaim.LockToken);

            await using var verifyDb =
                CreateDbContext(connectionString);

            var saved = await verifyDb.OutboxMessages
                .AsNoTracking()
                .SingleAsync(x => x.Id == messageId);

            Assert.Equal(workerBId, saved.LockedBy);
            Assert.Equal(secondClaim.LockToken, saved.LockToken);
            Assert.Null(saved.ProcessedAtUtc);
        }
        finally
        {
            await DeleteMessageAsync(
                connectionString,
                messageId);
        }
    }
}