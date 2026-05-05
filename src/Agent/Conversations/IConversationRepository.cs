namespace Agent.Conversations;

public interface IConversationRepository
{
    Task<Conversation?> Get(string conversationId, CancellationToken cancellationToken);

    Task<ConversationResolution> GetOrCreateMain(CancellationToken cancellationToken);

    Task<Conversation> CreateChild(
        ConversationKind kind,
        string parentConversationId,
        string parentEntryId,
        CancellationToken cancellationToken);

    Task<ConversationEntry> AddEntry(
        string conversationId,
        ConversationEntryRole role,
        string channel,
        string content,
        string? parentEntryId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationEntry>> ListEntries(string conversationId, CancellationToken cancellationToken);
}
