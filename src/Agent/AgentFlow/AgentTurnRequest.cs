namespace Agent.AgentFlow;

public sealed record AgentTurnRequest(
    string ConversationId,
    string Channel,
    string UserMessage,
    DateTimeOffset ReceivedAt);
