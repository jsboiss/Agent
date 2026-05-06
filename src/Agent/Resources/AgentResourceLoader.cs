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
            "Create a child sub-agent conversation for delegated work. Use this for code, file, web, slow, or risky work.",
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
                },
                "capabilities": {
                  "type": "string",
                  "description": "Comma-separated capabilities: ReadOnly, Code, Web, Memory, ExternalActions."
                },
                "requiresConfirmation": {
                  "type": "boolean",
                  "description": "Whether risky actions require explicit user confirmation."
                },
                "notificationTarget": {
                  "type": "string",
                  "description": "Optional channel-specific notification target."
                }
              },
              "required": ["task", "parentEntryId"]
            }
            """,
            "A child conversation id and final sub-agent result summary."),
        new AgentToolDefinition(
            "send_ack",
            "Send a short acknowledgement before slow delegated work.",
            """
            {
              "type": "object",
              "properties": {
                "message": {
                  "type": "string",
                  "description": "Short acknowledgement text."
                },
                "target": {
                  "type": "string",
                  "description": "Optional channel target."
                }
              },
              "required": ["message"]
            }
            """,
            "Acknowledgement delivery status."),
        new AgentToolDefinition(
            "save_draft",
            "Stage a risky or external action for later approval instead of applying it immediately.",
            """
            {
              "type": "object",
              "properties": {
                "kind": { "type": "string" },
                "summary": { "type": "string" },
                "payload": { "type": "string" },
                "sourceRunId": { "type": "string" }
              },
              "required": ["kind", "summary", "payload"]
            }
            """,
            "Pending draft id."),
        new AgentToolDefinition(
            "list_drafts",
            "List staged drafts pending approval or previously handled.",
            """
            {
              "type": "object",
              "properties": {
                "status": { "type": "string", "enum": ["Pending", "Approved", "Rejected", "Applied"] }
              }
            }
            """,
            "Draft summaries."),
        new AgentToolDefinition(
            "approve_draft",
            "Approve a pending draft after the user confirms it.",
            "{ \"type\": \"object\", \"properties\": { \"draftId\": { \"type\": \"string\" } }, \"required\": [\"draftId\"] }",
            "Draft approval status."),
        new AgentToolDefinition(
            "reject_draft",
            "Reject a pending draft after the user cancels it.",
            "{ \"type\": \"object\", \"properties\": { \"draftId\": { \"type\": \"string\" } }, \"required\": [\"draftId\"] }",
            "Draft rejection status."),
        new AgentToolDefinition(
            "create_automation",
            "Create a scheduled task that spawns a sub-agent when due.",
            """
            {
              "type": "object",
              "properties": {
                "name": { "type": "string" },
                "task": { "type": "string" },
                "schedule": { "type": "string", "description": "TimeSpan, 'every <TimeSpan>', or simple daily 5-field cron." },
                "capabilities": { "type": "string" },
                "notificationTarget": { "type": "string" }
              },
              "required": ["name", "task", "schedule"]
            }
            """,
            "Automation id and next run time."),
        new AgentToolDefinition(
            "list_automations",
            "List scheduled automations.",
            "{ \"type\": \"object\", \"properties\": {} }",
            "Automation summaries."),
        new AgentToolDefinition(
            "toggle_automation",
            "Enable or disable a scheduled automation.",
            "{ \"type\": \"object\", \"properties\": { \"automationId\": { \"type\": \"string\" }, \"enabled\": { \"type\": \"boolean\" } }, \"required\": [\"automationId\", \"enabled\"] }",
            "Automation status."),
        new AgentToolDefinition(
            "delete_automation",
            "Delete a scheduled automation.",
            "{ \"type\": \"object\", \"properties\": { \"automationId\": { \"type\": \"string\" } }, \"required\": [\"automationId\"] }",
            "Deletion status."),
        new AgentToolDefinition(
            "cancel_run",
            "Cancel a queued or running agent run.",
            "{ \"type\": \"object\", \"properties\": { \"runId\": { \"type\": \"string\" } }, \"required\": [\"runId\"] }",
            "Cancellation status."),
        new AgentToolDefinition(
            "retry_run",
            "Retry a prior sub-agent run as a new run.",
            "{ \"type\": \"object\", \"properties\": { \"runId\": { \"type\": \"string\" } }, \"required\": [\"runId\"] }",
            "New run id.")
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
        var compactionThreshold = GetCompactionThreshold(request.Settings.Values);
        var entries = await conversationRepository.ListEntries(request.Conversation.Id, cancellationToken);
        var rollingSummary = await summaryStore.Get(request.Conversation.Id, cancellationToken);

        if (ShouldCompact(entries, rollingSummary, recentEntryCount, compactionThreshold))
        {
            await conversationCompactor.Compact(
                new ConversationCompactionRequest(
                    request.Conversation,
                    recentEntryCount,
                    compactionThreshold,
                    request.Settings.Values),
                cancellationToken);
            rollingSummary = await summaryStore.Get(request.Conversation.Id, cancellationToken);
        }

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
        return """
            You are the dispatcher for the MainAgent harness.
            Answer quick control, memory, status, and conversational turns directly.
            For code changes, file changes, web research, slow work, automations, or risky actions, call send_ack first when useful, then spawn_agent with a crisp self-contained task.
            Mobile-originated risky actions must be staged or proposed and require confirmation before mutation.
            Keep outputs concise unless more detail is requested.
            """;
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

    private static int GetCompactionThreshold(IReadOnlyDictionary<string, string> settings)
    {
        return int.TryParse(settings.GetValueOrDefault("compaction.threshold"), out var threshold)
            ? Math.Max(1, threshold)
            : 8000;
    }

    private static bool ShouldCompact(
        IReadOnlyList<ConversationEntry> entries,
        ConversationSummary? rollingSummary,
        int recentEntryCount,
        int thresholdTokens)
    {
        if (entries.Count <= recentEntryCount)
        {
            return false;
        }

        var compactableEntries = GetCompactableEntries(entries, rollingSummary, recentEntryCount);

        if (compactableEntries.Count == 0)
        {
            return false;
        }

        return EstimateTokens(compactableEntries.Select(x => $"{x.Role}: {x.Content}")) > thresholdTokens;
    }

    private static int EstimateTokens(IEnumerable<string> values)
    {
        return values.Sum(x => string.IsNullOrWhiteSpace(x) ? 0 : Math.Max(1, (int)Math.Ceiling(x.Length / 4.0)));
    }

    private static IReadOnlyList<ConversationEntry> GetCompactableEntries(
        IReadOnlyList<ConversationEntry> entries,
        ConversationSummary? rollingSummary,
        int recentEntryCount)
    {
        if (string.IsNullOrWhiteSpace(rollingSummary?.ThroughEntryId))
        {
            return entries
                .Take(Math.Max(0, entries.Count - recentEntryCount))
                .ToArray();
        }

        var summaryIndex = entries
            .Select((x, y) => new { Entry = x, Index = y })
            .FirstOrDefault(x => string.Equals(x.Entry.Id, rollingSummary.ThroughEntryId, StringComparison.OrdinalIgnoreCase))
            ?.Index;

        var olderEntries = entries
            .Select((x, y) => new { Entry = x, Index = y })
            .Take(Math.Max(0, entries.Count - recentEntryCount))
            .ToArray();

        return summaryIndex is null
            ? olderEntries.Select(x => x.Entry).ToArray()
            : olderEntries
                .Where(x => x.Index > summaryIndex.Value)
                .Select(x => x.Entry)
                .ToArray();
    }
}
