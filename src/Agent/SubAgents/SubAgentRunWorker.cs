using Agent.Conversations;
using Agent.Events;
using Agent.Calendar;
using Agent.Notifications;
using Agent.Providers;
using Agent.Resources;
using Agent.Settings;
using Agent.Tokens;
using Agent.Workspaces;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Agent.SubAgents;

public sealed class SubAgentRunWorker(
    ISubAgentWorkQueue workQueue,
    IAgentRunStore runStore,
    IAgentWorkspaceStore workspaceStore,
    IConversationRepository conversationRepository,
    IConversationMirrorStore mirrorStore,
    IAgentProviderSelector providerSelector,
    IAgentProviderToolLoop providerToolLoop,
    ICalendarProvider calendarProvider,
    IAgentResourceLoader resourceLoader,
    IAgentSettingsResolver settingsResolver,
    IAgentEventSink eventSink,
    IAgentTokenTracker tokenTracker,
    IAgentNotifier notifier,
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

        if (run.Status == AgentRunStatus.Cancelled)
        {
            return;
        }

        if (item.Capabilities.HasFlag(SubAgentCapabilities.Calendar)
            && TryGetSingleDayCalendarRange(item.Task, out var start, out var end))
        {
            await ExecuteCalendarRead(item, run, start, end, cancellationToken);
            return;
        }

        var workspace = (await workspaceStore.List(cancellationToken))
            .FirstOrDefault(x => string.Equals(x.Id, item.WorkspaceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Workspace '{item.WorkspaceId}' was not found.");
        Directory.CreateDirectory(workspace.RootPath);
        var conversation = await conversationRepository.Get(item.ChildConversationId, cancellationToken)
            ?? throw new InvalidOperationException($"Sub-agent conversation '{item.ChildConversationId}' was not found.");
        var providerType = item.Capabilities.HasFlag(SubAgentCapabilities.Calendar)
            ? AgentProviderType.Ollama
            : AgentProviderType.Codex;
        var settings = await settingsResolver.Resolve(
            new AgentSettingsResolveRequest(
                conversation,
                item.Channel,
                workspace.RootPath,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["provider"] = providerType.ToString()
                }),
            cancellationToken);
        var resources = await resourceLoader.Load(
            new AgentResourceLoadRequest(conversation, item.Channel, providerType, settings, workspace.RootPath, item.Capabilities),
            cancellationToken);
        var provider = providerSelector.Get(providerType);

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
                ["capabilities"] = item.Capabilities.ToString(),
                ["requiresConfirmation"] = item.RequiresConfirmation.ToString(),
                ["message"] = "Sub-agent background run started."
            },
            cancellationToken);

        var request = new AgentProviderRequest(
            providerType,
            item.ChildConversationId,
            GetTaskPrompt(item),
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
        var result = await providerToolLoop.Run(
            provider,
            request,
            item.Channel,
            item.ParentEntryId,
            settings,
            item.NotificationTarget,
            cancellationToken);
        var latestRun = await runStore.Get(item.RunId, cancellationToken);

        if (latestRun?.Status == AgentRunStatus.Cancelled)
        {
            return;
        }

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
        await Notify(item, result, status, cancellationToken);
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

    private async Task ExecuteCalendarRead(
        SubAgentWorkItem item,
        AgentRun run,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        await runStore.Update(
            item.RunId,
            AgentRunStatus.Running,
            run.CodexThreadId,
            null,
            null,
            cancellationToken);
        await Publish(
            AgentEventKind.ToolCallStarted,
            item.ChildConversationId,
            new Dictionary<string, string>
            {
                ["runId"] = item.RunId,
                ["toolName"] = "calendar_list_events",
                ["start"] = start.ToString("O"),
                ["end"] = end.ToString("O")
            },
            cancellationToken);

        string content;
        AgentRunStatus status;
        string? error = null;

        try
        {
            var events = await calendarProvider.ListEvents(
                new GoogleCalendarEventQuery(start, end, null, "primary", 50),
                cancellationToken);
            content = FormatCalendarSummary(start, events);
            status = AgentRunStatus.Completed;
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            content = exception.Message;
            error = exception.Message;
            status = AgentRunStatus.Failed;
        }

        await runStore.Update(
            item.RunId,
            status,
            run.CodexThreadId,
            content,
            error,
            cancellationToken);
        await conversationRepository.AddEntry(
            item.ChildConversationId,
            status == AgentRunStatus.Completed ? ConversationEntryRole.Assistant : ConversationEntryRole.Tool,
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
        await Publish(
            string.IsNullOrWhiteSpace(error) ? AgentEventKind.ToolCallCompleted : AgentEventKind.ProviderError,
            item.ChildConversationId,
            new Dictionary<string, string>
            {
                ["runId"] = item.RunId,
                ["toolName"] = "calendar_list_events",
                ["succeeded"] = string.IsNullOrWhiteSpace(error).ToString(),
                ["error"] = error ?? string.Empty,
                ["message"] = content
            },
            cancellationToken);
        await Notify(item, new AgentProviderResult(content, [], new Dictionary<string, string>(), error), status, cancellationToken);
    }

    private static string GetTaskPrompt(SubAgentWorkItem item)
    {
        List<string> sections =
        [
            "Sub-agent task:",
            item.Task,
            $"Capabilities: {item.Capabilities}"
        ];

        if (item.RequiresConfirmation)
        {
            sections.Add("""
                Confirmation policy: this request originated from a mobile or confirmation-gated path.
                Do not apply file changes, commit, push, send external messages, delete data, or run destructive commands.
                Produce a concise plan, risk summary, and exact next confirmation needed. If code/file changes are requested, inspect and propose the change; wait for approval before mutation.
                """);
        }

        if (item.Capabilities.HasFlag(SubAgentCapabilities.Calendar))
        {
            sections.Add("""
                Calendar policy: use the available Google Calendar tools for event, schedule, and availability questions.
                Calendar access is read-only. Do not propose or perform calendar writes in this run.
                Use explicit ISO 8601 date/time ranges when calling calendar tools.
                """);
        }

        if (!item.AllowsMutation)
        {
            sections.Add("Mutation is disabled for this run. Read, inspect, and propose only.");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private static bool TryGetSingleDayCalendarRange(
        string task,
        out DateTimeOffset start,
        out DateTimeOffset end)
    {
        start = default;
        end = default;
        var match = Regex.Match(
            task,
            @"(?<month>January|February|March|April|May|June|July|August|September|October|November|December)\s+(?<day>\d{1,2}),\s+(?<year>\d{4})",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return false;
        }

        if (!DateTime.TryParseExact(
            $"{match.Groups["month"].Value} {match.Groups["day"].Value}, {match.Groups["year"].Value}",
            "MMMM d, yyyy",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date))
        {
            return false;
        }

        var timeZone = GetTimeZone(task);
        var offset = timeZone.GetUtcOffset(date);
        start = new DateTimeOffset(date.Date, offset);
        end = start.AddDays(1);

        return true;
    }

    private static TimeZoneInfo GetTimeZone(string task)
    {
        if (task.Contains("Australia/Brisbane", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Australia/Brisbane");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.FindSystemTimeZoneById("E. Australia Standard Time");
            }
        }

        return TimeZoneInfo.Local;
    }

    private static string FormatCalendarSummary(
        DateTimeOffset start,
        IReadOnlyList<GoogleCalendarEvent> events)
    {
        if (events.Count == 0)
        {
            return $"No calendar events found for {start:dddd, MMMM d, yyyy}.";
        }

        return string.Join(
            Environment.NewLine,
            events.Select(x =>
            {
                var location = string.IsNullOrWhiteSpace(x.Location) ? string.Empty : $" Location: {x.Location}.";
                var link = string.IsNullOrWhiteSpace(x.MeetingLink) ? string.Empty : $" Link: {x.MeetingLink}.";

                return $"- {x.Start:HH:mm}-{x.End:HH:mm}: {x.Title}.{location}{link}";
            }));
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

    private async Task Notify(
        SubAgentWorkItem item,
        AgentProviderResult result,
        AgentRunStatus status,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.NotificationTarget) && !IsMobileChannel(item.Channel))
        {
            return;
        }

        var content = status == AgentRunStatus.Completed
            ? result.AssistantMessage
            : $"Sub-agent run {item.RunId} finished with status {status}: {result.Error}";

        await notifier.Send(
            item.Channel,
            item.NotificationTarget,
            Shorten(content, 1800),
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

    private static bool IsMobileChannel(string channel)
    {
        return string.Equals(channel, "telegram", StringComparison.OrdinalIgnoreCase)
            || string.Equals(channel, "imessage", StringComparison.OrdinalIgnoreCase);
    }
}
