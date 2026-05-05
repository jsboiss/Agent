using Agent.Conversations;
using Agent.Events;
using Agent.Memory;
using Agent.Providers;
using Agent.Resources;
using Agent.Settings;

namespace Agent.Messages;

public sealed class AgentMessageProcessor(
    IAgentProviderSelector providerSelector,
    IConversationResolver conversationResolver,
    IConversationRepository conversationRepository,
    IAgentResourceLoader resourceLoader,
    IConversationPromptQueue promptQueue,
    IAgentSettingsResolver settingsResolver,
    IWebHostEnvironment environment) : IMessageProcessor
{
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
        var providerRequest = new AgentProviderRequest(
            providerType,
            conversation.Id,
            request.UserMessage,
            resources,
            string.Empty,
            Array.Empty<MemoryRecord>(),
            resources.Workspace.AvailableTools);

        var providerEvent = GetEvent(
            AgentEventKind.ProviderRequestStarted,
            conversation.Id,
            new Dictionary<string, string>
            {
                ["provider"] = providerType.ToString(),
                ["channel"] = request.Channel,
                ["ConversationEntryId"] = userEntry.Id
            });

        try
        {
            events.Add(providerEvent);
            var providerResult = await provider.Send(providerRequest, cancellationToken);

            if (!string.IsNullOrWhiteSpace(providerResult.AssistantMessage))
            {
                events.Add(GetEvent(
                    AgentEventKind.ProviderTextDelta,
                    conversation.Id,
                    new Dictionary<string, string>
                    {
                        ["provider"] = providerType.ToString(),
                        ["ConversationEntryId"] = userEntry.Id,
                        ["text"] = providerResult.AssistantMessage
                    }));
            }

            if (!string.IsNullOrWhiteSpace(providerResult.Error))
            {
                events.Add(GetEvent(
                    AgentEventKind.ProviderError,
                    conversation.Id,
                    new Dictionary<string, string>
                    {
                        ["provider"] = providerType.ToString(),
                        ["ConversationEntryId"] = userEntry.Id,
                        ["error"] = providerResult.Error
                    }));
            }

            events.Add(GetEvent(
                AgentEventKind.ProviderTurnCompleted,
                conversation.Id,
                new Dictionary<string, string>
                {
                    ["provider"] = providerType.ToString(),
                    ["ConversationEntryId"] = userEntry.Id,
                    ["toolCallCount"] = providerResult.ToolCalls.Count.ToString(),
                    ["hasError"] = (!string.IsNullOrWhiteSpace(providerResult.Error)).ToString()
                }));

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
