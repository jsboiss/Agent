namespace Agent.Context;

public sealed record EvidenceBundle(
    IReadOnlyList<EvidenceItem> Items,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static EvidenceBundle Empty { get; } = new([], new Dictionary<string, string>());

    public string ToPromptSection()
    {
        if (Items.Count == 0 && Metadata.Count == 0)
        {
            return string.Empty;
        }

        List<string> lines = [];

        lines.AddRange(Items
            .GroupBy(x => x.Source, StringComparer.OrdinalIgnoreCase)
            .SelectMany(x => new[] { $"Source: {x.Key}" }.Concat(x.Select(y => $"- {y.Label}: {y.Text}"))));

        foreach (var x in Metadata.Where(x => !string.Equals(x.Value, "ok", StringComparison.OrdinalIgnoreCase)))
        {
            lines.Add($"Source: {x.Key}");
            lines.Add($"- Context unavailable: {x.Value}");
        }

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
