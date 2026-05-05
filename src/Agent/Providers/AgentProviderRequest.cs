using Agent.Memory;
using Agent.Resources;
using Agent.Tools;

namespace Agent.Providers;

public sealed record AgentProviderRequest(
    AgentProviderType Kind,
    string ConversationId,
    string UserMessage,
    AgentResourceContext Resources,
    string MemoryContext,
    IReadOnlyList<MemoryRecord> InjectedMemories,
    IReadOnlyList<AgentToolDefinition> AvailableTools,
    IReadOnlyList<AgentProviderToolCall> PriorToolCalls,
    IReadOnlyList<AgentProviderToolResult> ToolResults);
