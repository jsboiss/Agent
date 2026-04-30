namespace Agent.Bridge;

public sealed record BridgeToolCall(
    string Id,
    string Name,
    IReadOnlyDictionary<string, string> Arguments);
