using Agent.Settings;

namespace Agent.Providers;

public interface IAgentProviderToolLoop
{
    Task<AgentProviderResult> Run(
        IAgentProviderClient provider,
        AgentProviderRequest initialRequest,
        string channel,
        string parentEntryId,
        AgentSettings settings,
        string? notificationTarget,
        CancellationToken cancellationToken);
}
