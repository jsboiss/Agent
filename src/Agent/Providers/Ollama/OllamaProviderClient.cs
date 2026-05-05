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
            ],
            request.AvailableTools.Select(GetToolDefinition).ToArray());

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
        var assistantMessage = ollamaResponse?.Choices.FirstOrDefault()?.Message.Content ?? string.Empty;
        var toolCalls = GetToolCalls(ollamaResponse);

        if (string.IsNullOrWhiteSpace(assistantMessage) && toolCalls.Count == 0)
        {
            return new AgentProviderResult(
                string.Empty,
                [],
                GetUsageMetadata(Options.Model, ollamaResponse),
                "Ollama returned no assistant message.");
        }

        return new AgentProviderResult(
            assistantMessage,
            toolCalls,
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
        prompt.AppendLine(request.Resources.BuildSystemPrompt());

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

    private static OllamaToolDefinition GetToolDefinition(Tools.AgentToolDefinition tool)
    {
        using var schema = JsonDocument.Parse(tool.JsonParameterSchema);

        return new OllamaToolDefinition(
            "function",
            new OllamaFunctionDefinition(
                tool.Name,
                tool.Description,
                schema.RootElement.Clone()));
    }

    private static IReadOnlyList<AgentProviderToolCall> GetToolCalls(OllamaChatResponse? response)
    {
        var toolCalls = response?.Choices.FirstOrDefault()?.Message.ToolCalls;

        if (toolCalls is null || toolCalls.Count == 0)
        {
            return [];
        }

        return toolCalls
            .Select(x => new AgentProviderToolCall(
                string.IsNullOrWhiteSpace(x.Id) ? Guid.NewGuid().ToString("N") : x.Id,
                x.Function.Name,
                GetArguments(x.Function.Arguments)))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> GetArguments(JsonElement arguments)
    {
        if (arguments.ValueKind == JsonValueKind.String)
        {
            using var parsedArguments = JsonDocument.Parse(arguments.GetString() ?? "{}");
            return GetArguments(parsedArguments.RootElement);
        }

        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>();
        }

        return arguments
            .EnumerateObject()
            .ToDictionary(
                x => x.Name,
                x => x.Value.ValueKind == JsonValueKind.String
                    ? x.Value.GetString() ?? string.Empty
                    : x.Value.GetRawText(),
                StringComparer.OrdinalIgnoreCase);
    }

    private sealed record OllamaChatRequest(
        string Model,
        bool Stream,
        IReadOnlyList<OllamaChatMessage> Messages,
        IReadOnlyList<OllamaToolDefinition> Tools);

    private sealed record OllamaChatMessage(
        string Role,
        string? Content,
        [property: JsonPropertyName("tool_calls")] IReadOnlyList<OllamaToolCall>? ToolCalls = null);

    private sealed record OllamaToolDefinition(
        string Type,
        OllamaFunctionDefinition Function);

    private sealed record OllamaFunctionDefinition(
        string Name,
        string Description,
        JsonElement Parameters);

    private sealed record OllamaChatResponse(
        string? Model,
        IReadOnlyList<OllamaChatChoice> Choices,
        OllamaUsage? Usage);

    private sealed record OllamaChatChoice(
        OllamaChatMessage Message);

    private sealed record OllamaToolCall(
        string? Id,
        string Type,
        OllamaToolCallFunction Function);

    private sealed record OllamaToolCallFunction(
        string Name,
        JsonElement Arguments);

    private sealed record OllamaUsage(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens,
        [property: JsonPropertyName("total_tokens")] int TotalTokens);
}
