using Agent.Conversations;

namespace Agent.Workspaces;

public interface IAgentWorkspaceStore
{
    Task<WorkspaceResolveResult> GetOrCreateActive(
        string defaultRootPath,
        CancellationToken cancellationToken);

    Task<AgentWorkspace> SetActiveRun(
        string workspaceId,
        string? runId,
        CancellationToken cancellationToken);

    Task<AgentWorkspace> SetThreadId(
        string workspaceId,
        AgentRouteKind routeKind,
        string threadId,
        CancellationToken cancellationToken);

    Task<AgentWorkspace> SetRemoteExecutionAllowed(
        string workspaceId,
        bool allowed,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentWorkspace>> List(CancellationToken cancellationToken);
}

public interface IAgentRunStore
{
    Task<AgentRun> Create(
        string workspaceId,
        string prompt,
        AgentRunKind kind,
        string channel,
        string? parentRunId,
        string? parentCodexThreadId,
        CancellationToken cancellationToken);

    Task<AgentRun?> Get(string runId, CancellationToken cancellationToken);

    Task<AgentRun?> GetActive(string workspaceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentRun>> List(
        AgentRunKind? kind,
        int limit,
        CancellationToken cancellationToken);

    Task<AgentRun> Update(
        string runId,
        AgentRunStatus status,
        string? codexThreadId,
        string? finalResponse,
        string? error,
        CancellationToken cancellationToken);
}

public interface IConversationMirrorStore
{
    Task<ConversationMirrorEntry> Add(
        string workspaceId,
        string? runId,
        string codexThreadId,
        string channel,
        ConversationEntryRole role,
        string content,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationMirrorEntry>> ListRecent(
        string workspaceId,
        string codexThreadId,
        int limit,
        CancellationToken cancellationToken);
}
