namespace Agent.Claude;

public sealed record ClaudeOptions
{
    public required string Command { get; init; }

    public string? WorkingDirectory { get; init; }

    public bool StripAnthropicApiKey { get; init; } = true;
}
