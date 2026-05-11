using Agent.SubAgents;

namespace Agent.Automations;

public sealed record AutomationWriteRequest(
    string Name,
    string Task,
    string Schedule,
    AutomationExecutionMode Mode,
    string ConversationId,
    string Channel,
    string? NotificationTarget,
    SubAgentCapabilities Capabilities,
    string? WorkspaceRootPath = null,
    string? SkillIds = null);
