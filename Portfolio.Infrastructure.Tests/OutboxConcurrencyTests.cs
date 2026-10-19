using Microsoft.EntityFrameworkCore;
using Portfolio.Infrastructure.Entities;
using Portfolio.Infrastructure.Outbox;
using Portfolio.Infrastructure.Persistence;
using Xunit;

namespace Portfolio.Infrastructure.Tests;

public sealed class OutboxConcurrencyTests
{
    private static string GetConnectionString()
    {
        var connectionString = @"Server=SSNC-PF66AM5D;Database=PortfolioDb;User ID=sa;Password=Advent.sa;TrustServerCertificate=True";

        return connectionString;
    }

    private static PortfolioDbContext CreateDb(
        string connectionString)
    {
        var options =
            new DbContextOptionsBuilder<PortfolioDbContext>()
                .UseSqlServer(connectionString)
                .Options;

        return new PortfolioDbContext(options);
    }

    [Fact]
    public async Task ConcurrentWorkers_ClaimDisjointMessages()
    {
        var connectionString = GetConnectionString();

        var messageIds = Enumerable.Range(0, 20)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        try
        {
            await using (var setup = CreateDb(connectionString))
            {
                await setup.Database.MigrateAsync();

                var hasUnprocessedMessages =
                    await setup.OutboxMessages.AnyAsync(x =>
                        x.ProcessedAtUtc == null &&
                        x.DeadLetteredAtUtc == null);

                Assert.False(
                    hasUnprocessedMessages,
                    "Use a dedicated test database with no pending messages.");

                for (var i = 0; i < messageIds.Length; i++)
                {
                    setup.OutboxMessages.Add(
                        new OutboxMessage(
                            messageIds[i],
                            "ConcurrencyTest",
                            $$"""{"sequence":{{i}}}""",
                            DateTimeOffset.UtcNow
                                .AddDays(-30)
                                .AddTicks(i)));
                }

                await setup.SaveChangesAsync();
            }

            await using var dbA = CreateDb(connectionString);
            await using var dbB = CreateDb(connectionString);

            var workerA = new OutboxClaimService(dbA);
            var workerB = new OutboxClaimService(dbB);

            var tasks = new[]
            {
                workerA.ClaimBatchAsync(
                    $"worker-A-{Guid.NewGuid():N}",
                    10,
                    TimeSpan.FromMinutes(2),
                    CancellationToken.None),

                workerB.ClaimBatchAsync(
                    $"worker-B-{Guid.NewGuid():N}",
                    10,
                    TimeSpan.FromMinutes(2),
                    CancellationToken.None)
            };

            var batches = await Task.WhenAll(tasks);

            var idsA = batches[0].Select(x => x.Id).ToArray();
            var idsB = batches[1].Select(x => x.Id).ToArray();

            Assert.Equal(10, idsA.Length);
            Assert.Equal(10, idsB.Length);

            Assert.Empty(idsA.Intersect(idsB));

            Assert.Equal(
                20,
                idsA.Concat(idsB).Distinct().Count());

            Assert.All(
                batches.SelectMany(x => x),
                message => Assert.NotEqual(
                    Guid.Empty,
                    message.LockToken));
        }
        finally
        {
            await using var cleanup = CreateDb(connectionString);

            await cleanup.OutboxMessages
                .Where(x => messageIds.Contains(x.Id))
                .ExecuteDeleteAsync();
        }
    }
}