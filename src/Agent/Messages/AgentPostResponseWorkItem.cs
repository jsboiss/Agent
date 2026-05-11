using Agent.Conversations;
using Agent.Settings;
using Agent.Workspaces;

namespace Agent.Messages;

public sealed record AgentPostResponseWorkItem(
    string ConversationId,
    ConversationEntry UserEntry,
    ConversationEntry AssistantEntry,
    AgentWorkspace Workspace,
    AgentSettings Settings);

