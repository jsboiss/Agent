namespace Agent.Memory;

public sealed record MemoryCandidateReviewResult(
    IReadOnlyList<MemoryCandidateReview> Reviews);
