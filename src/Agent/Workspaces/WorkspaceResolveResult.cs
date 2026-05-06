namespace Agent.Workspaces;

public sealed record WorkspaceResolveResult(
    AgentWorkspace Workspace,
    bool Created);
