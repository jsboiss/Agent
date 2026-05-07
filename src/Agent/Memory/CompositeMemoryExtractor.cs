namespace Agent.Memory;

public sealed class CompositeMemoryExtractor(
    RuleBasedMemoryExtractor ruleBasedExtractor,
    LlmMemoryExtractor llmExtractor) : IMemoryExtractor
{
    public async Task<MemoryExtractionResult> Extract(
        MemoryExtractionRequest request,
        CancellationToken cancellationToken)
    {
        var mode = request.Settings.GetValueOrDefault("memory.extraction.mode") ?? "llm";
        List<ExtractedMemory> memories = [];

        if (mode.Equals("rule", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("rule-and-llm", StringComparison.OrdinalIgnoreCase))
        {
            var ruleResult = await ruleBasedExtractor.Extract(request, cancellationToken);
            memories.AddRange(ruleResult.Memories);
        }

        if (mode.Equals("llm", StringComparison.OrdinalIgnoreCase)
            || mode.Equals("rule-and-llm", StringComparison.OrdinalIgnoreCase))
        {
            var llmResult = await llmExtractor.Extract(request, cancellationToken);
            memories.AddRange(llmResult.Memories);
        }

        return new MemoryExtractionResult(memories.DistinctBy(x => Normalize(x.Text)).ToArray());
    }

    private static string Normalize(string text)
    {
        return string.Join(
            " ",
            text
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.Trim('.', ',', ';', ':', '!', '?').ToLowerInvariant()));
    }
}
