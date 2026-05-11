namespace Agent.Skills;

public sealed record AgentSkill(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> TriggerHints,
    string Body,
    string Source,
    bool Enabled,
    string? WorkspaceScope);
