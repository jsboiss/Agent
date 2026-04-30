using Agent.Dispatching;

namespace Agent.Channels.Chat;

public interface IChatChannel
{
    string Channel { get; }

    Task Start(IAgentDispatcher dispatcher, CancellationToken cancellationToken);
}
