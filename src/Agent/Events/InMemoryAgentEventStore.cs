namespace Agent.Events;

public sealed class InMemoryAgentEventStore : IAgentEventStore
{
    private object SyncRoot { get; } = new();

    private List<AgentEvent> Events { get; } = [];

    public Task Publish(AgentEvent agentEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (SyncRoot)
        {
            Events.Add(agentEvent);

            if (Events.Count > 1000)
            {
                Events.RemoveRange(0, Events.Count - 1000);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AgentEvent>> List(
        string? conversationId,
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (SyncRoot)
        {
            IEnumerable<AgentEvent> events = Events;

            if (!string.IsNullOrWhiteSpace(conversationId))
            {
                events = events.Where(x => string.Equals(x.ConversationId, conversationId, StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult<IReadOnlyList<AgentEvent>>(events
                .OrderByDescending(x => x.CreatedAt)
                .Take(Math.Clamp(limit, 1, 500))
                .ToArray());
        }
    }
}
