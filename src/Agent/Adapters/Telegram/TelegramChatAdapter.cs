using Agent.Adapters.Chat;
using Agent.AgentFlow;

namespace Agent.Adapters.Telegram;

public sealed class TelegramChatAdapter : IChatAdapter
{
    public string Channel => "telegram";

    public Task Start(IAgentDispatcher dispatcher, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("Telegram adapter is scaffolded but not implemented.");
    }
}
