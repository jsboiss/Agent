namespace Agent.Memory;

public sealed record MemoryScoutRequest(
    string ConversationId,
    string UserMessage,
    IReadOnlyDictionary<string, string> Hints);
