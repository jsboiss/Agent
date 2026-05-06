using Agent.Conversations;

namespace Agent.Compaction;

public sealed record ConversationCompactionRequest(
    Conversation Conversation,
    int RecentEntryCount,
    int ThresholdTokens);
