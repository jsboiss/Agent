using Agent.AgentFlow;

namespace Agent.Bridge;

public sealed class ClaudeCodeBridgeClient : IClaudeCodeBridgeClient
{
    public Task<BridgeTurnResult> Send(BridgeTurnRequest request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException("Claude Code bridge client is scaffolded but not implemented.");
    }
}
