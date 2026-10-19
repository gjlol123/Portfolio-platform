namespace Portfolio.Domain.Entities;

public class PortfolioEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string BaseCurrency { get; set; } = string.Empty;

    public decimal MarketValue { get; set; }

    public PortfolioEntity(
string name,
string baseCurrency)
    {
        Name = name;
        BaseCurrency = baseCurrency;
    }

    public PortfolioEntity(
 string name,
 string baseCurrency,
 decimal marketValue)
    {
        Name = name;
        BaseCurrency = baseCurrency;
        MarketValue = marketValue;
    }

    public PortfolioEntity(
    int id,
    string name,
    string baseCurrency,
    decimal marketValue)
    {
        Id = id;
        Name = name;
        BaseCurrency = baseCurrency;
        MarketValue = marketValue;
    }

    public void UpdateMarketValue(decimal marketValue)
    {
        MarketValue = marketValue;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Portfolio name cannot be empty.",
                nameof(name));
        }

        Name = name.Trim();
    }
}