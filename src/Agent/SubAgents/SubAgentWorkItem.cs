namespace Agent.SubAgents;

public sealed record SubAgentWorkItem(
    string RunId,
    string WorkspaceId,
    string ChildConversationId,
    string ParentConversationId,
    string ParentEntryId,
    string Task,
    string Channel,
    bool AllowsMutation);
