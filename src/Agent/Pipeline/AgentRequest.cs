namespace Agent.Pipeline;

public sealed record AgentRequest(
    string ConversationId,
    string Channel,
    string UserMessage,
    DateTimeOffset ReceivedAt);
