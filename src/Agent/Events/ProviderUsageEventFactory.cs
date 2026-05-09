using Agent.Providers;

namespace Agent.Events;

public static class ProviderUsageEventFactory
{
    public static AgentEvent Create(
        AgentEventKind kind,
        string conversationId,
        AgentProviderType provider,
        AgentProviderResult result,
        IReadOnlyDictionary<string, string>? extraData = null)
    {
        Dictionary<string, string> data = new(StringComparer.OrdinalIgnoreCase)
        {
            ["provider"] = provider.ToString(),
            ["model"] = result.UsageMetadata.GetValueOrDefault("model") ?? string.Empty,
            ["promptTokens"] = GetMetadataValue(result.UsageMetadata, "prompt_tokens").ToString(),
            ["completionTokens"] = GetMetadataValue(result.UsageMetadata, "completion_tokens").ToString(),
            ["totalTokens"] = GetMetadataValue(result.UsageMetadata, "total_tokens").ToString(),
            ["tokenUsageSource"] = GetMetadataValue(result.UsageMetadata, "total_tokens") > 0 ? "provider" : "estimate",
            ["toolCallCount"] = result.ToolCalls.Count.ToString(),
            ["hasError"] = (!string.IsNullOrWhiteSpace(result.Error)).ToString()
        };

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            data["error"] = result.Error;
        }

        if (extraData is not null)
        {
            foreach (var x in extraData)
            {
                data[x.Key] = x.Value;
            }
        }

        return new AgentEvent(
            Guid.NewGuid().ToString("N"),
            kind,
            conversationId,
            DateTimeOffset.UtcNow,
            data);
    }

    private static int GetMetadataValue(IReadOnlyDictionary<string, string> metadata, string key)
    {
        return metadata.TryGetValue(key, out var value) && int.TryParse(value, out var tokens)
            ? tokens
            : 0;
    }
}
