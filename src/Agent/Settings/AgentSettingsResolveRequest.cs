using Agent.Conversations;

namespace Agent.Settings;

public sealed record AgentSettingsResolveRequest(
    Conversation Conversation,
    string Channel,
    string WorkspaceRootPath,
    IReadOnlyDictionary<string, string> PerMessageOverrides);
