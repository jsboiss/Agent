using Agent.Messages;

namespace Agent.Channels.Chat;

public interface IChatChannel
{
    string Channel { get; }

    Task Start(IMessageProcessor processor, CancellationToken cancellationToken);
}
