namespace Agent.Context;

public sealed record ContextPlan(
    bool NeedsContext,
    IReadOnlyList<ContextProviderPlan> Providers,
    double Confidence,
    bool MissingContextUserVisible,
    string? FailureReason = null)
{
    public static ContextPlan Empty { get; } = new(false, [], 1, false, null);
}

public sealed record ContextProviderPlan(
    string ProviderId,
    string? Query,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    string? DateWindowLabel,
    bool Required,
    double Confidence);
