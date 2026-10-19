namespace Portfolio.Infrastructure.Contracts.Events;

public sealed record TransactionCreatedV1(
    Guid TransactionId,
    Guid PortfolioId,
    string Symbol,
    decimal Quantity,
    DateTimeOffset OccurredAtUtc);