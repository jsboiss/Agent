using Agent.Events;

namespace Agent.Messages;

public sealed record MessageResult(
    string ConversationId,
    string AssistantMessage,
    IReadOnlyList<AgentEvent> Events);
