//using Portfolio.Domain.Entities;
//using System.Collections.Concurrent;

//namespace Portfolio.Infrastructure.Repositories;

//public class InMemoryPortfolioRepository : IPortfolioRepository
//{
//    private readonly ConcurrentDictionary<int, PortfolioEntity> _portfolios = new();
//    private int _nextId = 2;

//    public InMemoryPortfolioRepository()
//    {
//        _portfolios[1] = new PortfolioEntity(
//            1, "Global Equity", "EUR", 125000.50m);

//        _portfolios[2] = new PortfolioEntity(
//            2, "European Bonds", "SEK", 50000m);
//    }

//    public Task<IReadOnlyList<PortfolioEntity>> GetAllAsync(
//        CancellationToken cancellationToken)
//    {
//        cancellationToken.ThrowIfCancellationRequested();

//        IReadOnlyList<PortfolioEntity> result = _portfolios.Values
//            .OrderBy(p => p.Id)
//            .ToArray();

//        return Task.FromResult(result);
//    }

//    public Task<PortfolioEntity?> GetByIdAsync(
//        int id,
//        CancellationToken cancellationToken)
//    {
//        cancellationToken.ThrowIfCancellationRequested();

//        _portfolios.TryGetValue(id, out var portfolio);

//        return Task.FromResult(portfolio);
//    }

//    public Task<PortfolioEntity> CreateAsync(
//        CreatePortfolioRequest request,
//        CancellationToken cancellationToken)
//    {
//        cancellationToken.ThrowIfCancellationRequested();

//        var id = Interlocked.Increment(ref _nextId);

//        var portfolio = newPortfolioEntity(
//            id,
//            request.Name,
//            request.BaseCurrency,
//            0m);

//        _portfolios[id] = portfolio;

//        return Task.FromResult(portfolio);
//    }
//}

