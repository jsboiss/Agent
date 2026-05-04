using Agent.Providers;

namespace Agent.Providers.Codex;

public sealed class CodexProviderClient : IAgentProviderClient
{
    public AgentProviderType Kind => AgentProviderType.Codex;

    public Task<AgentProviderResult> Send(AgentProviderRequest request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("Codex provider is scaffolded but not implemented.");
    }
}
