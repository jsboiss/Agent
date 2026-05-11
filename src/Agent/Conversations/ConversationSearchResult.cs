namespace Agent.Conversations;

public sealed record ConversationSearchResult(
    Conversation Conversation,
    ConversationEntry Entry,
    double Rank);
