namespace Agent.SubAgents;

public interface ISubAgentWorkQueue
{
    ValueTask Enqueue(SubAgentWorkItem item, CancellationToken cancellationToken);

    ValueTask<SubAgentWorkItem> Dequeue(CancellationToken cancellationToken);
}
