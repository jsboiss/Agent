using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Agent.Channels.Chat;
using Agent.Messages;
using Agent.Notifications;
using Microsoft.Extensions.Options;

namespace Agent.Channels.Telegram;

public sealed class TelegramChatChannel(
    IHttpClientFactory httpClientFactory,
    IOptions<TelegramChannelOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<TelegramChatChannel> logger) : BackgroundService, IChatChannel, IChannelNotifier
{
    public string Channel => "telegram";

    private TelegramChannelOptions Options { get; } = options.Value;

    private long Offset { get; set; }

    public Task Start(IMessageProcessor processor, CancellationToken cancellationToken)
    {
        return ExecuteAsync(cancellationToken);
    }

    public bool CanNotify(string channel)
    {
        return string.Equals(channel, Channel, StringComparison.OrdinalIgnoreCase);
    }

    public async Task Send(string? target, string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        foreach (var chunk in Chunk(Sanitize(message), 3500))
        {
            await SendMessage(target, chunk, cancellationToken);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(Options.BotToken))
        {
            logger.LogInformation("Telegram channel disabled because Channels:Telegram:BotToken is not configured.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Poll(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, Options.PollingIntervalSeconds)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Telegram polling failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task Poll(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        var response = await client.GetFromJsonAsync<TelegramUpdatesResponse>(
            GetApiUrl($"getUpdates?timeout=20&offset={Offset}"),
            cancellationToken);

        if (response?.Ok != true || response.Result is null)
        {
            return;
        }

        foreach (var update in response.Result)
        {
            Offset = Math.Max(Offset, update.UpdateId + 1);
            var message = update.Message;

            if (message?.Chat is null || string.IsNullOrWhiteSpace(message.Text))
            {
                continue;
            }

            var chatId = message.Chat.Id.ToString();

            if (!IsTrusted(chatId))
            {
                await SendMessage(chatId, "This Telegram chat is not trusted for MainAgent.", cancellationToken);
                continue;
            }

            using var scope = scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IMessageProcessor>();
            var result = await processor.Process(
                new MessageRequest(
                    null,
                    Channel,
                    message.Text.Trim(),
                    DateTimeOffset.UtcNow,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["trustedSender"] = "true",
                        ["telegramChatId"] = chatId
                    }),
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(result.AssistantMessage))
            {
                await Send(chatId, result.AssistantMessage, cancellationToken);
            }
        }
    }

    private bool IsTrusted(string chatId)
    {
        return Options.TrustedChatIds.Length == 0
            || Options.TrustedChatIds.Contains(chatId, StringComparer.OrdinalIgnoreCase);
    }

    private async Task SendMessage(string chatId, string text, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        var response = await client.PostAsJsonAsync(
            GetApiUrl("sendMessage"),
            new
            {
                chat_id = chatId,
                text,
                disable_web_page_preview = true
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Telegram send failed with {Status}: {Body}", response.StatusCode, body);
        }
    }

    private string GetApiUrl(string method)
    {
        return $"https://api.telegram.org/bot{Options.BotToken}/{method}";
    }

    private static string Sanitize(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    }

    private static IEnumerable<string> Chunk(string value, int size)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        for (var x = 0; x < value.Length; x += size)
        {
            yield return value.Substring(x, Math.Min(size, value.Length - x));
        }
    }

    private sealed record TelegramUpdatesResponse(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("result")] IReadOnlyList<TelegramUpdate>? Result);

    private sealed record TelegramUpdate(
        [property: JsonPropertyName("update_id")] long UpdateId,
        [property: JsonPropertyName("message")] TelegramMessage? Message);

    private sealed record TelegramMessage(
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("chat")] TelegramChat? Chat);

    private sealed record TelegramChat([property: JsonPropertyName("id")] long Id);
}
