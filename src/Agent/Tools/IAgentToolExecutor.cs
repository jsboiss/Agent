namespace Agent.Tools;

public interface IAgentToolExecutor
{
    Task<AgentToolResult> Execute(AgentToolRequest request, CancellationToken cancellationToken);
}
