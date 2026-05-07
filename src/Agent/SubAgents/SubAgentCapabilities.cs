namespace Agent.SubAgents;

[Flags]
public enum SubAgentCapabilities
{
    None = 0,
    ReadOnly = 1,
    Code = 2,
    Web = 4,
    Memory = 8,
    ExternalActions = 16
}
