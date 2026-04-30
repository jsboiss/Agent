namespace Agent.Claude;

public sealed record ClaudeToolCall(
    string Id,
    string Name,
    IReadOnlyDictionary<string, string> Arguments);
