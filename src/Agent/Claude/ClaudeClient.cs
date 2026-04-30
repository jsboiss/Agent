using Agent.Dispatching;

namespace Agent.Claude;

public sealed class ClaudeClient : IClaudeClient
{
    public Task<ClaudeResult> Send(ClaudeRequest request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("Claude client is scaffolded but not implemented.");
    }
}
