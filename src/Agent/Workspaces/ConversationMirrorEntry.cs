using Agent.Conversations;

namespace Agent.Workspaces;

public sealed record ConversationMirrorEntry(
    string Id,
    string WorkspaceId,
    string? RunId,
    string CodexThreadId,
    string Channel,
    ConversationEntryRole Role,
    string Content,
    DateTimeOffset CreatedAt);
