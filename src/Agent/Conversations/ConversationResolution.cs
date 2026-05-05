namespace Agent.Conversations;

public sealed record ConversationResolution(
    Conversation Conversation,
    bool Created);
