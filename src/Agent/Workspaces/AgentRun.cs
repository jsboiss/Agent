namespace Agent.Workspaces;

public sealed record AgentRun(
    string Id,
    string WorkspaceId,
    string Prompt,
    string? CodexThreadId,
    AgentRunStatus Status,
    AgentRunKind Kind,
    string Channel,
    string? ParentRunId,
    string? ParentCodexThreadId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? FinalResponse,
    string? Error);
