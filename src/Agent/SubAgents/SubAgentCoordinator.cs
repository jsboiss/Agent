using Agent.Conversations;

namespace Agent.SubAgents;

public sealed class SubAgentCoordinator(
    IConversationRepository conversationRepository,
    IConversationSummaryStore summaryStore) : ISubAgentCoordinator
{
    public async Task<SubAgentRunResult> CreateAndReport(
        SubAgentRunRequest request,
        CancellationToken cancellationToken)
    {
        var childConversation = await conversationRepository.CreateChild(
            ConversationKind.SubAgent,
            request.ParentConversationId,
            request.ParentEntryId,
            cancellationToken);

        await conversationRepository.AddEntry(
            childConversation.Id,
            ConversationEntryRole.System,
            request.Channel,
            GetContextPackage(request),
            request.ParentEntryId,
            cancellationToken);

        var summary = $"Sub-agent {childConversation.Id} created for task: {request.Task}";
        await summaryStore.Upsert(
            childConversation.Id,
            summary,
            request.ParentEntryId,
            cancellationToken);

        var resultEntry = await conversationRepository.AddEntry(
            request.ParentConversationId,
            ConversationEntryRole.Tool,
            request.Channel,
            summary,
            request.ParentEntryId,
            cancellationToken);

        return new SubAgentRunResult(
            childConversation.Id,
            resultEntry.Id,
            summary);
    }

    private static string GetContextPackage(SubAgentRunRequest request)
    {
        return $"Task: {request.Task}{Environment.NewLine}ParentEntryId: {request.ParentEntryId}";
    }
}
