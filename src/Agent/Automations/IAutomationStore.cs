namespace Agent.Automations;

public interface IAutomationStore
{
    Task<AgentAutomation> Create(AutomationWriteRequest request, CancellationToken cancellationToken);

    Task<AgentAutomation?> Get(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentAutomation>> List(CancellationToken cancellationToken);

    Task<AgentAutomation> SetStatus(string id, AutomationStatus status, CancellationToken cancellationToken);

    Task<AgentAutomation> UpdateRunResult(
        string id,
        DateTimeOffset? nextRunAt,
        string? runId,
        string? result,
        CancellationToken cancellationToken);

    Task Delete(string id, CancellationToken cancellationToken);
}
