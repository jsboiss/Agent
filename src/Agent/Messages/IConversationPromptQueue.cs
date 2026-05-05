namespace Agent.Messages;

public interface IConversationPromptQueue
{
    bool TryStart(string conversationId);

    void Complete(string conversationId);

    QueuedMessageKind Classify(string message, bool isBusy);

    QueuedMessage Enqueue(
        string conversationId,
        string conversationEntryId,
        QueuedMessageKind kind,
        string channel,
        string content);

    IReadOnlyList<QueuedMessage> List(string conversationId);
}
