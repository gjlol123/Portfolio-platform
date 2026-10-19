//using System;
//using System.Collections.Generic;
//using System.Text;
//using System.Text.Json;

//namespace Portfolio.Infrastructure.Messaging
//{
//    public class OutboxMessage
//    {
//        public async Task<Guid> CreateTransactionAsync(
//    CreateTransactionCommand command,
//    CancellationToken cancellationToken)
//        {
//            var transaction = new PortfolioTransaction
//            {
//                Id = Guid.NewGuid(),
//                PortfolioId = command.PortfolioId,
//                Symbol = command.Symbol,
//                Quantity = command.Quantity,
//                CreatedAtUtc = DateTime.UtcNow
//            };

//            var outboxMessage = new OutboxMessage
//            {
//                Id = Guid.NewGuid(),
//                EventType = "TransactionCreated",
//                Payload = JsonSerializer.Serialize(new
//                {
//                    transaction.Id,
//                    transaction.PortfolioId,
//                    transaction.Symbol,
//                    transaction.Quantity
//                }),
//                OccurredAtUtc = transaction.CreatedAtUtc
//            };

//            _dbContext.Transactions.Add(transaction);
//            _dbContext.OutboxMessages.Add(outboxMessage);

//            // 同一次 SaveChanges 保存业务数据和 Outbox 记录。
//            await _dbContext.SaveChangesAsync(cancellationToken);

//            return transaction.Id;
//        }

//    }
//}
