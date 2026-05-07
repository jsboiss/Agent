using Agent.Settings;

namespace Agent.Context;

public sealed record ContextPlanningRequest(
    string ConversationId,
    string Channel,
    string UserMessage,
    AgentSettings Settings,
    DateTimeOffset ReceivedAt);
