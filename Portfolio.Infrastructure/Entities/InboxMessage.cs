namespace Portfolio.Infrastructure.Entities;

public sealed class InboxMessage
{
    public string ConsumerName { get; private set; } = string.Empty;

    public Guid MessageId { get; private set; }

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public InboxMessage()
    {
    }

    public InboxMessage(
        string consumerName,
        Guid messageId,
        DateTimeOffset receivedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(consumerName))
        {
            throw new ArgumentException(
                "Consumer name is required.",
                nameof(consumerName));
        }

        if (messageId == Guid.Empty)
        {
            throw new ArgumentException(
                "Message ID cannot be empty.",
                nameof(messageId));
        }

        ConsumerName = consumerName;
        MessageId = messageId;
        ReceivedAtUtc = receivedAtUtc;
    }
}