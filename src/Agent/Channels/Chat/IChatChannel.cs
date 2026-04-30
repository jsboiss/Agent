using Agent.Pipeline;

namespace Agent.Channels.Chat;

public interface IChatChannel
{
    string Channel { get; }

    Task Start(IAgentPipeline pipeline, CancellationToken cancellationToken);
}
