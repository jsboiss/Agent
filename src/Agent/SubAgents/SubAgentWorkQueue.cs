using System.Threading.Channels;

namespace Agent.SubAgents;

public sealed class SubAgentWorkQueue : ISubAgentWorkQueue
{
    private Channel<SubAgentWorkItem> Queue { get; } = Channel.CreateUnbounded<SubAgentWorkItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public async ValueTask Enqueue(SubAgentWorkItem item, CancellationToken cancellationToken)
    {
        await Queue.Writer.WriteAsync(item, cancellationToken);
    }

    public async ValueTask<SubAgentWorkItem> Dequeue(CancellationToken cancellationToken)
    {
        return await Queue.Reader.ReadAsync(cancellationToken);
    }
}
