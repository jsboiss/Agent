namespace Agent.Memory;

public sealed record MemoryScoutResult(
    bool IsMemoryRelevant,
    IReadOnlyList<MemoryRecord> Memories,
    string CompactContext);
