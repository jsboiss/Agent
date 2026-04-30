using Agent.Channels.Chat;
using Agent.Pipeline;

namespace Agent.Channels.Telegram;

public sealed class TelegramChatChannel : IChatChannel
{
    public string Channel => "telegram";

    public Task Start(IAgentPipeline pipeline, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("Telegram channel is scaffolded but not implemented.");
    }
}
