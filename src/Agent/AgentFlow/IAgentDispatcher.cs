namespace Agent.AgentFlow;

public interface IAgentDispatcher
{
    Task<AgentTurnResult> Dispatch(AgentTurnRequest request, CancellationToken cancellationToken);
}
