namespace Agent.Messages;

public sealed class AgentPostResponseWorker(
    IAgentPostResponseQueue queue,
    IAgentPostResponseProcessor processor,
    ILogger<AgentPostResponseWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var item = await queue.Dequeue(stoppingToken);
                await processor.Process(item, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Post-response processing failed.");
            }
        }
    }
}

