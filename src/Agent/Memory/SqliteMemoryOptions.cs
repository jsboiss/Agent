namespace Agent.Memory;

public sealed record SqliteMemoryOptions
{
    public required string ConnectionString { get; init; }
}
