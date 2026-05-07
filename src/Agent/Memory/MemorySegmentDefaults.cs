namespace Agent.Memory;

public static class MemorySegmentDefaults
{
    public static MemorySegmentDefault Get(MemorySegment segment)
    {
        return segment switch
        {
            MemorySegment.Identity => new MemorySegmentDefault(MemoryTier.Permanent, 0.85, 0.9, 0.01),
            MemorySegment.Correction => new MemorySegmentDefault(MemoryTier.Long, 0.8, 0.9, 0.015),
            MemorySegment.Relationship => new MemorySegmentDefault(MemoryTier.Long, 0.75, 0.85, 0.02),
            MemorySegment.Preference => new MemorySegmentDefault(MemoryTier.Long, 0.7, 0.85, 0.02),
            MemorySegment.Project => new MemorySegmentDefault(MemoryTier.Long, 0.65, 0.8, 0.025),
            MemorySegment.Knowledge => new MemorySegmentDefault(MemoryTier.Long, 0.6, 0.8, 0.03),
            _ => new MemorySegmentDefault(MemoryTier.Short, 0.4, 0.75, 0.08)
        };
    }
}
