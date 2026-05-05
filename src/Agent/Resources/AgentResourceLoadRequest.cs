using Agent.Conversations;
using Agent.Providers;

namespace Agent.Resources;

public sealed record AgentResourceLoadRequest(
    Conversation Conversation,
    string Channel,
    AgentProviderType ProviderType);
