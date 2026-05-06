namespace Agent.Memory;

public sealed record MemorySegmentDefault(
    MemoryTier Tier,
    double Importance,
    double Confidence,
    double DecayRate);
