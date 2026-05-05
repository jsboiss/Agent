namespace Agent.Conversations;

public interface IConversationResolver
{
    Task<ConversationResolution> Resolve(ConversationResolveRequest request, CancellationToken cancellationToken);
}
