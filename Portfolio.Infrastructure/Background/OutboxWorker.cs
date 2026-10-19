using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Portfolio.Application.Abstractions;
using Portfolio.Infrastructure.Messaging;
using Portfolio.Infrastructure.Outbox;

namespace Portfolio.Infrastructure.Background;

public sealed class OutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxWorker> _logger;

    private readonly string _workerId = $"outbox-{Guid.NewGuid():N}";

    private static readonly TimeSpan PollInterval =
        TimeSpan.FromSeconds(5);

    public OutboxWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Outbox processing cycle failed.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("OutboxWorker stopped.");
    }

    private async Task ProcessBatchAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        var claimService = scope.ServiceProvider
            .GetRequiredService<OutboxClaimService>();

        var publisher = scope.ServiceProvider
            .GetRequiredService<IEventPublisher>();

        var messages = await claimService.ClaimBatchAsync(
            workerId: _workerId,
            batchSize: 5,
            leaseDuration: TimeSpan.FromMinutes(2),
            cancellationToken);

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await publisher.PublishAsync(
                    message.Id,
                    message.MessageType,
                    message.Payload,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                try
                {
                    var recorded = await claimService.RecordFailureAsync(
                        message.Id,
                        message.LockToken,
                        message.AttemptCount,
                        ex.Message,
                        cancellationToken);

                    if (!recorded)
                    {
                        _logger.LogWarning(
                            "Failure state was not updated because the claim changed. " +
                            "MessageId={MessageId}",
                            message.Id);
                    }
                }
                catch (Exception updateException)
                {
                    _logger.LogError(
                        updateException,
                        "Failed to save the publishing failure. " +
                        "MessageId={MessageId}",
                        message.Id);
                }

                continue;
            }

            try
            {
                var completed = await claimService.MarkProcessedAsync(
                    message.Id,
                    message.LockToken,
                    cancellationToken);

                if (!completed)
                {
                    _logger.LogWarning(
                        "Message was published, but its claim is no longer current. " +
                        "MessageId={MessageId}",
                        message.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Message may have been published, but its completion state was not saved. " +
                    "MessageId={MessageId}",
                    message.Id);
            }
        }
    }
}