namespace Agent.Memory;

public interface IMemoryScout
{
    Task<MemoryScoutResult> Prefetch(MemoryScoutRequest request, CancellationToken cancellationToken);
}
