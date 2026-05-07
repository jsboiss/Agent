namespace Agent.ProjectNotes;

public sealed record AgentHomeOptions
{
    public const string SectionName = "Agent:Home";

    public string RootPath { get; init; } = string.Empty;
}
