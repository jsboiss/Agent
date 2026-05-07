using Agent.SubAgents;
using Agent.Tools;

namespace Agent.Capabilities;

public interface IAgentCapabilityRegistry
{
    SubAgentCapabilities DefaultCapabilities { get; }

    IReadOnlyList<SubAgentCapabilities> AvailableCapabilities { get; }

    SubAgentCapabilities Parse(string? value, SubAgentCapabilities? fallback = null);

    IReadOnlyList<AgentToolDefinition> GetToolDefinitions(SubAgentCapabilities capabilities);

    bool RequiresDraftForExternalSideEffects(SubAgentCapabilities capabilities);
}
