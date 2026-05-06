using Agent.Conversations;
using Agent.Providers;
using Agent.Settings;

namespace Agent.Tokens;

public sealed class AgentTokenTracker : IAgentTokenTracker
{
    private static double CharsPerToken => 4.0;

    public AgentTokenUsage Measure(
        AgentProviderRequest request,
        AgentProviderResult? result,
        AgentSettings settings,
        IReadOnlyList<ConversationEntry> mainContext)
    {
        var contextWindowTokens = GetSetting(settings, "tokens.contextWindow", 200000);
        var compactionThresholdTokens = GetSetting(settings, "compaction.threshold", 8000);
        var mainContextTokens = Estimate(mainContext.Select(x => $"{x.Role}: {x.Content}"));
        var promptTokens = GetMetadataValue(result, "prompt_tokens")
            ?? GetMetadataValue(result, "promptTokens")
            ?? GetMetadataValue(result, "input_tokens")
            ?? GetMetadataValue(result, "inputTokens")
            ?? EstimatePrompt(request);
        var completionTokens = GetMetadataValue(result, "completion_tokens")
            ?? GetMetadataValue(result, "completionTokens")
            ?? GetMetadataValue(result, "output_tokens")
            ?? GetMetadataValue(result, "outputTokens")
            ?? Estimate(result?.AssistantMessage ?? string.Empty);
        var totalTokens = GetMetadataValue(result, "total_tokens")
            ?? GetMetadataValue(result, "totalTokens")
            ?? promptTokens + completionTokens;
        var remainingTokens = Math.Max(0, contextWindowTokens - mainContextTokens);
        var compactableContextTokens = Math.Max(0, mainContextTokens - EstimateRecentContext(request));
        var remainingUntilCompactionTokens = Math.Max(0, compactionThresholdTokens - compactableContextTokens);
        var source = HasExactUsage(result)
            ? "provider"
            : "estimate";

        return new AgentTokenUsage(
            promptTokens,
            completionTokens,
            totalTokens,
            mainContextTokens,
            contextWindowTokens,
            remainingTokens,
            compactionThresholdTokens,
            remainingUntilCompactionTokens,
            source);
    }

    public IReadOnlyDictionary<string, string> ToMetadata(AgentTokenUsage usage)
    {
        return new Dictionary<string, string>
        {
            ["promptTokens"] = usage.PromptTokens.ToString(),
            ["completionTokens"] = usage.CompletionTokens.ToString(),
            ["totalTokens"] = usage.TotalTokens.ToString(),
            ["mainContextTokens"] = usage.MainContextTokens.ToString(),
            ["contextWindowTokens"] = usage.ContextWindowTokens.ToString(),
            ["remainingContextTokens"] = usage.RemainingTokens.ToString(),
            ["compactionThresholdTokens"] = usage.CompactionThresholdTokens.ToString(),
            ["remainingUntilCompactionTokens"] = usage.RemainingUntilCompactionTokens.ToString(),
            ["tokenUsageSource"] = usage.Source
        };
    }

    public AgentTokenUsage Aggregate(IReadOnlyList<IReadOnlyDictionary<string, string>> metadata)
    {
        var promptTokens = metadata.Sum(x => GetMetadataValue(x, "promptTokens") ?? 0);
        var completionTokens = metadata.Sum(x => GetMetadataValue(x, "completionTokens") ?? 0);
        var totalTokens = metadata.Sum(x => GetMetadataValue(x, "totalTokens") ?? 0);
        var latest = metadata.LastOrDefault(x => x.ContainsKey("mainContextTokens"));
        var mainContextTokens = GetMetadataValue(latest, "mainContextTokens") ?? 0;
        var contextWindowTokens = GetMetadataValue(latest, "contextWindowTokens") ?? 0;
        var remainingTokens = GetMetadataValue(latest, "remainingContextTokens") ?? 0;
        var compactionThresholdTokens = GetMetadataValue(latest, "compactionThresholdTokens") ?? 0;
        var remainingUntilCompactionTokens = GetMetadataValue(latest, "remainingUntilCompactionTokens") ?? 0;
        var source = metadata.Any(x => string.Equals(x.GetValueOrDefault("tokenUsageSource"), "provider", StringComparison.OrdinalIgnoreCase))
            ? "provider"
            : "estimate";

        return new AgentTokenUsage(
            promptTokens,
            completionTokens,
            totalTokens,
            mainContextTokens,
            contextWindowTokens,
            remainingTokens,
            compactionThresholdTokens,
            remainingUntilCompactionTokens,
            source);
    }

    private static int EstimatePrompt(AgentProviderRequest request)
    {
        List<string> parts =
        [
            request.Resources.BuildSystemPrompt(),
            request.MemoryContext,
            request.RecentMirroredContext,
            request.ChannelNotes,
            request.UserMessage,
            .. request.PriorToolCalls.SelectMany(x => x.Arguments.Select(y => $"{x.Name}:{y.Key}={y.Value}")),
            .. request.ToolResults.Select(x => $"{x.Name}:{x.Content}")
        ];

        return Estimate(parts);
    }

    private static int EstimateRecentContext(AgentProviderRequest request)
    {
        return Estimate(request.Resources.ConversationSummary
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.StartsWith("- ", StringComparison.Ordinal)));
    }

    private static int Estimate(IEnumerable<string> values)
    {
        return values.Sum(Estimate);
    }

    private static int Estimate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Ceiling(value.Length / CharsPerToken));
    }

    private static int GetSetting(AgentSettings settings, string key, int fallback)
    {
        return int.TryParse(settings.Get(key), out var value) && value > 0
            ? value
            : fallback;
    }

    private static int? GetMetadataValue(AgentProviderResult? result, string key)
    {
        return result is null
            ? null
            : GetMetadataValue(result.UsageMetadata, key);
    }

    private static int? GetMetadataValue(IReadOnlyDictionary<string, string>? metadata, string key)
    {
        return metadata?.TryGetValue(key, out var value) == true && int.TryParse(value, out var tokens)
            ? tokens
            : null;
    }

    private static bool HasExactUsage(AgentProviderResult? result)
    {
        return GetMetadataValue(result, "total_tokens") is not null
            || GetMetadataValue(result, "totalTokens") is not null
            || GetMetadataValue(result, "input_tokens") is not null
            || GetMetadataValue(result, "output_tokens") is not null;
    }
}
