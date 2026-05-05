namespace Agent.Resources;

public interface IAgentResourceLoader
{
    Task<AgentResourceContext> Load(AgentResourceLoadRequest request, CancellationToken cancellationToken);
}
