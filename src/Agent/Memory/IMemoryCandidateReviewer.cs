namespace Agent.Memory;

public interface IMemoryCandidateReviewer
{
    Task<MemoryCandidateReviewResult> Review(
        MemoryCandidateReviewRequest request,
        CancellationToken cancellationToken);
}
