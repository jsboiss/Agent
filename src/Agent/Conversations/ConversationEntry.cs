namespace Agent.Conversations;

public sealed record ConversationEntry(
    string Id,
    string ConversationId,
    ConversationEntryRole Role,
    string Channel,
    string Content,
    string? ParentEntryId,
    DateTimeOffset CreatedAt);
