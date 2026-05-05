namespace Agent.Compaction;

public interface IConversationCompactor
{
    Task<ConversationCompactionResult> Compact(
        ConversationCompactionRequest request,
        CancellationToken cancellationToken);
}
