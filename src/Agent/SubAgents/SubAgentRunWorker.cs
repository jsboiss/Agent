using Agent.Conversations;
using Agent.Events;
using Agent.Providers;
using Agent.Resources;
using Agent.Settings;
using Agent.Tokens;
using Agent.Workspaces;

namespace Agent.SubAgents;

public sealed class SubAgentRunWorker(
    ISubAgentWorkQueue workQueue,
    IAgentRunStore runStore,
    IAgentWorkspaceStore workspaceStore,
    IConversationRepository conversationRepository,
    IConversationMirrorStore mirrorStore,
    IAgentProviderSelector providerSelector,
    IAgentResourceLoader resourceLoader,
    IAgentSettingsResolver settingsResolver,
    IAgentEventSink eventSink,
    IAgentTokenTracker tokenTracker,
    ILogger<SubAgentRunWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var item = await workQueue.Dequeue(stoppingToken);
                await Execute(item, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Sub-agent worker failed while processing an item.");
            }
        }
    }

    private async Task Execute(SubAgentWorkItem item, CancellationToken cancellationToken)
    {
        var run = await runStore.Get(item.RunId, cancellationToken)
            ?? throw new InvalidOperationException($"Sub-agent run '{item.RunId}' was not found.");
        var workspace = (await workspaceStore.List(cancellationToken))
            .FirstOrDefault(x => string.Equals(x.Id, item.WorkspaceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Workspace '{item.WorkspaceId}' was not found.");
        var conversation = await conversationRepository.Get(item.ChildConversationId, cancellationToken)
            ?? throw new InvalidOperationException($"Sub-agent conversation '{item.ChildConversationId}' was not found.");
        var settings = await settingsResolver.Resolve(
            new AgentSettingsResolveRequest(
                conversation,
                item.Channel,
                workspace.RootPath,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["provider"] = AgentProviderType.Codex.ToString()
                }),
            cancellationToken);
        var resources = await resourceLoader.Load(
            new AgentResourceLoadRequest(conversation, item.Channel, AgentProviderType.Codex, settings),
            cancellationToken);
        var provider = providerSelector.Get(AgentProviderType.Codex);

        await runStore.Update(
            item.RunId,
            AgentRunStatus.Running,
            run.CodexThreadId,
            null,
            null,
            cancellationToken);
        await Publish(
            AgentEventKind.ProviderRequestStarted,
            item.ChildConversationId,
            new Dictionary<string, string>
            {
                ["runId"] = item.RunId,
                ["workspaceId"] = item.WorkspaceId,
                ["routeKind"] = AgentRouteKind.Work.ToString(),
                ["message"] = "Sub-agent background run started."
            },
            cancellationToken);

        var request = new AgentProviderRequest(
            AgentProviderType.Codex,
            item.ChildConversationId,
            item.Task,
            resources,
            string.Empty,
            [],
            resources.Workspace.AvailableTools,
            [],
            [],
            workspace.RootPath,
            run.CodexThreadId,
            AgentRouteKind.Work,
            item.RunId,
            settings.Get("codex.sandbox") ?? "danger-full-access",
            settings.Get("codex.approvalPolicy") ?? "never",
            string.Empty,
            resources.ChannelInstructions,
            item.AllowsMutation);
        var result = await provider.Send(request, cancellationToken);
        var mainContext = await conversationRepository.ListEntries(item.ChildConversationId, cancellationToken);
        var tokenUsage = tokenTracker.Measure(request, result, settings, mainContext);
        var status = string.IsNullOrWhiteSpace(result.Error)
            ? AgentRunStatus.Completed
            : AgentRunStatus.Failed;
        var threadId = result.CodexThreadId
            ?? result.UsageMetadata.GetValueOrDefault("codexThreadId")
            ?? run.CodexThreadId;

        await runStore.Update(
            item.RunId,
            status,
            threadId,
            result.AssistantMessage,
            result.Error,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(threadId))
        {
            await mirrorStore.Add(
                item.WorkspaceId,
                item.RunId,
                threadId,
                item.Channel,
                ConversationEntryRole.User,
                item.Task,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(result.AssistantMessage))
            {
                await mirrorStore.Add(
                    item.WorkspaceId,
                    item.RunId,
                    threadId,
                    item.Channel,
                    ConversationEntryRole.Assistant,
                    result.AssistantMessage,
                    cancellationToken);
            }
        }

        await AddConversationResult(item, result, status, cancellationToken);
        var completedData = new Dictionary<string, string>
        {
            ["runId"] = item.RunId,
            ["workspaceId"] = item.WorkspaceId,
            ["codexThreadId"] = threadId ?? string.Empty,
            ["status"] = status.ToString(),
            ["error"] = result.Error ?? string.Empty,
            ["message"] = result.AssistantMessage
        };

        foreach (var x in tokenTracker.ToMetadata(tokenUsage))
        {
            completedData[x.Key] = x.Value;
        }

        await Publish(
            string.IsNullOrWhiteSpace(result.Error) ? AgentEventKind.ProviderTurnCompleted : AgentEventKind.ProviderError,
            item.ChildConversationId,
            completedData,
            cancellationToken);
    }

    private async Task AddConversationResult(
        SubAgentWorkItem item,
        AgentProviderResult result,
        AgentRunStatus status,
        CancellationToken cancellationToken)
    {
        var content = status == AgentRunStatus.Completed
            ? result.AssistantMessage
            : $"Sub-agent run failed: {result.Error}";

        if (string.IsNullOrWhiteSpace(content))
        {
            content = $"Sub-agent run finished with status {status}.";
        }

        await conversationRepository.AddEntry(
            item.ChildConversationId,
            ConversationEntryRole.Assistant,
            item.Channel,
            content,
            item.ParentEntryId,
            cancellationToken);
        await conversationRepository.AddEntry(
            item.ParentConversationId,
            ConversationEntryRole.Tool,
            item.Channel,
            $"Sub-agent run {item.RunId} completed with status {status}: {Shorten(content, 800)}",
            item.ParentEntryId,
            cancellationToken);
    }

    private async Task Publish(
        AgentEventKind kind,
        string conversationId,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken)
    {
        await eventSink.Publish(
            new AgentEvent(
                Guid.NewGuid().ToString("N"),
                kind,
                conversationId,
                DateTimeOffset.UtcNow,
                data),
            cancellationToken);
    }

    private static string Shorten(string value, int length)
    {
        return value.Length <= length
            ? value
            : value[..length] + "...";
    }
}
