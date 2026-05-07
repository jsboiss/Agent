namespace Agent.Context;

public sealed record ContextProviderRequest(
    string ConversationId,
    string Channel,
    string UserMessage,
    ContextProviderPlan Plan,
    IReadOnlyDictionary<string, string> Hints);

public sealed record ContextProviderResult(
    string ProviderId,
    IReadOnlyList<EvidenceItem> Items,
    bool Succeeded,
    string? Error = null);
