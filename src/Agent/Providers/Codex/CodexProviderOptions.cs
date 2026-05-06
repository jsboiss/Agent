using Agent.Providers;

namespace Agent.Providers.Codex;

public sealed record CodexProviderOptions : AgentProviderOptions
{
    public CodexProviderOptions()
    {
        Kind = AgentProviderType.Codex;
        Command = "codex";
        BlockedEnvironmentVariables = ["OPENAI_API_KEY"];
    }

    public string Mode { get; init; } = "mcp";

    public string Sandbox { get; init; } = "danger-full-access";

    public string ApprovalPolicy { get; init; } = "never";

    public int TimeoutSeconds { get; init; } = 600;

    public string Model { get; init; } = "gpt-5.5";
}
