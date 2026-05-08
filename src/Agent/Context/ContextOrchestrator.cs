using System.Text.Json;
using Agent.Events;
using Microsoft.Extensions.Options;

namespace Agent.Context;

public sealed class ContextOrchestrator(
    IEnumerable<IContextProvider> providers,
    IAgentEventSink eventSink,
    IOptions<ContextPlannerOptions> options) : IContextOrchestrator
{
    private IReadOnlyDictionary<string, IContextProvider> Providers { get; } =
        providers.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

    private ContextPlannerOptions Options { get; } = options.Value;

    public async Task<EvidenceBundle> Gather(
        ContextPlan plan,
        ContextPlanningRequest request,
        CancellationToken cancellationToken)
    {
        if (!plan.NeedsContext || plan.Providers.Count == 0)
        {
            return EvidenceBundle.Empty;
        }

        var enabled = Options.EnabledProviders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tasks = plan.Providers
            .Where(x => enabled.Contains(x.ProviderId))
            .Where(x => Providers.ContainsKey(x.ProviderId))
            .Select(x => GatherProvider(x, request, cancellationToken))
            .ToArray();

        if (tasks.Length == 0)
        {
            return EvidenceBundle.Empty;
        }

        var results = await Task.WhenAll(tasks);
        var items = results.SelectMany(x => x.Items).ToArray();
        var metadata = results.ToDictionary(
            x => x.ProviderId,
            x => x.Succeeded ? "ok" : x.Error ?? "failed",
            StringComparer.OrdinalIgnoreCase);

        return new EvidenceBundle(items, metadata);
    }

    private async Task<ContextProviderResult> GatherProvider(
        ContextProviderPlan plan,
        ContextPlanningRequest request,
        CancellationToken cancellationToken)
    {
        if (!Providers.TryGetValue(plan.ProviderId, out var provider))
        {
            return new ContextProviderResult(plan.ProviderId, [], false, "Provider is not registered.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GetTimeout(provider));

        try
        {
            await Publish(
                AgentEventKind.ToolCallStarted,
                request.ConversationId,
                new Dictionary<string, string>
                {
                    ["toolName"] = $"context_{provider.Id}",
                    ["providerId"] = provider.Id,
                    ["query"] = plan.Query ?? request.UserMessage,
                    ["start"] = plan.Start?.ToString("O") ?? string.Empty,
                    ["end"] = plan.End?.ToString("O") ?? string.Empty,
                    ["dateWindowLabel"] = plan.DateWindowLabel ?? string.Empty,
                    ["arguments"] = JsonSerializer.Serialize(new
                    {
                        providerId = provider.Id,
                        query = plan.Query ?? request.UserMessage,
                        start = plan.Start?.ToString("O"),
                        end = plan.End?.ToString("O"),
                        dateWindowLabel = plan.DateWindowLabel
                    })
                },
                cancellationToken);
            var result = await provider.Gather(
                new ContextProviderRequest(
                    request.ConversationId,
                    request.Channel,
                    request.UserMessage,
                    plan,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["channel"] = request.Channel,
                        ["limit"] = request.Settings.Get("memory.scoutLimit") ?? "5"
                    }),
                timeout.Token);

            await Publish(
                AgentEventKind.ToolCallOutput,
                request.ConversationId,
                new Dictionary<string, string>
                {
                    ["toolName"] = $"context_{provider.Id}",
                    ["providerId"] = provider.Id,
                    ["itemCount"] = result.Items.Count.ToString(),
                    ["output"] = FormatEvidenceOutput(result)
                },
                cancellationToken);
            await Publish(
                result.Succeeded ? AgentEventKind.ToolCallCompleted : AgentEventKind.ProviderError,
                request.ConversationId,
                new Dictionary<string, string>
                {
                    ["toolName"] = $"context_{provider.Id}",
                    ["providerId"] = provider.Id,
                    ["succeeded"] = result.Succeeded.ToString(),
                    ["itemCount"] = result.Items.Count.ToString(),
                    ["error"] = result.Error ?? string.Empty
                },
                cancellationToken);

            return result;
        }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or InvalidOperationException)
        {
            await Publish(
                AgentEventKind.ProviderError,
                request.ConversationId,
                new Dictionary<string, string>
                {
                    ["toolName"] = $"context_{provider.Id}",
                    ["providerId"] = provider.Id,
                    ["succeeded"] = "False",
                    ["error"] = exception.Message
                },
                cancellationToken);

            return new ContextProviderResult(plan.ProviderId, [], false, exception.Message);
        }
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

    private static string FormatEvidenceOutput(ContextProviderResult result)
    {
        if (result.Items.Count == 0)
        {
            return result.Error ?? "No context items returned.";
        }

        return string.Join(
            Environment.NewLine,
            result.Items.Take(8).Select(x => $"- {x.Label}: {x.Text}"));
    }

    private TimeSpan GetTimeout(IContextProvider provider)
    {
        var configured = Math.Max(250, Options.TimeoutMs);
        var timeoutMs = provider.Latency switch
        {
            ContextProviderLatency.Fast => configured,
            ContextProviderLatency.Medium => Math.Max(configured, 10000),
            ContextProviderLatency.Slow => Math.Max(configured, 20000),
            _ => configured
        };

        return TimeSpan.FromMilliseconds(timeoutMs);
    }
}
