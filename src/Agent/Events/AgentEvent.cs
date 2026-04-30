namespace Agent.Events;

public sealed record AgentEvent(
    string Id,
    AgentEventKind Kind,
    string ConversationId,
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, string> Data);
