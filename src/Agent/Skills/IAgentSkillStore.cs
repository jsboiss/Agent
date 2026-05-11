namespace Agent.Skills;

public interface IAgentSkillStore
{
    Task<IReadOnlyList<AgentSkill>> List(
        string workspaceRootPath,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentSkill>> FindRelevant(
        string workspaceRootPath,
        string input,
        int limit,
        CancellationToken cancellationToken);
}
