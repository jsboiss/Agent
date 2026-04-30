namespace Agent.Dispatching;

public sealed record AgentRequest(
    string ConversationId,
    string Channel,
    string UserMessage,
    DateTimeOffset ReceivedAt);
