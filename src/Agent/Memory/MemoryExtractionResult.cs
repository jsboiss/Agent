namespace Agent.Memory;

public sealed record MemoryExtractionResult(
    IReadOnlyList<ExtractedMemory> Memories);
