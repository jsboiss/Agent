namespace Agent.Claude;

public interface IClaudeClient
{
    Task<ClaudeResult> Send(ClaudeRequest request, CancellationToken cancellationToken);
}
