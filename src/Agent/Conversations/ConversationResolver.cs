namespace Agent.Conversations;

public sealed class ConversationResolver(IConversationRepository repository) : IConversationResolver
{
    public async Task<ConversationResolution> Resolve(
        ConversationResolveRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.ConversationId))
        {
            var conversation = await repository.Get(request.ConversationId, cancellationToken);

            if (conversation is not null)
            {
                return new ConversationResolution(conversation, false);
            }
        }

        return await repository.GetOrCreateMain(cancellationToken);
    }
}
