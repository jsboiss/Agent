using Agent.Conversations;
using Agent.Compaction;
using Agent.Providers;
using Agent.Tools;

namespace Agent.Resources;

public sealed class AgentResourceLoader(
    IWebHostEnvironment environment,
    IConversationRepository conversationRepository,
    IConversationSummaryStore summaryStore,
    IConversationCompactor conversationCompactor) : IAgentResourceLoader
{
    private static IReadOnlyList<AgentToolDefinition> DefaultTools =>
    [
        new AgentToolDefinition(
            "search_memory",
            "Search durable agent memory for relevant records before answering.",
            """
            {
              "type": "object",
              "properties": {
                "query": {
                  "type": "string",
                  "description": "Search text for memory lookup."
                },
                "limit": {
                  "type": "integer",
                  "description": "Maximum memory records to return.",
                  "minimum": 1,
                  "maximum": 20
                }
              },
              "required": ["query"]
            }
            """,
            "Matching memory records with ids, tiers, lifecycle state, and content."),
        new AgentToolDefinition(
            "write_memory",
            "Write a durable memory record when the user gives stable information worth preserving.",
            """
            {
              "type": "object",
              "properties": {
                "content": {
                  "type": "string",
                  "description": "Memory content to store."
                },
                "tier": {
                  "type": "string",
                  "description": "Memory tier.",
                  "enum": ["Short", "Long", "Permanent"]
                },
                "segment": {
                  "type": "string",
                  "description": "Memory segment.",
                  "enum": ["Identity", "Preference", "Correction", "Relationship", "Project", "Knowledge", "Context"]
                },
                "importance": {
                  "type": "number",
                  "description": "Importance from 0 to 1."
                },
                "confidence": {
                  "type": "number",
                  "description": "Confidence from 0 to 1."
                }
              },
              "required": ["content"]
            }
            """,
            "The stored memory record id and metadata."),
        new AgentToolDefinition(
            "spawn_agent",
            "Create a child sub-agent conversation for explicit delegated work.",
            """
            {
              "type": "object",
              "properties": {
                "task": {
                  "type": "string",
                  "description": "Self-contained task for the sub-agent."
                },
                "parentEntryId": {
                  "type": "string",
                  "description": "Conversation entry id where the child conversation branches from."
                }
              },
              "required": ["task", "parentEntryId"]
            }
            """,
            "A child conversation id and final sub-agent result summary.")
    ];

    public async Task<AgentResourceContext> Load(
        AgentResourceLoadRequest request,
        CancellationToken cancellationToken)
    {
        var rootPath = GetRootPath(environment.ContentRootPath);
        var workspaceInstructions = await ReadInstructions(rootPath, cancellationToken);
        var workspace = new WorkspaceContext(
            rootPath,
            environment.ContentRootPath,
            Path.GetFileName(rootPath),
            string.IsNullOrWhiteSpace(workspaceInstructions) ? [] : [workspaceInstructions],
            request.Settings.Values,
            DefaultTools);

        var recentEntryCount = GetRecentEntryCount(request.Settings.Values);
        var entries = await conversationRepository.ListEntries(request.Conversation.Id, cancellationToken);

        if (entries.Count > recentEntryCount)
        {
            await conversationCompactor.Compact(
                new ConversationCompactionRequest(request.Conversation, recentEntryCount),
                cancellationToken);
        }

        var rollingSummary = await summaryStore.Get(request.Conversation.Id, cancellationToken);

        return new AgentResourceContext(
            workspace,
            GetGlobalInstructions(),
            workspaceInstructions,
            GetChannelInstructions(request.Channel),
            GetProviderConstraints(request.ProviderType),
            GetPromptTemplate(workspace),
            GetToolContext(DefaultTools),
            string.Empty,
            GetConversationSummary(rollingSummary, entries, recentEntryCount));
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

    private static string GetToolContext(IReadOnlyList<AgentToolDefinition> tools)
    {
        if (tools.Count == 0)
        {
            return string.Empty;
        }

        var lines = tools.Select(x =>
            $"- {x.Name}: {x.Description}{Environment.NewLine}  Parameters: {x.JsonParameterSchema}{Environment.NewLine}  Result: {x.ResultDescription}");

        return "Available tools:" + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    private static string GetConversationSummary(
        ConversationSummary? rollingSummary,
        IReadOnlyList<ConversationEntry> entries,
        int recentEntryCount)
    {
        if (entries.Count == 0)
        {
            return rollingSummary?.Content ?? string.Empty;
        }

        var recentEntries = entries
            .TakeLast(recentEntryCount)
            .Select(x => $"- {x.Role}: {x.Content}");

        var recentSummary = "Recent exact entries:" + Environment.NewLine + string.Join(Environment.NewLine, recentEntries);

        if (rollingSummary is null)
        {
            return recentSummary;
        }

        return rollingSummary.Content + Environment.NewLine + Environment.NewLine + recentSummary;
    }

    private static int GetRecentEntryCount(IReadOnlyDictionary<string, string> settings)
    {
        return int.TryParse(settings.GetValueOrDefault("compaction.recentEntryCount"), out var recentEntryCount)
            ? Math.Max(1, recentEntryCount)
            : 8;
    }
}
