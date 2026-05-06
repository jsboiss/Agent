namespace Agent.Memory;

public interface IMemoryStore
{
    Task<MemoryRecord?> Get(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<MemoryRecord>> Search(MemorySearchRequest request, CancellationToken cancellationToken);

    Task<MemoryRecord> Write(MemoryWriteRequest request, CancellationToken cancellationToken);

    Task<MemoryRecord> UpdateLifecycle(
        string id,
        MemoryLifecycle lifecycle,
        CancellationToken cancellationToken);

    Task Delete(string id, CancellationToken cancellationToken);
}
