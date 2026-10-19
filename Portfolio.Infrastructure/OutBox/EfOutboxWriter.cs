using Portfolio.Application.Abstractions;
using Portfolio.Infrastructure.Entities;
using Portfolio.Infrastructure.Persistence;
using System.Text.Json;

namespace Portfolio.Infrastructure.Outbox;

public sealed class EfOutboxWriter(
    PortfolioDbContext dbContext) : IOutboxWriter
{
    public void Add<T>(
        string messageType,
        T payload,
        DateTimeOffset occurredAtUtc)
    {
        var serializedPayload = JsonSerializer.Serialize(payload);

        dbContext.Set<OutboxMessage>().Add(
            new OutboxMessage(
                Guid.NewGuid(),
                messageType,
                serializedPayload,
                occurredAtUtc));
    }
}