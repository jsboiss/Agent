namespace Agent.Memory;

public sealed record MemorySearchRequest(
    string Query,
    int Limit,
    IReadOnlySet<MemoryLifecycle> IncludedLifecycles,
    IReadOnlyDictionary<string, string> Hints);
