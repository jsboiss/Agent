using Agent.Memory;

namespace Agent.Providers;

public sealed record AgentProviderRequest(
    AgentProviderType Kind,
    string ConversationId,
    string UserMessage,
    string MemoryContext,
    IReadOnlyList<MemoryRecord> InjectedMemories,
    IReadOnlyList<string> AvailableTools);
