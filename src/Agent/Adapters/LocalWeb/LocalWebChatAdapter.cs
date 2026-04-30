using Agent.Adapters.Chat;
using Agent.AgentFlow;

namespace Agent.Adapters.LocalWeb;

public sealed class LocalWebChatAdapter : IChatAdapter
{
    public string Channel => "local-web";

    public Task Start(IAgentDispatcher dispatcher, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
