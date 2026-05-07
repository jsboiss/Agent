namespace Agent.Memory;

public sealed record MemoryCandidateReview(
    ExtractedMemory Candidate,
    bool Accepted,
    string Reason,
    double Score,
    string? ExistingMemoryId,
    IReadOnlyList<string> SupersededMemoryIds);
