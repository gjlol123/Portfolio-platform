//using Microsoft.EntityFrameworkCore;
//using Microsoft.Extensions.Logging.Abstractions;
//using Portfolio.Infrastructure.Entities;
//using Portfolio.Infrastructure.Outbox;
//using Portfolio.Infrastructure.Persistence;
//using Xunit;

//namespace Portfolio.Infrastructure.Tests;

//public sealed class OutboxProcessorTests
//{
//    private static PortfolioDbContext CreateDbContext()
//    {
//        var options = new DbContextOptionsBuilder<PortfolioDbContext>()
//            .UseInMemoryDatabase($"OutboxTests-{Guid.NewGuid()}")
//            .Options;

//        return new PortfolioDbContext(options);
//    }

//    private static OutboxMessage CreateMessage()
//    {
//        return new OutboxMessage(
//            id: Guid.NewGuid(),
//            messageType: "PortfolioCreated",
//            payload: """{"portfolioId":"test-001"}""",
//            occurredAtUtc: DateTimeOffset.UtcNow);
//    }

//    [Fact]
//    public async Task PublishSucceeds_MessageIsMarkedProcessed()
//    {
//        await using var db = CreateDbContext();

//        var message = CreateMessage();
//        db.OutboxMessages.Add(message);
//        await db.SaveChangesAsync();

//        var publisher = new FakeEventPublisher();

//        var processor = new OutboxProcessor(
//            db,
//            publisher,
//            NullLogger<OutboxProcessor>.Instance);

//        await processor.ProcessBatchAsync(CancellationToken.None);

//        Assert.Equal(1, publisher.CallCount);
//        Assert.NotNull(message.ProcessedOnUtc);
//        Assert.Null(message.Error);
//    }

//    [Fact]
//    public async Task PublishFails_MessageIsScheduledForRetry()
//    {
//        await using var db = CreateDbContext();

//        var message = CreateMessage();
//        db.OutboxMessages.Add(message);
//        await db.SaveChangesAsync();

//        var publisher = new FakeEventPublisher
//        {
//            OnPublish = (_, _, _, _) =>
//                Task.FromException(
//                    new InvalidOperationException(
//                        "Broker unavailable"))
//        };

//        var processor = new OutboxProcessor(
//            db,
//            publisher,
//            NullLogger<OutboxProcessor>.Instance);

//        await processor.ProcessBatchAsync(CancellationToken.None);

//        Assert.Equal(1, publisher.CallCount);
//        Assert.Equal(1, message.Attempts);
//        Assert.Null(message.ProcessedOnUtc);
//        Assert.NotNull(message.Error);
//        Assert.Contains("Broker unavailable", message.Error);
//        Assert.NotNull(message.NextAttemptOnUtc);
//        Assert.True(
//            message.NextAttemptOnUtc.Value > DateTime.UtcNow);
//    }

//    [Fact]
//    public async Task RetrySucceeds_MessageIsEventuallyProcessed()
//    {
//        await using var db = CreateDbContext();

//        var message = CreateMessage();
//        db.OutboxMessages.Add(message);
//        await db.SaveChangesAsync();

//        var publisher = new FakeEventPublisher
//        {
//            OnPublish = (_, _, _, _) =>
//                Task.FromException(
//                    new InvalidOperationException(
//                        "Temporary failure"))
//        };

//        var processor = new OutboxProcessor(
//            db,
//            publisher,
//            NullLogger<OutboxProcessor>.Instance);

//        await processor.ProcessBatchAsync(CancellationToken.None);

//        Assert.Null(message.ProcessedOnUtc);
//        Assert.Equal(1, message.Attempts);

//        // Simulate that the retry delay has elapsed.
//        message.NextAttemptOnUtc = DateTime.UtcNow.AddSeconds(-1);
//        await db.SaveChangesAsync();

//        publisher.OnPublish = (_, _, _, _) =>
//            Task.CompletedTask;

//        await processor.ProcessBatchAsync(CancellationToken.None);

//        Assert.Equal(2, publisher.CallCount);
//        Assert.NotNull(message.ProcessedOnUtc);
//        Assert.Null(message.Error);
//    }
//}