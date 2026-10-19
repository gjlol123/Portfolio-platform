using Microsoft.EntityFrameworkCore;
using Portfolio.Infrastructure.Persistence;
using System.Text.Json;
using Portfolio.Domain.Entities;



//using Portfolio = Portfolio.PortfolioEntity;

namespace Portfolio.Infrastructure.Repositories;

public class SqlPortfolioRepository 
{
    private readonly PortfolioDbContext _db;

    public SqlPortfolioRepository(PortfolioDbContext db)
    {
        _db = db;
    }

    public async Task<Guid> CreateTransactionAsync(
    CreateTransactionCommand command,
    CancellationToken cancellationToken)
    {
        //var transaction = new PortfolioTransaction
        //{
        //    Id = Guid.NewGuid(),
        //    PortfolioId = command.PortfolioId,
        //    Symbol = command.Symbol,
        //    Quantity = command.Quantity,
        //    CreatedAtUtc = DateTime.UtcNow
        //};


    //    var message = new OutboxMessage(
    //Guid.NewGuid(),
    //messageType,
    //serializedPayload,
    //occurredAtUtc);
        //var outboxMessage = new OutboxMessage()
        //{
        //    Id = Guid.NewGuid(),
        //    EventType = "TransactionCreated",
        //    Payload = JsonSerializer.Serialize(new
        //    {
        //        //transaction.Id,
        //        //transaction.PortfolioId,
        //        //transaction.Symbol,
        //        //transaction.Quantity
        //    }),
        //    //OccurredAtUtc = transaction.CreatedAtUtc
        //};

        //_db.Transactions.Add(transaction);
        //_db.OutboxMessages.Add(outboxMessage);

        await _db.SaveChangesAsync(cancellationToken);

        return new Guid();
    }

    public async Task<IReadOnlyList<PortfolioEntity>> GetPageAsync(
    string? currency,
    int skip,
    int pageSize,
    CancellationToken cancellationToken)
    {
        IQueryable<PortfolioEntity> query = _db.Portfolios
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(currency))
        {
            query = query.Where(
                p => p.BaseCurrency == currency);
        }

        var rows = await query
            .OrderBy(p => p.Id)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<PortfolioEntity>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        var rows = await _db.Portfolios
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToArray();
    }

    public async Task<PortfolioEntity?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var row = await _db.Portfolios
            .AsNoTracking()
            .SingleOrDefaultAsync(
                p => p.Id == id,
                cancellationToken);

        return row is null ? null : Map(row);
    }

    public async Task<PortfolioEntity> CreateAsync(
        CreatePortfolioRequest request,
        CancellationToken cancellationToken)
    {
        var entity = new PortfolioEntity(
             request.Name,
             request.BaseCurrency,
             0m
            );

        _db.Portfolios.Add(entity);

        await _db.SaveChangesAsync(cancellationToken);

        return Map(entity);
    }

    private static PortfolioEntity Map(PortfolioEntity entity)
    {
        return new PortfolioEntity(
            entity.Id,
            entity.Name,
            entity.BaseCurrency,
            entity.MarketValue);
    }
}