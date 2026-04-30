using Agent.Memory;

namespace Agent.Claude;

public sealed record ClaudeTurnRequest(
    string ConversationId,
    string UserMessage,
    string MemoryContext,
    IReadOnlyList<MemoryRecord> InjectedMemories,
    IReadOnlyList<string> AvailableTools);
