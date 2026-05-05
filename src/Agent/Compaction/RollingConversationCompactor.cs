using Agent.Conversations;

namespace Agent.Compaction;

public sealed class RollingConversationCompactor(
    IConversationRepository conversationRepository,
    IConversationSummaryStore summaryStore) : IConversationCompactor
{
    public async Task<ConversationCompactionResult> Compact(
        ConversationCompactionRequest request,
        CancellationToken cancellationToken)
    {
        var entries = await conversationRepository.ListEntries(request.Conversation.Id, cancellationToken);
        var compactedEntries = entries
            .Take(Math.Max(0, entries.Count - request.RecentEntryCount))
            .ToArray();

        if (compactedEntries.Length == 0)
        {
            var existingSummary = await summaryStore.Get(request.Conversation.Id, cancellationToken);

            if (existingSummary is not null)
            {
                return new ConversationCompactionResult(existingSummary, entries.Count);
            }
        }

        var content = GetSummaryContent(request.Conversation, compactedEntries);
        var throughEntryId = compactedEntries.LastOrDefault()?.Id;
        var summary = await summaryStore.Upsert(
            request.Conversation.Id,
            content,
            throughEntryId,
            cancellationToken);

        return new ConversationCompactionResult(summary, entries.Count);
    }

    private static string GetSummaryContent(
        Conversation conversation,
        IReadOnlyList<ConversationEntry> entries)
    {
        if (entries.Count == 0)
        {
            return $"Conversation {conversation.Id} has no compacted entries yet.";
        }

        var lines = entries.Select(x => $"- {x.Role}: {x.Content}");

        return $"Rolling summary for {conversation.Kind} conversation {conversation.Id}:{Environment.NewLine}"
            + string.Join(Environment.NewLine, lines);
    }
}
