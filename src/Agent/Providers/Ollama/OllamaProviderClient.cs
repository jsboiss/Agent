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
            GetMessages(request),
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

    private static IReadOnlyList<OllamaChatRequestMessage> GetMessages(AgentProviderRequest request)
    {
        List<OllamaChatRequestMessage> messages =
        [
            new OllamaChatRequestMessage("system", GetSystemPrompt(request)),
            new OllamaChatRequestMessage("user", request.UserMessage)
        ];

        if (request.PriorToolCalls.Count == 0)
        {
            return messages;
        }

        messages.Add(new OllamaChatRequestMessage(
            "assistant",
            null,
            request.PriorToolCalls.Select(GetToolCallMessage).ToArray()));

        foreach (var toolResult in request.ToolResults)
        {
            messages.Add(new OllamaChatRequestMessage(
                "tool",
                toolResult.Content,
                null,
                toolResult.ToolCallId));
        }

        return messages;
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

    private static OllamaRequestToolCall GetToolCallMessage(AgentProviderToolCall toolCall)
    {
        return new OllamaRequestToolCall(
            toolCall.Id,
            "function",
            new OllamaRequestToolCallFunction(
                toolCall.Name,
                JsonSerializer.Serialize(toolCall.Arguments)));
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
        IReadOnlyList<OllamaChatRequestMessage> Messages,
        IReadOnlyList<OllamaToolDefinition> Tools);

    private sealed record OllamaChatRequestMessage(
        string Role,
        string? Content,
        [property: JsonPropertyName("tool_calls")] IReadOnlyList<OllamaRequestToolCall>? ToolCalls = null,
        [property: JsonPropertyName("tool_call_id")] string? ToolCallId = null);

    private sealed record OllamaRequestToolCall(
        string? Id,
        string Type,
        OllamaRequestToolCallFunction Function);

    private sealed record OllamaRequestToolCallFunction(
        string Name,
        string Arguments);

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
        OllamaChatResponseMessage Message);

    private sealed record OllamaChatResponseMessage(
        string? Content,
        [property: JsonPropertyName("tool_calls")] IReadOnlyList<OllamaToolCall>? ToolCalls = null);

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
