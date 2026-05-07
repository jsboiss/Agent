namespace Agent.ProjectNotes;

public sealed record ProjectNoteUpdate(
    string FileName,
    string Heading,
    string Content);
