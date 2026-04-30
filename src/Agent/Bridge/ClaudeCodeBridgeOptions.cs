namespace Agent.Bridge;

public sealed record ClaudeCodeBridgeOptions
{
    public required string Command { get; init; }

    public string? WorkingDirectory { get; init; }

    public bool StripAnthropicApiKey { get; init; } = true;
}
