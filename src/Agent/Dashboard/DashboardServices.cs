using System.Net;
using Agent.Conversations;
using Agent.Events;
using Agent.Memory;
using Agent.Memory.MemoryGraph;
using Agent.Messages;
using Agent.Settings;
using Markdig;

namespace Agent.Dashboard;

public sealed class ChatDashboardService(
    IConversationRepository conversationRepository,
    IMessageProcessor messageProcessor,
    IAgentEventStore eventStore,
    IMemoryStore memoryStore,
    IAgentSettingsResolver settingsResolver) : IChatDashboardService
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public async Task<ChatDashboardSnapshot> LoadMain(CancellationToken cancellationToken)
    {
        return await BuildSnapshot("main", false, null, cancellationToken);
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

    private async Task<ChatDashboardSnapshot> BuildSnapshot(
        string conversationId,
        bool isRunning,
        string? queuedPrompt,
        CancellationToken cancellationToken)
    {
        var entries = await conversationRepository.ListEntries(conversationId, cancellationToken);
        var events = await eventStore.List(conversationId, 120, cancellationToken);
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
                Directory.GetCurrentDirectory(),
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

        return new ChatDashboardSnapshot(
            conversationId,
            entries.Select(ToMessage).ToArray(),
            events.OrderByDescending(x => x.CreatedAt).Take(40).Select(RunTimelineService.ToRow).ToArray(),
            injectedMemories,
            ["search_memory", "write_memory", "spawn_agent"],
            settings.Get("provider") ?? "Ollama",
            settings.Get("model") ?? "qwen3.5:latest",
            isRunning,
            queuedPrompt);
    }

    private static ChatDashboardMessage ToMessage(ConversationEntry x)
    {
        var role = x.Role switch
        {
            ConversationEntryRole.User => "You",
            ConversationEntryRole.Assistant => "Assistant",
            ConversationEntryRole.System => "System",
            _ => x.Role.ToString()
        };

        return new ChatDashboardMessage(
            x.Id,
            role,
            x.Content,
            Markdown.ToHtml(WebUtility.HtmlEncode(x.Content), MarkdownPipeline),
            x.CreatedAt);
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
                request.Tier,
                request.Segment,
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
        var memory = await memoryStore.UpdateLifecycle(id, request.Lifecycle, cancellationToken);

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
            AgentEventKind.ProviderTurnCompleted => $"iteration: {x.Data.GetValueOrDefault("iteration") ?? "1"}, tools: {x.Data.GetValueOrDefault("toolCallCount") ?? "0"}",
            AgentEventKind.ProviderError => x.Data.GetValueOrDefault("error") ?? "provider error",
            _ => x.Data.GetValueOrDefault("message") ?? x.Data.GetValueOrDefault("text") ?? x.Kind.ToString()
        };
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
            AddNode(nodes, nodeIds, $"memory:{memory.Id}", Shorten(memory.Text, 42), "memory");
            AddNode(nodes, nodeIds, $"segment:{memory.Segment}", memory.Segment.ToString(), "segment");
            AddNode(nodes, nodeIds, $"tier:{memory.Tier}", memory.Tier.ToString(), "tier");
            edges.Add(new MemoryGraphEdge($"edge:segment:{memory.Id}", $"segment:{memory.Segment}", $"memory:{memory.Id}", "segment"));
            edges.Add(new MemoryGraphEdge($"edge:tier:{memory.Id}", $"tier:{memory.Tier}", $"memory:{memory.Id}", "tier"));

            if (!string.IsNullOrWhiteSpace(memory.SourceMessageId))
            {
                AddNode(nodes, nodeIds, $"source:{memory.SourceMessageId}", $"Source {Shorten(memory.SourceMessageId, 10)}", "source");
                edges.Add(new MemoryGraphEdge($"edge:source:{memory.Id}", $"source:{memory.SourceMessageId}", $"memory:{memory.Id}", "source"));
            }

            if (!string.IsNullOrWhiteSpace(memory.Supersedes))
            {
                edges.Add(new MemoryGraphEdge($"edge:supersedes:{memory.Id}", $"memory:{memory.Id}", $"memory:{memory.Supersedes}", "supersedes"));
            }
        }

        return new MemoryGraphSnapshot(nodes, edges, string.Empty);
    }

    private static void AddNode(
        ICollection<MemoryGraphNode> nodes,
        ISet<string> nodeIds,
        string id,
        string label,
        string kind)
    {
        if (nodeIds.Add(id))
        {
            nodes.Add(new MemoryGraphNode(id, label, kind, new Dictionary<string, string>()));
        }
    }

    private static string Shorten(string value, int length)
    {
        return value.Length <= length
            ? value
            : value[..length] + "...";
    }
}
