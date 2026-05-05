using Agent.Conversations;
using Agent.Providers;

namespace Agent.Resources;

public sealed class AgentResourceLoader(
    IWebHostEnvironment environment,
    IConfiguration configuration,
    IConversationRepository conversationRepository) : IAgentResourceLoader
{
    private static IReadOnlyList<string> DefaultTools => ["search_memory", "write_memory", "spawn_agent"];

    public async Task<AgentResourceContext> Load(
        AgentResourceLoadRequest request,
        CancellationToken cancellationToken)
    {
        var rootPath = GetRootPath(environment.ContentRootPath);
        var workspaceInstructions = await ReadInstructions(rootPath, cancellationToken);
        var applicableSettings = GetApplicableSettings(request);
        var workspace = new WorkspaceContext(
            rootPath,
            environment.ContentRootPath,
            Path.GetFileName(rootPath),
            string.IsNullOrWhiteSpace(workspaceInstructions) ? [] : [workspaceInstructions],
            applicableSettings,
            DefaultTools);

        var entries = await conversationRepository.ListEntries(request.Conversation.Id, cancellationToken);

        return new AgentResourceContext(
            workspace,
            GetGlobalInstructions(),
            workspaceInstructions,
            GetChannelInstructions(request.Channel),
            GetProviderConstraints(request.ProviderType),
            GetPromptTemplate(workspace),
            GetToolContext(DefaultTools),
            string.Empty,
            GetConversationSummary(entries));
    }

    private static string GetRootPath(string contentRootPath)
    {
        var directory = new DirectoryInfo(contentRootPath);

        while (directory.Parent is not null && directory.Name.Equals("src", StringComparison.OrdinalIgnoreCase))
        {
            directory = directory.Parent;
        }

        if (directory.Parent is not null && directory.Parent.Name.Equals("src", StringComparison.OrdinalIgnoreCase))
        {
            return directory.Parent.Parent?.FullName ?? directory.FullName;
        }

        return directory.FullName;
    }

    private static async Task<string> ReadInstructions(string rootPath, CancellationToken cancellationToken)
    {
        var path = Path.Combine(rootPath, "AGENTS.md");

        if (!File.Exists(path))
        {
            return string.Empty;
        }

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    private IReadOnlyDictionary<string, string> GetApplicableSettings(AgentResourceLoadRequest request)
    {
        Dictionary<string, string> settings = new(StringComparer.OrdinalIgnoreCase)
        {
            ["provider"] = request.ProviderType.ToString(),
            ["channel"] = request.Channel
        };

        var model = configuration["Providers:Ollama:Model"];

        if (!string.IsNullOrWhiteSpace(model))
        {
            settings["model"] = model;
        }

        return settings;
    }

    private static string GetGlobalInstructions()
    {
        return "You are the local development model for the MainAgent harness. Respond directly and keep outputs concise unless more detail is requested.";
    }

    private static string GetChannelInstructions(string channel)
    {
        return channel switch
        {
            "local-web" => "Channel: local web dashboard. Use clear formatting suitable for the dashboard chat surface.",
            "imessage" => "Channel: iMessage. Keep replies concise and readable on a phone.",
            "telegram" => "Channel: Telegram. Keep replies concise and readable on a phone.",
            _ => $"Channel: {channel}. Keep replies appropriate for the delivery channel."
        };
    }

    private static string GetProviderConstraints(AgentProviderType providerType)
    {
        return providerType switch
        {
            AgentProviderType.Ollama => "Provider constraints: local Ollama chat completion, non-streaming response.",
            AgentProviderType.ClaudeCode => "Provider constraints: Claude Code adapter process.",
            AgentProviderType.Codex => "Provider constraints: Codex adapter process.",
            _ => string.Empty
        };
    }

    private static string GetPromptTemplate(WorkspaceContext workspace)
    {
        return $"Workspace: {workspace.ProjectName}{Environment.NewLine}Root path: {workspace.RootPath}{Environment.NewLine}Current path: {workspace.CurrentPath}";
    }

    private static string GetToolContext(IReadOnlyList<string> tools)
    {
        if (tools.Count == 0)
        {
            return string.Empty;
        }

        return "Available tools:" + Environment.NewLine + string.Join(Environment.NewLine, tools.Select(x => $"- {x}"));
    }

    private static string GetConversationSummary(IReadOnlyList<ConversationEntry> entries)
    {
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        var recentEntries = entries
            .TakeLast(8)
            .Select(x => $"- {x.Role}: {x.Content}");

        return "Current conversation summary:" + Environment.NewLine + string.Join(Environment.NewLine, recentEntries);
    }
}
