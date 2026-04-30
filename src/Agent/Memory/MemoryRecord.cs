namespace Agent.Memory;

public sealed record MemoryRecord
{
    public required string Id { get; init; }

    public required string Text { get; init; }

    public required MemoryTier Tier { get; init; }

    public required MemorySegment Segment { get; init; }

    public MemoryLifecycle Lifecycle { get; init; } = MemoryLifecycle.Active;

    public double Importance { get; init; }

    public double Confidence { get; init; }

    public int AccessCount { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? LastAccessedAt { get; init; }

    public string? SourceMessageId { get; init; }

    public string? Supersedes { get; init; }

    public string? EmbeddingReference { get; init; }
}
