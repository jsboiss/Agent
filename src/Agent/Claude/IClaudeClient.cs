namespace Agent.Claude;

public interface IClaudeClient
{
    Task<ClaudeTurnResult> Send(ClaudeTurnRequest request, CancellationToken cancellationToken);
}
