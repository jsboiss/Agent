using Agent.Channels.Chat;
using Agent.Pipeline;

namespace Agent.Channels.IMessage;

public sealed class IMessageChatChannel : IChatChannel
{
    public string Channel => "imessage";

    public Task Start(IAgentPipeline pipeline, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("iMessage channel is scaffolded but not implemented.");
    }
}
