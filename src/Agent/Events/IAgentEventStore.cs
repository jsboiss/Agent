namespace Agent.Events;

public interface IAgentEventStore : IAgentEventSink
{
    Task<IReadOnlyList<AgentEvent>> List(string? conversationId, int limit, CancellationToken cancellationToken);
}
