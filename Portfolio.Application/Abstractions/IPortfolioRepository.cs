using Portfolio.Domain.Entities;

namespace Portfolio.Infrastructure.Repositories;

public interface IPortfolioRepository
{
    Task<IReadOnlyList<PortfolioEntity>> GetAllAsync(
        CancellationToken cancellationToken);

    Task<PortfolioEntity?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken);
    void Add(PortfolioEntity portfolio);
}
