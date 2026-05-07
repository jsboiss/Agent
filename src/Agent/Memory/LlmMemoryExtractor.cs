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

        var model = result.UsageMetadata.GetValueOrDefault("model") ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            return new MemoryExtractionResult(
                [],
                result.Error,
                providerType.ToString(),
                model,
                result.AssistantMessage.Length,
                Shorten(result.AssistantMessage, 500),
                "provider-error");
        }

        if (string.IsNullOrWhiteSpace(result.AssistantMessage))
        {
            return new MemoryExtractionResult(
                [],
                "Memory extraction provider returned an empty response.",
                providerType.ToString(),
                model,
                0,
                string.Empty,
                "empty-response");
        }

        var parseResult = ParseMemories(result.AssistantMessage, request.UserEntry.Id);

        return new MemoryExtractionResult(
            parseResult.Memories,
            parseResult.Error,
            providerType.ToString(),
            model,
            result.AssistantMessage.Length,
            Shorten(result.AssistantMessage, 500),
            parseResult.ParseStatus);
    }

    private static LlmParseResult ParseMemories(string json, string sourceMessageId)
    {
        try
        {
            var cleanedJson = GetJsonPayload(json);
            var result = JsonSerializer.Deserialize<LlmExtractionResult>(cleanedJson, JsonOptions);

            if (result?.Memories is null)
            {
                return new LlmParseResult([], "Memory extraction JSON did not contain a memories array.", "missing-memories");
            }

            var memories = result.Memories
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .Select(x => new ExtractedMemory(
                    x.Text.Trim(),
                    x.Tier,
                    x.Segment,
                    Math.Clamp(x.Importance, 0, 1),
                    Math.Clamp(x.Confidence, 0, 1),
                    sourceMessageId))
                .ToArray();

            return new LlmParseResult(memories, null, "parsed");
        }
        catch (JsonException exception)
        {
            return new LlmParseResult([], $"Memory extraction JSON parse failed: {exception.Message}", "parse-error");
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
        Extract durable user-authored memories from every completed turn, even when the user did not ask you to remember anything.
        Do not infer facts from assistant text; use assistant text only as context for interpreting the user's message.
        Store stable personal facts, identity details, preferences, relationships, recurring workflow instructions, corrections, project facts, and useful long-lived context.
        Personal facts are allowed when they are stable and useful. Do not store secrets such as API keys, tokens, passwords, private credentials, or recovery codes.
        Do not store transient requests, one-off tasks, temporary debugging state, calendar lookups, generated content, greetings, tool output, provider errors, assistant failures, or vague emotional chatter without durable value.
        Treat phrases like "remember this", "save this", "don't forget it", and "keep this in mind" as an importance and confidence boost, not as the memory content.
        Let the memory itself be the underlying durable fact from natural language, even when it is phrased casually or without punctuation.
        Choose the tier, segment, importance, and confidence yourself. Use Short for near-term project context, Long for stable preferences or facts, and Permanent for durable identity, correction, or major relationship facts.
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
        Dictionary<string, string> settings = new(request.Settings, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(settings.GetValueOrDefault("memory.extraction.model")))
        {
            settings["model"] = settings["memory.extraction.model"];
        }

        var workspace = new WorkspaceContext(
            string.Empty,
            string.Empty,
            "memory-extraction",
            [],
            settings,
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

    private static string Shorten(string value, int length)
    {
        return value.Length <= length
            ? value
            : value[..length] + "...";
    }

    private sealed record LlmParseResult(
        IReadOnlyList<ExtractedMemory> Memories,
        string? Error,
        string ParseStatus);

    private sealed record LlmExtractionResult(IReadOnlyList<LlmExtractedMemory> Memories);

    private sealed record LlmExtractedMemory(
        string Text,
        MemoryTier Tier,
        MemorySegment Segment,
        double Importance,
        double Confidence);
}
