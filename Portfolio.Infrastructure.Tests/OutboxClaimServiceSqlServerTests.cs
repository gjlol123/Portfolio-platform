using Microsoft.EntityFrameworkCore;
using Portfolio.Infrastructure.Entities;
using Portfolio.Infrastructure.Outbox;
using Portfolio.Infrastructure.Persistence;
using Xunit;

namespace Portfolio.Infrastructure.Tests;

public sealed class OutboxClaimServiceSqlServerTests
{
    private static PortfolioDbContext CreateDbContext()
    {
        var connectionString = @"Server=SSNC-PF66AM5D;Database=PortfolioDb;User ID=sa;Password=Advent.sa;TrustServerCertificate=True";



        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set PORTFOLIO_OUTBOX_TEST_CONNECTION " +
                "to a dedicated test database.");
        }

        var options =
            new DbContextOptionsBuilder<PortfolioDbContext>()
                .UseSqlServer(connectionString)
                .Options;

        return new PortfolioDbContext(options);
    }



    [Fact]
    public async Task AddMsgs()
    {
        var id = Guid.NewGuid();

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        db.OutboxMessages.Add(
            new OutboxMessage(
                id,
                "PortfolioCreated",
                """{"portfolioId":"test-001"}""",
                DateTimeOffset.UtcNow.AddYears(-1)));

        await db.SaveChangesAsync();
    }

        [Fact]
    public async Task ClaimBatchAsync_ClaimsNewMessage()
    {
        var id = Guid.NewGuid();

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        db.OutboxMessages.Add(
            new OutboxMessage(
                id,
                "PortfolioCreated",
                """{"portfolioId":"test-001"}""",
                DateTimeOffset.UtcNow.AddYears(-1)));

        await db.SaveChangesAsync();

        try
        {
            var service = new OutboxClaimService(db);

            var claimed = await service.ClaimBatchAsync(
                workerId: $"test-worker-{Guid.NewGuid():N}",
                batchSize: 100,
                leaseDuration: TimeSpan.FromMinutes(2),
                cancellationToken: CancellationToken.None);

            var message = Assert.Single(
                claimed.Where(x => x.Id == id));

            Assert.NotEqual(Guid.Empty, message.LockToken);
            Assert.Equal("PortfolioCreated", message.MessageType);
        }
        finally
        {
            await db.OutboxMessages
                .Where(x => x.Id == id)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task RecordFailureAsync_IncrementsAttemptCount()
    {
        var id = Guid.NewGuid();

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        db.OutboxMessages.Add(
            new OutboxMessage(
                id,
                "PortfolioCreated",
                """{"portfolioId":"test-002"}""",
                DateTimeOffset.UtcNow.AddYears(-1)));

        await db.SaveChangesAsync();

        try
        {
            var service = new OutboxClaimService(db);

            var claimedMessages = await service.ClaimBatchAsync(
                workerId: $"test-worker-{Guid.NewGuid():N}",
                batchSize: 100,
                leaseDuration: TimeSpan.FromMinutes(2),
                cancellationToken: CancellationToken.None);

            var claimed = Assert.Single(
                claimedMessages.Where(x => x.Id == id));

            var updated = await service.RecordFailureAsync(
                claimed.Id,
                claimed.LockToken,
                claimed.AttemptCount,
                "Broker unavailable",
                CancellationToken.None);

            Assert.True(updated);

            // ExecuteUpdateAsync bypasses tracked entity values.
            // Read the persisted state using a fresh DbContext.
            await using var verifyDb = CreateDbContext();

            var saved = await verifyDb.OutboxMessages
                .AsNoTracking()
                .SingleAsync(x => x.Id == id);

            Assert.Equal(1, saved.AttemptCount);
            Assert.Equal("Broker unavailable", saved.LastError);
            Assert.NotNull(saved.NextAttemptAtUtc);
            Assert.Null(saved.ProcessedAtUtc);
            Assert.Null(saved.LockToken);
            Assert.Null(saved.LockedBy);
            Assert.Null(saved.LockedUntilUtc);
            Assert.Null(saved.DeadLetteredAtUtc);
        }
        finally
        {
            await db.OutboxMessages
                .Where(x => x.Id == id)
                .ExecuteDeleteAsync();
        }
    }
}