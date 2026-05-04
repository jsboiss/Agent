using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Agent.Providers.Ollama;

public sealed class OllamaProviderClient(HttpClient httpClient, IOptions<OllamaProviderOptions> options) : IAgentProviderClient
{
    public AgentProviderType Type => AgentProviderType.Ollama;

    private OllamaProviderOptions Options { get; } = options.Value;

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<AgentProviderResult> Send(AgentProviderRequest request, CancellationToken cancellationToken)
    {
        var ollamaRequest = new OllamaChatRequest(
            Options.Model,
            false,
            [
                new OllamaChatMessage("system", GetSystemPrompt(request)),
                new OllamaChatMessage("user", request.UserMessage)
            ]);

        using var response = await httpClient.PostAsJsonAsync("chat/completions", ollamaRequest, JsonOptions, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new AgentProviderResult(
                string.Empty,
                [],
                GetUsageMetadata(Options.Model, null),
                $"Ollama returned {(int)response.StatusCode} {response.ReasonPhrase}: {responseText}");
        }

        var ollamaResponse = JsonSerializer.Deserialize<OllamaChatResponse>(responseText, JsonOptions);
        var assistantMessage = ollamaResponse?.Choices.FirstOrDefault()?.Message.Content;

        if (string.IsNullOrWhiteSpace(assistantMessage))
        {
            return new AgentProviderResult(
                string.Empty,
                [],
                GetUsageMetadata(Options.Model, ollamaResponse),
                "Ollama returned no assistant message.");
        }

        return new AgentProviderResult(
            assistantMessage,
            [],
            GetUsageMetadata(Options.Model, ollamaResponse),
            null);
    }

    public static void ConfigureHttpClient(HttpClient httpClient, OllamaProviderOptions options)
    {
        httpClient.BaseAddress = options.BaseUri;
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "ollama");
    }

    private static string GetSystemPrompt(AgentProviderRequest request)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("You are the local development model for the MainAgent harness.");
        prompt.AppendLine("Respond directly and keep outputs concise unless more detail is requested.");

        if (!string.IsNullOrWhiteSpace(request.MemoryContext))
        {
            prompt.AppendLine();
            prompt.AppendLine("Memory context:");
            prompt.AppendLine(request.MemoryContext);
        }

        if (request.InjectedMemories.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("Injected memory ids:");

            foreach (var memory in request.InjectedMemories)
            {
                prompt.AppendLine($"- {memory.Id}");
            }
        }

        if (request.AvailableTools.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("Available tools:");

            foreach (var tool in request.AvailableTools)
            {
                prompt.AppendLine($"- {tool}");
            }
        }

        return prompt.ToString();
    }

    private static IReadOnlyDictionary<string, string> GetUsageMetadata(
        string model,
        OllamaChatResponse? response)
    {
        Dictionary<string, string> usageMetadata = new()
        {
            ["provider"] = AgentProviderType.Ollama.ToString(),
            ["model"] = response?.Model ?? model
        };

        if (response?.Usage is not null)
        {
            usageMetadata["prompt_tokens"] = response.Usage.PromptTokens.ToString();
            usageMetadata["completion_tokens"] = response.Usage.CompletionTokens.ToString();
            usageMetadata["total_tokens"] = response.Usage.TotalTokens.ToString();
        }

        return usageMetadata;
    }

    private sealed record OllamaChatRequest(
        string Model,
        bool Stream,
        IReadOnlyList<OllamaChatMessage> Messages);

    private sealed record OllamaChatMessage(
        string Role,
        string Content);

    private sealed record OllamaChatResponse(
        string? Model,
        IReadOnlyList<OllamaChatChoice> Choices,
        OllamaUsage? Usage);

    private sealed record OllamaChatChoice(
        OllamaChatMessage Message);

    private sealed record OllamaUsage(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens,
        [property: JsonPropertyName("total_tokens")] int TotalTokens);
}
