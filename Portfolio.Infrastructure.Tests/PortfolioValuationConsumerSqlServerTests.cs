using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Application.Abstractions;
using Portfolio.Application.Events;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Entities;
using Portfolio.Infrastructure.Inbox;
using Portfolio.Infrastructure.Messaging;
using Portfolio.Infrastructure.Outbox;
using Portfolio.Infrastructure.Persistence;
using Xunit;

namespace Portfolio.Infrastructure.Tests;

public sealed class PortfolioValuationConsumerSqlServerTests
{
    private static string GetConnectionString()
    {
        var connectionString = @"Server=SSNC-PF66AM5D;Database=PortfolioDb;User ID=sa;Password=Advent.sa;TrustServerCertificate=True";


        return connectionString;
    }

    [Fact]
    public async Task DuplicateDelivery_UpdatesPortfolioOnlyOnce()
    {
        var connectionString = GetConnectionString();


        var  portfolioId = 4;

        var messageId = Guid.NewGuid();

        try
        {
            //await using (var setup =
            //    CreateDbContext(connectionString))
            //{
            //    await setup.Database.MigrateAsync();

            //    var portfolioNew =  setup.Set<PortfolioEntity>().Add(
            //        new PortfolioEntity(
            //            "Inbox Integration Test",
            //            "EUR",
            //            125000m));

            //    await setup.SaveChangesAsync();

            //    portfolioId = portfolioNew.Id;

            //}


            using var provider =
                CreateServiceProvider(connectionString);

            var incomingEvent =
                new PortfolioMarketValueChanged(
                    portfolioId,
                    135000m);

            InboxProcessingResult firstResult;

            await using (var scope =
                provider.CreateAsyncScope())
            {
                var consumer = scope.ServiceProvider
                    .GetRequiredService<PortfolioValuationConsumer>();

                firstResult = await consumer.HandleAsync(
                    messageId,
                    incomingEvent,
                    CancellationToken.None);
            }

            InboxProcessingResult secondResult;

            await using (var scope =
                provider.CreateAsyncScope())
            {
                var consumer = scope.ServiceProvider
                    .GetRequiredService<PortfolioValuationConsumer>();

                secondResult = await consumer.HandleAsync(
                    messageId,
                    incomingEvent,
                    CancellationToken.None);
            }

            Assert.Equal(
                InboxProcessingResult.Processed,
                firstResult);

            Assert.Equal(
                InboxProcessingResult.Duplicate,
                secondResult);

            await using var verify =
                CreateDbContext(connectionString);

            var portfolio = await verify
                .Set<PortfolioEntity>()
                .SingleAsync(x => x.Id == portfolioId);

            Assert.Equal(135000m, portfolio.MarketValue);

            Assert.Equal(
                1,
                await verify.InboxMessages.CountAsync(x =>
                    x.ConsumerName ==
                        "PortfolioValuationConsumer.v1" &&
                    x.MessageId == messageId));

            Assert.Equal(
                1,
                await verify.OutboxMessages.CountAsync(x =>
                    x.MessageType ==
                        "PortfolioMarketValueApplied" &&
                    x.Payload.Contains(messageId.ToString())));
        }
        finally
        {
            await using var cleanup =
                CreateDbContext(connectionString);

            await cleanup.OutboxMessages
                .Where(x =>
                    x.MessageType ==
                        "PortfolioMarketValueApplied" &&
                    x.Payload.Contains(messageId.ToString()))
                .ExecuteDeleteAsync();

            await cleanup.InboxMessages
                .Where(x =>
                    x.ConsumerName ==
                        "PortfolioValuationConsumer.v1" &&
                    x.MessageId == messageId)
                .ExecuteDeleteAsync();

            await cleanup.Set<PortfolioEntity>()
                .Where(x => x.Id == portfolioId)
                .ExecuteDeleteAsync();
        }
    }


    private static PortfolioDbContext CreateDbContext(
        string connectionString)
    {
        var options = new DbContextOptionsBuilder<PortfolioDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new PortfolioDbContext(options);
    }

    private static ServiceProvider CreateServiceProvider(
        string connectionString)
    {
        var services = new ServiceCollection();

        services.AddLogging();

        services.AddDbContext<PortfolioDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<InboxProcessor>();
        services.AddScoped<IOutboxWriter, EfOutboxWriter>();
        services.AddScoped<PortfolioValuationConsumer>();

        return services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
    }
}