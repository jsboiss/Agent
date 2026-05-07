using Agent.Providers;

namespace Agent.Context;

public sealed class ContextPlannerOptions
{
    public const string SectionName = "ContextPlanner";

    public AgentProviderType Provider { get; init; } = AgentProviderType.Gemini;

    public string Model { get; init; } = "gemini-2.5-flash-lite";

    public int TimeoutMs { get; init; } = 1500;

    public AgentProviderType FallbackProvider { get; init; } = AgentProviderType.Ollama;

    public bool FallbackOnRateLimit { get; init; } = true;

    public bool FallbackOnTransientFailure { get; init; } = true;

    public IReadOnlyList<string> EnabledProviders { get; init; } = ["Memory", "Calendar"];
}
