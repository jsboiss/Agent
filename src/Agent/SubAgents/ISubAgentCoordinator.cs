namespace Agent.SubAgents;

public interface ISubAgentCoordinator
{
    Task<SubAgentRunResult> CreateAndReport(
        SubAgentRunRequest request,
        CancellationToken cancellationToken);
}
