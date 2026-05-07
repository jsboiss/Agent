namespace Agent.ProjectNotes;

public sealed record ProjectNoteDistillationRequest(
    string Source,
    string UserMessage,
    string AssistantMessage,
    string? Error = null);
