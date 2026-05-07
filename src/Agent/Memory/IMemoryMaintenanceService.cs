namespace Agent.Memory;

public interface IMemoryMaintenanceService
{
    Task<MemoryMaintenanceResult> Cleanup(CancellationToken cancellationToken);

    Task<MemoryMaintenanceResult> Consolidate(CancellationToken cancellationToken);
}
