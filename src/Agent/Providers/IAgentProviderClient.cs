namespace Agent.Providers;

public interface IAgentProviderClient
{
    AgentProviderType Type { get; }

    Task<AgentProviderResult> Send(AgentProviderRequest request, CancellationToken cancellationToken);
}
