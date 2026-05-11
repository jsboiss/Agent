using Agent.Conversations;

namespace Agent.Memory;

public sealed class NoOpExternalMemoryProvider : IExternalMemoryProvider
{
    public string Id => "none";

    public Task<IReadOnlyList<MemoryRecord>> Prefetch(
        MemorySearchRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<MemoryRecord>>([]);
    }

    public Task SyncTurn(
        ConversationEntry userEntry,
        ConversationEntry assistantEntry,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task Commit(
        IReadOnlyList<ExtractedMemory> memories,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task MirrorWrite(
        MemoryRecord memory,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
