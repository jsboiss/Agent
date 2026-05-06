using Agent.Memory;
using Agent.Resources;
using Agent.Tools;
using Agent.Workspaces;

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
    IReadOnlyList<AgentProviderToolResult> ToolResults,
    string WorkspaceRootPath = "",
    string? CodexThreadId = null,
    AgentRouteKind RouteKind = AgentRouteKind.Chat,
    string? AgentRunId = null,
    string SandboxMode = "workspace-write",
    string ApprovalPolicy = "never",
    string RecentMirroredContext = "",
    string ChannelNotes = "",
    bool AllowsMutation = false);
