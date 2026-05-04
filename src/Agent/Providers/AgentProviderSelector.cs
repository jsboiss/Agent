namespace Agent.Providers;

public sealed class AgentProviderSelector(IEnumerable<IAgentProviderClient> clients) : IAgentProviderSelector
{
    private IReadOnlyDictionary<AgentProviderType, IAgentProviderClient> Clients { get; } =
        clients.ToDictionary(x => x.Kind);

    public IAgentProviderClient Get(AgentProviderType kind)
    {
        if (Clients.TryGetValue(kind, out var client))
        {
            return client;
        }

        throw new InvalidOperationException($"Agent provider '{kind}' is not registered.");
    }
}
