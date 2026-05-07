namespace Agent.Channels.Telegram;

public sealed class TelegramChannelOptions
{
    public static string SectionName => "Channels:Telegram";

    public string BotToken { get; set; } = string.Empty;

    public string[] TrustedChatIds { get; set; } = [];

    public int PollingIntervalSeconds { get; set; } = 2;
}
