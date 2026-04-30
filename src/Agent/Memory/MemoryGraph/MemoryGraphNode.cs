namespace Agent.Memory.MemoryGraph;

public sealed record MemoryGraphNode(
    string Id,
    string Label,
    string Kind,
    IReadOnlyDictionary<string, string> Metadata);
