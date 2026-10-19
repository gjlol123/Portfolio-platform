using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Portfolio.Application.Abstractions;

namespace Portfolio.Infrastructure.Messaging;

public sealed class LoggingEventPublisher : IEventPublisher
{
    private readonly ILogger<LoggingEventPublisher> _logger;
    private readonly IConfiguration _configuration;

    private readonly ConcurrentDictionary<Guid, int> _callsByEvent = new();

    public LoggingEventPublisher(
        ILogger<LoggingEventPublisher> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public Task PublishAsync(
        Guid eventId,
        string eventType,
        string payload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int failuresBeforeSuccess = Math.Max(
            0,
            _configuration.GetValue<int>(
                "Outbox:DemoFailuresBeforeSuccess"));

        int attempt = _callsByEvent.AddOrUpdate(
            eventId,
            1,
            (_, current) => current + 1);

        if (attempt <= failuresBeforeSuccess)
        {
            _logger.LogWarning(
                "Simulated publishing failure. EventId={EventId}, " +
                "EventType={EventType}, Attempt={Attempt}",
                eventId,
                eventType,
                attempt);

            throw new InvalidOperationException(
                "Simulated message publisher unavailable.");
        }

        _logger.LogInformation(
            "SIMULATED PUBLISH SUCCESS. EventId={EventId}, " +
            "EventType={EventType}",
            eventId,
            eventType);

        return Task.CompletedTask;
    }
}