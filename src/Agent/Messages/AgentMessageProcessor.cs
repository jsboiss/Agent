using Agent.Conversations;
using Agent.Events;
using Agent.Memory;
using Agent.Providers;
using Agent.Resources;
using Agent.Settings;
using Agent.Tools;

namespace Agent.Messages;

public sealed class AgentMessageProcessor(
    IAgentProviderSelector providerSelector,
    IConversationResolver conversationResolver,
    IConversationRepository conversationRepository,
    IAgentResourceLoader resourceLoader,
    IConversationPromptQueue promptQueue,
    IAgentSettingsResolver settingsResolver,
    IWebHostEnvironment environment,
    IAgentToolExecutor toolExecutor,
    IMemoryScout memoryScout) : IMessageProcessor
{
    private static int MaxToolIterations => 3;

    public async Task<MessageResult> Process(MessageRequest request, CancellationToken cancellationToken)
    {
        var resolution = await conversationResolver.Resolve(
            new ConversationResolveRequest(request.Channel, request.ConversationId),
            cancellationToken);
        var conversation = resolution.Conversation;
        var settings = await settingsResolver.Resolve(
            new AgentSettingsResolveRequest(
                conversation,
                request.Channel,
                GetWorkspaceRootPath(environment.ContentRootPath),
                request.Overrides ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            cancellationToken);
        var started = promptQueue.TryStart(conversation.Id);
        var queueKind = promptQueue.Classify(request.UserMessage, !started);
        List<AgentEvent> events =
        [
            GetEvent(
                AgentEventKind.MessageReceived,
                conversation.Id,
                new Dictionary<string, string>
                {
                    ["channel"] = request.Channel,
                    ["conversationCreated"] = resolution.Created.ToString(),
                    ["conversationKind"] = conversation.Kind.ToString(),
                    ["queueKind"] = queueKind.ToString(),
                    ["queueBehavior"] = settings.Get("queue.behavior") ?? string.Empty,
                    ["queued"] = (!started).ToString(),
                    ["receivedAt"] = request.ReceivedAt.ToString("O"),
                    ["message"] = request.UserMessage
                })
        ];

        var userEntry = await conversationRepository.AddEntry(
            conversation.Id,
            ConversationEntryRole.User,
            request.Channel,
            request.UserMessage,
            null,
            cancellationToken);

        events.Add(GetEvent(
            AgentEventKind.MessagePersisted,
            conversation.Id,
            new Dictionary<string, string>
            {
                ["role"] = ConversationEntryRole.User.ToString(),
                ["channel"] = request.Channel,
                ["ConversationEntryId"] = userEntry.Id,
                ["queueKind"] = queueKind.ToString(),
                ["message"] = request.UserMessage
            }));

        if (!started)
        {
            var queuedMessage = promptQueue.Enqueue(
                conversation.Id,
                userEntry.Id,
                queueKind,
                request.Channel,
                request.UserMessage);

            events.Add(GetEvent(
                AgentEventKind.MessagePersisted,
                conversation.Id,
                new Dictionary<string, string>
                {
                    ["queuedMessageId"] = queuedMessage.Id,
                    ["queueKind"] = queuedMessage.Kind.ToString(),
                    ["ConversationEntryId"] = userEntry.Id,
                    ["message"] = "Message queued while conversation is active."
                }));

            return new MessageResult(
                conversation.Id,
                string.Empty,
                events,
                queueKind,
                true);
        }

        var providerType = GetProviderType(settings);
        var provider = providerSelector.Get(providerType);
        var resources = await resourceLoader.Load(
            new AgentResourceLoadRequest(conversation, request.Channel, providerType, settings),
            cancellationToken);
        var memoryScoutResult = await PrefetchMemory(
            conversation.Id,
            request,
            userEntry.Id,
            settings,
            events,
            cancellationToken);

        var providerRequest = new AgentProviderRequest(
            providerType,
            conversation.Id,
            request.UserMessage,
            resources,
            memoryScoutResult.CompactContext,
            memoryScoutResult.Memories,
            resources.Workspace.AvailableTools,
            [],
            []);

        try
        {
            var providerResult = await RunProviderToolLoop(
                provider,
                providerRequest,
                request.Channel,
                userEntry.Id,
                events,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(providerResult.AssistantMessage))
            {
                var assistantEntry = await conversationRepository.AddEntry(
                    conversation.Id,
                    ConversationEntryRole.Assistant,
                    request.Channel,
                    providerResult.AssistantMessage,
                    userEntry.Id,
                    cancellationToken);

                events.Add(GetEvent(
                    AgentEventKind.MessagePersisted,
                    conversation.Id,
                    new Dictionary<string, string>
                    {
                        ["role"] = ConversationEntryRole.Assistant.ToString(),
                        ["channel"] = request.Channel,
                        ["ConversationEntryId"] = assistantEntry.Id,
                        ["ParentEntryId"] = userEntry.Id,
                        ["message"] = providerResult.AssistantMessage
                    }));
            }

            return new MessageResult(
                conversation.Id,
                providerResult.AssistantMessage,
                events,
                queueKind);
        }
        finally
        {
            promptQueue.Complete(conversation.Id);
        }
    }

    private async Task<AgentProviderResult> RunProviderToolLoop(
        IAgentProviderClient provider,
        AgentProviderRequest initialRequest,
        string channel,
        string userEntryId,
        List<AgentEvent> events,
        CancellationToken cancellationToken)
    {
        var providerRequest = initialRequest;
        AgentProviderResult? providerResult = null;
        List<AgentProviderToolCall> priorToolCalls = [];
        List<AgentProviderToolResult> providerToolResults = [];

        for (var iteration = 1; iteration <= MaxToolIterations; iteration++)
        {
            events.Add(GetProviderRequestStartedEvent(providerRequest, channel, userEntryId, iteration));
            providerResult = await provider.Send(providerRequest, cancellationToken);
            AddProviderEvents(providerResult, providerRequest, userEntryId, iteration, events);

            if (providerResult.ToolCalls.Count == 0)
            {
                return providerResult;
            }

            var toolResults = await ExecuteToolCalls(
                providerResult.ToolCalls,
                providerRequest.ConversationId,
                channel,
                userEntryId,
                events,
                cancellationToken);

            priorToolCalls.AddRange(providerResult.ToolCalls);
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
                    ? "Tool loop stopped after the maximum number of iterations."
                    : providerResult.AssistantMessage
            };
    }

    private async Task<MemoryScoutResult> PrefetchMemory(
        string conversationId,
        MessageRequest request,
        string userEntryId,
        AgentSettings settings,
        List<AgentEvent> events,
        CancellationToken cancellationToken)
    {
        if (string.Equals(settings.Get("memory.enabled"), "false", StringComparison.OrdinalIgnoreCase))
        {
            return new MemoryScoutResult(false, [], string.Empty);
        }

        events.Add(GetEvent(
            AgentEventKind.MemoryScoutStarted,
            conversationId,
            new Dictionary<string, string>
            {
                ["ConversationEntryId"] = userEntryId,
                ["channel"] = request.Channel
            }));
        var result = await memoryScout.Prefetch(
            new MemoryScoutRequest(
                conversationId,
                request.UserMessage,
                new Dictionary<string, string>
                {
                    ["channel"] = request.Channel,
                    ["ConversationEntryId"] = userEntryId,
                    ["limit"] = settings.Get("memory.scoutLimit") ?? "5"
                }),
            cancellationToken);
        events.Add(GetEvent(
            AgentEventKind.MemoryScoutCompleted,
            conversationId,
            new Dictionary<string, string>
            {
                ["ConversationEntryId"] = userEntryId,
                ["memoryCount"] = result.Memories.Count.ToString(),
                ["isMemoryRelevant"] = result.IsMemoryRelevant.ToString()
            }));

        if (result.IsMemoryRelevant)
        {
            events.Add(GetEvent(
                AgentEventKind.MemoryInjected,
                conversationId,
                new Dictionary<string, string>
                {
                    ["ConversationEntryId"] = userEntryId,
                    ["memoryIds"] = string.Join(",", result.Memories.Select(x => x.Id))
                }));
        }

        return result;
    }

    private async Task<IReadOnlyList<AgentProviderToolResult>> ExecuteToolCalls(
        IReadOnlyList<AgentProviderToolCall> toolCalls,
        string conversationId,
        string channel,
        string userEntryId,
        List<AgentEvent> events,
        CancellationToken cancellationToken)
    {
        List<AgentProviderToolResult> results = [];

        foreach (var toolCall in toolCalls)
        {
            events.Add(GetEvent(
                AgentEventKind.ToolCallStarted,
                conversationId,
                new Dictionary<string, string>
                {
                    ["toolCallId"] = toolCall.Id,
                    ["toolName"] = toolCall.Name,
                    ["ConversationEntryId"] = userEntryId
                }));

            var toolResult = await toolExecutor.Execute(
                new AgentToolRequest(
                    toolCall.Name,
                    toolCall.Arguments,
                    conversationId,
                    channel),
                cancellationToken);
            var toolEntry = await conversationRepository.AddEntry(
                conversationId,
                ConversationEntryRole.Tool,
                channel,
                toolResult.Content,
                userEntryId,
                cancellationToken);

            events.Add(GetEvent(
                AgentEventKind.ToolCallOutput,
                conversationId,
                new Dictionary<string, string>
                {
                    ["toolCallId"] = toolCall.Id,
                    ["toolName"] = toolCall.Name,
                    ["ConversationEntryId"] = toolEntry.Id,
                    ["ParentEntryId"] = userEntryId,
                    ["output"] = toolResult.Content
                }));
            events.Add(GetEvent(
                AgentEventKind.ToolCallCompleted,
                conversationId,
                new Dictionary<string, string>
                {
                    ["toolCallId"] = toolCall.Id,
                    ["toolName"] = toolCall.Name,
                    ["ConversationEntryId"] = toolEntry.Id,
                    ["succeeded"] = toolResult.Succeeded.ToString()
                }));

            results.Add(new AgentProviderToolResult(
                toolCall.Id,
                toolCall.Name,
                toolResult.Content));
        }

        return results;
    }

    private static AgentEvent GetProviderRequestStartedEvent(
        AgentProviderRequest request,
        string channel,
        string userEntryId,
        int iteration)
    {
        return GetEvent(
            AgentEventKind.ProviderRequestStarted,
            request.ConversationId,
            new Dictionary<string, string>
            {
                ["provider"] = request.Kind.ToString(),
                ["channel"] = channel,
                ["ConversationEntryId"] = userEntryId,
                ["iteration"] = iteration.ToString()
            });
    }

    private static void AddProviderEvents(
        AgentProviderResult providerResult,
        AgentProviderRequest request,
        string userEntryId,
        int iteration,
        List<AgentEvent> events)
    {
        if (!string.IsNullOrWhiteSpace(providerResult.AssistantMessage))
        {
            events.Add(GetEvent(
                AgentEventKind.ProviderTextDelta,
                request.ConversationId,
                new Dictionary<string, string>
                {
                    ["provider"] = request.Kind.ToString(),
                    ["ConversationEntryId"] = userEntryId,
                    ["iteration"] = iteration.ToString(),
                    ["text"] = providerResult.AssistantMessage
                }));
        }

        if (!string.IsNullOrWhiteSpace(providerResult.Error))
        {
            events.Add(GetEvent(
                AgentEventKind.ProviderError,
                request.ConversationId,
                new Dictionary<string, string>
                {
                    ["provider"] = request.Kind.ToString(),
                    ["ConversationEntryId"] = userEntryId,
                    ["iteration"] = iteration.ToString(),
                    ["error"] = providerResult.Error
                }));
        }

        events.Add(GetEvent(
            AgentEventKind.ProviderTurnCompleted,
            request.ConversationId,
            new Dictionary<string, string>
            {
                ["provider"] = request.Kind.ToString(),
                ["ConversationEntryId"] = userEntryId,
                ["iteration"] = iteration.ToString(),
                ["toolCallCount"] = providerResult.ToolCalls.Count.ToString(),
                ["hasError"] = (!string.IsNullOrWhiteSpace(providerResult.Error)).ToString()
            }));
    }

    private static AgentEvent GetEvent(
        AgentEventKind kind,
        string conversationId,
        IReadOnlyDictionary<string, string> data)
    {
        return new AgentEvent(
            Guid.NewGuid().ToString("N"),
            kind,
            conversationId,
            DateTimeOffset.UtcNow,
            data);
    }

    private static AgentProviderType GetProviderType(AgentSettings settings)
    {
        var provider = settings.Get("provider");

        if (Enum.TryParse<AgentProviderType>(provider, true, out var providerType))
        {
            return providerType;
        }

        return AgentProviderType.Ollama;
    }

    private static string GetWorkspaceRootPath(string contentRootPath)
    {
        var directory = new DirectoryInfo(contentRootPath);

        if (directory.Parent is not null && directory.Parent.Name.Equals("src", StringComparison.OrdinalIgnoreCase))
        {
            return directory.Parent.Parent?.FullName ?? directory.FullName;
        }

        return directory.FullName;
    }
}
