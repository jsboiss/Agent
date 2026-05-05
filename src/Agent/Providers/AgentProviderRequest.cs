using Agent.Memory;
using Agent.Resources;

namespace Agent.Providers;

public sealed record AgentProviderRequest(
    AgentProviderType Kind,
    string ConversationId,
    string UserMessage,
    AgentResourceContext Resources,
    string MemoryContext,
    IReadOnlyList<MemoryRecord> InjectedMemories,
    IReadOnlyList<string> AvailableTools);
