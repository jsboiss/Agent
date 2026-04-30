using Agent.Channels.Chat;
using Agent.AgentFlow;

namespace Agent.Channels.IMessage;

public sealed class IMessageChatChannel : IChatChannel
{
    public string Channel => "imessage";

    public Task Start(IAgentDispatcher dispatcher, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("iMessage channel is scaffolded but not implemented.");
    }
}
