namespace Agent.Drafts;

public sealed record AgentDraft(
    string Id,
    string Kind,
    string Summary,
    string Payload,
    string? SourceRunId,
    string ConversationId,
    string Channel,
    DraftStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
