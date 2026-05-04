using Agent.Providers;

namespace Agent.Providers.ClaudeCode;

public sealed record ClaudeCodeProviderOptions : AgentProviderOptions
{
    public ClaudeCodeProviderOptions()
    {
        Kind = AgentProviderType.ClaudeCode;
        Command = "node";
        BlockedEnvironmentVariables = ["ANTHROPIC_API_KEY"];
    }
}
