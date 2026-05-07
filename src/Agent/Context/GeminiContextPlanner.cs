using System.Text.Json;
using Agent.Providers;
using Agent.Resources;
using Microsoft.Extensions.Options;

namespace Agent.Context;

public sealed class GeminiContextPlanner(
    IAgentProviderSelector providerSelector,
    RuleBasedContextPlanner ruleBasedPlanner,
    IOptions<ContextPlannerOptions> options) : IContextPlanner
{
    private ContextPlannerOptions Options { get; } = options.Value;

    public async Task<ContextPlan> Plan(ContextPlanningRequest request, CancellationToken cancellationToken)
    {
        if (Options.Provider != AgentProviderType.Gemini)
        {
            return await ruleBasedPlanner.Plan(request, cancellationToken);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Max(250, Options.TimeoutMs));

        try
        {
            var provider = providerSelector.Get(AgentProviderType.Gemini);
            var providerRequest = new AgentProviderRequest(
                AgentProviderType.Gemini,
                request.ConversationId,
                request.UserMessage,
                GetPlannerResources(request),
                string.Empty,
                [],
                [],
                [],
                []);
            var result = await provider.Send(providerRequest, timeout.Token);

            if (IsRateLimitOrTransient(result.Error))
            {
                return await GetProviderFallback(request, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                return await ruleBasedPlanner.Plan(request, cancellationToken);
            }

            return Normalize(Parse(result.AssistantMessage), request);
        }
        catch (Exception exception) when (exception is JsonException or OperationCanceledException or HttpRequestException or InvalidOperationException)
        {
            return await ruleBasedPlanner.Plan(request, cancellationToken);
        }
    }

    private async Task<ContextPlan> GetProviderFallback(ContextPlanningRequest request, CancellationToken cancellationToken)
    {
        if (Options.FallbackProvider != AgentProviderType.Ollama
            || (!Options.FallbackOnRateLimit && !Options.FallbackOnTransientFailure))
        {
            return await ruleBasedPlanner.Plan(request, cancellationToken);
        }

        try
        {
            var provider = providerSelector.Get(AgentProviderType.Ollama);
            var providerRequest = new AgentProviderRequest(
                AgentProviderType.Ollama,
                request.ConversationId,
                request.UserMessage,
                GetPlannerResources(request),
                string.Empty,
                [],
                [],
                [],
                []);
            var result = await provider.Send(providerRequest, cancellationToken);

            return string.IsNullOrWhiteSpace(result.Error)
                ? Normalize(Parse(result.AssistantMessage), request)
                : await ruleBasedPlanner.Plan(request, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or HttpRequestException or InvalidOperationException)
        {
            return await ruleBasedPlanner.Plan(request, cancellationToken);
        }
    }

    private static AgentResourceContext GetPlannerResources(ContextPlanningRequest request)
    {
        var workspace = new WorkspaceContext(
            string.Empty,
            string.Empty,
            "planner",
            [],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["model"] = request.Settings.Get("contextPlanner.model") ?? "gemini-2.5-flash-lite"
            },
            []);

        return new AgentResourceContext(
            workspace,
            GetPlannerPrompt(),
            string.Empty,
            $"Channel: {request.Channel}.",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);
    }

    private static string GetPlannerPrompt()
    {
        return """
            You are a strict JSON context planner. Decide if the user message needs external context before the main assistant answers.
            Available provider ids: memory, calendar.
            Select memory for user preferences, durable history, personal facts, relationship/project context, or prior remembered information.
            Select calendar for schedules, events, availability, planning around time, or questions like fitting an activity into a day.
            Do not select providers for pure coding tasks, greetings, or date examples inside code snippets.
            Return only JSON with this schema:
            {"needsContext":true|false,"providers":[{"providerId":"memory|calendar","query":"string","start":"ISO timestamp or null","end":"ISO timestamp or null","dateWindowLabel":"string or null","required":true|false,"confidence":0.0}],"confidence":0.0,"missingContextUserVisible":true|false}
            """;
    }

    private static ContextPlan Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var providers = root.TryGetProperty("providers", out var providerElements) && providerElements.ValueKind == JsonValueKind.Array
            ? providerElements
                .EnumerateArray()
                .Select(x => new ContextProviderPlan(
                    GetString(x, "providerId") ?? string.Empty,
                    GetString(x, "query"),
                    GetDate(x, "start"),
                    GetDate(x, "end"),
                    GetString(x, "dateWindowLabel"),
                    GetBool(x, "required"),
                    GetDouble(x, "confidence")))
                .Where(x => !string.IsNullOrWhiteSpace(x.ProviderId))
                .ToArray()
            : [];

        return new ContextPlan(
            GetBool(root, "needsContext"),
            providers,
            GetDouble(root, "confidence"),
            GetBool(root, "missingContextUserVisible"),
            null);
    }

    private ContextPlan Normalize(ContextPlan plan, ContextPlanningRequest request)
    {
        var enabled = Options.EnabledProviders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var providers = plan.Providers
            .Where(x => enabled.Contains(x.ProviderId))
            .Select(x => x with
            {
                ProviderId = x.ProviderId.ToLowerInvariant(),
                Query = string.IsNullOrWhiteSpace(x.Query) ? request.UserMessage : x.Query
            })
            .ToArray();

        return providers.Length == 0
            ? ContextPlan.Empty
            : plan with { NeedsContext = true, Providers = providers };
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool GetBool(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();
    }

    private static double GetDouble(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.TryGetDouble(out var number)
            ? Math.Clamp(number, 0, 1)
            : 0;
    }

    private static DateTimeOffset? GetDate(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), out var date)
            ? date
            : null;
    }

    private static bool IsRateLimitOrTransient(string? error)
    {
        return !string.IsNullOrWhiteSpace(error)
            && (error.Contains("429", StringComparison.OrdinalIgnoreCase)
                || error.Contains("rate", StringComparison.OrdinalIgnoreCase)
                || error.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                || error.Contains("503", StringComparison.OrdinalIgnoreCase)
                || error.Contains("502", StringComparison.OrdinalIgnoreCase));
    }
}
