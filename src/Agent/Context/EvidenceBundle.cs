namespace Agent.Context;

public sealed record EvidenceBundle(
    IReadOnlyList<EvidenceItem> Items,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static EvidenceBundle Empty { get; } = new([], new Dictionary<string, string>());

    public string ToPromptSection()
    {
        if (Items.Count == 0)
        {
            return string.Empty;
        }

        var lines = Items
            .GroupBy(x => x.Source, StringComparer.OrdinalIgnoreCase)
            .SelectMany(x => new[] { $"Source: {x.Key}" }.Concat(x.Select(y => $"- {y.Label}: {y.Text}")));

        return "Evidence context:" + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }
}

public sealed record EvidenceItem(
    string Source,
    string Label,
    string Text,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    double Confidence,
    IReadOnlyDictionary<string, string> Metadata);
