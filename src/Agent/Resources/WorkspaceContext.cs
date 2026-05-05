using Agent.Tools;

namespace Agent.Resources;

public sealed record WorkspaceContext(
    string RootPath,
    string CurrentPath,
    string ProjectName,
    IReadOnlyList<string> LoadedInstructions,
    IReadOnlyDictionary<string, string> ApplicableSettings,
    IReadOnlyList<AgentToolDefinition> AvailableTools);
