namespace Agent.Workspaces;

public sealed record AgentRouteResolution(
    AgentWorkspace Workspace,
    AgentRouteKind RouteKind,
    string? CodexThreadId,
    string? RunId,
    bool AllowsMutation,
    string Reason);
