using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Agent.Providers.Codex;

public sealed class CodexProviderClient(IOptions<CodexProviderOptions> options) : IAgentProviderClient
{
    public AgentProviderType Type => AgentProviderType.Codex;

    private CodexProviderOptions Options { get; } = options.Value;

    private SemaphoreSlim SyncRoot { get; } = new(1, 1);

    private Process? ProcessInstance { get; set; }

    private Task<string>? StandardErrorTask { get; set; }

    private string? ProcessDescription { get; set; }

    private int NextRequestId { get; set; } = 1;

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<AgentProviderResult> Send(AgentProviderRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.WorkspaceRootPath))
        {
            return new AgentProviderResult(
                string.Empty,
                [],
                new Dictionary<string, string>(),
                "Codex request did not include a workspace root path.");
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(GetTimeout(request));
        await SyncRoot.WaitAsync(timeoutSource.Token);

        try
        {
            var process = await GetProcess(request.WorkspaceRootPath, timeoutSource.Token);

            var toolName = string.IsNullOrWhiteSpace(request.CodexThreadId) ? "codex" : "codex-reply";
            var arguments = GetToolArguments(request, toolName);
            var requestId = GetNextRequestId();
            var toolResponse = await SendRpc(
                process,
                new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = requestId,
                    ["method"] = "tools/call",
                    ["params"] = new JsonObject
                    {
                        ["name"] = toolName,
                        ["arguments"] = arguments
                    }
                },
                requestId,
                timeoutSource.Token);

            if (toolResponse["error"] is not null)
            {
                var errorResult = GetErrorResult("Codex MCP tool call failed.", toolResponse, request.CodexThreadId);

                if (string.Equals(toolName, "codex-reply", StringComparison.OrdinalIgnoreCase)
                    && (errorResult.Error?.Contains("Session not found", StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    return await SendNewThreadAfterStaleReply(
                        process,
                        request,
                        timeoutSource.Token);
                }

                return errorResult;
            }

            if (IsToolError(toolResponse))
            {
                var providerResult = GetProviderResult(toolResponse, request.CodexThreadId);
                var errorResult = providerResult with
                {
                    Error = providerResult.AssistantMessage
                };

                if (string.Equals(toolName, "codex-reply", StringComparison.OrdinalIgnoreCase)
                    && (errorResult.Error?.Contains("Session not found", StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    return await SendNewThreadAfterStaleReply(
                        process,
                        request,
                        timeoutSource.Token);
                }

                return errorResult with { AssistantMessage = string.Empty };
            }

            return GetProviderResult(toolResponse, request.CodexThreadId);
        }
        catch (OperationCanceledException exception)
        {
            var diagnostics = GetProcessDiagnostics();
            StopProcess();

            return new AgentProviderResult(
                string.Empty,
                [],
                new Dictionary<string, string>(),
                $"Codex MCP request timed out or was cancelled: {exception.Message}{diagnostics}",
                request.CodexThreadId);
        }
        catch (Exception exception)
        {
            var diagnostics = GetProcessDiagnostics();
            StopProcess();

            return new AgentProviderResult(
                string.Empty,
                [],
                new Dictionary<string, string>(),
                exception.Message + diagnostics,
                request.CodexThreadId);
        }
        finally
        {
            SyncRoot.Release();
        }
    }

    private static JsonObject GetToolArguments(AgentProviderRequest request, string toolName)
    {
        var prompt = GetPrompt(request);

        if (string.Equals(toolName, "codex-reply", StringComparison.OrdinalIgnoreCase))
        {
            return new JsonObject
            {
                ["prompt"] = prompt,
                ["threadId"] = request.CodexThreadId
            };
        }

        return new JsonObject
        {
            ["prompt"] = prompt,
            ["cwd"] = request.WorkspaceRootPath,
            ["sandbox"] = request.AllowsMutation ? request.SandboxMode : "read-only",
            ["approval-policy"] = request.ApprovalPolicy,
            ["base-instructions"] = request.Resources.BuildSystemPrompt(),
            ["model"] = request.Resources.Workspace.ApplicableSettings.GetValueOrDefault("model") ?? "gpt-5.5"
        };
    }

    private TimeSpan GetTimeout(AgentProviderRequest request)
    {
        var configuredSeconds = Math.Max(30, Options.TimeoutSeconds);

        if (request.RouteKind == Workspaces.AgentRouteKind.Chat)
        {
            configuredSeconds = Math.Min(configuredSeconds, 90);
        }

        return TimeSpan.FromSeconds(configuredSeconds);
    }

    private async Task<AgentProviderResult> SendNewThreadAfterStaleReply(
        Process process,
        AgentProviderRequest request,
        CancellationToken cancellationToken)
    {
        var requestId = GetNextRequestId();
        var toolResponse = await SendRpc(
            process,
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = requestId,
                ["method"] = "tools/call",
                ["params"] = new JsonObject
                {
                    ["name"] = "codex",
                    ["arguments"] = GetToolArguments(request with { CodexThreadId = null }, "codex")
                }
            },
            requestId,
            cancellationToken);

        return toolResponse["error"] is not null
            ? GetErrorResult("Codex MCP retry after stale session failed.", toolResponse, null)
            : GetProviderResult(toolResponse, null);
    }

    private ProcessStartInfo GetStartInfo(string workspaceRootPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Options.Command,
            WorkingDirectory = workspaceRootPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (OperatingSystem.IsWindows()
            && string.Equals(Options.Command, "codex", StringComparison.OrdinalIgnoreCase)
            && TryGetNpmCodexScript(out var codexScript))
        {
            startInfo.FileName = "node";
            startInfo.ArgumentList.Add(codexScript);
        }

        startInfo.ArgumentList.Add("mcp-server");

        foreach (var variable in Options.BlockedEnvironmentVariables)
        {
            startInfo.Environment.Remove(variable);
        }

        return startInfo;
    }

    private async Task<Process> GetProcess(string workspaceRootPath, CancellationToken cancellationToken)
    {
        if (ProcessInstance is not null && !ProcessInstance.HasExited)
        {
            return ProcessInstance;
        }

        var startInfo = GetStartInfo(workspaceRootPath);
        ProcessInstance = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Codex MCP server.");
        ProcessDescription = startInfo.FileName + " " + string.Join(" ", startInfo.ArgumentList);
        StandardErrorTask = ProcessInstance.StandardError.ReadToEndAsync(cancellationToken);

        var requestId = GetNextRequestId();
        var initialize = await SendRpc(
            ProcessInstance,
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = requestId,
                ["method"] = "initialize",
                ["params"] = new JsonObject
                {
                    ["protocolVersion"] = "2024-11-05",
                    ["capabilities"] = new JsonObject(),
                    ["clientInfo"] = new JsonObject
                    {
                        ["name"] = "MainAgent",
                        ["version"] = "0.1.0"
                    }
                }
            },
            requestId,
            cancellationToken);

        if (initialize["error"] is not null)
        {
            var result = GetErrorResult("Codex MCP initialize failed.", initialize, null);
            StopProcess();
            throw new InvalidOperationException(result.Error);
        }

        await WriteRpc(
            ProcessInstance,
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "notifications/initialized"
            },
            cancellationToken);

        return ProcessInstance;
    }

    private int GetNextRequestId()
    {
        return NextRequestId++;
    }

    private void StopProcess()
    {
        if (ProcessInstance is not null && !ProcessInstance.HasExited)
        {
            ProcessInstance.Kill(entireProcessTree: true);
        }

        ProcessInstance?.Dispose();
        ProcessInstance = null;
        StandardErrorTask = null;
        ProcessDescription = null;
    }

    private string GetProcessDiagnostics()
    {
        List<string> details = [];

        if (!string.IsNullOrWhiteSpace(ProcessDescription))
        {
            details.Add($"process={ProcessDescription}");
        }

        if (ProcessInstance is not null)
        {
            details.Add($"pid={ProcessInstance.Id}");
            details.Add($"exited={ProcessInstance.HasExited}");
        }

        if (StandardErrorTask?.IsCompletedSuccessfully == true)
        {
            var stderr = StandardErrorTask.Result.Trim();

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                details.Add($"stderr={stderr}");
            }
        }

        return details.Count == 0
            ? string.Empty
            : " (" + string.Join("; ", details) + ")";
    }

    private static bool TryGetNpmCodexScript(out string codexScript)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        codexScript = Path.Combine(appData, "npm", "node_modules", "@openai", "codex", "bin", "codex.js");

        return File.Exists(codexScript);
    }

    private static string GetPrompt(AgentProviderRequest request)
    {
        List<string> sections = [];

        if (request.RouteKind == Workspaces.AgentRouteKind.Chat)
        {
            sections.Add("""
            Route: general chat. Do not mutate files or run commands in this chat turn.
            If the user's request requires changing files, running builds/tests, shell commands, app/program launching, or other coding work, delegate it to a background sub-agent instead of doing the work in the chat turn.
            To delegate, include exactly one machine-readable directive in your response:
            <delegate_to_sub_agent>{"task":"A self-contained task for the background agent.","capabilities":"ReadOnly,Code","requiresConfirmation":true}</delegate_to_sub_agent>
            Set capabilities to include ExternalActions when the task needs shell commands, app/program launching, or other local external actions.
            For explicit, non-destructive app launch requests, such as opening Notepad with Start-Process, set requiresConfirmation to false because the user's request is the authorization.
            You may include a brief user-facing sentence before the directive. Do not use the directive for ordinary questions or read-only explanations.
            """);
        }
        else if (!request.AllowsMutation)
        {
            sections.Add("Route: work request, but mutation/execution is not authorized for this channel/workspace. Refuse file changes or command execution and explain what authorization is required.");
        }
        else
        {
            sections.Add("Route: coding work. Complete the user's request directly using the configured workspace access. Do not delegate this task to another sub-agent.");
        }

        if (!string.IsNullOrWhiteSpace(request.ChannelNotes))
        {
            sections.Add(request.ChannelNotes);
        }

        if (!string.IsNullOrWhiteSpace(request.MemoryContext))
        {
            sections.Add("Relevant memory:" + Environment.NewLine + request.MemoryContext);
        }

        if (!string.IsNullOrWhiteSpace(request.RecentMirroredContext))
        {
            sections.Add("Recent mirrored transcript:" + Environment.NewLine + request.RecentMirroredContext);
        }

        sections.Add("User request:" + Environment.NewLine + request.UserMessage);

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private static AgentProviderResult GetProviderResult(JsonObject response, string? fallbackThreadId)
    {
        var result = response["result"]?.AsObject();
        var structuredContent = result?["structuredContent"]?.AsObject();
        var threadId = structuredContent?["threadId"]?.GetValue<string>() ?? fallbackThreadId;
        var content = RepairMojibake(structuredContent?["content"]?.GetValue<string>() ?? GetLegacyText(result) ?? string.Empty);

        return new AgentProviderResult(
            content,
            [],
            new Dictionary<string, string>
            {
                ["codexThreadId"] = threadId ?? string.Empty
            },
            null,
            threadId);
    }

    private static bool IsToolError(JsonObject response)
    {
        return response["result"]?["isError"]?.GetValue<bool>() == true;
    }

    private static string? GetLegacyText(JsonObject? result)
    {
        var content = result?["content"]?.AsArray();

        if (content is null)
        {
            return null;
        }

        return string.Join(
            Environment.NewLine,
            content
                .Select(x => x?.AsObject())
                .Where(x => x is not null)
                .Select(x => x?["text"]?.GetValue<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string RepairMojibake(string value)
    {
        return value
            .Replace("ÔÇÖ", "'", StringComparison.Ordinal)
            .Replace("ÔÇ£", "\"", StringComparison.Ordinal)
            .Replace("ÔÇØ", "\"", StringComparison.Ordinal)
            .Replace("ÔÇô", "-", StringComparison.Ordinal)
            .Replace("ÔÇö", "-", StringComparison.Ordinal)
            .Replace("ÔÇª", "...", StringComparison.Ordinal);
    }

    private static AgentProviderResult GetErrorResult(
        string prefix,
        JsonObject response,
        string? threadId)
    {
        var error = response["error"]?.ToJsonString(JsonOptions) ?? response.ToJsonString(JsonOptions);

        return new AgentProviderResult(
            string.Empty,
            [],
            new Dictionary<string, string>(),
            $"{prefix} {error}",
            threadId);
    }

    private static async Task<JsonObject> SendRpc(
        Process process,
        JsonObject request,
        int id,
        CancellationToken cancellationToken)
    {
        await WriteRpc(process, request, cancellationToken);
        var buffer = new StringBuilder();
        var chars = new char[4096];

        while (true)
        {
            var read = await process.StandardOutput.ReadAsync(chars.AsMemory(), cancellationToken);

            if (read == 0)
            {
                break;
            }

            buffer.Append(chars.AsSpan(0, read));

            foreach (var response in ReadBufferedResponses(buffer))
            {
                if (TryGetResponseId(response, out var responseId) && responseId == id)
                {
                    return response;
                }
            }
        }

        throw new InvalidOperationException("Codex MCP server closed stdout before returning a response.");
    }

    private static IReadOnlyList<JsonObject> ReadBufferedResponses(StringBuilder buffer)
    {
        List<JsonObject> responses = [];

        while (true)
        {
            var text = buffer.ToString().TrimStart();

            if (string.IsNullOrWhiteSpace(text))
            {
                buffer.Clear();
                return responses;
            }

            var newlineIndex = text.IndexOf('\n');

            if (newlineIndex >= 0)
            {
                var line = text[..newlineIndex].Trim();
                buffer.Clear();
                buffer.Append(text[(newlineIndex + 1)..]);

                if (!string.IsNullOrWhiteSpace(line))
                {
                    responses.Add(ParseResponse(line));
                }

                continue;
            }

            if (!LooksCompleteJsonObject(text))
            {
                buffer.Clear();
                buffer.Append(text);
                return responses;
            }

            responses.Add(ParseResponse(text));
            buffer.Clear();
            return responses;
        }
    }

    private static JsonObject ParseResponse(string text)
    {
        return JsonNode.Parse(text)?.AsObject()
            ?? throw new InvalidOperationException($"Codex MCP returned invalid JSON: {text}");
    }

    private static bool LooksCompleteJsonObject(string text)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        foreach (var character in text)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (character == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (character == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (character == '{')
            {
                depth++;
            }
            else if (character == '}')
            {
                depth--;
            }
        }

        return depth == 0 && !inString && text.EndsWith('}');
    }

    private static async Task WriteRpc(
        Process process,
        JsonObject request,
        CancellationToken cancellationToken)
    {
        await process.StandardInput.WriteLineAsync(request.ToJsonString(JsonOptions).AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    private static bool TryGetResponseId(JsonObject response, out int id)
    {
        id = 0;
        var node = response["id"];

        if (node is null)
        {
            return false;
        }

        try
        {
            if (node.GetValueKind() == JsonValueKind.Number)
            {
                id = node.GetValue<int>();
                return true;
            }

            return node.GetValueKind() == JsonValueKind.String
                && int.TryParse(node.GetValue<string>(), out id);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
