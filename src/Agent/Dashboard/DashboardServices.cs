using System.Net;
using System.Text;
using System.Text.Json;
using Agent.Automations;
using Agent.Calendar;
using Agent.Capabilities;
using Agent.Compaction;
using Agent.Conversations;
using Agent.Channels.Telegram;
using Agent.Drafts;
using Agent.Events;
using Agent.Email;
using Agent.Memory;
using Agent.Memory.MemoryGraph;
using Agent.Messages;
using Agent.Settings;
using Agent.SubAgents;
using Agent.Tokens;
using Agent.Tools;
using Agent.Workspaces;
using Markdig;
using Microsoft.Extensions.Options;

namespace Agent.Dashboard;

public sealed class ChatDashboardService(
    IConversationRepository conversationRepository,
    IConversationSummaryStore summaryStore,
    IMessageProcessor messageProcessor,
    IAgentEventStore eventStore,
    IMemoryStore memoryStore,
    IAgentSettingsResolver settingsResolver,
    IAgentWorkspaceStore workspaceStore,
    IAgentRunStore runStore,
    IWebHostEnvironment environment,
    IAgentCapabilityRegistry capabilityRegistry) : IChatDashboardService
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public async Task<ChatDashboardSnapshot> LoadMain(CancellationToken cancellationToken)
    {
        return await BuildSnapshot("main", false, null, cancellationToken);
    }

    public async Task<DebugTranscriptExport> ExportMainTranscript(CancellationToken cancellationToken)
    {
        var entries = await conversationRepository.ListEntries("main", cancellationToken);
        var builder = new StringBuilder();
        builder.AppendLine("# Main Chat Transcript");
        builder.AppendLine();

        foreach (var x in entries.OrderBy(x => x.CreatedAt))
        {
            builder.AppendLine($"## {x.Role} - {x.Channel} - {x.CreatedAt:O}");
            builder.AppendLine();
            builder.AppendLine(RepairMojibake(x.Content));
            builder.AppendLine();
        }

        var directory = Path.Combine(WorkspacePathResolver.GetRepositoryRootPath(environment.ContentRootPath), "App_Data", "debug");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "main-chat-transcript.md");
        var content = builder.ToString().ReplaceLineEndings("\r\n");
        await File.WriteAllTextAsync(path, content, new UTF8Encoding(false), cancellationToken);

        return new DebugTranscriptExport(path, content);
    }

    public async Task<SendChatMessageResponse> SendPrompt(
        SendChatMessageRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return new SendChatMessageResponse(
                await BuildSnapshot("main", false, null, cancellationToken),
                "Prompt is required.");
        }

        MessageResult result;
        string? errorMessage = null;

        try
        {
            result = await messageProcessor.Process(
                new MessageRequest(null, "local-web", request.Prompt.Trim(), DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception exception)
        {
            return new SendChatMessageResponse(
                await BuildSnapshot("main", false, null, cancellationToken),
                exception.Message);
        }

        if (result.Queued)
        {
            errorMessage = $"Queued as {result.QueueKind}.";
        }
        else if (string.IsNullOrWhiteSpace(result.AssistantMessage))
        {
            errorMessage = result.Events
                .FirstOrDefault(x => x.Kind == AgentEventKind.ProviderError)
                ?.Data
                .GetValueOrDefault("error")
                ?? "The provider returned no assistant message. Inspect the run trace for details.";
        }

        return new SendChatMessageResponse(
            await BuildSnapshot(result.ConversationId, false, null, cancellationToken),
            errorMessage);
    }

    public async Task StreamPrompt(
        SendChatMessageRequest request,
        Stream responseStream,
        CancellationToken cancellationToken)
    {
        var response = await SendPrompt(request, cancellationToken);
        var message = response.ErrorMessage
            ?? response.Snapshot.Messages.LastOrDefault(x => x.Role == "Assistant")?.Content
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var chunks = Chunk(message, 48);

        foreach (var chunk in chunks)
        {
            var bytes = Encoding.UTF8.GetBytes(chunk);
            await responseStream.WriteAsync(bytes, cancellationToken);
            await responseStream.FlushAsync(cancellationToken);
        }
    }

    private async Task<ChatDashboardSnapshot> BuildSnapshot(
        string conversationId,
        bool isRunning,
        string? queuedPrompt,
        CancellationToken cancellationToken)
    {
        var entries = await conversationRepository.ListEntries(conversationId, cancellationToken);
        var events = await eventStore.List(conversationId, 120, cancellationToken);
        var workspaceResolution = await workspaceStore.GetOrCreateActive(
            WorkspacePathResolver.GetDefaultAgentWorkspacePath(environment.ContentRootPath),
            cancellationToken);
        var activeRun = string.IsNullOrWhiteSpace(workspaceResolution.Workspace.ActiveRunId)
            ? null
            : await runStore.Get(workspaceResolution.Workspace.ActiveRunId, cancellationToken);
        var settings = await settingsResolver.Resolve(
            new AgentSettingsResolveRequest(
                new Conversation(
                    conversationId,
                    ConversationKind.Main,
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow),
                "local-web",
                workspaceResolution.Workspace.RootPath,
                new Dictionary<string, string>()),
            cancellationToken);
        var injectedMemoryIds = events
            .Where(x => x.Kind == AgentEventKind.MemoryInjected)
            .SelectMany(x => (x.Data.GetValueOrDefault("memoryIds") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        List<MemoryRow> injectedMemories = [];

        foreach (var id in injectedMemoryIds)
        {
            var memory = await memoryStore.Get(id, cancellationToken);

            if (memory is not null)
            {
                injectedMemories.Add(MemoryDashboardService.ToRow(memory, false));
            }
        }

        var rollingSummary = await summaryStore.Get(conversationId, cancellationToken);
        var tokenSummary = TokenUsageDashboardMapper.FromConversation(entries, rollingSummary, settings);
        var tokenUsage = TokenUsageDashboardMapper.ByProvider(events);

        return new ChatDashboardSnapshot(
            conversationId,
            entries.Select(ToMessage).ToArray(),
            events.OrderByDescending(x => x.CreatedAt).Take(40).Select(RunTimelineService.ToRow).ToArray(),
            injectedMemories,
            capabilityRegistry.GetToolDefinitions(SubAgentCapabilities.None).Select(x => x.Name).ToArray(),
            settings.Get("provider") ?? "Ollama",
            settings.Get("model") ?? "qwen3.5:latest",
            isRunning,
            queuedPrompt,
            new WorkspaceStatus(
                workspaceResolution.Workspace.Id,
                workspaceResolution.Workspace.Name,
                workspaceResolution.Workspace.RootPath,
                workspaceResolution.Workspace.ChatThreadId,
                workspaceResolution.Workspace.WorkThreadId,
                workspaceResolution.Workspace.ActiveRunId,
                workspaceResolution.Workspace.RemoteExecutionAllowed,
                activeRun?.Status.ToString(),
                activeRun?.Kind.ToString()),
            tokenSummary,
            tokenUsage);
    }

    private static ChatDashboardMessage ToMessage(ConversationEntry x)
    {
        var role = x.Role switch
        {
            ConversationEntryRole.User => "You",
            ConversationEntryRole.Assistant => "Assistant",
            ConversationEntryRole.System => "System",
            ConversationEntryRole.Tool => "Sub-agent",
            _ => x.Role.ToString()
        };

        return new ChatDashboardMessage(
            x.Id,
            role,
            RepairMojibake(x.Content),
            Markdown.ToHtml(WebUtility.HtmlEncode(RepairMojibake(x.Content)), MarkdownPipeline),
            x.CreatedAt);
    }

    private static IEnumerable<string> Chunk(string value, int size)
    {
        for (var x = 0; x < value.Length; x += size)
        {
            yield return value.Substring(x, Math.Min(size, value.Length - x));
        }
    }

    private static string RepairMojibake(string value)
    {
        return value
            .Replace("ÃƒÆ’Ã¢â‚¬ÂÃƒÆ’Ã¢â‚¬Â¡ÃƒÆ’Ã¢â‚¬â€œ", "'", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã¢â‚¬ÂÃƒÆ’Ã¢â‚¬Â¡Ãƒâ€šÃ‚Â£", "\"", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã¢â‚¬ÂÃƒÆ’Ã¢â‚¬Â¡ÃƒÆ’Ã‹Å“", "\"", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã¢â‚¬ÂÃƒÆ’Ã¢â‚¬Â¡ÃƒÆ’Ã‚Â´", "-", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã¢â‚¬ÂÃƒÆ’Ã¢â‚¬Â¡ÃƒÆ’Ã‚Â¶", "-", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã¢â‚¬ÂÃƒÆ’Ã¢â‚¬Â¡Ãƒâ€šÃ‚Âª", "...", StringComparison.Ordinal);
    }
}

public sealed class MemoryDashboardService(IMemoryStore memoryStore) : IMemoryDashboardService
{
    public async Task<MemoryWorkspaceSnapshot> Search(
        MemorySearchFilter filter,
        CancellationToken cancellationToken)
    {
        var lifecycles = GetLifecycleFilter(filter.Lifecycle);
        var memories = await memoryStore.Search(
            new MemorySearchRequest(filter.Query, 200, lifecycles, new Dictionary<string, string>()),
            cancellationToken);
        var duplicateText = memories
            .GroupBy(x => x.Text.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = memories
            .Where(x => Matches(filter.Segment, x.Segment.ToString()))
            .Where(x => Matches(filter.Tier, x.Tier.ToString()))
            .Select(x => ToRow(x, duplicateText.Contains(x.Text.Trim())))
            .ToArray();

        return new MemoryWorkspaceSnapshot(
            rows,
            ["All", .. Enum.GetNames<MemoryLifecycle>()],
            ["All", .. Enum.GetNames<MemorySegment>()],
            ["All", .. Enum.GetNames<MemoryTier>()]);
    }

    public async Task<MemoryRow> Write(
        MemoryWriteDto request,
        CancellationToken cancellationToken)
    {
        var memory = await memoryStore.Write(
            new MemoryWriteRequest(
                request.Text.Trim(),
                Enum.Parse<MemoryTier>(request.Tier, true),
                Enum.Parse<MemorySegment>(request.Segment, true),
                request.Importance,
                request.Confidence,
                null),
            cancellationToken);

        return ToRow(memory, false);
    }

    public async Task<MemoryRow> UpdateLifecycle(
        string id,
        MemoryLifecycleUpdateDto request,
        CancellationToken cancellationToken)
    {
        var memory = await memoryStore.UpdateLifecycle(
            id,
            Enum.Parse<MemoryLifecycle>(request.Lifecycle, true),
            cancellationToken);

        return ToRow(memory, false);
    }

    public async Task Delete(string id, CancellationToken cancellationToken)
    {
        await memoryStore.Delete(id, cancellationToken);
    }

    internal static MemoryRow ToRow(MemoryRecord x, bool hasDuplicateText)
    {
        return new MemoryRow(
            x.Id,
            x.Text,
            x.Tier.ToString(),
            x.Segment.ToString(),
            x.Lifecycle.ToString(),
            x.Importance,
            x.Confidence,
            x.AccessCount,
            x.CreatedAt,
            x.UpdatedAt,
            x.LastAccessedAt,
            x.SourceMessageId,
            x.Supersedes,
            hasDuplicateText);
    }

    private static bool Matches(string filter, string value)
    {
        return string.IsNullOrWhiteSpace(filter)
            || string.Equals(filter, "All", StringComparison.OrdinalIgnoreCase)
            || string.Equals(filter, value, StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<MemoryLifecycle> GetLifecycleFilter(string lifecycle)
    {
        return string.Equals(lifecycle, "All", StringComparison.OrdinalIgnoreCase)
            ? Enum.GetValues<MemoryLifecycle>().ToHashSet()
            : [Enum.Parse<MemoryLifecycle>(lifecycle)];
    }
}

public sealed class RunTimelineService(IAgentEventStore eventStore) : IRunTimelineService
{
    public async Task<RunTimelineSnapshot> List(
        string? conversationId,
        string filter,
        CancellationToken cancellationToken)
    {
        var events = await eventStore.List(
            string.IsNullOrWhiteSpace(conversationId) ? null : conversationId,
            250,
            cancellationToken);
        var rows = events
            .OrderByDescending(x => x.CreatedAt)
            .Select(ToRow)
            .Where(x => MatchesFilter(x, filter))
            .ToArray();
        var turns = rows
            .OrderBy(x => x.CreatedAt)
            .Chunk(12)
            .Select((x, y) => new RunTurnGroup($"Turn {y + 1}", x.First().CreatedAt, x))
            .Reverse()
            .ToArray();

        return new RunTimelineSnapshot(conversationId, turns, rows);
    }

    internal static RunEventRow ToRow(AgentEvent x)
    {
        return new RunEventRow(
            x.Id,
            x.Kind.ToString(),
            GetPhase(x.Kind),
            x.ConversationId,
            x.CreatedAt,
            GetSummary(x),
            x.Data,
            x.Kind == AgentEventKind.ProviderError || !string.IsNullOrWhiteSpace(x.Data.GetValueOrDefault("error")));
    }

    private static bool MatchesFilter(RunEventRow row, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || string.Equals(filter, "All", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return row.Kind.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || row.Phase.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || row.Metadata.Any(x => x.Key.Contains(filter, StringComparison.OrdinalIgnoreCase) || x.Value.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetPhase(AgentEventKind kind)
    {
        return kind switch
        {
            AgentEventKind.MessageReceived or AgentEventKind.MessagePersisted or AgentEventKind.ChatMessage => "Message",
            AgentEventKind.MemoryScoutStarted or AgentEventKind.MemoryScoutCompleted or AgentEventKind.MemoryInjected or AgentEventKind.MemoryRecall => "Memory scout",
            AgentEventKind.ProviderRequestStarted or AgentEventKind.ProviderTextDelta or AgentEventKind.ProviderTurnCompleted or AgentEventKind.ProviderRequest => "Provider call",
            AgentEventKind.ToolCallStarted or AgentEventKind.ToolCallOutput or AgentEventKind.ToolCallCompleted or AgentEventKind.ToolCall => "Tool calls",
            AgentEventKind.MemoryExtractionStarted or AgentEventKind.MemoryExtractionCompleted or AgentEventKind.MemoryExtraction => "Memory extraction",
            AgentEventKind.ProviderError => "Error",
            _ => "System"
        };
    }

    private static string GetSummary(AgentEvent x)
    {
        return x.Kind switch
        {
            AgentEventKind.MemoryScoutCompleted => $"memories: {x.Data.GetValueOrDefault("memoryCount") ?? "0"}",
            AgentEventKind.MemoryExtractionCompleted => $"written: {x.Data.GetValueOrDefault("writtenCount") ?? "0"}, skipped: {x.Data.GetValueOrDefault("skippedCount") ?? "0"}",
            AgentEventKind.ToolCallStarted => GetToolStartedSummary(x),
            AgentEventKind.ToolCallOutput => GetToolOutputSummary(x),
            AgentEventKind.ToolCallCompleted => GetToolCompletedSummary(x),
            AgentEventKind.ProviderTurnCompleted => GetProviderSummary(x),
            AgentEventKind.ProviderError => x.Data.GetValueOrDefault("error") ?? "provider error",
            _ => x.Data.GetValueOrDefault("message") ?? x.Data.GetValueOrDefault("text") ?? x.Kind.ToString()
        };
    }

    private static string GetToolStartedSummary(AgentEvent x)
    {
        var toolName = x.Data.GetValueOrDefault("toolName") ?? "tool";
        var arguments = Shorten(x.Data.GetValueOrDefault("arguments") ?? string.Empty, 140);

        return string.IsNullOrWhiteSpace(arguments)
            ? toolName
            : $"{toolName} {arguments}";
    }

    private static string GetToolOutputSummary(AgentEvent x)
    {
        var toolName = x.Data.GetValueOrDefault("toolName") ?? "tool";
        var output = Shorten(x.Data.GetValueOrDefault("output") ?? string.Empty, 180);

        return string.IsNullOrWhiteSpace(output)
            ? toolName
            : $"{toolName}: {output}";
    }

    private static string GetToolCompletedSummary(AgentEvent x)
    {
        var toolName = x.Data.GetValueOrDefault("toolName") ?? "tool";
        var succeeded = x.Data.GetValueOrDefault("succeeded") ?? string.Empty;
        var itemCount = x.Data.GetValueOrDefault("itemCount");

        return string.IsNullOrWhiteSpace(itemCount)
            ? $"{toolName} succeeded: {succeeded}"
            : $"{toolName} succeeded: {succeeded}, items: {itemCount}";
    }

    private static string GetProviderSummary(AgentEvent x)
    {
        var baseSummary = $"iteration: {x.Data.GetValueOrDefault("iteration") ?? "1"}, tools: {x.Data.GetValueOrDefault("toolCallCount") ?? "0"}";
        var tokens = x.Data.GetValueOrDefault("totalTokens");

        return string.IsNullOrWhiteSpace(tokens)
            ? baseSummary
            : $"{baseSummary}, tokens: {tokens}";
    }

    private static string Shorten(string value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = string.Join(
            " ",
            value.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= length
            ? normalized
            : normalized[..length] + "...";
    }
}

public sealed class TraceDashboardService(
    IAgentEventStore eventStore,
    IAgentRunStore runStore,
    IAutomationRunStore automationRunStore,
    IAgentTokenTracker tokenTracker,
    IAgentSettingsResolver settingsResolver,
    IAgentWorkspaceStore workspaceStore,
    IWebHostEnvironment environment) : ITraceDashboardService
{
    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    public async Task<TraceListSnapshot> List(
        string? conversationId,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedConversationId = string.IsNullOrWhiteSpace(conversationId) ? "main" : conversationId;
        var events = await eventStore.List(normalizedConversationId, 800, cancellationToken);
        var turns = GetTurnEventGroups(events)
            .OrderByDescending(x => x.StartedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(x => ToTurnRow(x.Id, normalizedConversationId, x.Events, x.StartedAt))
            .ToArray();

        return new TraceListSnapshot(normalizedConversationId, turns);
    }

    public async Task<TraceDetailSnapshot> GetDetail(
        string turnId,
        bool exact,
        CancellationToken cancellationToken)
    {
        var events = await eventStore.List(null, 1200, cancellationToken);
        var group = GetTurnEventGroups(events)
            .FirstOrDefault(x => string.Equals(x.Id, turnId, StringComparison.OrdinalIgnoreCase));

        if (group is null)
        {
            throw new InvalidOperationException($"Trace turn '{turnId}' was not found.");
        }

        var rows = group.Events.OrderBy(x => x.CreatedAt).Select(RunTimelineService.ToRow).ToArray();
        var tokenSummary = TokenUsageDashboardMapper.FromEvents(group.Events, tokenTracker);
        var provider = group.Events
            .Select(x => x.Data.GetValueOrDefault("provider"))
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            ?? "unknown";
        var model = await GetConfiguredModel(group.ConversationId, cancellationToken);
        var tools = GetToolRows(group.Events, exact);
        var memories = GetMemoryRows(group.Events, exact);
        var agents = await GetAgentRows(group.Events, exact, cancellationToken);
        var promptSections = GetPromptSections(group.Events, exact);
        var turn = ToTurnRow(group.Id, group.ConversationId, group.Events, group.StartedAt);

        return new TraceDetailSnapshot(
            turn,
            provider,
            model,
            tokenSummary,
            rows.Select(ToTraceStep).ToArray(),
            promptSections,
            memories,
            tools,
            agents,
            rows,
            DateTimeOffset.UtcNow);
    }

    public async Task StreamDetail(
        string turnId,
        bool exact,
        Stream responseStream,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var snapshot = await GetDetail(turnId, exact, cancellationToken);
            await WriteSse("snapshot", snapshot, responseStream, cancellationToken);

            if (!string.Equals(snapshot.Turn.Status, "Running", StringComparison.OrdinalIgnoreCase))
            {
                await WriteSse("done", snapshot, responseStream, cancellationToken);
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private async Task<string> GetConfiguredModel(string conversationId, CancellationToken cancellationToken)
    {
        var workspaceResolution = await workspaceStore.GetOrCreateActive(
            WorkspacePathResolver.GetDefaultAgentWorkspacePath(environment.ContentRootPath),
            cancellationToken);
        var settings = await settingsResolver.Resolve(
            new AgentSettingsResolveRequest(
                new Conversation(
                    conversationId,
                    ConversationKind.Main,
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow),
                "local-web",
                workspaceResolution.Workspace.RootPath,
                new Dictionary<string, string>()),
            cancellationToken);

        return settings.Get("model") ?? "configured model";
    }

    private async Task<IReadOnlyList<TraceAgentRunRow>> GetAgentRows(
        IReadOnlyList<AgentEvent> events,
        bool exact,
        CancellationToken cancellationToken)
    {
        var runIds = events
            .SelectMany(x => new[]
            {
                x.Data.GetValueOrDefault("runId"),
                x.Data.GetValueOrDefault("sourceRunId"),
                x.Data.GetValueOrDefault("subAgentRunId")
            })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        List<TraceAgentRunRow> rows = [];

        foreach (var runId in runIds)
        {
            var run = await runStore.Get(runId!, cancellationToken);

            if (run is null)
            {
                continue;
            }

            rows.Add(new TraceAgentRunRow(
                run.Id,
                run.Status.ToString(),
                run.Kind.ToString(),
                run.Channel,
                exact ? "available" : "redacted",
                Shorten(run.Prompt, 180),
                exact ? run.Prompt : null,
                exact ? run.FinalResponse : Shorten(run.FinalResponse ?? string.Empty, 220),
                run.Error,
                run.StartedAt,
                run.CompletedAt));
        }

        var automationRunIds = events
            .Select(x => x.Data.GetValueOrDefault("automationRunId"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var runId in automationRunIds)
        {
            var run = await automationRunStore.Get(runId!, cancellationToken);

            if (run is null)
            {
                continue;
            }

            rows.Add(new TraceAgentRunRow(
                run.Id,
                run.Status.ToString(),
                $"Automation/{run.Trigger}",
                "automation",
                exact ? "available" : "redacted",
                Shorten(run.OutputSummary ?? run.AutomationId, 180),
                exact ? run.OutputSummary : null,
                exact ? run.OutputSummary : Shorten(run.OutputSummary ?? string.Empty, 220),
                run.Error,
                run.StartedAt,
                run.CompletedAt));
        }

        return rows;
    }

    private static IReadOnlyList<TraceToolCallRow> GetToolRows(IReadOnlyList<AgentEvent> events, bool exact)
    {
        var byId = events
            .Where(x => !string.IsNullOrWhiteSpace(x.Data.GetValueOrDefault("toolCallId")) || !string.IsNullOrWhiteSpace(x.Data.GetValueOrDefault("toolName")))
            .GroupBy(x => x.Data.GetValueOrDefault("toolCallId") ?? x.Id, StringComparer.OrdinalIgnoreCase);
        List<TraceToolCallRow> rows = [];

        foreach (var group in byId)
        {
            var ordered = group.OrderBy(x => x.CreatedAt).ToArray();
            var started = ordered.FirstOrDefault(x => x.Kind == AgentEventKind.ToolCallStarted) ?? ordered.First();
            var output = ordered.LastOrDefault(x => x.Kind == AgentEventKind.ToolCallOutput);
            var completed = ordered.LastOrDefault(x => x.Kind == AgentEventKind.ToolCallCompleted || x.Kind == AgentEventKind.ProviderError);
            var name = started.Data.GetValueOrDefault("toolName") ?? started.Data.GetValueOrDefault("providerId") ?? "tool";
            var arguments = started.Data.GetValueOrDefault("arguments") ?? started.Data.GetValueOrDefault("query") ?? string.Empty;
            var outputText = output?.Data.GetValueOrDefault("output") ?? string.Empty;
            var isError = ordered.Any(x => x.Kind == AgentEventKind.ProviderError || !string.IsNullOrWhiteSpace(x.Data.GetValueOrDefault("error")));

            rows.Add(new TraceToolCallRow(
                group.Key,
                name,
                completed is null ? "Running" : isError ? "Failed" : "Completed",
                exact ? "available" : "redacted",
                Shorten(arguments, 160),
                exact ? arguments : null,
                Shorten(outputText, 220),
                exact ? outputText : null,
                isError));
        }

        return rows;
    }

    private static IReadOnlyList<TraceMemoryRow> GetMemoryRows(IReadOnlyList<AgentEvent> events, bool exact)
    {
        List<TraceMemoryRow> rows = [];

        foreach (var agentEvent in events.Where(x => x.Kind is AgentEventKind.MemoryInjected or AgentEventKind.MemoryRecall or AgentEventKind.MemoryWrite or AgentEventKind.MemoryExtraction or AgentEventKind.MemoryScoutCompleted))
        {
            var memoryIds = (agentEvent.Data.GetValueOrDefault("memoryIds")
                    ?? agentEvent.Data.GetValueOrDefault("memoryId")
                    ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (memoryIds.Length == 0)
            {
                rows.Add(new TraceMemoryRow(
                    agentEvent.Id,
                    GetMemoryAction(agentEvent.Kind),
                    agentEvent.Data.GetValueOrDefault("segment") ?? "unknown",
                    agentEvent.Data.GetValueOrDefault("tier") ?? "unknown",
                    "unavailable",
                    RunTimelineService.ToRow(agentEvent).Summary,
                    null,
                    agentEvent.Data.GetValueOrDefault("reason") ?? agentEvent.Kind.ToString()));
                continue;
            }

            foreach (var memoryId in memoryIds)
            {
                var text = agentEvent.Data.GetValueOrDefault("text") ?? agentEvent.Data.GetValueOrDefault("content");
                rows.Add(new TraceMemoryRow(
                    memoryId,
                    GetMemoryAction(agentEvent.Kind),
                    agentEvent.Data.GetValueOrDefault("segment") ?? "unknown",
                    agentEvent.Data.GetValueOrDefault("tier") ?? "unknown",
                    exact && !string.IsNullOrWhiteSpace(text) ? "available" : "redacted",
                    string.IsNullOrWhiteSpace(text) ? $"Memory {memoryId}" : Shorten(text, 180),
                    exact ? text : null,
                    agentEvent.Data.GetValueOrDefault("reason") ?? agentEvent.Kind.ToString()));
            }
        }

        return rows;
    }

    private static IReadOnlyList<TracePromptSection> GetPromptSections(IReadOnlyList<AgentEvent> events, bool exact)
    {
        var providerStarts = events
            .Where(x => x.Kind == AgentEventKind.ProviderRequestStarted)
            .OrderBy(x => x.CreatedAt)
            .ToArray();
        List<TracePromptSection> sections = [];

        foreach (var agentEvent in providerStarts)
        {
            var iteration = agentEvent.Data.GetValueOrDefault("iteration") ?? "1";
            var systemPrompt = agentEvent.Data.GetValueOrDefault("systemPrompt") ?? string.Empty;
            var summary = string.IsNullOrWhiteSpace(systemPrompt)
                ? $"Provider request {iteration}. Exact prompt was not recorded for this event."
                : $"Provider request {iteration}; {systemPrompt.Length:N0} characters.";

            sections.Add(new TracePromptSection(
                $"provider:{agentEvent.Id}",
                $"Provider request {iteration}",
                exact && !string.IsNullOrWhiteSpace(systemPrompt) ? "available" : string.IsNullOrWhiteSpace(systemPrompt) ? "unavailable" : "redacted",
                summary,
                exact ? systemPrompt : null));

            var instructions = agentEvent.Data.GetValueOrDefault("instructionSources");

            if (!string.IsNullOrWhiteSpace(instructions))
            {
                sections.Add(new TracePromptSection(
                    $"instructions:{agentEvent.Id}",
                    "Instruction sources",
                    "available",
                    instructions,
                    instructions));
            }
        }

        return sections;
    }

    private static TraceStepRow ToTraceStep(RunEventRow row)
    {
        return new TraceStepRow(
            row.Id,
            row.Kind,
            row.Phase,
            GetStepTitle(row),
            row.Summary,
            row.CreatedAt,
            row.IsError ? "Failed" : "Completed",
            row.IsError);
    }

    private static string GetStepTitle(RunEventRow row)
    {
        if (row.Metadata.TryGetValue("toolName", out var toolName) && !string.IsNullOrWhiteSpace(toolName))
        {
            return toolName;
        }

        if (row.Metadata.TryGetValue("provider", out var provider) && !string.IsNullOrWhiteSpace(provider))
        {
            return provider;
        }

        return row.Phase;
    }

    private static TraceTurnRow ToTurnRow(
        string turnId,
        string conversationId,
        IReadOnlyList<AgentEvent> events,
        DateTimeOffset startedAt)
    {
        var completedAt = events.Max(x => x.CreatedAt);
        var errorCount = events.Count(x => x.Kind == AgentEventKind.ProviderError || !string.IsNullOrWhiteSpace(x.Data.GetValueOrDefault("error")));
        var running = !events.Any(x =>
            x.Kind is AgentEventKind.ProviderTurnCompleted or AgentEventKind.ProviderError
            || (x.Kind == AgentEventKind.MessagePersisted
                && string.Equals(x.Data.GetValueOrDefault("role"), ConversationEntryRole.Assistant.ToString(), StringComparison.OrdinalIgnoreCase)));
        var userMessage = events
            .FirstOrDefault(x => x.Kind == AgentEventKind.MessagePersisted
                && string.Equals(x.Data.GetValueOrDefault("role"), ConversationEntryRole.User.ToString(), StringComparison.OrdinalIgnoreCase))
            ?.Data
            .GetValueOrDefault("message");

        return new TraceTurnRow(
            turnId,
            conversationId,
            Shorten(userMessage ?? $"Turn {turnId}", 80),
            errorCount > 0 ? "Failed" : running ? "Running" : "Completed",
            startedAt,
            running ? null : completedAt,
            events.Count,
            events.Count(x => x.Kind == AgentEventKind.ToolCallStarted),
            CountMemoryEvents(events),
            events.Select(x => x.Data.GetValueOrDefault("runId")).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            errorCount,
            GetTurnSummary(events));
    }

    private static string GetTurnSummary(IReadOnlyList<AgentEvent> events)
    {
        var tools = events.Count(x => x.Kind == AgentEventKind.ToolCallStarted);
        var memories = CountMemoryEvents(events);
        var agents = events.Select(x => x.Data.GetValueOrDefault("runId")).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        List<string> parts = [];

        if (tools > 0)
        {
            parts.Add($"{tools} tools");
        }

        if (memories > 0)
        {
            parts.Add($"{memories} memories");
        }

        if (agents > 0)
        {
            parts.Add($"{agents} agent runs");
        }

        return parts.Count == 0 ? "Provider response" : string.Join(", ", parts);
    }

    private static int CountMemoryEvents(IReadOnlyList<AgentEvent> events)
    {
        return events.Count(x => x.Kind is AgentEventKind.MemoryScoutCompleted or AgentEventKind.MemoryInjected or AgentEventKind.MemoryRecall or AgentEventKind.MemoryWrite or AgentEventKind.MemoryExtraction);
    }

    private static IReadOnlyList<TurnEventGroup> GetTurnEventGroups(IReadOnlyList<AgentEvent> events)
    {
        var ordered = events.OrderBy(x => x.CreatedAt).ToArray();
        var starts = ordered
            .Where(x => x.Kind == AgentEventKind.MessagePersisted
                && string.Equals(x.Data.GetValueOrDefault("role"), ConversationEntryRole.User.ToString(), StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(x.Data.GetValueOrDefault("ConversationEntryId")))
            .ToArray();
        List<TurnEventGroup> groups = [];

        for (var x = 0; x < starts.Length; x++)
        {
            var start = starts[x];
            var end = x + 1 < starts.Length ? starts[x + 1].CreatedAt : DateTimeOffset.MaxValue;
            var id = start.Data.GetValueOrDefault("ConversationEntryId") ?? start.Id;
            var groupEvents = ordered
                .Where(y => y.ConversationId == start.ConversationId && y.CreatedAt >= start.CreatedAt && y.CreatedAt < end)
                .ToArray();

            groups.Add(new TurnEventGroup(id, start.ConversationId, start.CreatedAt, groupEvents));
        }

        if (groups.Count == 0 && ordered.Length > 0)
        {
            groups.Add(new TurnEventGroup(ordered[0].Id, ordered[0].ConversationId, ordered[0].CreatedAt, ordered));
        }

        return groups;
    }

    private static string GetMemoryAction(AgentEventKind kind)
    {
        return kind switch
        {
            AgentEventKind.MemoryInjected => "Injected",
            AgentEventKind.MemoryRecall => "Fetched",
            AgentEventKind.MemoryWrite => "Written",
            AgentEventKind.MemoryExtraction => "Extracted",
            AgentEventKind.MemoryScoutCompleted => "Considered",
            _ => "Memory"
        };
    }

    private static string Shorten(string value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = string.Join(
            " ",
            value.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= length
            ? normalized
            : normalized[..length] + "...";
    }

    private static async Task WriteSse(
        string eventName,
        object payload,
        Stream responseStream,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var content = $"event: {eventName}\ndata: {json}\n\n";
        var bytes = Encoding.UTF8.GetBytes(content);
        await responseStream.WriteAsync(bytes, cancellationToken);
        await responseStream.FlushAsync(cancellationToken);
    }

    private sealed record TurnEventGroup(
        string Id,
        string ConversationId,
        DateTimeOffset StartedAt,
        IReadOnlyList<AgentEvent> Events);
}

public sealed class SubAgentDashboardService(
    IAgentRunStore runStore,
    IAgentEventStore eventStore,
    IConversationRepository conversationRepository,
    IAgentTokenTracker tokenTracker) : ISubAgentDashboardService
{
    public async Task<SubAgentRunsSnapshot> List(CancellationToken cancellationToken)
    {
        var runs = await runStore.List(AgentRunKind.SubAgent, 100, cancellationToken);
        var events = await eventStore.List(null, 500, cancellationToken);

        List<SubAgentRunRow> rows = [];

        foreach (var run in runs)
        {
            var runEvents = events
                .Where(x => string.Equals(x.Data.GetValueOrDefault("runId"), run.Id, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            rows.Add(new SubAgentRunRow(
                run.Id,
                run.WorkspaceId,
                run.Status.ToString(),
                run.Kind.ToString(),
                run.Channel,
                run.Prompt,
                run.CodexThreadId,
                run.ParentRunId,
                run.ParentCodexThreadId,
                run.ChildConversationId,
                run.StartedAt,
                run.CompletedAt,
                run.FinalResponse,
                run.Error,
                TokenUsageDashboardMapper.FromEvents(runEvents, tokenTracker)));
        }

        return new SubAgentRunsSnapshot(
            rows,
            TokenUsageDashboardMapper.FromSummaries(rows.Select(x => x.Tokens).ToArray()));
    }

    public async Task<SubAgentRunDetailSnapshot> GetDetail(string runId, CancellationToken cancellationToken)
    {
        var run = await runStore.Get(runId, cancellationToken)
            ?? throw new InvalidOperationException($"Run '{runId}' was not found.");

        return await BuildDetail(run, cancellationToken);
    }

    public async Task StreamDetail(
        string runId,
        Stream responseStream,
        CancellationToken cancellationToken)
    {
        var lastSignature = string.Empty;

        while (!cancellationToken.IsCancellationRequested)
        {
            var detail = await GetDetail(runId, cancellationToken);
            var signature = GetSignature(detail);

            if (!string.Equals(signature, lastSignature, StringComparison.Ordinal))
            {
                await WriteSse(responseStream, "snapshot", detail, cancellationToken);
                lastSignature = signature;
            }

            if (IsTerminal(detail.Run.Status))
            {
                await WriteSse(responseStream, "done", detail, cancellationToken);
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private async Task<SubAgentRunDetailSnapshot> BuildDetail(
        AgentRun run,
        CancellationToken cancellationToken)
    {
        var allEvents = await eventStore.List(null, 500, cancellationToken);
        var runEvents = allEvents
            .Where(x => string.Equals(x.Data.GetValueOrDefault("runId"), run.Id, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(run.ChildConversationId)
                    && string.Equals(x.ConversationId, run.ChildConversationId, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.CreatedAt)
            .ToArray();
        var row = ToRow(run, runEvents);

        if (string.IsNullOrWhiteSpace(run.ChildConversationId))
        {
            return new SubAgentRunDetailSnapshot(
                row,
                null,
                false,
                "Transcript unavailable for this run because it was created before child conversation tracking was added.",
                GetFallbackTranscript(run, runEvents),
                DateTimeOffset.UtcNow);
        }

        var entries = await conversationRepository.ListEntries(run.ChildConversationId, cancellationToken);
        var transcript = BuildTranscript(run, entries, runEvents);

        return new SubAgentRunDetailSnapshot(
            row,
            run.ChildConversationId,
            true,
            null,
            transcript,
            DateTimeOffset.UtcNow);
    }

    private SubAgentRunRow ToRow(
        AgentRun run,
        IReadOnlyList<AgentEvent> runEvents)
    {
        return new SubAgentRunRow(
            run.Id,
            run.WorkspaceId,
            run.Status.ToString(),
            run.Kind.ToString(),
            run.Channel,
            run.Prompt,
            run.CodexThreadId,
            run.ParentRunId,
            run.ParentCodexThreadId,
            run.ChildConversationId,
            run.StartedAt,
            run.CompletedAt,
            run.FinalResponse,
            run.Error,
            TokenUsageDashboardMapper.FromEvents(runEvents, tokenTracker));
    }

    private static IReadOnlyList<SubAgentTranscriptEntry> BuildTranscript(
        AgentRun run,
        IReadOnlyList<ConversationEntry> entries,
        IReadOnlyList<AgentEvent> events)
    {
        List<SubAgentTranscriptEntry> transcript =
        [
            new SubAgentTranscriptEntry(
                "task:" + run.Id,
                "Task",
                "User",
                "Task",
                run.Prompt,
                run.StartedAt,
                false,
                new Dictionary<string, string>())
        ];

        transcript.AddRange(entries
            .Where(x => x.Role != ConversationEntryRole.System)
            .Select(ToTranscriptEntry));
        transcript.AddRange(events.Select(ToTranscriptEntry));

        if (!string.IsNullOrWhiteSpace(run.FinalResponse)
            && !transcript.Any(x => string.Equals(x.Content, run.FinalResponse, StringComparison.Ordinal)))
        {
            transcript.Add(new SubAgentTranscriptEntry(
                "final:" + run.Id,
                "FinalResponse",
                "Assistant",
                "Final response",
                run.FinalResponse,
                run.CompletedAt ?? DateTimeOffset.UtcNow,
                false,
                new Dictionary<string, string>()));
        }

        if (!string.IsNullOrWhiteSpace(run.Error)
            && !transcript.Any(x => x.IsError && string.Equals(x.Content, run.Error, StringComparison.Ordinal)))
        {
            transcript.Add(new SubAgentTranscriptEntry(
                "error:" + run.Id,
                "Error",
                "System",
                "Error",
                run.Error,
                run.CompletedAt ?? DateTimeOffset.UtcNow,
                true,
                new Dictionary<string, string>()));
        }

        return transcript
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => GetKindOrder(x.Kind))
            .ToArray();
    }

    private static IReadOnlyList<SubAgentTranscriptEntry> GetFallbackTranscript(
        AgentRun run,
        IReadOnlyList<AgentEvent> events)
    {
        return BuildTranscript(run, [], events);
    }

    private static SubAgentTranscriptEntry ToTranscriptEntry(ConversationEntry entry)
    {
        var role = entry.Role switch
        {
            ConversationEntryRole.User => "User",
            ConversationEntryRole.Assistant => "Assistant",
            ConversationEntryRole.Tool => "Tool",
            _ => entry.Role.ToString()
        };

        return new SubAgentTranscriptEntry(
            "entry:" + entry.Id,
            "ConversationEntry",
            role,
            role,
            RepairMojibake(entry.Content),
            entry.CreatedAt,
            false,
            new Dictionary<string, string>
            {
                ["conversationEntryId"] = entry.Id,
                ["channel"] = entry.Channel
            });
    }

    private static SubAgentTranscriptEntry ToTranscriptEntry(AgentEvent agentEvent)
    {
        var content = GetEventContent(agentEvent);

        return new SubAgentTranscriptEntry(
            "event:" + agentEvent.Id,
            agentEvent.Kind.ToString(),
            GetEventRole(agentEvent.Kind),
            GetEventTitle(agentEvent),
            RepairMojibake(content),
            agentEvent.CreatedAt,
            agentEvent.Kind == AgentEventKind.ProviderError
                || !string.IsNullOrWhiteSpace(agentEvent.Data.GetValueOrDefault("error")),
            agentEvent.Data);
    }

    private static string GetEventRole(AgentEventKind kind)
    {
        return kind switch
        {
            AgentEventKind.ProviderTextDelta or AgentEventKind.ProviderTurnCompleted => "Assistant",
            AgentEventKind.ToolCallStarted or AgentEventKind.ToolCallOutput or AgentEventKind.ToolCallCompleted => "Tool",
            AgentEventKind.ProviderError => "Error",
            _ => "Event"
        };
    }

    private static string GetEventTitle(AgentEvent agentEvent)
    {
        return agentEvent.Kind switch
        {
            AgentEventKind.ProviderRequestStarted => "Provider started",
            AgentEventKind.ProviderTextDelta => "Assistant output",
            AgentEventKind.ProviderTurnCompleted => "Provider turn completed",
            AgentEventKind.ToolCallStarted => $"Tool started: {agentEvent.Data.GetValueOrDefault("toolName") ?? "tool"}",
            AgentEventKind.ToolCallOutput => $"Tool output: {agentEvent.Data.GetValueOrDefault("toolName") ?? "tool"}",
            AgentEventKind.ToolCallCompleted => $"Tool completed: {agentEvent.Data.GetValueOrDefault("toolName") ?? "tool"}",
            AgentEventKind.ProviderError => "Provider error",
            _ => agentEvent.Kind.ToString()
        };
    }

    private static string GetEventContent(AgentEvent agentEvent)
    {
        return agentEvent.Kind switch
        {
            AgentEventKind.ProviderTextDelta => agentEvent.Data.GetValueOrDefault("text") ?? string.Empty,
            AgentEventKind.ToolCallOutput => agentEvent.Data.GetValueOrDefault("output") ?? string.Empty,
            AgentEventKind.ToolCallStarted => agentEvent.Data.GetValueOrDefault("toolName") ?? "Tool call started.",
            AgentEventKind.ToolCallCompleted => $"Succeeded: {agentEvent.Data.GetValueOrDefault("succeeded") ?? string.Empty}",
            AgentEventKind.ProviderError => agentEvent.Data.GetValueOrDefault("error") ?? "Provider error.",
            _ => agentEvent.Data.GetValueOrDefault("message")
                ?? agentEvent.Data.GetValueOrDefault("error")
                ?? agentEvent.Data.GetValueOrDefault("text")
                ?? agentEvent.Kind.ToString()
        };
    }

    private static int GetKindOrder(string kind)
    {
        return kind switch
        {
            "Task" => 0,
            "ConversationEntry" => 1,
            _ => 2
        };
    }

    private static bool IsTerminal(string status)
    {
        return string.Equals(status, AgentRunStatus.Completed.ToString(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, AgentRunStatus.Failed.ToString(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, AgentRunStatus.Cancelled.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSignature(SubAgentRunDetailSnapshot detail)
    {
        return string.Join(
            "|",
            [
                detail.Run.Status,
                detail.Run.CompletedAt?.ToString("O") ?? string.Empty,
                detail.Run.FinalResponse ?? string.Empty,
                detail.Run.Error ?? string.Empty,
                detail.Transcript.Count.ToString(),
                detail.Transcript.LastOrDefault()?.Id ?? string.Empty
            ]);
    }

    private static async Task WriteSse(
        Stream responseStream,
        string eventName,
        SubAgentRunDetailSnapshot detail,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(detail, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var content = $"event: {eventName}\ndata: {json}\n\n";
        var bytes = Encoding.UTF8.GetBytes(content);
        await responseStream.WriteAsync(bytes, cancellationToken);
        await responseStream.FlushAsync(cancellationToken);
    }

    private static string RepairMojibake(string value)
    {
        return value
            .Replace("ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚ÂÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Å“", "'", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚ÂÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â£", "\"", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚ÂÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€¹Ã…â€œ", "\"", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚ÂÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´", "-", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚ÂÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¡ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¶", "-", StringComparison.Ordinal)
            .Replace("ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚ÂÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Âª", "...", StringComparison.Ordinal);
    }
}

internal static class TokenUsageDashboardMapper
{
    private static double CharsPerToken => 4.0;

    public static TokenUsageSummary FromConversation(
        IReadOnlyList<ConversationEntry> entries,
        ConversationSummary? summary,
        AgentSettings settings)
    {
        var recentEntryCount = GetSetting(settings, "compaction.recentEntryCount", 8);
        var contextWindowTokens = GetSetting(settings, "tokens.contextWindow", 200000);
        var compactionThresholdTokens = GetSetting(settings, "compaction.threshold", 8000);
        var recentEntries = entries.TakeLast(recentEntryCount).ToArray();
        var currentContextTokens = Estimate([
            summary?.Content ?? string.Empty,
            .. recentEntries.Select(x => $"{x.Role}: {x.Content}")
        ]);
        var compactableTokens = Estimate(GetCompactableEntries(entries, summary, recentEntryCount).Select(x => $"{x.Role}: {x.Content}"));

        return new TokenUsageSummary(
            0,
            0,
            currentContextTokens,
            currentContextTokens,
            contextWindowTokens,
            Math.Max(0, contextWindowTokens - currentContextTokens),
            compactionThresholdTokens,
            Math.Max(0, compactionThresholdTokens - compactableTokens),
            "current-context");
    }

    public static TokenUsageSummary FromEvents(
        IReadOnlyList<AgentEvent> events,
        IAgentTokenTracker tokenTracker)
    {
        var usage = tokenTracker.Aggregate(
            events
                .OrderBy(x => x.CreatedAt)
                .Where(x => x.Kind is AgentEventKind.ProviderTurnCompleted or AgentEventKind.ProviderError)
                .Select(x => x.Data)
                .Where(x => x.ContainsKey("totalTokens"))
                .ToArray());

        return FromUsage(usage);
    }

    public static TokenUsageSummary FromSummaries(IReadOnlyList<TokenUsageSummary> summaries)
    {
        if (summaries.Count == 0)
        {
            return Empty();
        }

        var latest = summaries.Last();

        return new TokenUsageSummary(
            summaries.Sum(x => x.PromptTokens),
            summaries.Sum(x => x.CompletionTokens),
            summaries.Sum(x => x.TotalTokens),
            latest.MainContextTokens,
            latest.ContextWindowTokens,
            latest.RemainingContextTokens,
            latest.CompactionThresholdTokens,
            latest.RemainingUntilCompactionTokens,
            summaries.Any(x => string.Equals(x.Source, "provider", StringComparison.OrdinalIgnoreCase)) ? "provider" : "estimate");
    }

    public static IReadOnlyList<TokenUsageBreakdown> ByProvider(IReadOnlyList<AgentEvent> events)
    {
        return events
            .Where(x => x.Kind == AgentEventKind.ProviderTurnCompleted)
            .Where(x => !string.IsNullOrWhiteSpace(x.Data.GetValueOrDefault("provider")))
            .GroupBy(x => x.Data.GetValueOrDefault("provider")!, StringComparer.OrdinalIgnoreCase)
            .Select(x => new TokenUsageBreakdown(
                x.Key,
                x.Sum(y => GetMetadataValue(y.Data, "promptTokens") ?? 0),
                x.Sum(y => GetMetadataValue(y.Data, "completionTokens") ?? 0),
                x.Sum(y => GetMetadataValue(y.Data, "totalTokens") ?? 0),
                x.Count(),
                x.Any(y => string.Equals(y.Data.GetValueOrDefault("tokenUsageSource"), "provider", StringComparison.OrdinalIgnoreCase))
                    ? "provider"
                    : "estimate"))
            .OrderByDescending(x => IsPreferredUsageProvider(x.Provider))
            .ThenBy(x => x.Provider)
            .ToArray();
    }

    private static TokenUsageSummary FromUsage(AgentTokenUsage usage)
    {
        return new TokenUsageSummary(
            usage.PromptTokens,
            usage.CompletionTokens,
            usage.TotalTokens,
            usage.MainContextTokens,
            usage.ContextWindowTokens,
            usage.RemainingTokens,
            usage.CompactionThresholdTokens,
            usage.RemainingUntilCompactionTokens,
            usage.Source);
    }

    private static TokenUsageSummary Empty()
    {
        return new TokenUsageSummary(0, 0, 0, 0, 0, 0, 0, 0, "estimate");
    }

    private static IReadOnlyList<ConversationEntry> GetCompactableEntries(
        IReadOnlyList<ConversationEntry> entries,
        ConversationSummary? summary,
        int recentEntryCount)
    {
        if (string.IsNullOrWhiteSpace(summary?.ThroughEntryId))
        {
            return entries
                .Take(Math.Max(0, entries.Count - recentEntryCount))
                .ToArray();
        }

        var summaryIndex = entries
            .Select((x, y) => new { Entry = x, Index = y })
            .FirstOrDefault(x => string.Equals(x.Entry.Id, summary.ThroughEntryId, StringComparison.OrdinalIgnoreCase))
            ?.Index;

        var olderEntries = entries
            .Select((x, y) => new { Entry = x, Index = y })
            .Take(Math.Max(0, entries.Count - recentEntryCount))
            .ToArray();

        return summaryIndex is null
            ? olderEntries.Select(x => x.Entry).ToArray()
            : olderEntries
                .Where(x => x.Index > summaryIndex.Value)
                .Select(x => x.Entry)
                .ToArray();
    }

    private static int Estimate(IEnumerable<string> values)
    {
        return values.Sum(Estimate);
    }

    private static int Estimate(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? 0
            : Math.Max(1, (int)Math.Ceiling(value.Length / CharsPerToken));
    }

    private static int GetSetting(AgentSettings settings, string key, int fallback)
    {
        return int.TryParse(settings.Get(key), out var value) && value > 0
            ? value
            : fallback;
    }

    private static int? GetMetadataValue(IReadOnlyDictionary<string, string>? metadata, string key)
    {
        return metadata?.TryGetValue(key, out var value) == true && int.TryParse(value, out var tokens)
            ? tokens
            : null;
    }

    private static bool IsPreferredUsageProvider(string provider)
    {
        return string.Equals(provider, "Codex", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "Gemini", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class MemoryGraphService(IMemoryStore memoryStore) : IMemoryGraphService
{
    private static int MaxTopicNodes => 80;

    private static int MaxEntityNodes => 80;

    private static int MaxTopicLinksPerMemory => 4;

    private static int MaxEntityLinksPerMemory => 4;

    public async Task<MemoryGraphSnapshot> Build(CancellationToken cancellationToken)
    {
        var memories = await memoryStore.Search(
            new MemorySearchRequest(
                string.Empty,
                250,
                Enum.GetValues<MemoryLifecycle>().ToHashSet(),
                new Dictionary<string, string>()),
            cancellationToken);

        if (memories.Count == 0)
        {
            return new MemoryGraphSnapshot([], [], "No memories exist yet. Send a prompt or write a memory to build graph data.");
        }

        List<MemoryGraphNode> nodes = [];
        List<MemoryGraphEdge> edges = [];
        HashSet<string> nodeIds = [];
        HashSet<string> edgeIds = [];
        var topicCounts = GetTopicCounts(memories);
        var entityCounts = GetEntityCounts(memories);

        foreach (var memory in memories)
        {
            AddNode(
                nodes,
                nodeIds,
                $"memory:{memory.Id}",
                Shorten(memory.Text, 42),
                "memory",
                memory.Segment.ToString(),
                memory.Tier.ToString(),
                memory.Lifecycle.ToString(),
                memory.Importance,
                memory.Text,
                memory.AccessCount,
                6 + (memory.Importance * 12),
                new Dictionary<string, string>
                {
                    ["confidence"] = memory.Confidence.ToString("0.###"),
                    ["createdAt"] = memory.CreatedAt.ToString("O"),
                    ["updatedAt"] = memory.UpdatedAt.ToString("O"),
                    ["sourceMessageId"] = memory.SourceMessageId ?? string.Empty,
                    ["supersedes"] = memory.Supersedes ?? string.Empty
                });
            AddNode(nodes, nodeIds, $"segment:{memory.Segment}", memory.Segment.ToString(), "segment", memory.Segment.ToString(), string.Empty, string.Empty, 1, memory.Segment.ToString(), 0, 18, new Dictionary<string, string>());
            AddNode(nodes, nodeIds, $"tier:{memory.Segment}:{memory.Tier}", memory.Tier.ToString(), "tier", memory.Segment.ToString(), memory.Tier.ToString(), string.Empty, 1, $"{memory.Segment} / {memory.Tier}", 0, 14, new Dictionary<string, string>());
            AddEdge(edges, edgeIds, $"edge:segment-tier:{memory.Segment}:{memory.Tier}", $"segment:{memory.Segment}", $"tier:{memory.Segment}:{memory.Tier}", "tier", "tier");
            AddEdge(edges, edgeIds, $"edge:tier-memory:{memory.Id}", $"tier:{memory.Segment}:{memory.Tier}", $"memory:{memory.Id}", "tier", "tier");
            AddScope(nodes, edges, nodeIds, edgeIds, memory);

            if (!string.IsNullOrWhiteSpace(memory.Supersedes))
            {
                foreach (var supersededMemoryId in SplitIds(memory.Supersedes))
                {
                    AddEdge(edges, edgeIds, $"edge:supersedes:{memory.Id}:{supersededMemoryId}", $"memory:{memory.Id}", $"memory:{supersededMemoryId}", "supersedes", "updates");
                }
            }

            foreach (var topic in GetTopics(memory.Text, topicCounts).Take(MaxTopicLinksPerMemory))
            {
                AddNode(nodes, nodeIds, $"topic:{topic}", topic, "topic", string.Empty, string.Empty, string.Empty, 1, topic, topicCounts.GetValueOrDefault(topic), 10 + topicCounts.GetValueOrDefault(topic), new Dictionary<string, string>());
                AddEdge(edges, edgeIds, $"edge:topic:{topic}:{memory.Id}", $"topic:{topic}", $"memory:{memory.Id}", "topic", "topic");
            }

            foreach (var entity in GetEntities(memory.Text, entityCounts).Take(MaxEntityLinksPerMemory))
            {
                AddNode(nodes, nodeIds, $"entity:{entity}", entity, "entity", string.Empty, string.Empty, string.Empty, 1, entity, entityCounts.GetValueOrDefault(entity), 10 + entityCounts.GetValueOrDefault(entity), new Dictionary<string, string>());
                AddEdge(edges, edgeIds, $"edge:entity:{entity}:{memory.Id}", $"entity:{entity}", $"memory:{memory.Id}", "entity", "entity");
            }
        }

        return new MemoryGraphSnapshot(nodes, edges, string.Empty);
    }

    private static void AddNode(
        ICollection<MemoryGraphNode> nodes,
        ISet<string> nodeIds,
        string id,
        string label,
        string kind,
        string segment,
        string tier,
        string lifecycle,
        double importance,
        string text,
        int count,
        double size,
        IReadOnlyDictionary<string, string> metadata)
    {
        if (nodeIds.Add(id))
        {
            nodes.Add(new MemoryGraphNode(id, label, kind, segment, tier, lifecycle, importance, text, count, size, metadata));
        }
    }

    private static void AddEdge(
        ICollection<MemoryGraphEdge> edges,
        ISet<string> edgeIds,
        string id,
        string sourceId,
        string targetId,
        string kind,
        string label)
    {
        if (edgeIds.Add(id))
        {
            edges.Add(new MemoryGraphEdge(id, sourceId, targetId, kind, label));
        }
    }

    private static void AddScope(
        ICollection<MemoryGraphNode> nodes,
        ICollection<MemoryGraphEdge> edges,
        ISet<string> nodeIds,
        ISet<string> edgeIds,
        MemoryRecord memory)
    {
        var scope = GetScope(memory);
        AddNode(nodes, nodeIds, $"scope:{scope}", scope, "scope", string.Empty, string.Empty, string.Empty, 1, scope, 0, 12, new Dictionary<string, string>());
        AddEdge(edges, edgeIds, $"edge:scope:{scope}:{memory.Id}", $"scope:{scope}", $"memory:{memory.Id}", "scope", "scope");
    }

    private static string GetScope(MemoryRecord memory)
    {
        if (memory.Segment is MemorySegment.Project)
        {
            return "Project scoped";
        }

        if (memory.Segment is MemorySegment.AgentIdentity
            or MemorySegment.AgentPreference
            or MemorySegment.AgentRelationship)
        {
            return "Agent scoped";
        }

        if (memory.Segment is MemorySegment.Relationship or MemorySegment.Identity)
        {
            return "Person scoped";
        }

        if (memory.Segment is MemorySegment.Preference or MemorySegment.Correction)
        {
            return "Global scoped";
        }

        return memory.Tier is MemoryTier.Short
            ? "Session scoped"
            : "Context scoped";
    }

    private static Dictionary<string, int> GetTopicCounts(IEnumerable<MemoryRecord> memories)
    {
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);

        foreach (var memory in memories)
        {
            foreach (var topic in GetCandidateTopics(memory.Text).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                counts[topic] = counts.GetValueOrDefault(topic) + 1;
            }
        }

        return counts
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key)
            .Take(MaxTopicNodes)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, int> GetEntityCounts(IEnumerable<MemoryRecord> memories)
    {
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);

        foreach (var memory in memories)
        {
            foreach (var entity in GetCandidateEntities(memory.Text).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                counts[entity] = counts.GetValueOrDefault(entity) + 1;
            }
        }

        return counts
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key)
            .Take(MaxEntityNodes)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetTopics(string text, IReadOnlyDictionary<string, int> topicCounts)
    {
        return GetCandidateTopics(text)
            .Where(x => topicCounts.ContainsKey(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => topicCounts[x])
            .ThenBy(x => x);
    }

    private static IEnumerable<string> GetEntities(string text, IReadOnlyDictionary<string, int> entityCounts)
    {
        return GetCandidateEntities(text)
            .Where(x => entityCounts.ContainsKey(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => entityCounts[x])
            .ThenBy(x => x);
    }

    private static IEnumerable<string> GetCandidateTopics(string text)
    {
        return text
            .Split([' ', '\r', '\n', '\t', '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '{', '}', '/', '\\', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim('-', '_').ToLowerInvariant())
            .Where(x => x.Length >= 4 && !MemoryGraphStopWords.Contains(x))
            .Select(x => x.Length > 32 ? x[..32] : x);
    }

    private static IEnumerable<string> GetCandidateEntities(string text)
    {
        var words = text.Split([' ', '\r', '\n', '\t', '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '{', '}', '/', '\\', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var word in words)
        {
            var entity = word.Trim('-', '_');
            if (entity.Length >= 3 && char.IsUpper(entity[0]) && !MemoryGraphEntityStopWords.Contains(entity))
            {
                yield return entity.Length > 40 ? entity[..40] : entity;
            }
        }
    }

    private static IEnumerable<string> SplitIds(string value)
    {
        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x));
    }

    private static string Shorten(string value, int length)
    {
        return value.Length <= length
            ? value
            : value[..length] + "...";
    }

    private static ISet<string> MemoryGraphStopWords { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "about",
        "after",
        "also",
        "because",
        "been",
        "before",
        "being",
        "could",
        "does",
        "from",
        "have",
        "into",
        "know",
        "like",
        "memory",
        "more",
        "needs",
        "only",
        "should",
        "that",
        "their",
        "there",
        "these",
        "they",
        "this",
        "under",
        "using",
        "when",
        "where",
        "with",
        "would",
        "your"
    };

    private static ISet<string> MemoryGraphEntityStopWords { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "The",
        "This",
        "That",
        "When",
        "Where",
        "User"
    };
}

public sealed class SettingsDashboardService(
    IAgentSettingsResolver settingsResolver,
    IOptions<SqliteMemoryOptions> memoryOptions,
    IAgentWorkspaceStore workspaceStore,
    IGoogleCalendarClient googleCalendarClient,
    IEmailProvider emailProvider,
    IWebHostEnvironment environment) : ISettingsDashboardService
{
    public async Task<SettingsDashboardSnapshot> Load(CancellationToken cancellationToken)
    {
        var workspaceResolution = await workspaceStore.GetOrCreateActive(
            WorkspacePathResolver.GetDefaultAgentWorkspacePath(environment.ContentRootPath),
            cancellationToken);
        var settings = await settingsResolver.Resolve(
            new AgentSettingsResolveRequest(
                new Conversation(
                    "main",
                    ConversationKind.Main,
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow),
                "local-web",
                workspaceResolution.Workspace.RootPath,
                new Dictionary<string, string>()),
            cancellationToken);

        return new SettingsDashboardSnapshot(
            settings.Values,
            settings.AppliedLayers,
            memoryOptions.Value.ConnectionString,
            ToStatus(workspaceResolution.Workspace, null),
            ToCalendarStatus(await googleCalendarClient.GetStatus(cancellationToken)),
            ToEmailStatus(await emailProvider.GetStatus(cancellationToken)),
            GetPersonalityProfiles());
    }

    public async Task<WorkspaceStatus> UpdateWorkspacePermissions(
        WorkspacePermissionUpdateDto request,
        CancellationToken cancellationToken)
    {
        var workspaceResolution = await workspaceStore.GetOrCreateActive(
            WorkspacePathResolver.GetDefaultAgentWorkspacePath(environment.ContentRootPath),
            cancellationToken);
        var workspace = await workspaceStore.SetRemoteExecutionAllowed(
            workspaceResolution.Workspace.Id,
            request.RemoteExecutionAllowed,
            cancellationToken);

        return ToStatus(workspace, null);
    }

    public async Task<WorkspaceStatus> UpdateWorkspaceRootPath(
        WorkspaceRootPathUpdateDto request,
        CancellationToken cancellationToken)
    {
        var rootPath = WorkspacePathResolver.NormalizeRootPath(request.RootPath, environment.ContentRootPath);
        var workspaceResolution = await workspaceStore.GetOrCreateActive(
            WorkspacePathResolver.GetDefaultAgentWorkspacePath(environment.ContentRootPath),
            cancellationToken);
        var workspace = await workspaceStore.SetRootPath(
            workspaceResolution.Workspace.Id,
            rootPath,
            cancellationToken);

        return ToStatus(workspace, null);
    }

    public async Task<SettingsDashboardSnapshot> UpdateWorkspaceSettings(
        WorkspaceSettingsUpdateDto request,
        CancellationToken cancellationToken)
    {
        var workspaceResolution = await workspaceStore.GetOrCreateActive(
            WorkspacePathResolver.GetDefaultAgentWorkspacePath(environment.ContentRootPath),
            cancellationToken);
        var path = Path.Combine(workspaceResolution.Workspace.RootPath, ".mainagent.settings.json");
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(path))
        {
            await using var readStream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            var existing = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(
                readStream,
                cancellationToken: cancellationToken);

            if (existing is not null)
            {
                values = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);
            }
        }

        foreach (var item in request.Values)
        {
            if (string.IsNullOrWhiteSpace(item.Value))
            {
                values.Remove(item.Key);
            }
            else
            {
                values[item.Key] = item.Value;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? workspaceResolution.Workspace.RootPath);
        await using (var writeStream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read))
        {
            await JsonSerializer.SerializeAsync(
                writeStream,
                values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Value),
                new JsonSerializerOptions { WriteIndented = true },
                cancellationToken);
        }

        return await Load(cancellationToken);
    }

    private static WorkspaceStatus ToStatus(AgentWorkspace workspace, AgentRun? activeRun)
    {
        return new WorkspaceStatus(
            workspace.Id,
            workspace.Name,
            workspace.RootPath,
            workspace.ChatThreadId,
            workspace.WorkThreadId,
            workspace.ActiveRunId,
            workspace.RemoteExecutionAllowed,
            activeRun?.Status.ToString(),
            activeRun?.Kind.ToString());
    }

    private static CalendarStatusResponse ToCalendarStatus(GoogleCalendarConnectionStatus status)
    {
        return new CalendarStatusResponse(
            status.Configured,
            status.Connected,
            status.AccountEmail,
            status.UpdatedAt);
    }

    private static EmailStatusResponse ToEmailStatus(EmailConnectionStatus status)
    {
        return new EmailStatusResponse(
            status.Configured,
            status.Connected,
            status.AccountEmail,
            status.UpdatedAt);
    }

    private static IReadOnlyList<AgentPersonalityProfileDto> GetPersonalityProfiles()
    {
        return AgentPersonalityCatalogue.Profiles
            .Select(x => new AgentPersonalityProfileDto(
                x.Id,
                x.Name,
                x.Description,
                x.Personality,
                x.ResponseStyle))
            .ToArray();
    }

}

public sealed class CalendarDashboardService(IGoogleCalendarClient googleCalendarClient) : ICalendarDashboardService
{
    public async Task<CalendarStatusResponse> GetStatus(CancellationToken cancellationToken)
    {
        var status = await googleCalendarClient.GetStatus(cancellationToken);

        return new CalendarStatusResponse(
            status.Configured,
            status.Connected,
            status.AccountEmail,
            status.UpdatedAt);
    }

    public async Task<string> GetConnectUrl(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var state = Guid.NewGuid().ToString("N");
        httpContext.Session.SetString("google-calendar-oauth-state", state);

        return await googleCalendarClient.GetAuthorizationUrl(
            state,
            GetCallbackUrl(httpContext, "/api/dashboard/calendar/oauth-callback"),
            cancellationToken);
    }

    public async Task CompleteConnect(string connectedAccountId, CancellationToken cancellationToken)
    {
        await googleCalendarClient.Connect(connectedAccountId, cancellationToken);
    }

    public async Task Disconnect(CancellationToken cancellationToken)
    {
        await googleCalendarClient.Disconnect(cancellationToken);
    }

    private static string GetCallbackUrl(HttpContext httpContext, string path)
    {
        return $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{path}";
    }
}

public sealed class EmailDashboardService(IEmailProvider emailProvider) : IEmailDashboardService
{
    public async Task<EmailStatusResponse> GetStatus(CancellationToken cancellationToken)
    {
        var status = await emailProvider.GetStatus(cancellationToken);

        return new EmailStatusResponse(
            status.Configured,
            status.Connected,
            status.AccountEmail,
            status.UpdatedAt);
    }

    public async Task<string> GetConnectUrl(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var state = Guid.NewGuid().ToString("N");
        httpContext.Session.SetString("gmail-oauth-state", state);

        return await emailProvider.GetAuthorizationUrl(
            state,
            GetCallbackUrl(httpContext, "/api/dashboard/email/oauth-callback"),
            cancellationToken);
    }

    public async Task CompleteConnect(string connectedAccountId, CancellationToken cancellationToken)
    {
        await emailProvider.Connect(connectedAccountId, cancellationToken);
    }

    public async Task Disconnect(CancellationToken cancellationToken)
    {
        await emailProvider.Disconnect(cancellationToken);
    }

    private static string GetCallbackUrl(HttpContext httpContext, string path)
    {
        return $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{path}";
    }
}

public sealed class CompactionDashboardService(
    IConversationRepository conversationRepository,
    IConversationCompactor conversationCompactor,
    IAgentSettingsResolver settingsResolver,
    IAgentWorkspaceStore workspaceStore,
    IAgentEventSink eventSink,
    IWebHostEnvironment environment) : ICompactionDashboardService
{
    public async Task<ManualCompactionResponse> CompactMain(CancellationToken cancellationToken)
    {
        var conversation = (await conversationRepository.GetOrCreateMain(cancellationToken)).Conversation;
        var workspaceResolution = await workspaceStore.GetOrCreateActive(
            WorkspacePathResolver.GetDefaultAgentWorkspacePath(environment.ContentRootPath),
            cancellationToken);
        var settings = await settingsResolver.Resolve(
            new AgentSettingsResolveRequest(
                conversation,
                "local-web",
                workspaceResolution.Workspace.RootPath,
                new Dictionary<string, string>()),
            cancellationToken);

        await Publish(
            AgentEventKind.CompactionStarted,
            conversation.Id,
            new Dictionary<string, string>
            {
                ["source"] = "manual-dashboard",
                ["recentEntryCount"] = "0",
                ["thresholdTokens"] = "0"
            },
            cancellationToken);

        var result = await conversationCompactor.Compact(
            new ConversationCompactionRequest(
                conversation,
                0,
                0,
                settings.Values,
                true),
            cancellationToken);

        await Publish(
            AgentEventKind.CompactionCompleted,
            conversation.Id,
            new Dictionary<string, string>
            {
                ["source"] = "manual-dashboard",
                ["throughEntryId"] = result.Summary.ThroughEntryId ?? string.Empty,
                ["exactEntryCount"] = result.ExactEntryCount.ToString(),
                ["newlyCompactedEntryCount"] = result.NewlyCompactedEntryCount.ToString(),
                ["memoryExtractionEntryCount"] = result.MemoryExtractionEntryCount.ToString(),
                ["proposedMemoryCount"] = result.ProposedMemoryCount.ToString(),
                ["writtenMemoryCount"] = result.WrittenMemoryCount.ToString(),
                ["skippedMemoryCount"] = result.SkippedMemoryCount.ToString()
            },
            cancellationToken);

        return new ManualCompactionResponse(
            result.Summary.ConversationId,
            result.Summary.ThroughEntryId,
            result.ExactEntryCount,
            result.NewlyCompactedEntryCount,
            result.MemoryExtractionEntryCount,
            result.ProposedMemoryCount,
            result.WrittenMemoryCount,
            result.SkippedMemoryCount,
            result.Summary.UpdatedAt);
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
}

public sealed class OperationsDashboardService(
    IOptions<TelegramChannelOptions> telegramOptions,
    IAgentRunStore runStore,
    ISubAgentCoordinator subAgentCoordinator,
    IAgentDraftStore draftStore,
    IAutomationStore automationStore,
    IAutomationRunStore automationRunStore,
    IAgentToolExecutor toolExecutor,
    IMemoryMaintenanceService memoryMaintenanceService,
    IAgentCapabilityRegistry capabilityRegistry) : IOperationsDashboardService
{
    public TelegramStatusResponse GetTelegramStatus()
    {
        var options = telegramOptions.Value;

        return new TelegramStatusResponse(
            !string.IsNullOrWhiteSpace(options.BotToken),
            options.TrustedChatIds.Length);
    }

    public async Task<RunActionResponse> CancelRun(string runId, CancellationToken cancellationToken)
    {
        var run = await runStore.Get(runId, cancellationToken)
            ?? throw new InvalidOperationException($"Run '{runId}' was not found.");
        var updated = await runStore.Update(
            run.Id,
            AgentRunStatus.Cancelled,
            run.CodexThreadId,
            run.FinalResponse,
            "Cancelled from dashboard.",
            cancellationToken);

        return new RunActionResponse(updated.Id, updated.Status.ToString(), "Run cancelled.");
    }

    public async Task<RunActionResponse> RetryRun(string runId, CancellationToken cancellationToken)
    {
        var run = await runStore.Get(runId, cancellationToken)
            ?? throw new InvalidOperationException($"Run '{runId}' was not found.");
        var result = await subAgentCoordinator.CreateAndReport(
            new SubAgentRunRequest(
                "main",
                run.Id,
                run.Prompt,
                run.Channel,
                capabilityRegistry.DefaultCapabilities,
                IsMobileChannel(run.Channel),
                null),
            cancellationToken);

        return new RunActionResponse(result.RunId ?? string.Empty, result.Status, result.Summary);
    }

    public async Task<IReadOnlyList<DraftRow>> ListDrafts(
        string? status,
        CancellationToken cancellationToken)
    {
        var filter = Enum.TryParse<DraftStatus>(status, true, out var parsed) ? parsed : (DraftStatus?)null;
        var drafts = await draftStore.List(filter, 100, cancellationToken);

        return drafts.Select(ToRow).ToArray();
    }

    public async Task<DraftRow> ApproveDraft(string id, CancellationToken cancellationToken)
    {
        return ToRow(await draftStore.UpdateStatus(id, DraftStatus.Approved, cancellationToken));
    }

    public async Task<DraftRow> RejectDraft(string id, CancellationToken cancellationToken)
    {
        return ToRow(await draftStore.UpdateStatus(id, DraftStatus.Rejected, cancellationToken));
    }

    public async Task<IReadOnlyList<AutomationRow>> ListAutomations(CancellationToken cancellationToken)
    {
        var automations = await automationStore.List(cancellationToken);
        List<AutomationRow> rows = [];

        foreach (var automation in automations)
        {
            rows.Add(await ToRow(automation, cancellationToken));
        }

        return rows;
    }

    public async Task<AutomationRow> CreateAutomation(
        AutomationCreateDto request,
        CancellationToken cancellationToken)
    {
        var automation = await automationStore.Create(
            new AutomationWriteRequest(
                request.Name.Trim(),
                request.Task.Trim(),
                request.Schedule.Trim(),
                Enum.TryParse<AutomationExecutionMode>(request.Mode, true, out var mode) ? mode : AutomationExecutionMode.Agent,
                string.IsNullOrWhiteSpace(request.ConversationId) ? "main" : request.ConversationId,
                string.IsNullOrWhiteSpace(request.Channel) ? "local-web" : request.Channel,
                request.NotificationTarget,
                capabilityRegistry.Parse(request.Capabilities),
                request.WorkspaceRootPath,
                request.SkillIds),
            cancellationToken);

        return await ToRow(automation, cancellationToken);
    }

    public async Task<AutomationRow> UpdateAutomation(
        string id,
        AutomationUpdateDto request,
        CancellationToken cancellationToken)
    {
        var existing = await automationStore.Get(id, cancellationToken)
            ?? throw new InvalidOperationException($"Automation '{id}' was not found.");
        var automation = await automationStore.Update(
            id,
            new AutomationWriteRequest(
                string.IsNullOrWhiteSpace(request.Name) ? existing.Name : request.Name.Trim(),
                string.IsNullOrWhiteSpace(request.Task) ? existing.Task : request.Task.Trim(),
                string.IsNullOrWhiteSpace(request.Schedule) ? existing.Schedule : request.Schedule.Trim(),
                Enum.TryParse<AutomationExecutionMode>(request.Mode, true, out var mode) ? mode : existing.Mode,
                string.IsNullOrWhiteSpace(request.ConversationId) ? existing.ConversationId : request.ConversationId,
                string.IsNullOrWhiteSpace(request.Channel) ? existing.Channel : request.Channel,
                request.NotificationTarget,
                capabilityRegistry.Parse(request.Capabilities, existing.Capabilities),
                string.IsNullOrWhiteSpace(request.WorkspaceRootPath) ? existing.WorkspaceRootPath : request.WorkspaceRootPath,
                string.IsNullOrWhiteSpace(request.SkillIds) ? existing.SkillIds : request.SkillIds),
            cancellationToken);

        return await ToRow(automation, cancellationToken);
    }

    public async Task<AutomationRow> ToggleAutomation(
        string id,
        AutomationToggleDto request,
        CancellationToken cancellationToken)
    {
        var automation = await automationStore.SetStatus(
            id,
            request.Enabled ? AutomationStatus.Enabled : AutomationStatus.Disabled,
            cancellationToken);

        return await ToRow(automation, cancellationToken);
    }

    public async Task<AutomationRow> RunAutomation(string id, CancellationToken cancellationToken)
    {
        var automation = await automationStore.Get(id, cancellationToken)
            ?? throw new InvalidOperationException($"Automation '{id}' was not found.");

        await toolExecutor.Execute(
            new AgentToolRequest(
                "automation",
                new Dictionary<string, string>
                {
                    ["action"] = "run_now",
                    ["automationId"] = automation.Id
                },
                automation.ConversationId,
                "local-web",
                automation.LastRunId ?? automation.Id),
            cancellationToken);

        return await ToRow(
            await automationStore.Get(id, cancellationToken)
                ?? throw new InvalidOperationException($"Automation '{id}' was not found."),
            cancellationToken);
    }

    public async Task DeleteAutomation(string id, CancellationToken cancellationToken)
    {
        await automationStore.Delete(id, cancellationToken);
    }

    public async Task<MemoryMaintenanceResponse> CleanupMemory(CancellationToken cancellationToken)
    {
        return ToResponse(await memoryMaintenanceService.Cleanup(cancellationToken));
    }

    public async Task<MemoryMaintenanceResponse> ConsolidateMemory(CancellationToken cancellationToken)
    {
        return ToResponse(await memoryMaintenanceService.Consolidate(cancellationToken));
    }

    private static DraftRow ToRow(AgentDraft draft)
    {
        return new DraftRow(
            draft.Id,
            draft.Kind,
            draft.Summary,
            draft.Payload,
            draft.SourceRunId,
            draft.ConversationId,
            draft.Channel,
            draft.Status.ToString(),
            draft.CreatedAt,
            draft.UpdatedAt);
    }

    private async Task<AutomationRow> ToRow(
        AgentAutomation automation,
        CancellationToken cancellationToken)
    {
        var runs = await automationRunStore.List(automation.Id, 5, cancellationToken);

        return new AutomationRow(
            automation.Id,
            automation.Name,
            automation.Task,
            automation.Schedule,
            automation.Status.ToString(),
            automation.Mode.ToString(),
            automation.ConversationId,
            automation.Channel,
            automation.NotificationTarget,
            automation.Capabilities.ToString(),
            automation.NextRunAt,
            automation.LastRunAt,
            automation.LastRunId,
            automation.LastResult,
            automation.WorkspaceRootPath,
            automation.SkillIds,
            runs.Select(ToRunRow).ToArray());
    }

    private static AutomationRunRow ToRunRow(AgentAutomationRun run)
    {
        return new AutomationRunRow(
            run.Id,
            run.AutomationId,
            run.SubAgentRunId,
            run.Status.ToString(),
            run.Trigger.ToString(),
            run.OutputSummary,
            run.Error,
            run.WorkspaceRootPath,
            run.SkillIds,
            run.StartedAt,
            run.CompletedAt);
    }

    private static MemoryMaintenanceResponse ToResponse(MemoryMaintenanceResult result)
    {
        return new MemoryMaintenanceResponse(
            result.Scanned,
            result.Archived,
            result.Pruned,
            result.Merged,
            result.Superseded,
            result.Summary);
    }

    private static bool IsMobileChannel(string channel)
    {
        return string.Equals(channel, "telegram", StringComparison.OrdinalIgnoreCase)
            || string.Equals(channel, "imessage", StringComparison.OrdinalIgnoreCase);
    }
}
