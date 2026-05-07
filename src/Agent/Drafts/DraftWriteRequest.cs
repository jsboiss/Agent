namespace Agent.Drafts;

public sealed record DraftWriteRequest(
    string Kind,
    string Summary,
    string Payload,
    string? SourceRunId,
    string ConversationId,
    string Channel);
