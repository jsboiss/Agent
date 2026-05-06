namespace Agent.Memory;

public sealed record MemoryCandidateReviewRequest(
    string ConversationId,
    IReadOnlyList<ExtractedMemory> Candidates);
