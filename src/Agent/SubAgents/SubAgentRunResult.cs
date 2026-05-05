namespace Agent.SubAgents;

public sealed record SubAgentRunResult(
    string ConversationId,
    string ResultEntryId,
    string Summary);
