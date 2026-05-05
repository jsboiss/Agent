namespace Agent.Settings;

public interface IAgentSettingsResolver
{
    Task<AgentSettings> Resolve(AgentSettingsResolveRequest request, CancellationToken cancellationToken);
}
