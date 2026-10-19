namespace Portfolio.Application.Contracts;

public sealed record PortfolioRenamedV1(
    int PortfolioId,
    string Name,
    DateTimeOffset OccurredAtUtc);