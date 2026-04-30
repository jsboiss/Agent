namespace Agent.Events;

public interface IAgentEventSink
{
    Task Publish(AgentEvent agentEvent, CancellationToken cancellationToken);
}
