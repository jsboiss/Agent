using System.Text.RegularExpressions;

namespace Agent.Memory;

public sealed partial class RuleBasedMemoryExtractor : IMemoryExtractor
{
    public Task<MemoryExtractionResult> Extract(
        MemoryExtractionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var explicitMemory = GetExplicitMemoryText(request.UserEntry.Content);

        if (string.IsNullOrWhiteSpace(explicitMemory))
        {
            return Task.FromResult(new MemoryExtractionResult([]));
        }

        var defaults = MemorySegmentDefaults.Get(MemorySegment.Context);
        return Task.FromResult(new MemoryExtractionResult(
        [
            new ExtractedMemory(
                explicitMemory,
                defaults.Tier,
                MemorySegment.Context,
                Math.Max(defaults.Importance, 0.8),
                Math.Max(defaults.Confidence, 0.9),
                request.UserEntry.Id)
        ]));
    }

    private static string GetExplicitMemoryText(string content)
    {
        var match = ExplicitRememberRegex().Match(content.Trim());

        if (!match.Success)
        {
            return string.Empty;
        }

        return match.Groups["value"].Value.Trim().TrimEnd('.');
    }

    [GeneratedRegex(@"^\s*(?:remember|save|store|memorize)\s+(?:that\s+)?(?<value>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitRememberRegex();
}
