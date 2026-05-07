using System.Text;
using Agent.Workspaces;
using Microsoft.Extensions.Options;

namespace Agent.ProjectNotes;

public sealed class FileProjectNoteStore(IOptions<AgentHomeOptions> options, IWebHostEnvironment environment) : IProjectNoteStore
{
    private static IReadOnlyList<string> DefaultFiles =>
    [
        "overview.md",
        "commands.md",
        "architecture.md",
        "integrations.md",
        "decisions.md",
        "gotchas.md",
        "active-summary.md"
    ];

    private AgentHomeOptions Options { get; } = options.Value;

    public async Task<ProjectNoteContext> Load(AgentWorkspace workspace, CancellationToken cancellationToken)
    {
        var projectName = GetProjectName(workspace.RootPath);
        var directory = GetProjectDirectory(projectName);
        Directory.CreateDirectory(directory);
        await EnsureDefaultFiles(directory, projectName, cancellationToken);

        Dictionary<string, string> notes = new(StringComparer.OrdinalIgnoreCase);

        foreach (var fileName in DefaultFiles)
        {
            var path = Path.Combine(directory, fileName);

            if (!File.Exists(path))
            {
                continue;
            }

            var content = (await File.ReadAllTextAsync(path, cancellationToken)).Trim();

            if (!string.IsNullOrWhiteSpace(content) && !content.Contains("No durable notes yet.", StringComparison.OrdinalIgnoreCase))
            {
                notes[fileName] = Trim(content, 2400);
            }
        }

        return new ProjectNoteContext(projectName, directory, notes);
    }

    public async Task RecordActivity(
        AgentWorkspace workspace,
        string source,
        string content,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var projectName = GetProjectName(workspace.RootPath);
        var directory = GetProjectDirectory(projectName);
        Directory.CreateDirectory(directory);
        await EnsureDefaultFiles(directory, projectName, cancellationToken);

        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var entry = new StringBuilder()
            .AppendLine($"## {timestamp} - {source}")
            .AppendLine()
            .AppendLine(Trim(content.Trim(), 1600))
            .AppendLine()
            .ToString()
            .ReplaceLineEndings("\r\n");

        await File.AppendAllTextAsync(
            Path.Combine(directory, "activity.md"),
            entry,
            new UTF8Encoding(false),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(directory, "active-summary.md"),
            GetActiveSummary(source, content, timestamp).ReplaceLineEndings("\r\n"),
            new UTF8Encoding(false),
            cancellationToken);
    }

    private async Task EnsureDefaultFiles(
        string directory,
        string projectName,
        CancellationToken cancellationToken)
    {
        foreach (var fileName in DefaultFiles)
        {
            var path = Path.Combine(directory, fileName);

            if (File.Exists(path))
            {
                continue;
            }

            var content = GetInitialContent(projectName, fileName).ReplaceLineEndings("\r\n");
            await File.WriteAllTextAsync(path, content, new UTF8Encoding(false), cancellationToken);
        }
    }

    private string GetProjectDirectory(string projectName)
    {
        return Path.Combine(GetAgentHomePath(), "memory", "projects", SanitizePathSegment(projectName));
    }

    private string GetAgentHomePath()
    {
        if (!string.IsNullOrWhiteSpace(Options.RootPath))
        {
            return WorkspacePathResolver.NormalizeRootPath(Options.RootPath, environment.ContentRootPath);
        }

        return WorkspacePathResolver.GetDefaultAgentWorkspacePath(environment.ContentRootPath);
    }

    private static string GetProjectName(string rootPath)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(rootPath));

        return string.IsNullOrWhiteSpace(name) ? "AgentHome" : name;
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Select(x => invalid.Contains(x) ? '-' : x).ToArray()).Trim();

        return string.IsNullOrWhiteSpace(safe) ? "Project" : safe;
    }

    private static string GetInitialContent(string projectName, string fileName)
    {
        var title = Path.GetFileNameWithoutExtension(fileName);

        return fileName switch
        {
            "overview.md" => $"# {projectName} Overview{Environment.NewLine}{Environment.NewLine}No durable notes yet.{Environment.NewLine}",
            "integrations.md" => $"""
                # {projectName} Integrations

                Integration policy:

                - Prefer provider-agnostic interfaces before adding provider-specific behavior.
                - Keep read, draft, send, delete, and external side-effect permissions separate.
                - Stage externally visible or destructive actions for approval unless the user explicitly authorizes the action.
                - Store setup notes and operational decisions here rather than in the main conversation thread.
                """,
            "decisions.md" => $"# {projectName} Decisions{Environment.NewLine}{Environment.NewLine}No durable notes yet.{Environment.NewLine}",
            "gotchas.md" => $"# {projectName} Gotchas{Environment.NewLine}{Environment.NewLine}No durable notes yet.{Environment.NewLine}",
            "commands.md" => $"# {projectName} Commands{Environment.NewLine}{Environment.NewLine}No durable notes yet.{Environment.NewLine}",
            "architecture.md" => $"# {projectName} Architecture{Environment.NewLine}{Environment.NewLine}No durable notes yet.{Environment.NewLine}",
            "active-summary.md" => $"# {projectName} Active Summary{Environment.NewLine}{Environment.NewLine}No durable notes yet.{Environment.NewLine}",
            _ => $"# {title}{Environment.NewLine}{Environment.NewLine}No durable notes yet.{Environment.NewLine}"
        };
    }

    private static string GetActiveSummary(string source, string content, string timestamp)
    {
        return $"""
            # Active Summary

            Last updated: {timestamp}
            Source: {source}

            {Trim(content.Trim(), 1800)}
            """;
    }

    private static string Trim(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..maxLength] + "...";
    }
}
