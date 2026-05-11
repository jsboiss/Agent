using Agent.SubAgents;

namespace Agent.Automations;

public sealed record AgentAutomation(
    string Id,
    string Name,
    string Task,
    string Schedule,
    AutomationStatus Status,
    AutomationExecutionMode Mode,
    string ConversationId,
    string Channel,
    string? NotificationTarget,
    SubAgentCapabilities Capabilities,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastRunAt,
    string? LastRunId,
    string? LastResult,
    string? WorkspaceRootPath,
    string? SkillIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
