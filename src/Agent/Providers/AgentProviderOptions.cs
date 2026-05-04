namespace Agent.Providers;

public record AgentProviderOptions
{
    public required AgentProviderType Kind { get; init; }

    public required string Command { get; init; }

    public string? WorkingDirectory { get; init; }

    public IReadOnlyList<string> BlockedEnvironmentVariables { get; init; } = [];
}
