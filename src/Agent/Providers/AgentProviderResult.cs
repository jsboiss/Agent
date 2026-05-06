namespace Agent.Providers;

public sealed record AgentProviderResult(
    string AssistantMessage,
    IReadOnlyList<AgentProviderToolCall> ToolCalls,
    IReadOnlyDictionary<string, string> UsageMetadata,
    string? Error,
    string? CodexThreadId = null);
