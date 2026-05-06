namespace Agent.Tokens;

public sealed record AgentTokenUsage(
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    int MainContextTokens,
    int ContextWindowTokens,
    int RemainingTokens,
    int CompactionThresholdTokens,
    int RemainingUntilCompactionTokens,
    string Source);
