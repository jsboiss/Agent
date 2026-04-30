namespace Agent.Dispatching;

public interface IAgentDispatcher
{
    Task<AgentResult> Dispatch(AgentRequest request, CancellationToken cancellationToken);
}
