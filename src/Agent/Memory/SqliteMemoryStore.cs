using Agent.Memory;

namespace Agent.Memory;

public sealed class SqliteMemoryStore : IMemoryStore
{
    public Task<IReadOnlyList<MemoryRecord>> Search(MemorySearchRequest request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("SQLite memory search is scaffolded but not implemented.");
    }

    public Task<MemoryRecord> Write(MemoryWriteRequest request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("SQLite memory writes are scaffolded but not implemented.");
    }
}
