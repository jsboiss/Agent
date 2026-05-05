using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Agent.Resources;
using Agent.Tools;

namespace Agent.Providers.ClaudeCode;

public sealed class ClaudeCodeProviderClient(IHostEnvironment environment) : IAgentProviderClient
{
    public AgentProviderType Type => AgentProviderType.ClaudeCode;

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<AgentProviderResult> Send(AgentProviderRequest request, CancellationToken cancellationToken)
    {
        var providerDirectory = GetProviderDirectory(environment.ContentRootPath);
        var adapterPath = Path.Combine(providerDirectory, "dist", "index.js");

        if (!File.Exists(adapterPath))
        {
            throw new InvalidOperationException(
                $"Claude Code provider adapter was not found at '{adapterPath}'. Run 'npm install' and 'npm run build' in '{providerDirectory}'.");
        }

        var startInfo = GetProcessStartInfo(providerDirectory, adapterPath);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start Claude Code provider process.");
        
        var providerRequest = GetProviderProcessRequest(request);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await JsonSerializer.SerializeAsync(
            process.StandardInput.BaseStream,
            providerRequest,
            JsonOptions,
            cancellationToken);

        process.StandardInput.Close();

        var result = await JsonSerializer.DeserializeAsync<AgentProviderResult>(
            process.StandardOutput.BaseStream,
            JsonOptions,
            cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var standardError = await standardErrorTask;

        if (result is null)
        {
            throw new InvalidOperationException("Claude Code provider returned no JSON result.");
        }

        if (process.ExitCode != 0)
        {
            return result with
            {
                Error = string.IsNullOrWhiteSpace(result.Error)
                    ? $"Claude Code provider exited with code {process.ExitCode}: {standardError}"
                    : result.Error
            };
        }

        return result;
    }

    private static ProcessStartInfo GetProcessStartInfo(string providerDirectory, string adapterPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "node",
            WorkingDirectory = providerDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add(adapterPath);
        startInfo.Environment.Remove("ANTHROPIC_API_KEY");

        return startInfo;
    }

    private static ProviderProcessRequest GetProviderProcessRequest(AgentProviderRequest request)
    {
        return new ProviderProcessRequest(
            request.Kind,
            request.ConversationId,
            request.UserMessage,
            request.Resources.BuildSystemPrompt(),
            request.Resources.Workspace,
            request.MemoryContext,
            request.InjectedMemories.Select(x => x.Id).ToArray(),
            request.AvailableTools);
    }

    private static string GetProviderDirectory(string contentRootPath)
    {
        var directory = new DirectoryInfo(contentRootPath);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "ClaudeCode");

            if (File.Exists(Path.Combine(candidate, "package.json")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find tools/ClaudeCode from content root '{contentRootPath}'.");
    }

    private sealed record ProviderProcessRequest(
        AgentProviderType Type,
        string ConversationId,
        string UserMessage,
        string SystemPrompt,
        WorkspaceContext Workspace,
        string MemoryContext,
        IReadOnlyList<string> InjectedMemoryIds,
        IReadOnlyList<AgentToolDefinition> AvailableTools);
}
