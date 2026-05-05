namespace Agent.Conversations;

public sealed record Conversation(
    string Id,
    ConversationKind Kind,
    string? ParentConversationId,
    string? ParentEntryId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
