using Agent.Events;

namespace Agent.AgentFlow;

public sealed record AgentTurnResult(
    string ConversationId,
    string AssistantMessage,
    IReadOnlyList<AgentEvent> Events);
