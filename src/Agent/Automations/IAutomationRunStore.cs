namespace Agent.Automations;

public interface IAutomationRunStore
{
    Task<AgentAutomationRun?> Get(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentAutomationRun>> List(string? automationId, int limit, CancellationToken cancellationToken);

    Task<AgentAutomationRun?> TryStart(
        AgentAutomation automation,
        AutomationRunTrigger trigger,
        CancellationToken cancellationToken);

    Task<AgentAutomationRun> Complete(
        string id,
        AutomationRunStatus status,
        string? subAgentRunId,
        string? outputSummary,
        string? error,
        CancellationToken cancellationToken);
}
