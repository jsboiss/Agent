using Microsoft.Extensions.Options;

namespace Agent.Context;

public sealed class ContextOrchestrator(
    IEnumerable<IContextProvider> providers,
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
        timeout.CancelAfter(Math.Max(250, Options.TimeoutMs));

        try
        {
            return await provider.Gather(
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
        }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or InvalidOperationException)
        {
            return new ContextProviderResult(plan.ProviderId, [], false, exception.Message);
        }
    }
}
