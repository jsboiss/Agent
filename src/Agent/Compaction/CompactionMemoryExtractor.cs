using System.Text.Json;
using System.Text.Json.Serialization;
using Agent.Conversations;
using Agent.Events;
using Agent.Memory;
using Agent.Providers;
using Agent.Resources;

namespace Agent.Compaction;

public sealed class CompactionMemoryExtractor(
    IAgentProviderSelector providerSelector,
    IMemoryCandidateReviewer memoryCandidateReviewer,
    IMemoryStore memoryStore,
    IAgentEventSink eventSink) : ICompactionMemoryExtractor
{
    private static int DefaultMaxEntries => 24;

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<CompactionMemoryExtractionResult> Extract(
        Conversation conversation,
        IReadOnlyList<ConversationEntry> entries,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return CompactionMemoryExtractionResult.Empty;
        }

        if (string.Equals(settings.GetValueOrDefault("memory.enabled"), "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(settings.GetValueOrDefault("memory.compactionExtraction.enabled"), "false", StringComparison.OrdinalIgnoreCase))
        {
            return CompactionMemoryExtractionResult.Empty;
        }

        var mode = settings.GetValueOrDefault("memory.compactionExtraction.mode") ?? "llm";

        if (!mode.Equals("llm", StringComparison.OrdinalIgnoreCase))
        {
            return CompactionMemoryExtractionResult.Empty;
        }

        var providerType = GetProviderType(settings);
        var selectedEntries = entries
            .TakeLast(GetMaxEntries(settings))
            .ToArray();
        var firstEntryId = selectedEntries.First().Id;
        var lastEntryId = selectedEntries.Last().Id;

        await Publish(
            AgentEventKind.MemoryExtractionStarted,
            conversation.Id,
            new Dictionary<string, string>
            {
                ["source"] = "compaction",
                ["firstEntryId"] = firstEntryId,
                ["lastEntryId"] = lastEntryId,
                ["entryCount"] = selectedEntries.Length.ToString(),
                ["provider"] = providerType.ToString(),
                ["model"] = settings.GetValueOrDefault("memory.compactionExtraction.model") ?? settings.GetValueOrDefault("model") ?? string.Empty
            },
            cancellationToken);

        var extraction = await GetCandidates(conversation, selectedEntries, providerType, settings, cancellationToken);
        extraction = extraction with
        {
            Provider = string.IsNullOrWhiteSpace(extraction.Provider) ? providerType.ToString() : extraction.Provider,
            Model = string.IsNullOrWhiteSpace(extraction.Model)
                ? settings.GetValueOrDefault("memory.compactionExtraction.model") ?? settings.GetValueOrDefault("model") ?? string.Empty
                : extraction.Model
        };

        if (!string.IsNullOrWhiteSpace(extraction.Error))
        {
            await Publish(
                AgentEventKind.MemoryExtraction,
                conversation.Id,
                new Dictionary<string, string>
                {
                    ["source"] = "compaction",
                    ["status"] = "failed",
                    ["provider"] = extraction.Provider,
                    ["model"] = extraction.Model,
                    ["parseStatus"] = extraction.ParseStatus,
                    ["rawResponseLength"] = extraction.RawResponseLength.ToString(),
                    ["rawResponsePreview"] = extraction.RawResponsePreview,
                    ["error"] = extraction.Error
                },
                cancellationToken);
        }

        var reviewResult = await memoryCandidateReviewer.Review(
            new MemoryCandidateReviewRequest(conversation.Id, extraction.Memories),
            cancellationToken);
        var written = 0;
        var skipped = 0;
        var superseded = 0;

        foreach (var review in reviewResult.Reviews)
        {
            if (!review.Accepted)
            {
                skipped++;
                continue;
            }

            var candidate = review.Candidate;
            var memory = await memoryStore.Write(
                new MemoryWriteRequest(
                    candidate.Text,
                    candidate.Tier,
                    candidate.Segment,
                    candidate.Importance,
                    candidate.Confidence,
                    candidate.SourceMessageId,
                    GetSupersedesValue(review)),
                cancellationToken);
            written++;

            foreach (var memoryId in review.SupersededMemoryIds)
            {
                await memoryStore.UpdateLifecycle(memoryId, MemoryLifecycle.Archived, cancellationToken);
                superseded++;
            }

            await Publish(
                AgentEventKind.MemoryWrite,
                conversation.Id,
                new Dictionary<string, string>
                {
                    ["source"] = "compaction",
                    ["sourceEntryId"] = candidate.SourceMessageId,
                    ["memoryId"] = memory.Id,
                    ["tier"] = memory.Tier.ToString(),
                    ["segment"] = memory.Segment.ToString(),
                    ["reviewScore"] = review.Score.ToString("0.###"),
                    ["supersededCount"] = review.SupersededMemoryIds.Count.ToString(),
                    ["supersedes"] = memory.Supersedes ?? string.Empty,
                    ["text"] = memory.Text
                },
                cancellationToken);
        }

        await Publish(
            AgentEventKind.MemoryExtractionCompleted,
            conversation.Id,
            new Dictionary<string, string>
            {
                ["source"] = "compaction",
                ["firstEntryId"] = firstEntryId,
                ["lastEntryId"] = lastEntryId,
                ["proposedCount"] = extraction.Memories.Count.ToString(),
                ["writtenCount"] = written.ToString(),
                ["skippedCount"] = skipped.ToString(),
                ["supersededCount"] = superseded.ToString(),
                ["reviewedCount"] = reviewResult.Reviews.Count.ToString(),
                ["provider"] = extraction.Provider,
                ["model"] = extraction.Model,
                ["parseStatus"] = extraction.ParseStatus,
                ["rawResponseLength"] = extraction.RawResponseLength.ToString(),
                ["rawResponsePreview"] = extraction.RawResponsePreview,
                ["error"] = extraction.Error ?? string.Empty,
                ["reviewSummary"] = string.Join("; ", reviewResult.Reviews.Select(x => $"{x.Score:0.##}:{x.Reason}"))
            },
            cancellationToken);

        return new CompactionMemoryExtractionResult(extraction.Memories.Count, written, skipped);
    }

    private async Task<MemoryExtractionResult> GetCandidates(
        Conversation conversation,
        IReadOnlyList<ConversationEntry> entries,
        AgentProviderType providerType,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var provider = providerSelector.Get(providerType);
            var result = await provider.Send(
                new AgentProviderRequest(
                    providerType,
                    conversation.Id,
                    GetExtractionPrompt(conversation, entries),
                    GetResourceContext(settings),
                    string.Empty,
                    [],
                    [],
                    [],
                    []),
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(result.Error) || string.IsNullOrWhiteSpace(result.AssistantMessage))
            {
                var error = !string.IsNullOrWhiteSpace(result.Error)
                    ? result.Error
                    : "Compaction memory extraction provider returned an empty response.";

                return new MemoryExtractionResult(
                    [],
                    error,
                    providerType.ToString(),
                    result.UsageMetadata.GetValueOrDefault("model") ?? string.Empty,
                    result.AssistantMessage.Length,
                    Shorten(result.AssistantMessage, 500),
                    string.IsNullOrWhiteSpace(result.Error) ? "empty-response" : "provider-error");
            }

            return ParseMemories(result.AssistantMessage, entries.Last().Id);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new MemoryExtractionResult(
                [],
                $"Compaction memory extraction failed: {exception.Message}",
                providerType.ToString(),
                settings.GetValueOrDefault("memory.compactionExtraction.model") ?? string.Empty,
                0,
                string.Empty,
                "exception");
        }
    }

    private static MemoryExtractionResult ParseMemories(string json, string fallbackSourceEntryId)
    {
        try
        {
            var cleanedJson = GetJsonPayload(json);
            var result = JsonSerializer.Deserialize<CompactionExtractionResult>(cleanedJson, JsonOptions);

            if (result?.Memories is null)
            {
                return new MemoryExtractionResult(
                    [],
                    "Compaction memory extraction JSON did not contain a memories array.",
                    RawResponseLength: json.Length,
                    RawResponsePreview: Shorten(json, 500),
                    ParseStatus: "missing-memories");
            }

            var memories = result.Memories
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .Select(x => new ExtractedMemory(
                    x.Text.Trim(),
                    x.Tier,
                    x.Segment,
                    Math.Clamp(x.Importance, 0, 1),
                    Math.Clamp(x.Confidence, 0, 1),
                    string.IsNullOrWhiteSpace(x.SourceEntryId) ? fallbackSourceEntryId : x.SourceEntryId.Trim()))
                .ToArray();

            return new MemoryExtractionResult(
                memories,
                RawResponseLength: json.Length,
                RawResponsePreview: Shorten(json, 500),
                ParseStatus: "parsed");
        }
        catch (JsonException exception)
        {
            return new MemoryExtractionResult(
                [],
                $"Compaction memory extraction JSON parse failed: {exception.Message}",
                RawResponseLength: json.Length,
                RawResponsePreview: Shorten(json, 500),
                ParseStatus: "parse-error");
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

    private static string GetExtractionPrompt(
        Conversation conversation,
        IReadOnlyList<ConversationEntry> entries)
    {
        var transcript = string.Join(
            Environment.NewLine,
            entries.Select(x => $"[{x.Id}] {x.Role}: {Shorten(x.Content, 1200)}"));

        return $$"""
        Extract sparse durable memory candidates from the conversation span that is about to be compacted.
        Extract stable user-authored facts even when no memory keyword appears.
        Prefer personal facts, preferences, relationships, corrections, recurring workflow instructions, and clear project decisions.
        Personal facts are allowed when stable and useful. Do not store secrets such as API keys, tokens, passwords, private credentials, or recovery codes.
        Do not store transient tasks, one-off lookups, temporary debugging state, greetings, assistant speculation, generated content, raw tool output, or vague inferred preferences.
        Treat explicit memory phrasing as an importance and confidence boost, not as a requirement.
        Let the model decide tier, segment, importance, and confidence.
        Do not duplicate facts that are merely repeated in this span.
        Return JSON only with this schema:
        {
          "memories": [
            {
              "text": "durable memory text",
              "tier": "Short|Long|Permanent",
              "segment": "Identity|Preference|Correction|Relationship|Project|Knowledge|Context",
              "importance": 0.0,
              "confidence": 0.0,
              "sourceEntryId": "entry id most responsible for this memory, or the last entry id if it spans multiple entries"
            }
          ]
        }

        Conversation: {{conversation.Id}} ({{conversation.Kind}})
        Span:
        {{transcript}}
        """;
    }

    private static AgentResourceContext GetResourceContext(IReadOnlyDictionary<string, string> sourceSettings)
    {
        Dictionary<string, string> settings = new(sourceSettings, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(settings.GetValueOrDefault("memory.compactionExtraction.model")))
        {
            settings["model"] = settings["memory.compactionExtraction.model"];
        }

        var workspace = new WorkspaceContext(
            string.Empty,
            string.Empty,
            "compaction-memory-extraction",
            [],
            settings,
            []);

        return new AgentResourceContext(
            workspace,
            "You extract durable memory candidates from compacted conversation spans. Return JSON only.",
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
        var provider = settings.GetValueOrDefault("memory.compactionExtraction.provider");

        return Enum.TryParse<AgentProviderType>(provider, true, out var providerType)
            ? providerType
            : AgentProviderType.Ollama;
    }

    private static int GetMaxEntries(IReadOnlyDictionary<string, string> settings)
    {
        return int.TryParse(settings.GetValueOrDefault("memory.compactionExtraction.maxEntries"), out var maxEntries)
            ? Math.Clamp(maxEntries, 1, 100)
            : DefaultMaxEntries;
    }

    private static string Shorten(string value, int length)
    {
        return value.Length <= length
            ? value
            : value[..length] + "...";
    }

    private static string? GetSupersedesValue(MemoryCandidateReview review)
    {
        return review.SupersededMemoryIds.Count == 0
            ? null
            : string.Join(",", review.SupersededMemoryIds);
    }

    private async Task Publish(
        AgentEventKind kind,
        string conversationId,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken)
    {
        await eventSink.Publish(
            new AgentEvent(
                Guid.NewGuid().ToString("N"),
                kind,
                conversationId,
                DateTimeOffset.UtcNow,
                data),
            cancellationToken);
    }

    private sealed record CompactionExtractionResult(IReadOnlyList<CompactionExtractedMemory> Memories);

    private sealed record CompactionExtractedMemory(
        string Text,
        MemoryTier Tier,
        MemorySegment Segment,
        double Importance,
        double Confidence,
        string? SourceEntryId);
}
