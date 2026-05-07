namespace Agent.Context;

public interface IContextOrchestrator
{
    Task<EvidenceBundle> Gather(
        ContextPlan plan,
        ContextPlanningRequest request,
        CancellationToken cancellationToken);
}
