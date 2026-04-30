namespace Agent.Memory;

public interface IMemoryStore
{
    Task<IReadOnlyList<MemoryRecord>> Search(MemorySearchRequest request, CancellationToken cancellationToken);

    Task<MemoryRecord> Write(MemoryWriteRequest request, CancellationToken cancellationToken);
}
