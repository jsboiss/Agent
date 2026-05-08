using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Agent.Integrations.Composio;

public sealed class ComposioClient(
    HttpClient httpClient,
    IOptions<ComposioOptions> options,
    IComposioConnectionStore connectionStore) : IComposioClient
{
    private ComposioOptions Options { get; } = options.Value;

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void ConfigureHttpClient(HttpClient httpClient, ComposioOptions options)
    {
        httpClient.BaseAddress = new Uri(options.BaseUri.EndsWith("/", StringComparison.Ordinal) ? options.BaseUri : options.BaseUri + "/");
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            httpClient.DefaultRequestHeaders.Remove("x-api-key");
            httpClient.DefaultRequestHeaders.Add("x-api-key", options.ApiKey);
        }
    }

    public async Task<ComposioConnectionStatus> GetStatus(string toolkit, CancellationToken cancellationToken)
    {
        var stored = await connectionStore.Get(toolkit, cancellationToken);
        var configured = IsConfigured(toolkit);

        if (!configured || stored is null)
        {
            return new ComposioConnectionStatus(configured, false, stored?.AccountEmail, stored?.ConnectedAccountId, stored?.Status ?? "NOT_CONNECTED", stored?.UpdatedAt);
        }

        var refreshed = await TryRefreshConnection(toolkit, stored, cancellationToken);

        return new ComposioConnectionStatus(
            configured,
            string.Equals(refreshed.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase),
            refreshed.AccountEmail,
            refreshed.ConnectedAccountId,
            refreshed.Status,
            refreshed.UpdatedAt);
    }

    public async Task<ComposioConnectRequest> CreateConnectLink(
        string toolkit,
        string callbackUrl,
        CancellationToken cancellationToken)
    {
        EnsureConfigured(toolkit);
        var authConfigId = GetAuthConfigId(toolkit);
        var payload = new
        {
            user_id = Options.UserId,
            auth_config_id = authConfigId,
            callback_url = callbackUrl
        };
        using var response = await PostJson("connected_accounts/link", payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Composio connect link failed: {(int)response.StatusCode} {body}");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var id = GetString(root, "id", "connection_id", "connected_account_id") ?? string.Empty;
        var redirectUrl = GetString(root, "redirect_url", "redirectUrl", "url")
            ?? throw new InvalidOperationException("Composio connect link returned no redirect URL.");

        return new ComposioConnectRequest(id, redirectUrl);
    }

    public async Task<ComposioConnection> CompleteConnect(
        string toolkit,
        string connectedAccountId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured(toolkit);

        var connection = await FetchConnection(toolkit, connectedAccountId, cancellationToken)
            ?? new ComposioConnection(toolkit, connectedAccountId, "ACTIVE", null, DateTimeOffset.UtcNow);
        await connectionStore.Save(connection, cancellationToken);

        return connection;
    }

    public async Task Disconnect(string toolkit, CancellationToken cancellationToken)
    {
        await connectionStore.Clear(toolkit, cancellationToken);
    }

    public async Task<ComposioToolExecutionResult> ExecuteTool(
        string toolkit,
        string toolSlug,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var connection = await GetActiveConnection(toolkit, cancellationToken);
        var payload = new
        {
            user_id = Options.UserId,
            connected_account_id = connection.ConnectedAccountId,
            arguments
        };
        using var response = await PostJson($"tools/execute/{Uri.EscapeDataString(toolSlug)}", payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new ComposioToolExecutionResult(
                false,
                $"Composio tool {toolSlug} failed: {(int)response.StatusCode} {body}",
                new Dictionary<string, string>());
        }

        var successful = IsSuccessfulToolResponse(body);

        return new ComposioToolExecutionResult(
            successful,
            body,
            new Dictionary<string, string> { ["tool"] = toolSlug, ["connectedAccountId"] = connection.ConnectedAccountId });
    }

    public async Task<JsonElement> Proxy(
        string toolkit,
        HttpMethod method,
        string endpoint,
        IReadOnlyDictionary<string, string?> query,
        JsonElement? body,
        CancellationToken cancellationToken)
    {
        var connection = await GetActiveConnection(toolkit, cancellationToken);
        var payload = new
        {
            connected_account_id = connection.ConnectedAccountId,
            method = method.Method,
            endpoint = endpoint + ToQueryString(query),
            body
        };
        using var response = await PostJson("tools/execute/proxy", payload, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Composio proxy failed: {(int)response.StatusCode} {content}");
        }

        using var document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }

    private async Task<ComposioConnection> TryRefreshConnection(
        string toolkit,
        ComposioConnection stored,
        CancellationToken cancellationToken)
    {
        var refreshed = await FetchConnection(toolkit, stored.ConnectedAccountId, cancellationToken);

        if (refreshed is null)
        {
            return stored;
        }

        await connectionStore.Save(refreshed, cancellationToken);
        return refreshed;
    }

    private async Task<ComposioConnection?> FetchConnection(
        string toolkit,
        string connectedAccountId,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"connected_accounts/{Uri.EscapeDataString(connectedAccountId)}", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var document = JsonDocument.Parse(body);
        return ToConnection(toolkit, connectedAccountId, document.RootElement);
    }

    private ComposioConnection ToConnection(string toolkit, string fallbackId, JsonElement root)
    {
        var id = GetString(root, "id", "connected_account_id", "connectedAccountId") ?? fallbackId;
        var status = GetString(root, "status") ?? "ACTIVE";
        var accountEmail = GetString(root, "account_email", "accountEmail", "email")
            ?? FindString(root, "email");

        return new ComposioConnection(toolkit, id, status, accountEmail, DateTimeOffset.UtcNow);
    }

    private async Task<ComposioConnection> GetActiveConnection(string toolkit, CancellationToken cancellationToken)
    {
        var status = await GetStatus(toolkit, cancellationToken);

        if (!status.Connected || string.IsNullOrWhiteSpace(status.ConnectedAccountId))
        {
            throw new InvalidOperationException($"{toolkit} is not connected. Connect it from Settings first.");
        }

        return new ComposioConnection(
            toolkit,
            status.ConnectedAccountId,
            status.Status,
            status.AccountEmail,
            status.UpdatedAt ?? DateTimeOffset.UtcNow);
    }

    private async Task<HttpResponseMessage> PostJson(string path, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return await httpClient.PostAsync(
            path,
            new StringContent(json, Encoding.UTF8, "application/json"),
            cancellationToken);
    }

    private static bool IsSuccessfulToolResponse(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            return !root.TryGetProperty("successful", out var successful)
                || successful.ValueKind is not JsonValueKind.False;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private bool IsConfigured(string toolkit)
    {
        return !string.IsNullOrWhiteSpace(Options.ApiKey)
            && !string.IsNullOrWhiteSpace(Options.UserId)
            && !string.IsNullOrWhiteSpace(GetAuthConfigId(toolkit));
    }

    private void EnsureConfigured(string toolkit)
    {
        if (!IsConfigured(toolkit))
        {
            throw new InvalidOperationException($"Composio {toolkit} is not configured. Set Integrations:Composio:ApiKey, UserId, and the toolkit auth config id.");
        }
    }

    private string GetAuthConfigId(string toolkit)
    {
        return toolkit switch
        {
            ComposioToolkits.Gmail => Options.GmailAuthConfigId,
            ComposioToolkits.GoogleCalendar => Options.GoogleCalendarAuthConfigId,
            _ => string.Empty
        };
    }

    private static string ToQueryString(IReadOnlyDictionary<string, string?> values)
    {
        var parts = values
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value ?? string.Empty)}")
            .ToArray();

        return parts.Length == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    private static string? GetString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(name, out var property)
                && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }

        return null;
    }

    private static string? FindString(JsonElement root, string name)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }

                var nested = FindString(property.Value, name);

                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var nested = FindString(item, name);

                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }
}

public static class ComposioToolkits
{
    public const string Gmail = "gmail";

    public const string GoogleCalendar = "googlecalendar";
}
