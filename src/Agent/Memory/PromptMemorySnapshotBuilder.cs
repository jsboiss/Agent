namespace Agent.Memory;

public sealed class PromptMemorySnapshotBuilder(IMemoryStore memoryStore) : IPromptMemorySnapshotBuilder
{
    public async Task<PromptMemorySnapshot> Build(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        if (string.Equals(settings.GetValueOrDefault("memory.prompt.enabled"), "false", StringComparison.OrdinalIgnoreCase))
        {
            return new PromptMemorySnapshot(string.Empty, string.Empty, [], []);
        }

        var agentBudget = GetInt(settings, "memory.prompt.agentNotesBudget", 2200);
        var userBudget = GetInt(settings, "memory.prompt.userProfileBudget", 1375);
        var memories = await memoryStore.Search(
            new MemorySearchRequest(
                string.Empty,
                GetInt(settings, "memory.prompt.candidateLimit", 80),
                new HashSet<MemoryLifecycle> { MemoryLifecycle.Active },
                new Dictionary<string, string> { ["source"] = "prompt-memory" }),
            cancellationToken);

        List<string> includedIds = [];
        List<string> excludedIds = [];

        var agentNotes = BuildSection(
            memories.Where(IsAgentNote),
            agentBudget,
            includedIds,
            excludedIds);
        var userProfile = BuildSection(
            memories.Where(IsUserProfile),
            userBudget,
            includedIds,
            excludedIds);

        return new PromptMemorySnapshot(agentNotes, userProfile, includedIds.Distinct().ToArray(), excludedIds.Distinct().ToArray());
    }

    private static string BuildSection(
        IEnumerable<MemoryRecord> memories,
        int budget,
        ICollection<string> includedIds,
        ICollection<string> excludedIds)
    {
        List<string> lines = [];
        var remaining = Math.Max(0, budget);

        foreach (var memory in memories
            .OrderByDescending(x => x.Importance)
            .ThenByDescending(x => x.Confidence)
            .ThenByDescending(x => x.UpdatedAt))
        {
            if (!PromptMemorySafety.IsSafeForPrompt(memory.Text))
            {
                excludedIds.Add(memory.Id);
                continue;
            }

            var line = $"- [{memory.Segment}/{memory.Tier}] {memory.Text}";

            if (line.Length > remaining)
            {
                excludedIds.Add(memory.Id);
                continue;
            }

            lines.Add(line);
            includedIds.Add(memory.Id);
            remaining -= line.Length + Environment.NewLine.Length;

            if (remaining <= 0)
            {
                break;
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static bool IsAgentNote(MemoryRecord memory)
    {
        return memory.Segment is MemorySegment.AgentIdentity
            or MemorySegment.AgentPreference
            or MemorySegment.AgentRelationship
            or MemorySegment.Project
            or MemorySegment.Knowledge
            or MemorySegment.Context
            or MemorySegment.Correction;
    }

    private static bool IsUserProfile(MemoryRecord memory)
    {
        return memory.Segment is MemorySegment.Identity or MemorySegment.Preference or MemorySegment.Relationship;
    }

    private static int GetInt(IReadOnlyDictionary<string, string> settings, string key, int fallback)
    {
        return int.TryParse(settings.GetValueOrDefault(key), out var value)
            ? Math.Max(0, value)
            : fallback;
    }
}
