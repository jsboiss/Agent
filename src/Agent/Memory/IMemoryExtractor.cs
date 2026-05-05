namespace Agent.Memory;

public interface IMemoryExtractor
{
    Task<MemoryExtractionResult> Extract(
        MemoryExtractionRequest request,
        CancellationToken cancellationToken);
}
