using Agent.Events;

namespace Agent.Pipeline;

public sealed record AgentResult(
    string ConversationId,
    string AssistantMessage,
    IReadOnlyList<AgentEvent> Events);
