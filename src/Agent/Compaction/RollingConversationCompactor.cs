using Agent.Conversations;

namespace Agent.Compaction;

public sealed class RollingConversationCompactor(
    IConversationRepository conversationRepository,
    IConversationSummaryStore summaryStore) : IConversationCompactor
{
    private static int SummaryTokenBudget => 1200;

    public async Task<ConversationCompactionResult> Compact(
        ConversationCompactionRequest request,
        CancellationToken cancellationToken)
    {
        var entries = await conversationRepository.ListEntries(request.Conversation.Id, cancellationToken);
        var existingSummary = await summaryStore.Get(request.Conversation.Id, cancellationToken);
        var compactedEntries = entries
            .Take(Math.Max(0, entries.Count - request.RecentEntryCount))
            .Where(x => IsAfterSummary(existingSummary, entries, x))
            .ToArray();

        if (compactedEntries.Length == 0)
        {
            if (existingSummary is not null)
            {
                return new ConversationCompactionResult(existingSummary, entries.Count);
            }
        }

        var content = GetSummaryContent(request.Conversation, existingSummary, compactedEntries);
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
        ConversationSummary? existingSummary,
        IReadOnlyList<ConversationEntry> entries)
    {
        if (entries.Count == 0)
        {
            return existingSummary?.Content ?? $"Conversation {conversation.Id} has no compacted entries yet.";
        }

        var prior = existingSummary is null
            ? string.Empty
            : TrimToTokenBudget(existingSummary.Content, SummaryTokenBudget / 2);
        var lines = entries.Select(x => $"- {x.Role}: {Shorten(x.Content, 700)}");
        var combined = string.IsNullOrWhiteSpace(prior)
            ? string.Join(Environment.NewLine, lines)
            : prior + Environment.NewLine + string.Join(Environment.NewLine, lines);

        return $"Rolling summary for {conversation.Kind} conversation {conversation.Id}:{Environment.NewLine}"
            + TrimToTokenBudget(combined, SummaryTokenBudget);
    }

    private static string TrimToTokenBudget(string value, int tokenBudget)
    {
        var maxChars = tokenBudget * 4;

        if (value.Length <= maxChars)
        {
            return value;
        }

        return "[older summary trimmed]" + Environment.NewLine + value[^maxChars..];
    }

    private static string Shorten(string value, int length)
    {
        return value.Length <= length
            ? value
            : value[..length] + "...";
    }

    private static bool IsAfterSummary(
        ConversationSummary? existingSummary,
        IReadOnlyList<ConversationEntry> entries,
        ConversationEntry entry)
    {
        if (string.IsNullOrWhiteSpace(existingSummary?.ThroughEntryId))
        {
            return true;
        }

        var summaryIndex = entries
            .Select((x, y) => new { Entry = x, Index = y })
            .FirstOrDefault(x => string.Equals(x.Entry.Id, existingSummary.ThroughEntryId, StringComparison.OrdinalIgnoreCase))
            ?.Index;
        var entryIndex = entries
            .Select((x, y) => new { Entry = x, Index = y })
            .FirstOrDefault(x => string.Equals(x.Entry.Id, entry.Id, StringComparison.OrdinalIgnoreCase))
            ?.Index;

        return summaryIndex is null
            || entryIndex is not null && entryIndex > summaryIndex;
    }
}
