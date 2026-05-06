namespace Agent.Compaction;

public sealed record CompactionMemoryExtractionResult(
    int ProposedCount,
    int WrittenCount,
    int SkippedCount)
{
    public static CompactionMemoryExtractionResult Empty { get; } = new(0, 0, 0);
}
