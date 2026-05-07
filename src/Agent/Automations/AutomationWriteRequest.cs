using Agent.SubAgents;

namespace Agent.Automations;

public sealed record AutomationWriteRequest(
    string Name,
    string Task,
    string Schedule,
    string ConversationId,
    string Channel,
    string? NotificationTarget,
    SubAgentCapabilities Capabilities);
