using System.Text.RegularExpressions;

namespace Agent.Memory;

public sealed partial class RuleBasedMemoryExtractor : IMemoryExtractor
{
    public Task<MemoryExtractionResult> Extract(
        MemoryExtractionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var memoryText = GetExplicitMemoryText(request.UserEntry.Content);

        if (string.IsNullOrWhiteSpace(memoryText))
        {
            return Task.FromResult(new MemoryExtractionResult([]));
        }

        var segment = GetSegment(memoryText);
        var defaults = MemorySegmentDefaults.Get(segment);
        return Task.FromResult(new MemoryExtractionResult(
        [
            new ExtractedMemory(
                memoryText,
                defaults.Tier,
                segment,
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

    private static MemorySegment GetSegment(string text)
    {
        return text.Contains(" prefer ", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("I prefer ", StringComparison.OrdinalIgnoreCase)
            || text.Contains(" like ", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("I like ", StringComparison.OrdinalIgnoreCase)
            || text.Contains("best ", StringComparison.OrdinalIgnoreCase)
            ? MemorySegment.Preference
            : MemorySegment.Context;
    }

    [GeneratedRegex(@"^\s*(?:(?:make sure you|please|you should)\s+)?(?:remember|save|store|memorize|don't forget)[\s,]+(?:that\s+)?(?<value>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitRememberRegex();
}
