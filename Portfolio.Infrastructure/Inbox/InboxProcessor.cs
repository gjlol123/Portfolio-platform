using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Portfolio.Infrastructure.Entities;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.Infrastructure.Inbox;

public sealed class InboxProcessor
{
    private readonly PortfolioDbContext _dbContext;

    public InboxProcessor(PortfolioDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<InboxProcessingResult> ProcessOnceAsync(
        string consumerName,
        Guid messageId,
        Func<CancellationToken, Task> handleMessage,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        ArgumentNullException.ThrowIfNull(handleMessage);

        if (messageId == Guid.Empty)
        {
            throw new ArgumentException(
                "Message ID cannot be empty.",
                nameof(messageId));
        }

        if (_dbContext.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException(
                "InboxProcessor requires a DbContext " +
                "without pending changes.");
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        var inboxMessage = new InboxMessage(
            consumerName,
            messageId,
            DateTimeOffset.UtcNow);

        _dbContext.InboxMessages.Add(inboxMessage);

        // Save the inbox record inside the transaction.
        // It is not committed until the business operation succeeds.
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
            when (IsSqlServerUniqueKeyViolation(ex))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _dbContext.ChangeTracker.Clear();

            return InboxProcessingResult.Duplicate;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _dbContext.ChangeTracker.Clear();

            throw;
        }

        try
        {
            // The handler must modify business data using the same
            // scoped DbContext and must not perform external side effects.
            await handleMessage(cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return InboxProcessingResult.Processed;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _dbContext.ChangeTracker.Clear();

            throw;
        }
    }

    private static bool IsSqlServerUniqueKeyViolation(
        DbUpdateException exception)
    {
        var sqlException =
            exception.GetBaseException() as SqlException;

        return sqlException is not null &&
               (sqlException.Number == 2601 ||
                sqlException.Number == 2627);
    }
}