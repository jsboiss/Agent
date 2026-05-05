namespace Agent.Conversations;

public interface IConversationSummaryStore
{
    Task<ConversationSummary?> Get(string conversationId, CancellationToken cancellationToken);

    Task<ConversationSummary> Upsert(
        string conversationId,
        string content,
        string? throughEntryId,
        CancellationToken cancellationToken);
}
