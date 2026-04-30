namespace Agent.Bridge;

public sealed record BridgeTurnResult(
    string AssistantMessage,
    IReadOnlyList<BridgeToolCall> ToolCalls,
    IReadOnlyDictionary<string, string> UsageMetadata,
    string? Error);
