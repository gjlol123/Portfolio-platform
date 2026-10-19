//using Microsoft.EntityFrameworkCore;
//using Microsoft.Extensions.Logging;
//using Portfolio.Infrastructure.Contracts.Events;
//using Portfolio.Infrastructure.Entities;
//using Portfolio.Infrastructure.Persistence;

//namespace Portfolio.Api.Consumers;

//public sealed class TransactionCreatedConsumer
//{
//    private const string ConsumerName = "TransactionReportingConsumer";

//    private readonly PortfolioDbContext _dbContext;
//    private readonly ILogger<TransactionCreatedConsumer> _logger;

//    public TransactionCreatedConsumer(
//        PortfolioDbContext dbContext,
//        ILogger<TransactionCreatedConsumer> logger)
//    {
//        _dbContext = dbContext;
//        _logger = logger;
//    }

//    public async Task HandleAsync(
//        Guid messageId,
//        TransactionCreatedV1 message,
//        CancellationToken cancellationToken)
//    {
//        var alreadyProcessed = await _dbContext.InboxMessages
//            .AsNoTracking()
//            .AnyAsync(
//                x => x.ConsumerName == ConsumerName
//                     && x.MessageId == messageId,
//                cancellationToken);

//        if (alreadyProcessed)
//        {
//            _logger.LogInformation(
//                "Duplicate message ignored. Consumer={Consumer}, " +
//                "MessageId={MessageId}",
//                ConsumerName,
//                messageId);

//            return;
//        }

//        var inboxMessage = new InboxMessage
//        {
//            ConsumerName = ConsumerName,
//            MessageId = messageId,
//            ProcessedAtUtc = DateTimeOffset.UtcNow
//        };

//        var reportEntry = new TransactionReportEntry
//        {
//            TransactionId = message.TransactionId,
//            PortfolioId = message.PortfolioId,
//            Symbol = message.Symbol,
//            Quantity = message.Quantity,
//            OccurredAtUtc = message.OccurredAtUtc
//        };

//        _dbContext.InboxMessages.Add(inboxMessage);
//        _dbContext.TransactionReportEntries.Add(reportEntry);

//        try
//        {
//            // Both records are saved in the same database transaction.
//            await _dbContext.SaveChangesAsync(cancellationToken);

//            _logger.LogInformation(
//                "Transaction event processed. Consumer={Consumer}, " +
//                "MessageId={MessageId}, TransactionId={TransactionId}",
//                ConsumerName,
//                messageId,
//                message.TransactionId);
//        }
//        catch (DbUpdateException ex)
//        {
//            // Clear tracked entities after the failed database operation.
//            _dbContext.ChangeTracker.Clear();

//            // Another concurrent invocation may have processed this message.
//            var processedByAnotherInvocation =
//                await _dbContext.InboxMessages
//                    .AsNoTracking()
//                    .AnyAsync(
//                        x => x.ConsumerName == ConsumerName
//                             && x.MessageId == messageId,
//                        cancellationToken);

//            if (processedByAnotherInvocation)
//            {
//                _logger.LogInformation(
//                    "Duplicate message ignored after a concurrent insert. " +
//                    "Consumer={Consumer}, MessageId={MessageId}",
//                    ConsumerName,
//                    messageId);

//                return;
//            }

//            _logger.LogError(
//                ex,
//                "Failed to process transaction event. MessageId={MessageId}",
//                messageId);

//            throw;
//        }
//    }
//}