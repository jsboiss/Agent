namespace Agent.Messages;

public interface IAgentPostResponseQueue
{
    void Enqueue(AgentPostResponseWorkItem item);

    ValueTask<AgentPostResponseWorkItem> Dequeue(CancellationToken cancellationToken);
}

