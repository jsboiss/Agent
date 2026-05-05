namespace Agent.Memory;

public sealed record ExtractedMemory(
    string Text,
    MemoryTier Tier,
    MemorySegment Segment,
    double Importance,
    double Confidence,
    string SourceMessageId);
