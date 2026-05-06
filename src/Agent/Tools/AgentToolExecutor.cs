using Agent.Memory;
using Agent.SubAgents;

namespace Agent.Tools;

public sealed class AgentToolExecutor(
    IMemoryStore memoryStore,
    ISubAgentCoordinator subAgentCoordinator) : IAgentToolExecutor
{
    public async Task<AgentToolResult> Execute(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        return request.Name switch
        {
            "search_memory" => await SearchMemory(request, cancellationToken),
            "write_memory" => await WriteMemory(request, cancellationToken),
            "spawn_agent" => await SpawnAgent(request, cancellationToken),
            _ => new AgentToolResult(
                request.Name,
                false,
                $"Unknown tool '{request.Name}'.",
                new Dictionary<string, string>())
        };
    }

    private async Task<AgentToolResult> SpawnAgent(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var task = request.Arguments.GetValueOrDefault("task") ?? string.Empty;
        var parentEntryId = request.Arguments.GetValueOrDefault("parentEntryId") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(task) || string.IsNullOrWhiteSpace(parentEntryId))
        {
            return new AgentToolResult(
                request.Name,
                false,
                "Missing required arguments 'task' and 'parentEntryId'.",
                new Dictionary<string, string>());
        }

        var result = await subAgentCoordinator.CreateAndReport(
            new SubAgentRunRequest(
                request.ConversationId,
                parentEntryId,
                task,
                request.Channel),
            cancellationToken);

        return new AgentToolResult(
            request.Name,
            true,
            result.Summary,
            new Dictionary<string, string>
            {
                ["conversationId"] = result.ConversationId,
                ["resultEntryId"] = result.ResultEntryId,
                ["runId"] = result.RunId ?? string.Empty,
                ["codexThreadId"] = result.CodexThreadId ?? string.Empty,
                ["status"] = result.Status
            });
    }

    private async Task<AgentToolResult> SearchMemory(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var query = request.Arguments.GetValueOrDefault("query") ?? string.Empty;
        var limit = int.TryParse(request.Arguments.GetValueOrDefault("limit"), out var parsedLimit)
            ? parsedLimit
            : 5;
        var memories = await memoryStore.Search(
            new MemorySearchRequest(
                query,
                limit,
                new HashSet<MemoryLifecycle> { MemoryLifecycle.Active },
                new Dictionary<string, string>
                {
                    ["conversationId"] = request.ConversationId
                }),
            cancellationToken);

        var content = memories.Count == 0
            ? "No matching memories found."
            : string.Join(Environment.NewLine, memories.Select(x => $"- {x.Id}: {x.Text}"));

        return new AgentToolResult(
            request.Name,
            true,
            content,
            new Dictionary<string, string>
            {
                ["count"] = memories.Count.ToString()
            });
    }

    private async Task<AgentToolResult> WriteMemory(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var content = request.Arguments.GetValueOrDefault("content") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(content))
        {
            return new AgentToolResult(
                request.Name,
                false,
                "Missing required argument 'content'.",
                new Dictionary<string, string>());
        }

        var tier = GetEnum(request.Arguments.GetValueOrDefault("tier"), MemoryTier.Long);
        var segment = GetEnum(request.Arguments.GetValueOrDefault("segment"), MemorySegment.Context);
        var existingMemories = await memoryStore.Search(
            new MemorySearchRequest(
                content,
                10,
                new HashSet<MemoryLifecycle> { MemoryLifecycle.Active },
                new Dictionary<string, string>
                {
                    ["conversationId"] = request.ConversationId,
                    ["source"] = "write-memory-dedupe"
                }),
            cancellationToken);
        var existingMemory = existingMemories.FirstOrDefault(x => string.Equals(
            Normalize(x.Text),
            Normalize(content),
            StringComparison.OrdinalIgnoreCase));

        if (existingMemory is not null)
        {
            return new AgentToolResult(
                request.Name,
                true,
                $"Memory already exists: {existingMemory.Id}",
                new Dictionary<string, string>
                {
                    ["memoryId"] = existingMemory.Id,
                    ["duplicate"] = "true"
                });
        }

        var memory = await memoryStore.Write(
            new MemoryWriteRequest(
                content,
                tier,
                segment,
                GetDouble(request.Arguments.GetValueOrDefault("importance"), 0.5),
                GetDouble(request.Arguments.GetValueOrDefault("confidence"), 0.8),
                request.Arguments.GetValueOrDefault("sourceMessageId")),
            cancellationToken);

        return new AgentToolResult(
            request.Name,
            true,
            $"Memory written: {memory.Id}",
            new Dictionary<string, string>
            {
                ["memoryId"] = memory.Id,
                ["tier"] = memory.Tier.ToString(),
                ["segment"] = memory.Segment.ToString()
            });
    }

    private static T GetEnum<T>(string? value, T fallback)
        where T : struct
    {
        return Enum.TryParse<T>(value, true, out var parsed)
            ? parsed
            : fallback;
    }

    private static double GetDouble(string? value, double fallback)
    {
        return double.TryParse(value, out var parsed)
            ? parsed
            : fallback;
    }

    private static string Normalize(string value)
    {
        return string.Join(
            " ",
            value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.Trim('.', ',', ';', ':', '!', '?').ToLowerInvariant()));
    }
}
