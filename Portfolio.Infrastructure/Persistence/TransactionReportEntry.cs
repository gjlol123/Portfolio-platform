namespace Portfolio.Infrastructure.Persistence;

public sealed class TransactionReportEntry
{
    public Guid TransactionId { get; set; }

    public Guid PortfolioId { get; set; }

    public string Symbol { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }
}