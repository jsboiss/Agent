namespace Agent.Conversations;

public sealed class InMemoryConversationRepository : IConversationRepository
{
    private static string MainConversationId => "main";

    private object SyncRoot { get; } = new();

    private Dictionary<string, Conversation> Conversations { get; } = new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, List<ConversationEntry>> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<Conversation?> Get(string conversationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (SyncRoot)
        {
            Conversations.TryGetValue(conversationId, out var conversation);
            return Task.FromResult(conversation);
        }
    }

    public Task<ConversationResolution> GetOrCreateMain(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (SyncRoot)
        {
            if (Conversations.TryGetValue(MainConversationId, out var existingConversation))
            {
                return Task.FromResult(new ConversationResolution(existingConversation, false));
            }

            var timestamp = DateTimeOffset.UtcNow;
            var conversation = new Conversation(
                MainConversationId,
                ConversationKind.Main,
                null,
                null,
                timestamp,
                timestamp);

            Conversations[conversation.Id] = conversation;
            Entries[conversation.Id] = [];

            return Task.FromResult(new ConversationResolution(conversation, true));
        }
    }

    public Task<Conversation> CreateChild(
        ConversationKind kind,
        string parentConversationId,
        string parentEntryId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (kind == ConversationKind.Main)
        {
            throw new ArgumentException("Child conversations must be Branch or SubAgent.", nameof(kind));
        }

        lock (SyncRoot)
        {
            if (!Conversations.ContainsKey(parentConversationId))
            {
                throw new InvalidOperationException($"Parent conversation '{parentConversationId}' was not found.");
            }

            var timestamp = DateTimeOffset.UtcNow;
            var conversation = new Conversation(
                Guid.NewGuid().ToString("N"),
                kind,
                parentConversationId,
                parentEntryId,
                timestamp,
                timestamp);

            Conversations[conversation.Id] = conversation;
            Entries[conversation.Id] = [];

            return Task.FromResult(conversation);
        }
    }

    public Task<ConversationEntry> AddEntry(
        string conversationId,
        ConversationEntryRole role,
        string channel,
        string content,
        string? parentEntryId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (SyncRoot)
        {
            if (!Conversations.TryGetValue(conversationId, out var conversation))
            {
                throw new InvalidOperationException($"Conversation '{conversationId}' was not found.");
            }

            var timestamp = DateTimeOffset.UtcNow;
            var entry = new ConversationEntry(
                Guid.NewGuid().ToString("N"),
                conversationId,
                role,
                channel,
                content,
                parentEntryId,
                timestamp);

            if (!Entries.TryGetValue(conversationId, out var entries))
            {
                entries = [];
                Entries[conversationId] = entries;
            }

            entries.Add(entry);
            Conversations[conversationId] = conversation with { UpdatedAt = timestamp };

            return Task.FromResult(entry);
        }
    }

    public Task<IReadOnlyList<ConversationEntry>> ListEntries(
        string conversationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (SyncRoot)
        {
            if (!Entries.TryGetValue(conversationId, out var entries))
            {
                return Task.FromResult<IReadOnlyList<ConversationEntry>>([]);
            }

            return Task.FromResult<IReadOnlyList<ConversationEntry>>(entries.ToArray());
        }
    }
}
