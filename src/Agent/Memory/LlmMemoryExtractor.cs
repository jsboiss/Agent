using System.Text.Json;
using System.Text.Json.Serialization;
using Agent.Providers;
using Agent.Resources;
using Agent.Tools;

namespace Agent.Memory;

public sealed class LlmMemoryExtractor(IAgentProviderSelector providerSelector) : IMemoryExtractor
{
    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<MemoryExtractionResult> Extract(
        MemoryExtractionRequest request,
        CancellationToken cancellationToken)
    {
        var providerType = GetProviderType(request.Settings);
        var provider = providerSelector.Get(providerType);
        var providerRequest = new AgentProviderRequest(
            providerType,
            request.ConversationId,
            GetExtractionPrompt(request),
            GetResourceContext(request),
            string.Empty,
            [],
            [],
            [],
            []);
        var result = await provider.Send(providerRequest, cancellationToken);

        if (!string.IsNullOrWhiteSpace(result.Error) || string.IsNullOrWhiteSpace(result.AssistantMessage))
        {
            return new MemoryExtractionResult([]);
        }

        return new MemoryExtractionResult(ParseMemories(result.AssistantMessage, request.UserEntry.Id));
    }

    private static IReadOnlyList<ExtractedMemory> ParseMemories(string json, string sourceMessageId)
    {
        try
        {
            var cleanedJson = GetJsonPayload(json);
            var result = JsonSerializer.Deserialize<LlmExtractionResult>(cleanedJson, JsonOptions);

            if (result?.Memories is null)
            {
                return [];
            }

            return result.Memories
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .Select(x => new ExtractedMemory(
                    x.Text.Trim(),
                    x.Tier,
                    x.Segment,
                    Math.Clamp(x.Importance, 0, 1),
                    Math.Clamp(x.Confidence, 0, 1),
                    sourceMessageId))
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string GetJsonPayload(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        return start >= 0 && end > start
            ? text[start..(end + 1)]
            : text;
    }

    private static string GetExtractionPrompt(MemoryExtractionRequest request)
    {
        return $$"""
        Extract only durable user-authored memories from this turn.
        Do not infer facts from assistant text.
        Do not store transient requests, one-off tasks, greetings, tool output, provider errors, or assistant failures.
        Treat phrases like "remember this", "don't forget it", and "keep this in mind" as emphasis, not as the memory content.
        Extract the underlying durable fact from natural language, even when it is phrased casually or without punctuation.
        Rewrite memories as clear third-person facts about the user or the user's world.
        Return JSON only with this schema:
        {
          "memories": [
            {
              "text": "durable memory text",
              "tier": "Short|Long|Permanent",
              "segment": "Identity|Preference|Correction|Relationship|Project|Knowledge|Context",
              "importance": 0.0,
              "confidence": 0.0
            }
          ]
        }

        User message:
        {{request.UserEntry.Content}}

        Assistant response:
        {{request.AssistantEntry?.Content ?? string.Empty}}
        """;
    }

    private static AgentResourceContext GetResourceContext(MemoryExtractionRequest request)
    {
        var workspace = new WorkspaceContext(
            string.Empty,
            string.Empty,
            "memory-extraction",
            [],
            request.Settings,
            []);

        return new AgentResourceContext(
            workspace,
            "You extract durable memory candidates. Return JSON only.",
            string.Empty,
            string.Empty,
            "Provider constraints: structured JSON extraction only.",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);
    }

    private static AgentProviderType GetProviderType(IReadOnlyDictionary<string, string> settings)
    {
        var provider = settings.GetValueOrDefault("memory.extraction.provider")
            ?? settings.GetValueOrDefault("provider");

        return Enum.TryParse<AgentProviderType>(provider, true, out var providerType)
            ? providerType
            : AgentProviderType.Ollama;
    }

    private sealed record LlmExtractionResult(IReadOnlyList<LlmExtractedMemory> Memories);

    private sealed record LlmExtractedMemory(
        string Text,
        MemoryTier Tier,
        MemorySegment Segment,
        double Importance,
        double Confidence);
}
