namespace Portfolio.Domain.Entities;

public record PortfolioResponse(
    int Id,
    string Name,
    string BaseCurrency,
    decimal MarketValue
);