namespace Agent.Memory;

public sealed class MemoryScout(IMemoryStore memoryStore) : IMemoryScout
{
    public async Task<MemoryScoutResult> Prefetch(
        MemoryScoutRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserMessage))
        {
            return new MemoryScoutResult(false, [], string.Empty);
        }

        var limit = GetLimit(request.Hints);
        var memories = await memoryStore.Search(
            new MemorySearchRequest(
                request.UserMessage,
                limit,
                new HashSet<MemoryLifecycle> { MemoryLifecycle.Active },
                request.Hints),
            cancellationToken);

        if (memories.Count == 0)
        {
            return new MemoryScoutResult(false, memories, string.Empty);
        }

        var context = "Relevant memories:" + Environment.NewLine
            + string.Join(Environment.NewLine, memories.Select(x => $"- [{x.Id}] {x.Text}"));

        return new MemoryScoutResult(true, memories, context);
    }

    private static int GetLimit(IReadOnlyDictionary<string, string> hints)
    {
        return int.TryParse(hints.GetValueOrDefault("limit"), out var limit)
            ? Math.Clamp(limit, 1, 20)
            : 5;
    }
}
