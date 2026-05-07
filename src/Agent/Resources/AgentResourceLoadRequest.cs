using Agent.Conversations;
using Agent.Providers;
using Agent.Settings;
using Agent.SubAgents;

namespace Agent.Resources;

public sealed record AgentResourceLoadRequest(
    Conversation Conversation,
    string Channel,
    AgentProviderType ProviderType,
    AgentSettings Settings,
    string WorkspaceRootPath,
    SubAgentCapabilities Capabilities = SubAgentCapabilities.None);
