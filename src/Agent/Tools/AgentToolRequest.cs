namespace Agent.Tools;

public sealed record AgentToolRequest(
    string Name,
    IReadOnlyDictionary<string, string> Arguments,
    string ConversationId,
    string Channel = "tool");
