namespace Agent.Claude;

public sealed record ClaudeResult(
    string AssistantMessage,
    IReadOnlyList<ClaudeToolCall> ToolCalls,
    IReadOnlyDictionary<string, string> UsageMetadata,
    string? Error);
