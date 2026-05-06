namespace Agent.Workspaces;

public sealed class AgentMessageRouter(IAgentRunStore runStore) : IAgentMessageRouter
{
    private static string[] WorkPhrases =>
    [
        "run test", "run tests", "write test", "write tests", "fix test", "fix tests",
        "create file", "update file", "delete file", "rename file", "working directory",
        "what's your working directory", "what is your working directory"
    ];

    private static string[] WorkWords =>
    [
        "change", "edit", "implement", "fix", "debug", "refactor", "build",
        "commit", "review", "delete", "rename", "workspace", "subagent", "sub-agent"
    ];

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

        return WorkPhrases.Any(x => value.Contains(x, StringComparison.OrdinalIgnoreCase))
            || WorkWords.Any(x => ContainsWord(value, x));
    }

    private static bool ContainsWord(string value, string word)
    {
        var index = value.IndexOf(word, StringComparison.OrdinalIgnoreCase);

        while (index >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(value[index - 1]);
            var afterIndex = index + word.Length;
            var after = afterIndex >= value.Length || !char.IsLetterOrDigit(value[afterIndex]);

            if (before && after)
            {
                return true;
            }

            index = value.IndexOf(word, index + word.Length, StringComparison.OrdinalIgnoreCase);
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
