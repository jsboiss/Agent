namespace Agent.Conversations;

public sealed class InMemoryConversationSummaryStore : IConversationSummaryStore
{
    private object SyncRoot { get; } = new();

    private Dictionary<string, ConversationSummary> Summaries { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<ConversationSummary?> Get(string conversationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (SyncRoot)
        {
            Summaries.TryGetValue(conversationId, out var summary);
            return Task.FromResult(summary);
        }
    }

    public Task<ConversationSummary> Upsert(
        string conversationId,
        string content,
        string? throughEntryId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (SyncRoot)
        {
            var summary = new ConversationSummary(
                conversationId,
                content,
                throughEntryId,
                DateTimeOffset.UtcNow);

            Summaries[conversationId] = summary;

            return Task.FromResult(summary);
        }
    }
}
