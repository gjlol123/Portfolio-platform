using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Application.Events;
using Portfolio.Infrastructure.Inbox;

namespace Portfolio.Infrastructure.Messaging;

public sealed class PortfolioValuationDeliveryHandler(
    IServiceScopeFactory scopeFactory)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<InboxProcessingResult> HandleAsync(
        Guid messageId,
        string payload,
        CancellationToken cancellationToken)
    {
        var message =
            JsonSerializer.Deserialize<PortfolioMarketValueChanged>(
                payload,
                JsonOptions)
            ?? throw new JsonException(
                "The portfolio event payload is empty.");

        await using var scope = scopeFactory.CreateAsyncScope();

        var consumer = scope.ServiceProvider
            .GetRequiredService<PortfolioValuationConsumer>();

        return await consumer.HandleAsync(
            messageId,
            message,
            cancellationToken);
    }
}