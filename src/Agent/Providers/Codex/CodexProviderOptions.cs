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
}
