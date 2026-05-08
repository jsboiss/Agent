namespace Agent.Integrations.Composio;

public sealed record ComposioConnection(
    string Toolkit,
    string ConnectedAccountId,
    string Status,
    string? AccountEmail,
    DateTimeOffset UpdatedAt);

public sealed record ComposioConnectionStatus(
    bool Configured,
    bool Connected,
    string? AccountEmail,
    string? ConnectedAccountId,
    string Status,
    DateTimeOffset? UpdatedAt);

public sealed record ComposioConnectRequest(
    string Id,
    string RedirectUrl);

public sealed record ComposioToolExecutionResult(
    bool Successful,
    string Content,
    IReadOnlyDictionary<string, string> Metadata);
