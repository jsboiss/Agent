using Agent.Conversations;

namespace Agent.Compaction;

public sealed record ConversationCompactionResult(
    ConversationSummary Summary,
    int ExactEntryCount);
