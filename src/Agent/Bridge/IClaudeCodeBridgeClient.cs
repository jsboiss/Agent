namespace Agent.Bridge;

public interface IClaudeCodeBridgeClient
{
    Task<BridgeTurnResult> Send(BridgeTurnRequest request, CancellationToken cancellationToken);
}
