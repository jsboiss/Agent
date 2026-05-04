namespace Agent.Messages;

public sealed record MessageRequest(
    string ConversationId,
    string Channel,
    string UserMessage,
    DateTimeOffset ReceivedAt);
