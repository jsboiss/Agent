using System.Text;
using System.Text.Json;
using Agent.Integrations.Composio;

namespace Agent.Email;

public sealed class GmailProvider(IComposioClient composioClient) : IEmailProvider
{
    public async Task<EmailConnectionStatus> GetStatus(CancellationToken cancellationToken)
    {
        var status = await composioClient.GetStatus(ComposioToolkits.Gmail, cancellationToken);

        return new EmailConnectionStatus(
            status.Configured,
            status.Connected,
            status.AccountEmail,
            status.UpdatedAt);
    }

    public async Task<string> GetAuthorizationUrl(
        string state,
        string callbackUrl,
        CancellationToken cancellationToken)
    {
        var separator = callbackUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var request = await composioClient.CreateConnectLink(
            ComposioToolkits.Gmail,
            $"{callbackUrl}{separator}state={Uri.EscapeDataString(state)}",
            cancellationToken);

        return request.RedirectUrl;
    }

    public async Task Connect(string connectedAccountId, CancellationToken cancellationToken)
    {
        await composioClient.CompleteConnect(ComposioToolkits.Gmail, connectedAccountId, cancellationToken);
    }

    public async Task Disconnect(CancellationToken cancellationToken)
    {
        await composioClient.Disconnect(ComposioToolkits.Gmail, cancellationToken);
    }

    public async Task<IReadOnlyList<GmailMessageSummary>> SearchMessages(
        GmailMessageQuery query,
        CancellationToken cancellationToken)
    {
        var root = await composioClient.Proxy(
            ComposioToolkits.Gmail,
            HttpMethod.Get,
            "/gmail/v1/users/me/messages",
            new Dictionary<string, string?>
            {
                ["q"] = query.Query,
                ["maxResults"] = Math.Clamp(query.Limit, 1, 20).ToString()
            },
            null,
            cancellationToken);
        root = UnwrapData(root);
        var messages = FindArray(root, "messages");

        if (messages is null)
        {
            return [];
        }

        List<GmailMessageSummary> results = [];

        foreach (var message in messages.Value.EnumerateArray())
        {
            var id = GetString(message, "id");

            if (!string.IsNullOrWhiteSpace(id))
            {
                results.Add(await GetMessage(id, cancellationToken));
            }
        }

        return results;
    }

    public async Task<GmailMessageSummary> GetMessage(string messageId, CancellationToken cancellationToken)
    {
        var root = await composioClient.Proxy(
            ComposioToolkits.Gmail,
            HttpMethod.Get,
            $"/gmail/v1/users/me/messages/{Uri.EscapeDataString(messageId)}",
            new Dictionary<string, string?> { ["format"] = "metadata" },
            null,
            cancellationToken);
        root = UnwrapData(root);

        return ToSummary(root);
    }

    public async Task<string> CreateDraft(GmailDraftRequest request, CancellationToken cancellationToken)
    {
        var raw = ToBase64Url(BuildMessage(request));
        var payload = JsonSerializer.SerializeToElement(new
        {
            message = new
            {
                raw
            }
        });
        var root = await composioClient.Proxy(
            ComposioToolkits.Gmail,
            HttpMethod.Post,
            "/gmail/v1/users/me/drafts",
            new Dictionary<string, string?>(),
            payload,
            cancellationToken);
        root = UnwrapData(root);

        return GetString(root, "id") ?? string.Empty;
    }

    public async Task<string> SendDraft(string draftId, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            id = draftId
        });
        var root = await composioClient.Proxy(
            ComposioToolkits.Gmail,
            HttpMethod.Post,
            "/gmail/v1/users/me/drafts/send",
            new Dictionary<string, string?>(),
            payload,
            cancellationToken);
        root = UnwrapData(root);

        return GetString(root, "id") ?? draftId;
    }

    private static GmailMessageSummary ToSummary(JsonElement root)
    {
        var payload = root.TryGetProperty("payload", out var payloadElement) ? payloadElement : root;
        var headers = payload.TryGetProperty("headers", out var headersElement) && headersElement.ValueKind == JsonValueKind.Array
            ? headersElement
            : default;

        return new GmailMessageSummary(
            GetString(root, "id") ?? string.Empty,
            GetString(root, "threadId") ?? string.Empty,
            GetHeader(headers, "From"),
            GetHeader(headers, "To"),
            GetHeader(headers, "Subject"),
            TryParseDate(GetHeader(headers, "Date")),
            GetString(root, "snippet") ?? string.Empty);
    }

    private static JsonElement UnwrapData(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
                ? data
                : root;
    }

    private static string BuildMessage(GmailDraftRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"To: {request.To}");

        if (!string.IsNullOrWhiteSpace(request.Cc))
        {
            builder.AppendLine($"Cc: {request.Cc}");
        }

        if (!string.IsNullOrWhiteSpace(request.Bcc))
        {
            builder.AppendLine($"Bcc: {request.Bcc}");
        }

        builder.AppendLine($"Subject: {request.Subject}");
        builder.AppendLine("Content-Type: text/plain; charset=utf-8");
        builder.AppendLine();
        builder.Append(request.Body);

        return builder.ToString();
    }

    private static string ToBase64Url(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static DateTimeOffset? TryParseDate(string value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string GetHeader(JsonElement headers, string name)
    {
        if (headers.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var header in headers.EnumerateArray())
        {
            if (string.Equals(GetString(header, "name"), name, StringComparison.OrdinalIgnoreCase))
            {
                return GetString(header, "value") ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static JsonElement? FindArray(JsonElement root, string propertyName)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.Array)
                {
                    return property.Value;
                }

                var nested = FindArray(property.Value, propertyName);

                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string? GetString(JsonElement root, string name)
    {
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }
}
