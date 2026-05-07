using Agent.Conversations;
using Agent.Compaction;
using Agent.Capabilities;
using Agent.Providers;
using Agent.ProjectNotes;
using Agent.Tools;
using Agent.Workspaces;

namespace Agent.Resources;

public sealed class AgentResourceLoader(
    IWebHostEnvironment environment,
    IConversationRepository conversationRepository,
    IConversationSummaryStore summaryStore,
    IConversationCompactor conversationCompactor,
    IProjectNoteStore projectNoteStore,
    IAgentCapabilityRegistry capabilityRegistry) : IAgentResourceLoader
{
    public async Task<AgentResourceContext> Load(
        AgentResourceLoadRequest request,
        CancellationToken cancellationToken)
    {
        var rootPath = WorkspacePathResolver.NormalizeRootPath(request.WorkspaceRootPath, environment.ContentRootPath);
        var workspaceInstructions = await ReadInstructions(rootPath, cancellationToken);
        var availableTools = capabilityRegistry.GetToolDefinitions(request.Capabilities);
        var workspace = new WorkspaceContext(
            rootPath,
            environment.ContentRootPath,
            Path.GetFileName(rootPath),
            string.IsNullOrWhiteSpace(workspaceInstructions) ? [] : [workspaceInstructions],
            request.Settings.Values,
            availableTools);
        var projectNotes = await projectNoteStore.Load(
            new AgentWorkspace(
                string.Empty,
                workspace.ProjectName,
                rootPath,
                null,
                null,
                null,
                false,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow),
            cancellationToken);

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
            GetToolContext(availableTools),
            projectNotes.ToPromptSection(),
            string.Empty,
            GetConversationSummary(rollingSummary, entries, recentEntryCount));
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
            For code changes, file changes, web research, slow work, automations, app/program launching, shell commands, or risky actions, call send_ack first when useful, then spawn_agent with a crisp self-contained task.
            For Google Calendar or schedule questions, use the calendar tools directly and answer from the tool results in the same turn. Do not answer calendar questions from memory or guess current events.
            When the user asks to open, start, or launch a local app or program, treat it as an external action and delegate to a sub-agent with ExternalActions capability so it can use the shell, for example Windows Start-Process.
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
