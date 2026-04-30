using Agent.Channels.Chat;
using Agent.Dispatching;

namespace Agent.Channels.Telegram;

public sealed class TelegramChatChannel : IChatChannel
{
    public string Channel => "telegram";

    public Task Start(IAgentDispatcher dispatcher, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("Telegram channel is scaffolded but not implemented.");
    }
}
