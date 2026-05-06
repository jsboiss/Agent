namespace Agent.Workspaces;

public sealed record SqliteAgentStateOptions
{
    public string ConnectionString { get; init; } = "Data Source=App_Data/mainagent.db";
}
