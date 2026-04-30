using Agent.AgentFlow;

namespace Agent.Claude;

public sealed class ClaudeClient : IClaudeClient
{
    public Task<ClaudeTurnResult> Send(ClaudeTurnRequest request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("Claude client is scaffolded but not implemented.");
    }
}
