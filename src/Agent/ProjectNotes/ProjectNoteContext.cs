namespace Agent.ProjectNotes;

public sealed record ProjectNoteContext(
    string ProjectName,
    string RootPath,
    IReadOnlyDictionary<string, string> Notes)
{
    public string ToPromptSection()
    {
        if (Notes.Count == 0)
        {
            return string.Empty;
        }

        var sections = Notes.Select(x =>
            $"## {Path.GetFileNameWithoutExtension(x.Key)}{Environment.NewLine}{x.Value.Trim()}");

        return "Durable project notes:" + Environment.NewLine + string.Join(Environment.NewLine + Environment.NewLine, sections);
    }
}
