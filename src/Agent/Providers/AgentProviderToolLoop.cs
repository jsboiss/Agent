using Agent.Conversations;
using Agent.Events;
using Agent.Notifications;
using Agent.Settings;
using Agent.Tokens;
using Agent.Tools;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Agent.Providers;

public sealed class AgentProviderToolLoop(
    IConversationRepository conversationRepository,
    IAgentToolExecutor toolExecutor,
    IAgentEventSink eventSink,
    IAgentTokenTracker tokenTracker,
    IAgentNotifier notifier) : IAgentProviderToolLoop
{
    private static int MaxToolIterations => 3;

    public async Task<AgentProviderResult> Run(
        IAgentProviderClient provider,
        AgentProviderRequest initialRequest,
        string channel,
        string parentEntryId,
        AgentSettings settings,
        string? notificationTarget,
        CancellationToken cancellationToken)
    {
        var providerRequest = initialRequest;
        AgentProviderResult? providerResult = null;
        List<AgentProviderToolCall> priorToolCalls = [];
        List<AgentProviderToolResult> providerToolResults = [];
        HashSet<string> executedToolKeys = new(StringComparer.OrdinalIgnoreCase);

        for (var iteration = 1; iteration <= MaxToolIterations; iteration++)
        {
            await Publish(GetProviderRequestStartedEvent(providerRequest, channel, parentEntryId, iteration), cancellationToken);
            providerResult = await provider.Send(providerRequest, cancellationToken);
            var context = await conversationRepository.ListEntries(providerRequest.ConversationId, cancellationToken);
            var tokenUsage = tokenTracker.Measure(providerRequest, providerResult, settings, context);
            await PublishProviderEvents(providerResult, providerRequest, parentEntryId, iteration, tokenUsage, cancellationToken);

            var textToolCalls = GetTextToolCalls(providerResult.AssistantMessage);
            var effectiveToolCalls = providerResult.ToolCalls.Count == 0
                ? textToolCalls
                : providerResult.ToolCalls;
            var delegationToolCall = GetDelegationToolCall(providerResult.AssistantMessage, parentEntryId);

            if (effectiveToolCalls.Count == 0 && delegationToolCall is not null)
            {
                await SendDelegationAck(channel, notificationTarget, cancellationToken);
                await ExecuteToolCalls(
                    [delegationToolCall],
                    providerRequest.ConversationId,
                    channel,
                    parentEntryId,
                    notificationTarget,
                    executedToolKeys,
                    cancellationToken);
                var assistantMessage = StripDelegationDirective(providerResult.AssistantMessage);

                return providerResult with
                {
                    AssistantMessage = string.IsNullOrWhiteSpace(assistantMessage)
                        ? "Background sub-agent queued."
                        : assistantMessage
                };
            }

            if (effectiveToolCalls.Count == 0)
            {
                return string.IsNullOrWhiteSpace(providerResult.AssistantMessage)
                    ? providerResult with { AssistantMessage = GetToolLoopFallback(providerResult, providerToolResults) }
                    : providerResult;
            }

            var toolResults = await ExecuteToolCalls(
                effectiveToolCalls,
                providerRequest.ConversationId,
                channel,
                parentEntryId,
                notificationTarget,
                executedToolKeys,
                cancellationToken);

            priorToolCalls.AddRange(effectiveToolCalls);
            providerToolResults.AddRange(toolResults);

            providerRequest = providerRequest with
            {
                PriorToolCalls = priorToolCalls.ToArray(),
                ToolResults = providerToolResults.ToArray()
            };
        }

        return providerResult is null
            ? new AgentProviderResult(string.Empty, [], new Dictionary<string, string>(), "Provider loop did not run.")
            : providerResult with
            {
                AssistantMessage = string.IsNullOrWhiteSpace(providerResult.AssistantMessage)
                    ? GetToolLoopFallback(providerResult, providerToolResults)
                    : providerResult.AssistantMessage
            };
    }

    private async Task<IReadOnlyList<AgentProviderToolResult>> ExecuteToolCalls(
        IReadOnlyList<AgentProviderToolCall> toolCalls,
        string conversationId,
        string channel,
        string parentEntryId,
        string? notificationTarget,
        ISet<string> executedToolKeys,
        CancellationToken cancellationToken)
    {
        List<AgentProviderToolResult> results = [];

        foreach (var toolCall in toolCalls)
        {
            var toolKey = GetToolKey(toolCall);
            await Publish(
                GetEvent(
                    AgentEventKind.ToolCallStarted,
                    conversationId,
                    new Dictionary<string, string>
                    {
                        ["toolCallId"] = toolCall.Id,
                        ["toolName"] = toolCall.Name,
                        ["ConversationEntryId"] = parentEntryId
                    }),
                cancellationToken);

            AgentToolResult toolResult;

            if (!executedToolKeys.Add(toolKey))
            {
                toolResult = new AgentToolResult(
                    toolCall.Name,
                    false,
                    $"Skipped duplicate tool call '{toolCall.Name}' in the same turn.",
                    new Dictionary<string, string> { ["duplicate"] = "true" });
            }
            else
            {
                toolResult = await toolExecutor.Execute(
                    new AgentToolRequest(
                        toolCall.Name,
                        AddNotificationTarget(toolCall.Arguments, notificationTarget),
                        conversationId,
                        channel,
                        parentEntryId),
                    cancellationToken);
            }

            var toolEntry = await conversationRepository.AddEntry(
                conversationId,
                ConversationEntryRole.Tool,
                channel,
                toolResult.Content,
                parentEntryId,
                cancellationToken);
            await Publish(
                GetEvent(
                    AgentEventKind.ToolCallOutput,
                    conversationId,
                    new Dictionary<string, string>
                    {
                        ["toolCallId"] = toolCall.Id,
                        ["toolName"] = toolCall.Name,
                        ["ConversationEntryId"] = toolEntry.Id,
                        ["ParentEntryId"] = parentEntryId,
                        ["output"] = toolResult.Content
                    }),
                cancellationToken);
            await Publish(
                GetEvent(
                    AgentEventKind.ToolCallCompleted,
                    conversationId,
                    new Dictionary<string, string>
                    {
                        ["toolCallId"] = toolCall.Id,
                        ["toolName"] = toolCall.Name,
                        ["ConversationEntryId"] = toolEntry.Id,
                        ["succeeded"] = toolResult.Succeeded.ToString()
                    }),
                cancellationToken);

            results.Add(new AgentProviderToolResult(toolCall.Id, toolCall.Name, toolResult.Content));
        }

        return results;
    }

    private async Task PublishProviderEvents(
        AgentProviderResult providerResult,
        AgentProviderRequest request,
        string parentEntryId,
        int iteration,
        AgentTokenUsage tokenUsage,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(providerResult.AssistantMessage))
        {
            await Publish(
                GetEvent(
                    AgentEventKind.ProviderTextDelta,
                    request.ConversationId,
                    new Dictionary<string, string>
                    {
                        ["provider"] = request.Kind.ToString(),
                        ["ConversationEntryId"] = parentEntryId,
                        ["iteration"] = iteration.ToString(),
                        ["text"] = providerResult.AssistantMessage
                    }),
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(providerResult.Error))
        {
            await Publish(
                GetEvent(
                    AgentEventKind.ProviderError,
                    request.ConversationId,
                    new Dictionary<string, string>
                    {
                        ["provider"] = request.Kind.ToString(),
                        ["ConversationEntryId"] = parentEntryId,
                        ["iteration"] = iteration.ToString(),
                        ["error"] = providerResult.Error
                    }),
                cancellationToken);
        }

        var data = new Dictionary<string, string>
        {
            ["provider"] = request.Kind.ToString(),
            ["ConversationEntryId"] = parentEntryId,
            ["iteration"] = iteration.ToString(),
            ["toolCallCount"] = providerResult.ToolCalls.Count.ToString(),
            ["hasError"] = (!string.IsNullOrWhiteSpace(providerResult.Error)).ToString()
        };

        foreach (var x in tokenTracker.ToMetadata(tokenUsage))
        {
            data[x.Key] = x.Value;
        }

        await Publish(GetEvent(AgentEventKind.ProviderTurnCompleted, request.ConversationId, data), cancellationToken);
    }

    private async Task Publish(AgentEvent agentEvent, CancellationToken cancellationToken)
    {
        await eventSink.Publish(agentEvent, cancellationToken);
    }

    private async Task SendDelegationAck(
        string channel,
        string? notificationTarget,
        CancellationToken cancellationToken)
    {
        if (!IsMobileChannel(channel))
        {
            return;
        }

        await notifier.Send(channel, notificationTarget, "Queued that for a background Codex run.", cancellationToken);
    }

    private static AgentProviderToolCall? GetDelegationToolCall(string assistantMessage, string parentEntryId)
    {
        var json = GetDelegationJson(assistantMessage);

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var node = JsonNode.Parse(json)?.AsObject();
            var task = node?["task"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(task))
            {
                return null;
            }

            var capabilities = node?["capabilities"]?.GetValue<string>();
            var requiresConfirmation = node?["requiresConfirmation"]?.GetValue<bool?>();

            return new AgentProviderToolCall(
                $"delegate-{Guid.NewGuid():N}",
                "spawn_agent",
                new Dictionary<string, string>
                {
                    ["task"] = task,
                    ["parentEntryId"] = parentEntryId,
                    ["capabilities"] = string.IsNullOrWhiteSpace(capabilities) ? "ReadOnly,Code" : capabilities,
                    ["requiresConfirmation"] = (requiresConfirmation ?? true).ToString()
                });
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static IReadOnlyList<AgentProviderToolCall> GetTextToolCalls(string assistantMessage)
    {
        var bulletToolCalls = GetBulletTextToolCalls(assistantMessage);

        if (bulletToolCalls.Count > 0)
        {
            return bulletToolCalls;
        }

        foreach (var json in GetJsonCandidates(assistantMessage))
        {
            var toolCalls = TryGetTextToolCalls(json);

            if (toolCalls.Count > 0)
            {
                return toolCalls;
            }
        }

        return [];
    }

    private static IReadOnlyList<AgentProviderToolCall> GetBulletTextToolCalls(string assistantMessage)
    {
        List<AgentProviderToolCall> result = [];
        var lines = assistantMessage.Split(Environment.NewLine);

        for (var x = 0; x < lines.Length; x++)
        {
            var line = lines[x].Trim();

            if (!line.StartsWith("- calendar_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separatorIndex = line.IndexOf(':');

            if (separatorIndex < 0)
            {
                continue;
            }

            var name = line[2..separatorIndex].Trim();
            var jsonStart = line[(separatorIndex + 1)..].Trim();
            var builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(jsonStart))
            {
                builder.AppendLine(jsonStart);
            }

            for (var y = x + 1; y < lines.Length; y++)
            {
                builder.AppendLine(lines[y]);

                if (lines[y].Trim() == "}")
                {
                    x = y;
                    break;
                }
            }

            var arguments = TryParseArguments(builder.ToString());

            if (arguments is not null)
            {
                result.Add(new AgentProviderToolCall(
                    $"text-tool-{Guid.NewGuid():N}",
                    name,
                    arguments));
            }
        }

        return result;
    }

    private static IReadOnlyList<string> GetJsonCandidates(string assistantMessage)
    {
        List<string> candidates = [];
        var index = 0;

        while (index < assistantMessage.Length)
        {
            var fenceStart = assistantMessage.IndexOf("```", index, StringComparison.Ordinal);

            if (fenceStart < 0)
            {
                break;
            }

            var contentStart = assistantMessage.IndexOf('\n', fenceStart);

            if (contentStart < 0)
            {
                break;
            }

            var fenceEnd = assistantMessage.IndexOf("```", contentStart + 1, StringComparison.Ordinal);

            if (fenceEnd < 0)
            {
                break;
            }

            candidates.Add(assistantMessage[(contentStart + 1)..fenceEnd].Trim());
            index = fenceEnd + 3;
        }

        var firstBrace = assistantMessage.IndexOf('{');
        var lastBrace = assistantMessage.LastIndexOf('}');

        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            candidates.Add(assistantMessage[firstBrace..(lastBrace + 1)].Trim());
        }

        return candidates;
    }

    private static IReadOnlyList<AgentProviderToolCall> TryGetTextToolCalls(string json)
    {
        try
        {
            var node = JsonNode.Parse(json)?.AsObject();
            var toolCalls = node?["toolCalls"]?.AsArray();

            if (toolCalls is null)
            {
                return [];
            }

            List<AgentProviderToolCall> result = [];

            foreach (var toolCall in toolCalls)
            {
                var toolCallObject = toolCall?.AsObject();
                var name = toolCallObject?["name"]?.GetValue<string>();
                var arguments = toolCallObject?["arguments"]?.AsObject();

                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                result.Add(new AgentProviderToolCall(
                    $"text-tool-{Guid.NewGuid():N}",
                    name,
                    GetArguments(arguments)));
            }

            return result;
        }
        catch (JsonException)
        {
            return [];
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }

    private static IReadOnlyDictionary<string, string> GetArguments(JsonObject? arguments)
    {
        if (arguments is null)
        {
            return new Dictionary<string, string>();
        }

        return arguments.ToDictionary(
            x => x.Key,
            x => x.Value?.GetValueKind() == JsonValueKind.String
                ? x.Value.GetValue<string>()
                : x.Value?.ToJsonString() ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string>? TryParseArguments(string json)
    {
        try
        {
            return GetArguments(JsonNode.Parse(json)?.AsObject());
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? GetDelegationJson(string assistantMessage)
    {
        var startTag = "<delegate_to_sub_agent>";
        var endTag = "</delegate_to_sub_agent>";
        var start = assistantMessage.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);

        if (start < 0)
        {
            return null;
        }

        var contentStart = start + startTag.Length;
        var end = assistantMessage.IndexOf(endTag, contentStart, StringComparison.OrdinalIgnoreCase);

        return end < 0 ? null : assistantMessage[contentStart..end].Trim();
    }

    private static string StripDelegationDirective(string assistantMessage)
    {
        var start = assistantMessage.IndexOf("<delegate_to_sub_agent>", StringComparison.OrdinalIgnoreCase);

        if (start < 0)
        {
            return assistantMessage;
        }

        var end = assistantMessage.IndexOf("</delegate_to_sub_agent>", start, StringComparison.OrdinalIgnoreCase);

        if (end < 0)
        {
            return assistantMessage[..start].Trim();
        }

        var afterEnd = end + "</delegate_to_sub_agent>".Length;
        return (assistantMessage[..start] + assistantMessage[afterEnd..]).Trim();
    }

    private static IReadOnlyDictionary<string, string> AddNotificationTarget(
        IReadOnlyDictionary<string, string> arguments,
        string? notificationTarget)
    {
        if (string.IsNullOrWhiteSpace(notificationTarget)
            || arguments.ContainsKey("notificationTarget")
            || arguments.ContainsKey("target"))
        {
            return arguments;
        }

        var values = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase)
        {
            ["notificationTarget"] = notificationTarget,
            ["target"] = notificationTarget
        };

        return values;
    }

    private static string GetToolLoopFallback(
        AgentProviderResult providerResult,
        IReadOnlyList<AgentProviderToolResult> toolResults)
    {
        if (!string.IsNullOrWhiteSpace(providerResult.Error))
        {
            return providerResult.Error;
        }

        if (toolResults.Count == 0)
        {
            return "The provider returned no assistant message.";
        }

        if (toolResults.All(x => string.Equals(x.Name, "write_memory", StringComparison.OrdinalIgnoreCase)))
        {
            return toolResults.Any(x => x.Content.StartsWith("Memory already exists", StringComparison.OrdinalIgnoreCase))
                ? "That memory is already saved."
                : "I've saved that to memory.";
        }

        return string.Join(Environment.NewLine, toolResults.Select(x => x.Content));
    }

    private static string GetToolKey(AgentProviderToolCall toolCall)
    {
        return toolCall.Name + ":"
            + string.Join(
                "|",
                toolCall.Arguments
                    .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(x => $"{x.Key}={x.Value}"));
    }

    private static bool IsMobileChannel(string channel)
    {
        return string.Equals(channel, "telegram", StringComparison.OrdinalIgnoreCase)
            || string.Equals(channel, "imessage", StringComparison.OrdinalIgnoreCase);
    }

    private static AgentEvent GetProviderRequestStartedEvent(
        AgentProviderRequest request,
        string channel,
        string parentEntryId,
        int iteration)
    {
        return GetEvent(
            AgentEventKind.ProviderRequestStarted,
            request.ConversationId,
            new Dictionary<string, string>
            {
                ["provider"] = request.Kind.ToString(),
                ["channel"] = channel,
                ["ConversationEntryId"] = parentEntryId,
                ["iteration"] = iteration.ToString()
            });
    }

    private static AgentEvent GetEvent(
        AgentEventKind kind,
        string conversationId,
        IReadOnlyDictionary<string, string> data)
    {
        return new AgentEvent(Guid.NewGuid().ToString("N"), kind, conversationId, DateTimeOffset.UtcNow, data);
    }
}
