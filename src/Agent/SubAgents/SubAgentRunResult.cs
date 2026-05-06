namespace Agent.SubAgents;

public sealed record SubAgentRunResult(
    string ConversationId,
    string ResultEntryId,
    string Summary,
    string? RunId = null,
    string? CodexThreadId = null,
    string Status = "Created");
