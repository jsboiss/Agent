using Agent.Conversations;

namespace Agent.Compaction;

public interface ICompactionMemoryExtractor
{
    Task<CompactionMemoryExtractionResult> Extract(
        Conversation conversation,
        IReadOnlyList<ConversationEntry> entries,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken);
}
