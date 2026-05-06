using Agent.Conversations;
using Agent.Providers;
using Agent.Settings;

namespace Agent.Tokens;

public interface IAgentTokenTracker
{
    AgentTokenUsage Measure(
        AgentProviderRequest request,
        AgentProviderResult? result,
        AgentSettings settings,
        IReadOnlyList<ConversationEntry> mainContext);

    IReadOnlyDictionary<string, string> ToMetadata(AgentTokenUsage usage);

    AgentTokenUsage Aggregate(IReadOnlyList<IReadOnlyDictionary<string, string>> metadata);
}
