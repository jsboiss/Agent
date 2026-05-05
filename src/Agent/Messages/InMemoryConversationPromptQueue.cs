namespace Agent.Messages;

public sealed class InMemoryConversationPromptQueue : IConversationPromptQueue
{
    private object SyncRoot { get; } = new();

    private HashSet<string> ActiveConversationIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, List<QueuedMessage>> Queues { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool TryStart(string conversationId)
    {
        lock (SyncRoot)
        {
            return ActiveConversationIds.Add(conversationId);
        }
    }

    public void Complete(string conversationId)
    {
        lock (SyncRoot)
        {
            ActiveConversationIds.Remove(conversationId);
        }
    }

    public QueuedMessageKind Classify(string message, bool isBusy)
    {
        if (!isBusy)
        {
            return QueuedMessageKind.Prompt;
        }

        var normalizedMessage = message.Trim().ToLowerInvariant();

        if (ContainsAny(normalizedMessage, ["actually", "stop", "instead", "use this"]))
        {
            return QueuedMessageKind.Steer;
        }

        if (ContainsAny(normalizedMessage, ["also", "after that", "when done"]))
        {
            return QueuedMessageKind.FollowUp;
        }

        return QueuedMessageKind.FollowUp;
    }

    public QueuedMessage Enqueue(
        string conversationId,
        string conversationEntryId,
        QueuedMessageKind kind,
        string channel,
        string content)
    {
        lock (SyncRoot)
        {
            if (!Queues.TryGetValue(conversationId, out var queue))
            {
                queue = [];
                Queues[conversationId] = queue;
            }

            var queuedMessage = new QueuedMessage(
                Guid.NewGuid().ToString("N"),
                conversationId,
                conversationEntryId,
                kind,
                channel,
                content,
                DateTimeOffset.UtcNow);

            queue.Add(queuedMessage);

            return queuedMessage;
        }
    }

    public IReadOnlyList<QueuedMessage> List(string conversationId)
    {
        lock (SyncRoot)
        {
            if (!Queues.TryGetValue(conversationId, out var queue))
            {
                return [];
            }

            return queue.ToArray();
        }
    }

    private static bool ContainsAny(string value, IReadOnlyList<string> needles)
    {
        return needles.Any(x => value.Contains(x, StringComparison.OrdinalIgnoreCase));
    }
}
