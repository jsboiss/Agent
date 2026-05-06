namespace Agent.Memory;

public sealed record MemoryMaintenanceResult(
    int Scanned,
    int Archived,
    int Pruned,
    int Merged,
    int Superseded,
    string Summary);
