using Agent.Channels.Chat;
using Agent.Messages;

namespace Agent.Channels.LocalWeb;

public sealed class LocalWebChatChannel : IChatChannel
{
    public string Channel => "local-web";

    public Task Start(IMessageProcessor processor, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
