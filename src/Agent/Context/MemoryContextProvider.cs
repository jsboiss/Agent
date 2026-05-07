using Agent.Memory;

namespace Agent.Context;

public sealed class MemoryContextProvider(IMemoryScout memoryScout) : IContextProvider
{
    public string Id => "memory";

    public IReadOnlySet<string> Capabilities { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "durable-memory",
        "search"
    };

    public ContextProviderCost Cost => ContextProviderCost.Free;

    public ContextProviderLatency Latency => ContextProviderLatency.Fast;

    public ContextProviderSafety Safety => ContextProviderSafety.ReadOnly;

    public async Task<ContextProviderResult> Gather(
        ContextProviderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await memoryScout.Prefetch(
            new MemoryScoutRequest(
                request.ConversationId,
                request.Plan.Query ?? request.UserMessage,
                request.Hints),
            cancellationToken);

        var items = result.Memories
            .Select(x => new EvidenceItem(
                Id,
                $"memory:{x.Id}",
                x.Text,
                null,
                null,
                x.Confidence,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = x.Id,
                    ["tier"] = x.Tier.ToString(),
                    ["segment"] = x.Segment.ToString(),
                    ["lifecycle"] = x.Lifecycle.ToString()
                }))
            .ToArray();

        return new ContextProviderResult(Id, items, true, null);
    }
}
