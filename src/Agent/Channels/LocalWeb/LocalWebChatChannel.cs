using Agent.Channels.Chat;
using Agent.Pipeline;

namespace Agent.Channels.LocalWeb;

public sealed class LocalWebChatChannel : IChatChannel
{
    public string Channel => "local-web";

    public Task Start(IAgentPipeline pipeline, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
