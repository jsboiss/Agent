namespace Agent.Providers;

public interface IAgentProviderSelector
{
    IAgentProviderClient Get(AgentProviderType kind);
}
