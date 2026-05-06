using Agent.Conversations;

namespace Agent.Memory;

public sealed record MemoryExtractionRequest(
    string ConversationId,
    ConversationEntry UserEntry,
    ConversationEntry? AssistantEntry,
    IReadOnlyList<ConversationEntry> ToolEntries,
    IReadOnlyList<MemoryRecord> InjectedMemories,
    IReadOnlyDictionary<string, string> Settings);
