using System.Text.Json;

namespace Agent.Integrations.Composio;

public interface IComposioClient
{
    Task<ComposioConnectionStatus> GetStatus(string toolkit, CancellationToken cancellationToken);

    Task<ComposioConnectRequest> CreateConnectLink(
        string toolkit,
        string callbackUrl,
        CancellationToken cancellationToken);

    Task<ComposioConnection> CompleteConnect(
        string toolkit,
        string connectedAccountId,
        CancellationToken cancellationToken);

    Task Disconnect(string toolkit, CancellationToken cancellationToken);

    Task<ComposioToolExecutionResult> ExecuteTool(
        string toolkit,
        string toolSlug,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken);

    Task<JsonElement> Proxy(
        string toolkit,
        HttpMethod method,
        string endpoint,
        IReadOnlyDictionary<string, string?> query,
        JsonElement? body,
        CancellationToken cancellationToken);
}
