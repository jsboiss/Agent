namespace Agent.Conversations;

public sealed record ConversationSummary(
    string ConversationId,
    string Content,
    string? ThroughEntryId,
    DateTimeOffset UpdatedAt);
