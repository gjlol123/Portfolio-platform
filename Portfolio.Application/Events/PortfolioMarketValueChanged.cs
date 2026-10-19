namespace Portfolio.Application.Events;

public sealed record PortfolioMarketValueChanged(
    int PortfolioId,
    decimal NewMarketValue);