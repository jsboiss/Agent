using Agent.Capabilities;
using Agent.Tools;

namespace Agent.Resources;

public sealed record WorkspaceContext(
    string RootPath,
    string CurrentPath,
    string ProjectName,
    IReadOnlyList<string> LoadedInstructions,
    IReadOnlyList<string> LoadedInstructionSources,
    IReadOnlyDictionary<string, string> ApplicableSettings,
    IReadOnlyList<AgentToolDefinition> AvailableTools,
    ToolsetProfile ToolsetProfile);
