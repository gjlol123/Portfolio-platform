namespace Portfolio.Application.Events;

public sealed record PortfolioMarketValueApplied(
    Guid SourceMessageId,
    int PortfolioId,
    decimal PreviousMarketValue,
    decimal NewMarketValue);