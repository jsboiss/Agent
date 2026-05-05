using System.Text.RegularExpressions;

namespace Agent.Memory;

public sealed partial class RuleBasedMemoryExtractor : IMemoryExtractor
{
    public Task<MemoryExtractionResult> Extract(
        MemoryExtractionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var text = request.UserEntry.Content.Trim();
        List<ExtractedMemory> memories = [];

        AddMatch(memories, request.UserEntry.Id, text, RememberRegex(), MemoryTier.Long, MemorySegment.Context, 0.8, 0.9);
        AddMatch(memories, request.UserEntry.Id, text, PreferenceRegex(), MemoryTier.Permanent, MemorySegment.Preference, 0.8, 0.85);
        AddMatch(memories, request.UserEntry.Id, text, DontRegex(), MemoryTier.Permanent, MemorySegment.Correction, 0.9, 0.85);
        AddMatch(memories, request.UserEntry.Id, text, FromNowOnRegex(), MemoryTier.Permanent, MemorySegment.Correction, 0.9, 0.85);
        AddMatch(memories, request.UserEntry.Id, text, CallMeRegex(), MemoryTier.Permanent, MemorySegment.Identity, 0.9, 0.9, "User prefers to be called ");
        AddMatch(memories, request.UserEntry.Id, text, ProjectRegex(), MemoryTier.Long, MemorySegment.Project, 0.8, 0.8, "User project: ");
        AddMatch(memories, request.UserEntry.Id, text, WorkOnRegex(), MemoryTier.Long, MemorySegment.Project, 0.75, 0.75, "User works on ");
        AddMatch(memories, request.UserEntry.Id, text, IdentityRegex(), MemoryTier.Permanent, MemorySegment.Identity, 0.75, 0.75, "User is ");

        return Task.FromResult(new MemoryExtractionResult(memories.DistinctBy(x => x.Text).ToArray()));
    }

    private static void AddMatch(
        ICollection<ExtractedMemory> memories,
        string sourceMessageId,
        string text,
        Regex regex,
        MemoryTier tier,
        MemorySegment segment,
        double importance,
        double confidence,
        string prefix = "")
    {
        var match = regex.Match(text);

        if (!match.Success)
        {
            return;
        }

        var memoryText = (prefix + match.Groups["value"].Value).Trim().TrimEnd('.');

        if (memoryText.Length < 3)
        {
            return;
        }

        memories.Add(new ExtractedMemory(
            memoryText,
            tier,
            segment,
            importance,
            confidence,
            sourceMessageId));
    }

    [GeneratedRegex(@"\bremember that (?<value>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex RememberRegex();

    [GeneratedRegex(@"\b(?:my preference is|i prefer|i like) (?<value>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex PreferenceRegex();

    [GeneratedRegex(@"\b(?:don't|do not) (?<value>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DontRegex();

    [GeneratedRegex(@"\bfrom now on,? (?<value>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex FromNowOnRegex();

    [GeneratedRegex(@"\bcall me (?<value>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex CallMeRegex();

    [GeneratedRegex(@"\bmy project is (?<value>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ProjectRegex();

    [GeneratedRegex(@"\bi work on (?<value>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex WorkOnRegex();

    [GeneratedRegex(@"\bi am (?<value>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex IdentityRegex();
}
