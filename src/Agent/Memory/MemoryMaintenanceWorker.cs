namespace Agent.Memory;

public sealed class MemoryMaintenanceWorker(
    IMemoryMaintenanceService maintenanceService,
    ILogger<MemoryMaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
                await maintenanceService.Cleanup(stoppingToken);
                await maintenanceService.Consolidate(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Memory maintenance failed.");
            }
        }
    }
}
