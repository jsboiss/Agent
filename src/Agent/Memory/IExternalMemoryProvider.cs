using Agent.Conversations;

namespace Agent.Memory;

public interface IExternalMemoryProvider
{
    string Id { get; }

    Task<IReadOnlyList<MemoryRecord>> Prefetch(
        MemorySearchRequest request,
        CancellationToken cancellationToken);

    Task SyncTurn(
        ConversationEntry userEntry,
        ConversationEntry assistantEntry,
        CancellationToken cancellationToken);

    Task Commit(
        IReadOnlyList<ExtractedMemory> memories,
        CancellationToken cancellationToken);

    Task MirrorWrite(
        MemoryRecord memory,
        CancellationToken cancellationToken);
}
