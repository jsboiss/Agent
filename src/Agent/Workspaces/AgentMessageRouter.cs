namespace Agent.Workspaces;

public sealed class AgentMessageRouter(IAgentRunStore runStore) : IAgentMessageRouter
{
    public async Task<AgentRouteResolution> Resolve(
        AgentWorkspace workspace,
        string channel,
        string userMessage,
        CancellationToken cancellationToken)
    {
        var activeRun = string.IsNullOrWhiteSpace(workspace.ActiveRunId)
            ? null
            : await runStore.Get(workspace.ActiveRunId, cancellationToken);

        if (activeRun is not null && activeRun.Status is AgentRunStatus.Created or AgentRunStatus.Running)
        {
            return new AgentRouteResolution(
                workspace,
                AgentRouteKind.RunFollowUp,
                activeRun.CodexThreadId,
                activeRun.Id,
                true,
                "Active run follow-up.");
        }

        return new AgentRouteResolution(
            workspace,
            AgentRouteKind.Chat,
            workspace.ChatThreadId,
            null,
            false,
            "Strict dispatcher mode routes main messages through chat; work is delegated to sub-agents.");
    }
}
