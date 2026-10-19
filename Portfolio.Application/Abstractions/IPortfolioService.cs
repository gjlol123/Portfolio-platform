using Portfolio.Domain.Entities;

namespace Portfolio.Application.Abstractions;

public interface IPortfolioService
{
    Task RenameAsync(
        int portfolioId,
        string newName,
        CancellationToken cancellationToken);

        Task<IReadOnlyList<PortfolioEntity>> GetAllAsync(
            CancellationToken cancellationToken);

        Task<PortfolioEntity?> GetByIdAsync(int Id,
            CancellationToken cancellationToken);

        Task<PortfolioEntity> CreateAsync(
            CreatePortfolioRequest request,
            CancellationToken cancellationToken);
    
}