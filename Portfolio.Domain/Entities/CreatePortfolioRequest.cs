namespace Portfolio.Domain.Entities;

    public record CreatePortfolioRequest(
     string Name,
     string BaseCurrency
    );