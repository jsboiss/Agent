using System.Net;
using System.Text;
using Agent.Conversations;
using Agent.Events;
using Agent.Memory;
using Agent.Memory.MemoryGraph;
using Agent.Messages;
using Agent.Settings;
using Agent.Tokens;
using Agent.Workspaces;
using Markdig;
using Microsoft.Extensions.Options;

namespace Agent.Dashboard;

public sealed class ChatDashboardService(
    IConversationRepository conversationRepository,
    IMessageProcessor messageProcessor,
    IAgentEventStore eventStore,
    IMemoryStore memoryStore,
    IAgentSettingsResolver settingsResolver,
    IAgentWorkspaceStore workspaceStore,
    IAgentRunStore runStore,
    IAgentTokenTracker tokenTracker,
    IWebHostEnvironment environment) : IChatDashboardService
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

        var directory = Path.Combine(GetWorkspaceRootPath(environment.ContentRootPath), "App_Data", "debug");
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
            GetWorkspaceRootPath(environment.ContentRootPath),
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

        var tokenSummary = TokenUsageDashboardMapper.FromEvents(events, tokenTracker);

        return new ChatDashboardSnapshot(
            conversationId,
            entries.Select(ToMessage).ToArray(),
            events.OrderByDescending(x => x.CreatedAt).Take(40).Select(RunTimelineService.ToRow).ToArray(),
            injectedMemories,
            ["search_memory", "write_memory", "spawn_agent"],
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
            tokenSummary);
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

    private static string GetWorkspaceRootPath(string contentRootPath)
    {
        var directory = new DirectoryInfo(contentRootPath);

        if (directory.Parent is not null && directory.Parent.Name.Equals("src", StringComparison.OrdinalIgnoreCase))
        {
            return directory.Parent.Parent?.FullName ?? directory.FullName;
        }

        return directory.FullName;
    }

    private static string RepairMojibake(string value)
    {
        return value
            .Replace("ÔÇÖ", "'", StringComparison.Ordinal)
            .Replace("ÔÇ£", "\"", StringComparison.Ordinal)
            .Replace("ÔÇØ", "\"", StringComparison.Ordinal)
            .Replace("ÔÇô", "-", StringComparison.Ordinal)
            .Replace("ÔÇö", "-", StringComparison.Ordinal)
            .Replace("ÔÇª", "...", StringComparison.Ordinal);
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
            x.Kind == AgentEventKind.ProviderError || x.Data.ContainsKey("error"));
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
            AgentEventKind.ToolCallStarted => x.Data.GetValueOrDefault("toolName") ?? "tool started",
            AgentEventKind.ToolCallCompleted => $"succeeded: {x.Data.GetValueOrDefault("succeeded") ?? string.Empty}",
            AgentEventKind.ProviderTurnCompleted => GetProviderSummary(x),
            AgentEventKind.ProviderError => x.Data.GetValueOrDefault("error") ?? "provider error",
            _ => x.Data.GetValueOrDefault("message") ?? x.Data.GetValueOrDefault("text") ?? x.Kind.ToString()
        };
    }

    private static string GetProviderSummary(AgentEvent x)
    {
        var baseSummary = $"iteration: {x.Data.GetValueOrDefault("iteration") ?? "1"}, tools: {x.Data.GetValueOrDefault("toolCallCount") ?? "0"}";
        var tokens = x.Data.GetValueOrDefault("totalTokens");

        return string.IsNullOrWhiteSpace(tokens)
            ? baseSummary
            : $"{baseSummary}, tokens: {tokens}";
    }
}

public sealed class SubAgentDashboardService(
    IAgentRunStore runStore,
    IAgentEventStore eventStore,
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
}

internal static class TokenUsageDashboardMapper
{
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
}

public sealed class MemoryGraphService(IMemoryStore memoryStore) : IMemoryGraphService
{
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
            AddNode(nodes, nodeIds, $"tier:{memory.Tier}", memory.Tier.ToString(), "tier", string.Empty, memory.Tier.ToString(), string.Empty, 1, memory.Tier.ToString(), 0, 14, new Dictionary<string, string>());
            edges.Add(new MemoryGraphEdge($"edge:segment:{memory.Id}", $"segment:{memory.Segment}", $"memory:{memory.Id}", "segment", "segment"));
            edges.Add(new MemoryGraphEdge($"edge:tier:{memory.Id}", $"tier:{memory.Tier}", $"memory:{memory.Id}", "tier", "tier"));

            if (!string.IsNullOrWhiteSpace(memory.SourceMessageId))
            {
                AddNode(nodes, nodeIds, $"source:{memory.SourceMessageId}", $"Source {Shorten(memory.SourceMessageId, 10)}", "source", string.Empty, string.Empty, string.Empty, 1, memory.SourceMessageId, 0, 10, new Dictionary<string, string>());
                edges.Add(new MemoryGraphEdge($"edge:source:{memory.Id}", $"source:{memory.SourceMessageId}", $"memory:{memory.Id}", "source", "source"));
            }

            if (!string.IsNullOrWhiteSpace(memory.Supersedes))
            {
                edges.Add(new MemoryGraphEdge($"edge:supersedes:{memory.Id}", $"memory:{memory.Id}", $"memory:{memory.Supersedes}", "supersedes", "supersedes"));
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

    private static string Shorten(string value, int length)
    {
        return value.Length <= length
            ? value
            : value[..length] + "...";
    }
}

public sealed class SettingsDashboardService(
    IAgentSettingsResolver settingsResolver,
    IOptions<SqliteMemoryOptions> memoryOptions) : ISettingsDashboardService
{
    public async Task<SettingsDashboardSnapshot> Load(CancellationToken cancellationToken)
    {
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
                Directory.GetCurrentDirectory(),
                new Dictionary<string, string>()),
            cancellationToken);

        return new SettingsDashboardSnapshot(
            settings.Values,
            settings.AppliedLayers,
            memoryOptions.Value.ConnectionString);
    }
}
