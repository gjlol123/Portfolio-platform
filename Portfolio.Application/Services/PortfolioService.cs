using Portfolio.Application.Abstractions;
using Portfolio.Application.Contracts;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Repositories;
using System.Collections.Concurrent;

namespace Portfolio.Application.Services;


public sealed class PortfolioService(
 IPortfolioRepository portfolioRepository,
 IOutboxWriter outboxWriter,
 IUnitOfWork unitOfWork,
 TimeProvider timeProvider) : IPortfolioService
{
    public async Task RenameAsync(
     int portfolioId,
     string newName,
     CancellationToken cancellationToken)
    {
        var portfolio = await portfolioRepository.GetByIdAsync(
            portfolioId,
            cancellationToken);

        if (portfolio is null)
        {
            throw new KeyNotFoundException(
                $"Portfolio {portfolioId} was not found.");
        }

        portfolio.Rename(newName);

        var occurredAt = timeProvider.GetUtcNow();

        var integrationEvent = new PortfolioRenamedV1(
            portfolio.Id,
            portfolio.Name,
            occurredAt);

        outboxWriter.Add(
            "portfolio.renamed.v1",
            integrationEvent,
            occurredAt);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public Task<IReadOnlyList<PortfolioEntity>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        return portfolioRepository.GetAllAsync(cancellationToken);
    }

    public Task<PortfolioEntity?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return portfolioRepository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<PortfolioEntity> CreateAsync(
        CreatePortfolioRequest request,
        CancellationToken cancellationToken)
    {
        var portfolio = new PortfolioEntity(
          request.Name,
          request.BaseCurrency);

        portfolioRepository.Add(portfolio);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return portfolio;
    }
}
         

 


