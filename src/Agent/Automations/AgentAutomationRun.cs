namespace Agent.Automations;

public sealed record AgentAutomationRun(
    string Id,
    string AutomationId,
    string? SubAgentRunId,
    AutomationRunStatus Status,
    AutomationRunTrigger Trigger,
    string? OutputSummary,
    string? Error,
    string? WorkspaceRootPath,
    string? SkillIds,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
