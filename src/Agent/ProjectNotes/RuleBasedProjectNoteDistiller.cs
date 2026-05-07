using Agent.Workspaces;

namespace Agent.ProjectNotes;

public sealed class RuleBasedProjectNoteDistiller(IProjectNoteStore projectNoteStore) : IProjectNoteDistiller
{
    public async Task Distill(
        AgentWorkspace workspace,
        ProjectNoteDistillationRequest request,
        CancellationToken cancellationToken)
    {
        List<ProjectNoteUpdate> updates = [];
        var combined = $"{request.UserMessage}{Environment.NewLine}{request.AssistantMessage}";
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'");

        AddIfRelevant(
            updates,
            combined,
            "architecture.md",
            "Architecture Updates",
            ["architecture", "resource loader", "provider", "workspace", "context", "sub-agent", "subagent", "message processor", "service", "interface"],
            GetEntry(timestamp, request, "Architecture-relevant implementation or workflow detail."));
        AddIfRelevant(
            updates,
            combined,
            "integrations.md",
            "Integration Updates",
            ["integration", "gmail", "composio", "calendar", "oauth", "provider-agnostic", "capability", "permission", "external account"],
            GetEntry(timestamp, request, "Integration or permission-boundary detail."));
        AddIfRelevant(
            updates,
            combined,
            "commands.md",
            "Commands",
            ["dotnet ", "npm ", "git ", "powershell", "build", "test", "run ", "command"],
            GetEntry(timestamp, request, "Command or verification detail."));
        AddIfRelevant(
            updates,
            combined,
            "decisions.md",
            "Decisions",
            ["decided", "decision", "prefer", "policy", "should", "default", "instead"],
            GetEntry(timestamp, request, "Decision or durable policy detail."));
        AddIfRelevant(
            updates,
            combined,
            "gotchas.md",
            "Gotchas",
            ["failed", "error", "mixed line", "line ending", "warning", "blocker", "gotcha", "left alone", "caveat"],
            GetEntry(timestamp, request, "Operational caveat or risk detail."));

        if (updates.Count == 0 && IsProjectShapingWork(combined))
        {
            updates.Add(new ProjectNoteUpdate(
                "overview.md",
                "Project Updates",
                GetEntry(timestamp, request, "General durable project update.")));
        }

        await projectNoteStore.ApplyDistillation(workspace, updates, cancellationToken);
    }

    private static void AddIfRelevant(
        ICollection<ProjectNoteUpdate> updates,
        string combined,
        string fileName,
        string heading,
        IReadOnlyList<string> keywords,
        string content)
    {
        if (keywords.Any(x => combined.Contains(x, StringComparison.OrdinalIgnoreCase)))
        {
            updates.Add(new ProjectNoteUpdate(fileName, heading, content));
        }
    }

    private static bool IsProjectShapingWork(string combined)
    {
        return combined.Contains("add", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("implement", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("change", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("create", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetEntry(
        string timestamp,
        ProjectNoteDistillationRequest request,
        string reason)
    {
        List<string> lines =
        [
            $"- Time: {timestamp}",
            $"- Source: {request.Source}",
            $"- Reason: {reason}",
            $"- Request: {Shorten(request.UserMessage, 500)}",
            $"- Result: {Shorten(request.AssistantMessage, 900)}"
        ];

        if (!string.IsNullOrWhiteSpace(request.Error))
        {
            lines.Add($"- Error: {Shorten(request.Error, 500)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string Shorten(string value, int length)
    {
        return value.Length <= length
            ? value
            : value[..length] + "...";
    }
}
