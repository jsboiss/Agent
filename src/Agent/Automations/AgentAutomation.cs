using Agent.SubAgents;

namespace Agent.Automations;

public sealed record AgentAutomation(
    string Id,
    string Name,
    string Task,
    string Schedule,
    AutomationStatus Status,
    string ConversationId,
    string Channel,
    string? NotificationTarget,
    SubAgentCapabilities Capabilities,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastRunAt,
    string? LastRunId,
    string? LastResult,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
