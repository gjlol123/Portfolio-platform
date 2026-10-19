using Microsoft.EntityFrameworkCore;
using Portfolio.Infrastructure.Inbox;
using Portfolio.Infrastructure.Persistence;
using Xunit;

namespace Portfolio.Infrastructure.Tests;

public sealed class InboxProcessorSqlServerTests
{
    private static async Task<PortfolioDbContext>
        CreateDbContextAsync()
    {
        var connectionString = @"Server=SSNC-PF66AM5D;Database=PortfolioDb;User ID=sa;Password=Advent.sa;TrustServerCertificate=True";

        var options =
            new DbContextOptionsBuilder<PortfolioDbContext>()
                .UseSqlServer(connectionString)
                .Options;

        var db = new PortfolioDbContext(options);

        await db.Database.MigrateAsync();

        return db;
    }

    private static async Task CleanupAsync(
        string consumerName,
        Guid messageId)
    {
        await using var db = await CreateDbContextAsync();

        await db.InboxMessages
            .Where(x =>
                x.ConsumerName == consumerName &&
                x.MessageId == messageId)
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task DuplicateDelivery_ProcessesMessageOnlyOnce()
    {
        const string consumerName = "PortfolioProjection.v1";
        var messageId = Guid.NewGuid();
        var handlerCalls = 0;

        try
        {
            // First delivery.
            await using (var db = await CreateDbContextAsync())
            {
                var processor = new InboxProcessor(db);

                var result = await processor.ProcessOnceAsync(
                    consumerName,
                    messageId,
                    _ =>
                    {
                        handlerCalls++;
                        return Task.CompletedTask;
                    },
                    CancellationToken.None);

                Assert.Equal(
                    InboxProcessingResult.Processed,
                    result);
            }

            // Simulate broker redelivery in a new DI scope.
            await using (var db = await CreateDbContextAsync())
            {
                var processor = new InboxProcessor(db);

                var result = await processor.ProcessOnceAsync(
                    consumerName,
                    messageId,
                    _ =>
                    {
                        handlerCalls++;
                        return Task.CompletedTask;
                    },
                    CancellationToken.None);

                Assert.Equal(
                    InboxProcessingResult.Duplicate,
                    result);
            }

            Assert.Equal(1, handlerCalls);

            await using var verifyDb =
                await CreateDbContextAsync();

            var inboxCount = await verifyDb.InboxMessages
                .CountAsync(x =>
                    x.ConsumerName == consumerName &&
                    x.MessageId == messageId);

            Assert.Equal(1, inboxCount);
        }
        finally
        {
            await CleanupAsync(consumerName, messageId);
        }
    }

    [Fact]
    public async Task FailedHandler_RollsBackInboxAndAllowsRetry()
    {
        const string consumerName = "PortfolioProjection.v1";
        var messageId = Guid.NewGuid();

        try
        {
            // The first delivery fails during business processing.
            await using (var db = await CreateDbContextAsync())
            {
                var processor = new InboxProcessor(db);

                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => processor.ProcessOnceAsync(
                        consumerName,
                        messageId,
                        _ => Task.FromException(
                            new InvalidOperationException(
                                "Simulated business failure")),
                        CancellationToken.None));

                var inboxExists = await db.InboxMessages
                    .AsNoTracking()
                    .AnyAsync(x =>
                        x.ConsumerName == consumerName &&
                        x.MessageId == messageId);

                Assert.False(inboxExists);
            }

            // The second delivery uses a new scope and succeeds.
            await using (var db = await CreateDbContextAsync())
            {
                var processor = new InboxProcessor(db);

                var result = await processor.ProcessOnceAsync(
                    consumerName,
                    messageId,
                    _ => Task.CompletedTask,
                    CancellationToken.None);

                Assert.Equal(
                    InboxProcessingResult.Processed,
                    result);
            }

            await using var verifyDb =
                await CreateDbContextAsync();

            Assert.True(
                await verifyDb.InboxMessages.AnyAsync(x =>
                    x.ConsumerName == consumerName &&
                    x.MessageId == messageId));
        }
        finally
        {
            await CleanupAsync(consumerName, messageId);
        }
    }
}