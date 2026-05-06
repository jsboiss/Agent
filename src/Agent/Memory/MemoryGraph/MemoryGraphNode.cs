namespace Agent.Memory.MemoryGraph;

public sealed record MemoryGraphNode(
    string Id,
    string Label,
    string Kind,
    string Segment,
    string Tier,
    string Lifecycle,
    double Importance,
    string Text,
    int Count,
    double Size,
    IReadOnlyDictionary<string, string> Metadata);
