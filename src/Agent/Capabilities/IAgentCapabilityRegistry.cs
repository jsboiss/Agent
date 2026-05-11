using Agent.SubAgents;
using Agent.Tools;

namespace Agent.Capabilities;

public interface IAgentCapabilityRegistry
{
    SubAgentCapabilities DefaultCapabilities { get; }

    IReadOnlyList<SubAgentCapabilities> AvailableCapabilities { get; }

    SubAgentCapabilities Parse(string? value, SubAgentCapabilities? fallback = null);

    IReadOnlyList<AgentToolDefinition> GetToolDefinitions(SubAgentCapabilities capabilities);

    IReadOnlyList<AgentToolDefinition> GetToolDefinitionsForToolsets(
        SubAgentCapabilities capabilities,
        IReadOnlySet<string> toolsets);

    IReadOnlyList<AgentToolDefinition> GetToolDefinitionsForProfile(
        SubAgentCapabilities capabilities,
        ToolsetProfile profile);

    bool RequiresDraftForExternalSideEffects(SubAgentCapabilities capabilities);
}
