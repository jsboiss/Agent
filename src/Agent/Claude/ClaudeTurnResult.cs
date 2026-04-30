namespace Agent.Claude;

public sealed record ClaudeTurnResult(
    string AssistantMessage,
    IReadOnlyList<ClaudeToolCall> ToolCalls,
    IReadOnlyDictionary<string, string> UsageMetadata,
    string? Error);
