using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Abstractions;
using Portfolio.Application.Events;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Inbox;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.Infrastructure.Messaging;

public sealed class PortfolioValuationConsumer(
    InboxProcessor inboxProcessor,
    PortfolioDbContext dbContext,
    IOutboxWriter outboxWriter)
{
    private const string ConsumerName =
        "PortfolioValuationConsumer.v1";

    public Task<InboxProcessingResult> HandleAsync(
        Guid messageId,
        PortfolioMarketValueChanged message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (messageId == Guid.Empty)
        {
            throw new ArgumentException(
                "Message ID cannot be empty.",
                nameof(messageId));
        }

        if (message.PortfolioId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(message.PortfolioId));
        }

        return inboxProcessor.ProcessOnceAsync(
            consumerName: ConsumerName,
            messageId: messageId,
            handleMessage: async token =>
            {
                var portfolio = await dbContext
                    .Set<PortfolioEntity>()
                    .SingleOrDefaultAsync(
                        x => x.Id == message.PortfolioId,
                        token);

                if (portfolio is null)
                {
                    throw new InvalidOperationException(
                        $"Portfolio {message.PortfolioId} was not found.");
                }

                var previousMarketValue = portfolio.MarketValue;

                portfolio.UpdateMarketValue(
                    message.NewMarketValue);

                var occurredAtUtc = DateTimeOffset.UtcNow;

                var outgoingEvent = new PortfolioMarketValueApplied(
                    SourceMessageId: messageId,
                    PortfolioId: portfolio.Id,
                    PreviousMarketValue: previousMarketValue,
                    NewMarketValue: message.NewMarketValue);

                outboxWriter.Add(
                    messageType: "PortfolioMarketValueApplied",
                    payload: outgoingEvent,
                    occurredAtUtc: occurredAtUtc);
            },
            cancellationToken: cancellationToken);
    }
}