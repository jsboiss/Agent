namespace Agent.Conversations;

public sealed record ConversationResolveRequest(
    string Channel,
    string? ConversationId);
