using Agent.Events;
using Agent.Memory;
using Agent.Providers;

namespace Agent.Messages;

public sealed class AgentMessageProcessor(IAgentProviderSelector providerSelector) : IMessageProcessor
{
    public async Task<MessageResult> Process(MessageRequest request, CancellationToken cancellationToken)
    {
        var provider = providerSelector.Get(AgentProviderType.Ollama);
        var providerRequest = new AgentProviderRequest(
            AgentProviderType.Ollama,
            request.ConversationId,
            request.UserMessage,
            string.Empty,
            Array.Empty<MemoryRecord>(),
            ["search_memory", "write_memory", "spawn_agent"]);

        var providerEvent = GetEvent(
            AgentEventKind.ProviderRequest,
            request.ConversationId,
            new Dictionary<string, string>
            {
                ["provider"] = AgentProviderType.Ollama.ToString(),
                ["channel"] = request.Channel
            });

        var providerResult = await provider.Send(providerRequest, cancellationToken);
        List<AgentEvent> events = [providerEvent];

        if (!string.IsNullOrWhiteSpace(providerResult.Error))
        {
            events.Add(GetEvent(
                AgentEventKind.ProviderError,
                request.ConversationId,
                new Dictionary<string, string>
                {
                    ["provider"] = AgentProviderType.Ollama.ToString(),
                    ["error"] = providerResult.Error
                }));
        }

        events.Add(GetEvent(
            AgentEventKind.ChatMessage,
            request.ConversationId,
            new Dictionary<string, string>
            {
                ["role"] = "assistant",
                ["message"] = providerResult.AssistantMessage
            }));

        return new MessageResult(
            request.ConversationId,
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
