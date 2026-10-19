using Microsoft.EntityFrameworkCore;
using Portfolio.Application.Abstractions;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.Infrastructure.Repositories;

public sealed class EfPortfolioRepository(
    PortfolioDbContext dbContext) : IPortfolioRepository
{
    public Task<PortfolioEntity?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        // Load a tracked entity because the caller may modify it.
        return dbContext.Portfolios.FirstOrDefaultAsync(
            x => x.Id == id,
            cancellationToken);
    }

    public void Add(PortfolioEntity portfolio)
    {
        dbContext.Portfolios.Add(portfolio);
    }

    public async Task<IReadOnlyList<PortfolioEntity>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        return await dbContext.Portfolios
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
    }
}