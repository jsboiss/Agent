using Agent.Conversations;
using Agent.Events;
using Agent.Memory;
using Agent.Providers;

namespace Agent.Messages;

public sealed class AgentMessageProcessor(
    IAgentProviderSelector providerSelector,
    IConversationResolver conversationResolver,
    IConversationRepository conversationRepository) : IMessageProcessor
{
    public async Task<MessageResult> Process(MessageRequest request, CancellationToken cancellationToken)
    {
        var resolution = await conversationResolver.Resolve(
            new ConversationResolveRequest(request.Channel, request.ConversationId),
            cancellationToken);
        var conversation = resolution.Conversation;
        var userEntry = await conversationRepository.AddEntry(
            conversation.Id,
            ConversationEntryRole.User,
            request.Channel,
            request.UserMessage,
            null,
            cancellationToken);

        var provider = providerSelector.Get(AgentProviderType.Ollama);
        var providerRequest = new AgentProviderRequest(
            AgentProviderType.Ollama,
            conversation.Id,
            request.UserMessage,
            string.Empty,
            Array.Empty<MemoryRecord>(),
            ["search_memory", "write_memory", "spawn_agent"]);

        List<AgentEvent> events =
        [
            GetEvent(
                AgentEventKind.ChatMessage,
                conversation.Id,
                new Dictionary<string, string>
                {
                    ["role"] = "user",
                    ["channel"] = request.Channel,
                    ["conversationEntryId"] = userEntry.Id,
                    ["message"] = request.UserMessage
                })
        ];

        if (resolution.Created)
        {
            events.Add(GetEvent(
                AgentEventKind.ChatMessage,
                conversation.Id,
                new Dictionary<string, string>
                {
                    ["conversationKind"] = conversation.Kind.ToString(),
                    ["message"] = "Conversation created."
                }));
        }

        var providerEvent = GetEvent(
            AgentEventKind.ProviderRequest,
            conversation.Id,
            new Dictionary<string, string>
            {
                ["provider"] = AgentProviderType.Ollama.ToString(),
                ["channel"] = request.Channel,
                ["conversationEntryId"] = userEntry.Id
            });

        events.Add(providerEvent);
        var providerResult = await provider.Send(providerRequest, cancellationToken);

        if (!string.IsNullOrWhiteSpace(providerResult.Error))
        {
            events.Add(GetEvent(
                AgentEventKind.ProviderError,
                conversation.Id,
                new Dictionary<string, string>
                {
                    ["provider"] = AgentProviderType.Ollama.ToString(),
                    ["conversationEntryId"] = userEntry.Id,
                    ["error"] = providerResult.Error
                }));
        }

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
                AgentEventKind.ChatMessage,
                conversation.Id,
                new Dictionary<string, string>
                {
                    ["role"] = "assistant",
                    ["channel"] = request.Channel,
                    ["conversationEntryId"] = assistantEntry.Id,
                    ["parentEntryId"] = userEntry.Id,
                    ["message"] = providerResult.AssistantMessage
                }));
        }

        return new MessageResult(
            conversation.Id,
            providerResult.AssistantMessage,
            events);
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
}
