namespace Agent.Memory;

public sealed record MemoryWriteRequest(
    string Text,
    MemoryTier Tier,
    MemorySegment Segment,
    double Importance,
    double Confidence,
    string? SourceTurnId);
