namespace Agent.Providers;

public sealed record AgentProviderToolCall(
    string Id,
    string Name,
    IReadOnlyDictionary<string, string> Arguments);
