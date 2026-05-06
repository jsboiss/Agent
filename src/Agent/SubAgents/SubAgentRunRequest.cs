namespace Agent.SubAgents;

public sealed record SubAgentRunRequest(
    string ParentConversationId,
    string ParentEntryId,
    string Task,
    string Channel,
    SubAgentCapabilities Capabilities,
    bool RequiresConfirmation,
    string? NotificationTarget);
