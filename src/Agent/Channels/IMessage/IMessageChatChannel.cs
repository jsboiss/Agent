using Agent.Channels.Chat;
using Agent.Messages;

namespace Agent.Channels.IMessage;

public sealed class IMessageChatChannel : IChatChannel
{
    public string Channel => "imessage";

    public Task Start(IMessageProcessor processor, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("iMessage channel is scaffolded but not implemented.");
    }
}
