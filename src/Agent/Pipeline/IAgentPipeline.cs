namespace Agent.Pipeline;

public interface IAgentPipeline
{
    Task<AgentResult> Process(AgentRequest request, CancellationToken cancellationToken);
}
