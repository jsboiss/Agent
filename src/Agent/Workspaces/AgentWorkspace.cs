namespace Agent.Workspaces;

public sealed record AgentWorkspace(
    string Id,
    string Name,
    string RootPath,
    string? ChatThreadId,
    string? WorkThreadId,
    string? ActiveRunId,
    bool RemoteExecutionAllowed,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
