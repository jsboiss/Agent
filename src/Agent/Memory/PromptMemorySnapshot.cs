namespace Agent.Memory;

public sealed record PromptMemorySnapshot(
    string AgentNotes,
    string UserProfile,
    IReadOnlyList<string> IncludedMemoryIds,
    IReadOnlyList<string> ExcludedMemoryIds)
{
    public string ToPromptSection()
    {
        var sections = new List<string>();

        if (!string.IsNullOrWhiteSpace(AgentNotes))
        {
            sections.Add("Agent Notes:" + Environment.NewLine + AgentNotes);
        }

        if (!string.IsNullOrWhiteSpace(UserProfile))
        {
            sections.Add("User Profile:" + Environment.NewLine + UserProfile);
        }

        return sections.Count == 0
            ? string.Empty
            : "Curated prompt memory:" + Environment.NewLine + string.Join(Environment.NewLine + Environment.NewLine, sections);
    }
}
