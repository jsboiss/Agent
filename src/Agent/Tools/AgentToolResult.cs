namespace Agent.Tools;

public sealed record AgentToolResult(
    string Name,
    bool Succeeded,
    string Content,
    IReadOnlyDictionary<string, string> Metadata);
