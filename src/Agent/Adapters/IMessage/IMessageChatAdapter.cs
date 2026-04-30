using Agent.Adapters.Chat;
using Agent.AgentFlow;

namespace Agent.Adapters.IMessage;

public sealed class IMessageChatAdapter : IChatAdapter
{
    public string Channel => "imessage";

    public Task Start(IAgentDispatcher dispatcher, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("iMessage adapter is scaffolded but not implemented.");
    }
}
