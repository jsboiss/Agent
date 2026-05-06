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

        if (IsWorkRequest(userMessage))
        {
            return new AgentRouteResolution(
                workspace,
                AgentRouteKind.Work,
                workspace.WorkThreadId,
                null,
                AllowsMutation(channel, workspace),
                "Message classified as coding/execution work.");
        }

        return new AgentRouteResolution(
            workspace,
            AgentRouteKind.Chat,
            workspace.ChatThreadId,
            null,
            false,
            "Message classified as general chat.");
    }

    private static bool IsWorkRequest(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return false;
        }

        var value = userMessage.Trim();

        if (value.StartsWith("/work", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.StartsWith("/chat", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return false;
    }

    private static bool AllowsMutation(string channel, AgentWorkspace workspace)
    {
        if (string.Equals(channel, "local-web", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return workspace.RemoteExecutionAllowed;
    }
}
