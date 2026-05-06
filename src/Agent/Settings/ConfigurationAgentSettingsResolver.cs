using System.Text.Json;
using Agent.Providers;

namespace Agent.Settings;

public sealed class ConfigurationAgentSettingsResolver(IConfiguration configuration) : IAgentSettingsResolver
{
    private static IReadOnlyDictionary<string, string> AppDefaults => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["provider"] = AgentProviderType.Codex.ToString(),
        ["model"] = "gpt-5.5",
        ["queue.behavior"] = "enqueue-while-busy",
        ["compaction.threshold"] = "8000",
        ["compaction.recentEntryCount"] = "8",
        ["memory.enabled"] = "true",
        ["memory.scoutLimit"] = "5",
        ["memory.extraction.enabled"] = "true",
        ["memory.extraction.mode"] = "rule-and-llm",
        ["memory.extraction.provider"] = AgentProviderType.Codex.ToString(),
        ["codex.sandbox"] = "danger-full-access",
        ["codex.approvalPolicy"] = "never",
        ["codex.mode"] = "mcp",
        ["channel.delivery"] = "default"
    };

    public async Task<AgentSettings> Resolve(
        AgentSettingsResolveRequest request,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        List<string> appliedLayers = [];

        Apply(values, AppDefaults);
        appliedLayers.Add("app-defaults");

        var configuredProvider = values.GetValueOrDefault("provider");
        var providerModel = string.Equals(configuredProvider, AgentProviderType.Codex.ToString(), StringComparison.OrdinalIgnoreCase)
            ? configuration["Providers:Codex:Model"]
            : configuration["Providers:Ollama:Model"];

        if (!string.IsNullOrWhiteSpace(providerModel))
        {
            values["model"] = providerModel;
        }

        Apply(values, GetConfigurationValues("Agent:Settings"));
        appliedLayers.Add("global-user");

        Apply(values, await GetWorkspaceSettings(request.WorkspaceRootPath, cancellationToken));
        appliedLayers.Add("workspace");

        Apply(values, GetConfigurationValues($"Agent:Conversations:{request.Conversation.Id}:Settings"));
        appliedLayers.Add("conversation");

        Apply(values, GetConfigurationValues($"Agent:Channels:{request.Channel}:Settings"));
        values["channel"] = request.Channel;
        appliedLayers.Add("channel");

        Apply(values, request.PerMessageOverrides);
        appliedLayers.Add("per-message");

        return new AgentSettings(values, appliedLayers);
    }

    private IReadOnlyDictionary<string, string> GetConfigurationValues(string sectionPath)
    {
        var section = configuration.GetSection(sectionPath);

        if (!section.Exists())
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return section
            .AsEnumerable()
            .Where(x => x.Value is not null && !string.Equals(x.Key, section.Path, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                x => x.Key[(section.Path.Length + 1)..],
                x => x.Value ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyDictionary<string, string>> GetWorkspaceSettings(
        string workspaceRootPath,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(workspaceRootPath, ".mainagent.settings.json");

        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        await using var stream = File.OpenRead(path);
        var values = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(
            stream,
            cancellationToken: cancellationToken);

        return values is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
    }

    private static void Apply(
        IDictionary<string, string> target,
        IReadOnlyDictionary<string, string> source)
    {
        foreach (var item in source)
        {
            target[item.Key] = item.Value;
        }
    }
}
