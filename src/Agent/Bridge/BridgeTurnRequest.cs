using Agent.Memory;

namespace Agent.Bridge;

public sealed record BridgeTurnRequest(
    string ConversationId,
    string UserMessage,
    string MemoryContext,
    IReadOnlyList<MemoryRecord> InjectedMemories,
    IReadOnlyList<string> AvailableTools);
