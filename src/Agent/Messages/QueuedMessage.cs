namespace Agent.Messages;

public sealed record QueuedMessage(
    string Id,
    string ConversationId,
    string ConversationEntryId,
    QueuedMessageKind Kind,
    string Channel,
    string Content,
    DateTimeOffset CreatedAt);
