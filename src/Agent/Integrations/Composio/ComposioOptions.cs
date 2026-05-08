namespace Agent.Integrations.Composio;

public sealed class ComposioOptions
{
    public const string SectionName = "Integrations:Composio";

    public string ApiKey { get; init; } = string.Empty;

    public string BaseUri { get; init; } = "https://backend.composio.dev/api/v3.1/";

    public string UserId { get; init; } = "main-agent-owner";

    public string GmailAuthConfigId { get; init; } = string.Empty;

    public string GoogleCalendarAuthConfigId { get; init; } = string.Empty;
}
