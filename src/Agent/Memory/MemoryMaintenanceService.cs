using Agent.Events;

namespace Agent.Memory;

public sealed class MemoryMaintenanceService(
    IMemoryStore memoryStore,
    IAgentEventSink eventSink) : IMemoryMaintenanceService
{
    public async Task<MemoryMaintenanceResult> Cleanup(CancellationToken cancellationToken)
    {
        var memories = await memoryStore.Search(
            new MemorySearchRequest(
                string.Empty,
                500,
                new HashSet<MemoryLifecycle> { MemoryLifecycle.Active },
                new Dictionary<string, string>()),
            cancellationToken);
        var archived = 0;
        var pruned = 0;

        foreach (var memory in memories)
        {
            if (memory.Tier == MemoryTier.Permanent)
            {
                continue;
            }

            var score = GetEffectiveScore(memory);

            if (score < 0.05)
            {
                await memoryStore.UpdateLifecycle(memory.Id, MemoryLifecycle.Pruned, cancellationToken);
                pruned++;
            }
            else if (score < 0.15 && memory.Tier == MemoryTier.Short)
            {
                await memoryStore.UpdateLifecycle(memory.Id, MemoryLifecycle.Archived, cancellationToken);
                archived++;
            }
        }

        await Publish(
            AgentEventKind.MemoryConsolidationCompleted,
            "main",
            new Dictionary<string, string>
            {
                ["operation"] = "cleanup",
                ["scanned"] = memories.Count.ToString(),
                ["archived"] = archived.ToString(),
                ["pruned"] = pruned.ToString()
            },
            cancellationToken);

        return new MemoryMaintenanceResult(memories.Count, archived, pruned, 0, 0, "Memory cleanup completed.");
    }

    public async Task<MemoryMaintenanceResult> Consolidate(CancellationToken cancellationToken)
    {
        var memories = await memoryStore.Search(
            new MemorySearchRequest(
                string.Empty,
                500,
                new HashSet<MemoryLifecycle> { MemoryLifecycle.Active },
                new Dictionary<string, string>()),
            cancellationToken);
        var merged = 0;
        var superseded = 0;

        foreach (var group in memories.GroupBy(x => Normalize(x.Text), StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group
                .OrderByDescending(x => x.Importance)
                .ThenByDescending(x => x.Confidence)
                .ThenByDescending(x => x.UpdatedAt)
                .ToArray();

            if (ordered.Length <= 1)
            {
                continue;
            }

            var keeper = ordered[0];
            var absorbed = ordered.Skip(1).Select(x => x.Id).ToArray();
            await memoryStore.Update(
                keeper.Id,
                keeper.Text,
                keeper.Tier,
                keeper.Segment,
                Math.Clamp(ordered.Max(x => x.Importance), 0, 1),
                Math.Clamp(ordered.Max(x => x.Confidence), 0, 1),
                string.Join(",", absorbed),
                cancellationToken);

            foreach (var memory in ordered.Skip(1))
            {
                await memoryStore.UpdateLifecycle(memory.Id, MemoryLifecycle.Archived, cancellationToken);
                merged++;
            }
        }

        foreach (var correction in memories.Where(x => x.Segment == MemorySegment.Correction))
        {
            var related = memories
                .Where(x => x.Segment != MemorySegment.Correction)
                .Where(x => x.Lifecycle == MemoryLifecycle.Active)
                .Where(x => HasOverlap(correction.Text, x.Text))
                .Where(x => x.CreatedAt < correction.CreatedAt)
                .Take(3)
                .ToArray();

            if (related.Length == 0)
            {
                continue;
            }

            await memoryStore.Update(
                correction.Id,
                correction.Text,
                correction.Tier,
                correction.Segment,
                correction.Importance,
                correction.Confidence,
                string.Join(",", related.Select(x => x.Id)),
                cancellationToken);

            foreach (var memory in related)
            {
                await memoryStore.UpdateLifecycle(memory.Id, MemoryLifecycle.Archived, cancellationToken);
                superseded++;
            }
        }

        await Publish(
            AgentEventKind.MemoryConsolidationCompleted,
            "main",
            new Dictionary<string, string>
            {
                ["operation"] = "consolidation",
                ["scanned"] = memories.Count.ToString(),
                ["merged"] = merged.ToString(),
                ["superseded"] = superseded.ToString()
            },
            cancellationToken);

        return new MemoryMaintenanceResult(memories.Count, 0, 0, merged, superseded, "Memory consolidation completed.");
    }

    private static double GetEffectiveScore(MemoryRecord memory)
    {
        var lastAccessedAt = memory.LastAccessedAt ?? memory.UpdatedAt;
        var daysSinceAccess = Math.Max(0, (DateTimeOffset.UtcNow - lastAccessedAt).TotalDays);
        var defaults = MemorySegmentDefaults.Get(memory.Segment);
        var decay = Math.Exp(-defaults.DecayRate * daysSinceAccess);
        var reinforcement = 1 + (Math.Log(1 + memory.AccessCount) * 0.1);

        return Math.Clamp(memory.Importance * memory.Confidence * decay * reinforcement, 0, 1);
    }

    private static bool HasOverlap(string a, string b)
    {
        var aTerms = GetTerms(a);
        var bTerms = GetTerms(b);

        return aTerms.Intersect(bTerms, StringComparer.OrdinalIgnoreCase).Count() >= 2;
    }

    private static ISet<string> GetTerms(string value)
    {
        return value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim('.', ',', ';', ':', '!', '?', '"', '\'').ToLowerInvariant())
            .Where(x => x.Length >= 4)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
    {
        return string.Join(" ", GetTerms(value).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
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
