namespace Agent.Skills;

public sealed class FileAgentSkillStore : IAgentSkillStore
{
    public async Task<IReadOnlyList<AgentSkill>> List(
        string workspaceRootPath,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(workspaceRootPath, ".mainagent", "skills");

        if (!Directory.Exists(directory))
        {
            return [];
        }

        List<AgentSkill> skills = [];

        foreach (var path in Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = await File.ReadAllTextAsync(path, cancellationToken);
            var name = Path.GetFileNameWithoutExtension(path);
            var description = GetMetadata(body, "description") ?? name;
            var triggers = (GetMetadata(body, "triggers") ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var enabled = !string.Equals(GetMetadata(body, "enabled"), "false", StringComparison.OrdinalIgnoreCase);

            skills.Add(new AgentSkill(
                name,
                name,
                description,
                triggers,
                body,
                path,
                enabled,
                workspaceRootPath));
        }

        return skills;
    }

    public async Task<IReadOnlyList<AgentSkill>> FindRelevant(
        string workspaceRootPath,
        string input,
        int limit,
        CancellationToken cancellationToken)
    {
        var skills = await List(workspaceRootPath, cancellationToken);
        var terms = input
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length >= 3)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return skills
            .Where(x => x.Enabled)
            .Select(x => new
            {
                Skill = x,
                Score = x.TriggerHints.Count(y => input.Contains(y, StringComparison.OrdinalIgnoreCase))
                    + terms.Count(y => x.Name.Contains(y, StringComparison.OrdinalIgnoreCase) || x.Description.Contains(y, StringComparison.OrdinalIgnoreCase))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Skill.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, limit))
            .Select(x => x.Skill)
            .ToArray();
    }

    private static string? GetMetadata(string body, string key)
    {
        foreach (var line in body.Split('\n').Take(12))
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[(key.Length + 1)..].Trim();
            }
        }

        return null;
    }
}
