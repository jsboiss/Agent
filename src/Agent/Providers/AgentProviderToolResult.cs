namespace Agent.Providers;

public sealed record AgentProviderToolResult(
    string ToolCallId,
    string Name,
    string Content);
