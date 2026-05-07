using Agent.Conversations;
using Agent.Workspaces;

namespace Agent.SubAgents;

public sealed class SubAgentCoordinator(
    IConversationRepository conversationRepository,
    IConversationSummaryStore summaryStore,
    IAgentWorkspaceStore workspaceStore,
    IAgentRunStore runStore,
    ISubAgentWorkQueue workQueue,
    IWebHostEnvironment environment) : ISubAgentCoordinator
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

        var workspace = (await workspaceStore.GetOrCreateActive(
            WorkspacePathResolver.GetDefaultAgentWorkspacePath(environment.ContentRootPath),
            cancellationToken)).Workspace;
        var allowsMutation = workspace.RemoteExecutionAllowed || string.Equals(
            request.Channel,
            "local-web",
            StringComparison.OrdinalIgnoreCase);

        if (!allowsMutation && !request.Capabilities.HasFlag(SubAgentCapabilities.ReadOnly))
        {
            var refusal = "Remote execution is disabled for the active workspace. Enable RemoteExecutionAllowed before starting background work from this channel.";
            var refusalEntry = await conversationRepository.AddEntry(
                request.ParentConversationId,
                ConversationEntryRole.Tool,
                request.Channel,
                refusal,
                request.ParentEntryId,
                cancellationToken);

            return new SubAgentRunResult(
                childConversation.Id,
                refusalEntry.Id,
                refusal,
                null,
                null,
                AgentRunStatus.Failed.ToString());
        }

        var run = await runStore.Create(
            workspace.Id,
            request.Task,
            AgentRunKind.SubAgent,
            request.Channel,
            null,
            workspace.WorkThreadId,
            cancellationToken);
        await workQueue.Enqueue(
            new SubAgentWorkItem(
                run.Id,
                workspace.Id,
                childConversation.Id,
                request.ParentConversationId,
                request.ParentEntryId,
                request.Task,
                request.Channel,
                allowsMutation && !request.RequiresConfirmation,
                request.Capabilities,
                request.RequiresConfirmation,
                request.NotificationTarget),
            cancellationToken);

        var summary = $"Sub-agent {childConversation.Id} queued as background run {run.Id}: {request.Task}";
        await summaryStore.Upsert(
            childConversation.Id,
            summary,
            request.ParentEntryId,
            cancellationToken);

        return new SubAgentRunResult(
            childConversation.Id,
            string.Empty,
            summary,
            run.Id,
            run.CodexThreadId,
            run.Status.ToString());
    }

    private static string GetContextPackage(SubAgentRunRequest request)
    {
        return string.Join(
            Environment.NewLine,
            [
                $"Task: {request.Task}",
                $"ParentEntryId: {request.ParentEntryId}",
                $"Capabilities: {request.Capabilities}",
                $"RequiresConfirmation: {request.RequiresConfirmation}"
            ]);
    }

}
