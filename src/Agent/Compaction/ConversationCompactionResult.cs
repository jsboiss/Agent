using Agent.Conversations;

namespace Agent.Compaction;

public sealed record ConversationCompactionResult(
    ConversationSummary Summary,
    int ExactEntryCount,
    int NewlyCompactedEntryCount,
    int MemoryExtractionEntryCount,
    int ProposedMemoryCount,
    int WrittenMemoryCount,
    int SkippedMemoryCount);
