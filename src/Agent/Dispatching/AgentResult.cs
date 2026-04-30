using Agent.Events;

namespace Agent.Dispatching;

public sealed record AgentResult(
    string ConversationId,
    string AssistantMessage,
    IReadOnlyList<AgentEvent> Events);
