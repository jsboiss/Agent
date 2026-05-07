using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Agent.Providers.Gemini;

public sealed class GeminiProviderClient(HttpClient httpClient, IOptions<GeminiProviderOptions> options) : IAgentProviderClient
{
    public AgentProviderType Type => AgentProviderType.Gemini;

    private GeminiProviderOptions Options { get; } = options.Value;

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<AgentProviderResult> Send(AgentProviderRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Options.ApiKey))
        {
            return new AgentProviderResult(string.Empty, [], GetUsageMetadata(Options.Model, null), "Gemini API key is not configured.");
        }

        var model = GetModel(request);
        var geminiRequest = new GeminiGenerateContentRequest(
            [
                new GeminiContent(
                    "user",
                    [new GeminiPart(GetPrompt(request))])
            ],
            new GeminiGenerationConfig(0.1, 1024, "application/json"));
        var path = $"models/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(Options.ApiKey)}";
        using var response = await httpClient.PostAsJsonAsync(path, geminiRequest, JsonOptions, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new AgentProviderResult(
                string.Empty,
                [],
                GetUsageMetadata(model, null),
                $"Gemini returned {(int)response.StatusCode} {response.ReasonPhrase}: {responseText}");
        }

        var geminiResponse = JsonSerializer.Deserialize<GeminiGenerateContentResponse>(responseText, JsonOptions);
        var assistantMessage = geminiResponse?.Candidates.FirstOrDefault()?.Content.Parts.FirstOrDefault()?.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(assistantMessage))
        {
            return new AgentProviderResult(string.Empty, [], GetUsageMetadata(model, geminiResponse), "Gemini returned no text.");
        }

        return new AgentProviderResult(assistantMessage, [], GetUsageMetadata(model, geminiResponse), null);
    }

    public static void ConfigureHttpClient(HttpClient httpClient, GeminiProviderOptions options)
    {
        httpClient.BaseAddress = options.BaseUri;
    }

    private static string GetPrompt(AgentProviderRequest request)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine(request.Resources.BuildSystemPrompt());
        prompt.AppendLine();
        prompt.AppendLine("User message:");
        prompt.AppendLine(request.UserMessage);

        return prompt.ToString();
    }

    private string GetModel(AgentProviderRequest request)
    {
        var settings = request.Resources.Workspace.ApplicableSettings;
        var model = settings.GetValueOrDefault("memory.extraction.model")
            ?? settings.GetValueOrDefault("memory.compactionExtraction.model")
            ?? settings.GetValueOrDefault("contextPlanner.model")
            ?? settings.GetValueOrDefault("model");

        return string.IsNullOrWhiteSpace(model) || model.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase)
            ? Options.Model
            : model;
    }

    private static IReadOnlyDictionary<string, string> GetUsageMetadata(
        string model,
        GeminiGenerateContentResponse? response)
    {
        Dictionary<string, string> usageMetadata = new(StringComparer.OrdinalIgnoreCase)
        {
            ["provider"] = AgentProviderType.Gemini.ToString(),
            ["model"] = model
        };

        if (response?.UsageMetadata is not null)
        {
            usageMetadata["prompt_tokens"] = response.UsageMetadata.PromptTokenCount.ToString();
            usageMetadata["completion_tokens"] = response.UsageMetadata.CandidatesTokenCount.ToString();
            usageMetadata["total_tokens"] = response.UsageMetadata.TotalTokenCount.ToString();
        }

        return usageMetadata;
    }

    private sealed record GeminiGenerateContentRequest(
        IReadOnlyList<GeminiContent> Contents,
        [property: JsonPropertyName("generationConfig")] GeminiGenerationConfig GenerationConfig);

    private sealed record GeminiContent(
        string Role,
        IReadOnlyList<GeminiPart> Parts);

    private sealed record GeminiPart(string Text);

    private sealed record GeminiGenerationConfig(
        double Temperature,
        [property: JsonPropertyName("maxOutputTokens")] int MaxOutputTokens,
        [property: JsonPropertyName("responseMimeType")] string ResponseMimeType);

    private sealed record GeminiGenerateContentResponse(
        IReadOnlyList<GeminiCandidate> Candidates,
        GeminiUsageMetadata? UsageMetadata);

    private sealed record GeminiCandidate(GeminiContent Content);

    private sealed record GeminiUsageMetadata(
        int PromptTokenCount,
        int CandidatesTokenCount,
        int TotalTokenCount);
}
