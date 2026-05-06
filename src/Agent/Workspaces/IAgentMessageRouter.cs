namespace Agent.Workspaces;

public interface IAgentMessageRouter
{
    Task<AgentRouteResolution> Resolve(
        AgentWorkspace workspace,
        string channel,
        string userMessage,
        CancellationToken cancellationToken);
}
