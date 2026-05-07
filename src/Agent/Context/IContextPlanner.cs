namespace Agent.Context;

public interface IContextPlanner
{
    Task<ContextPlan> Plan(ContextPlanningRequest request, CancellationToken cancellationToken);
}
