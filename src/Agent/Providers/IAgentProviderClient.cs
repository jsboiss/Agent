namespace Agent.Providers;

public interface IAgentProviderClient
{
    AgentProviderType Kind { get; }

    Task<AgentProviderResult> Send(AgentProviderRequest request, CancellationToken cancellationToken);
}
