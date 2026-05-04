using Agent.Channels.Chat;
using Agent.Messages;

namespace Agent.Channels.Telegram;

public sealed class TelegramChatChannel : IChatChannel
{
    public string Channel => "telegram";

    public Task Start(IMessageProcessor processor, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("Telegram channel is scaffolded but not implemented.");
    }
}
