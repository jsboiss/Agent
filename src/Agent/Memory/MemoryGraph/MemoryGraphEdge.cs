namespace Agent.Memory.MemoryGraph;

public sealed record MemoryGraphEdge(
    string Id,
    string SourceId,
    string TargetId,
    string Kind,
    string Label);
