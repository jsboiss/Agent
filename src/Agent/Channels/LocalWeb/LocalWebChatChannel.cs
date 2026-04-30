using Agent.Channels.Chat;
using Agent.Dispatching;

namespace Agent.Channels.LocalWeb;

public sealed class LocalWebChatChannel : IChatChannel
{
    public string Channel => "local-web";

    public Task Start(IAgentDispatcher dispatcher, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
