namespace Agent.Tools;

public sealed record AgentToolDefinition(
    string Name,
    string Description,
    string JsonParameterSchema,
    string? ResultDescription);
