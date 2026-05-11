using Agent.Conversations;
using Agent.Compaction;
using Agent.Capabilities;
using Agent.Providers;
using Agent.ProjectNotes;
using Agent.Memory;
using Agent.Skills;
using Agent.Tools;
using Agent.Workspaces;

namespace Agent.Resources;

public sealed class AgentResourceLoader(
    IWebHostEnvironment environment,
    IConversationRepository conversationRepository,
    IConversationSummaryStore summaryStore,
    IConversationCompactor conversationCompactor,
    IProjectNoteStore projectNoteStore,
    IAgentCapabilityRegistry capabilityRegistry,
    IPromptMemorySnapshotBuilder promptMemorySnapshotBuilder,
    IAgentSkillStore skillStore) : IAgentResourceLoader
{
    public async Task<AgentResourceContext> Load(
        AgentResourceLoadRequest request,
        CancellationToken cancellationToken)
    {
        var rootPath = WorkspacePathResolver.NormalizeRootPath(request.WorkspaceRootPath, environment.ContentRootPath);
        var instructionFiles = await ReadInstructions(rootPath, cancellationToken);
        var workspaceInstructions = string.Join(Environment.NewLine + Environment.NewLine, instructionFiles.Select(x => x.Content));
        var availableTools = capabilityRegistry.GetToolDefinitionsForProfile(request.Capabilities, request.ToolsetProfile);
        var workspace = new WorkspaceContext(
            rootPath,
            environment.ContentRootPath,
            Path.GetFileName(rootPath),
            instructionFiles.Select(x => x.Content).ToArray(),
            instructionFiles.Select(x => x.SourcePath).ToArray(),
            request.Settings.Values,
            availableTools,
            request.ToolsetProfile);
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
        var skillInput = string.Join(Environment.NewLine, entries.TakeLast(4).Select(x => x.Content));
        var promptMemory = await promptMemorySnapshotBuilder.Build(request.Settings.Values, cancellationToken);
        var skills = await skillStore.FindRelevant(
            rootPath,
            skillInput,
            GetSkillLimit(request.Settings.Values),
            cancellationToken);
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
            GetGlobalInstructions(request.Settings.Values),
            workspaceInstructions,
            GetChannelInstructions(request.Channel),
            GetProviderConstraints(request.ProviderType),
            GetPromptTemplate(workspace),
            GetToolContext(availableTools),
            promptMemory.ToPromptSection(),
            GetSkillContext(skills),
            projectNotes.ToPromptSection(),
            string.Empty,
            GetConversationSummary(rollingSummary, entries, recentEntryCount));
    }

    private static async Task<IReadOnlyList<InstructionFile>> ReadInstructions(string rootPath, CancellationToken cancellationToken)
    {
        List<string> paths =
        [
            Path.Combine(rootPath, "AGENTS.md"),
            Path.Combine(rootPath, ".mainagent.md"),
            Path.Combine(rootPath, "CLAUDE.md"),
            Path.Combine(rootPath, ".cursorrules")
        ];
        var cursorRulesDirectory = Path.Combine(rootPath, ".cursor", "rules");

        if (Directory.Exists(cursorRulesDirectory))
        {
            paths.AddRange(Directory.EnumerateFiles(cursorRulesDirectory, "*.md", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase));
        }

        List<InstructionFile> instructions = [];

        foreach (var path in paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var content = await File.ReadAllTextAsync(path, cancellationToken);
            instructions.Add(new InstructionFile(
                path,
                $"Instructions from {Path.GetRelativePath(rootPath, path)}:{Environment.NewLine}{content}"));
        }

        return instructions;
    }

    private static string GetGlobalInstructions(IReadOnlyDictionary<string, string> settings)
    {
        return string.Join(
            Environment.NewLine + Environment.NewLine,
            new[]
            {
                """
            You are Main Agent, the user's primary general assistant.
            You can answer normal conversation, planning, personal context, memory, status, and quick control turns directly.
            You can also create sub-agents for long-running, code-heavy, file-changing, research-heavy, automation, app/program launching, shell-command, or risky actions.
            For those larger tasks, call send_ack first when useful, then spawn_agent with a crisp self-contained task.
            For Google Calendar, Gmail, schedule, availability, inbox, receipt, invoice, booking, confirmation, or email questions, use prefetched evidence or read tools directly and answer in the same turn. Do not delegate simple read-only personal-context lookups.
            Valid calendar tools are calendar_list_events, calendar_search_events, and calendar_get_availability. Never invent names like calendar_search.
            Use Gmail read tools for inbox/email searches when needed. Do not create drafts or send email unless the user explicitly asks, and respect draft/approval policy for side effects.
            Do not answer personal calendar or email state from memory or guess current events. If calendar or email context is unavailable, say the check failed instead of saying nothing was found.
            When the user asks to open, start, or launch a local app or program, treat it as an external action and delegate to a sub-agent with ExternalActions capability so it can use the shell, for example Windows Start-Process.
            Mobile-originated risky actions must be staged or proposed and require confirmation before mutation.
            Do not mention goblins, gremlins, trolls, or orcs unless the user explicitly asks about them.
            Keep outputs useful and appropriately sized for the request.
            """,
                GetStyleInstructions(settings)
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string GetStyleInstructions(IReadOnlyDictionary<string, string> settings)
    {
        List<string> lines = [];
        var personality = settings.GetValueOrDefault("agent.personality");
        var responseStyle = settings.GetValueOrDefault("agent.responseStyle");

        if (!string.IsNullOrWhiteSpace(personality))
        {
            lines.Add("Personality: " + personality);
        }

        if (!string.IsNullOrWhiteSpace(responseStyle))
        {
            lines.Add("Response style: " + responseStyle);
        }

        return lines.Count == 0
            ? string.Empty
            : "Assistant style:" + Environment.NewLine + string.Join(Environment.NewLine, lines);
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
        return $"Workspace: {workspace.ProjectName}{Environment.NewLine}Root path: {workspace.RootPath}{Environment.NewLine}Current path: {workspace.CurrentPath}{Environment.NewLine}Toolset profile: {workspace.ToolsetProfile}{Environment.NewLine}Loaded instruction files: {string.Join(", ", workspace.LoadedInstructionSources)}";
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

    private static string GetSkillContext(IReadOnlyList<AgentSkill> skills)
    {
        if (skills.Count == 0)
        {
            return string.Empty;
        }

        return "Relevant procedural skills:"
            + Environment.NewLine
            + string.Join(
                Environment.NewLine + Environment.NewLine,
                skills.Select(x => $"Skill: {x.Name}{Environment.NewLine}Description: {x.Description}{Environment.NewLine}{x.Body}"));
    }

    private static int GetSkillLimit(IReadOnlyDictionary<string, string> settings)
    {
        return int.TryParse(settings.GetValueOrDefault("skills.prompt.limit"), out var value)
            ? Math.Max(0, value)
            : 3;
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

    private sealed record InstructionFile(string SourcePath, string Content);
}
