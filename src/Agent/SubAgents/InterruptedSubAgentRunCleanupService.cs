using Agent.Workspaces;

namespace Agent.SubAgents;

public sealed class InterruptedSubAgentRunCleanupService(
    SqliteAgentStateStore stateStore,
    ILogger<InterruptedSubAgentRunCleanupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var count = await stateStore.FailInterruptedSubAgentRuns(cancellationToken);

        if (count > 0)
        {
            logger.LogWarning("Marked {Count} interrupted sub-agent runs as failed.", count);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
