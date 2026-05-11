using System.Threading.Channels;

namespace Agent.Messages;

public sealed class AgentPostResponseQueue : IAgentPostResponseQueue
{
    private Channel<AgentPostResponseWorkItem> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<AgentPostResponseWorkItem>();

    public void Enqueue(AgentPostResponseWorkItem item)
    {
        Channel.Writer.TryWrite(item);
    }

    public ValueTask<AgentPostResponseWorkItem> Dequeue(CancellationToken cancellationToken)
    {
        return Channel.Reader.ReadAsync(cancellationToken);
    }
}

