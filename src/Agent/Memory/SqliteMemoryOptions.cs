namespace Agent.Memory;

public sealed record SqliteMemoryOptions
{
    public string ConnectionString { get; init; } = "Data Source=App_Data/mainagent.db";
}
