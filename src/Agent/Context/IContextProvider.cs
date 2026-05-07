namespace Agent.Context;

public interface IContextProvider
{
    string Id { get; }

    IReadOnlySet<string> Capabilities { get; }

    ContextProviderCost Cost { get; }

    ContextProviderLatency Latency { get; }

    ContextProviderSafety Safety { get; }

    Task<ContextProviderResult> Gather(
        ContextProviderRequest request,
        CancellationToken cancellationToken);
}

public enum ContextProviderCost
{
    Free,
    Low,
    Medium,
    High
}

public enum ContextProviderLatency
{
    Fast,
    Medium,
    Slow
}

public enum ContextProviderSafety
{
    ReadOnly,
    ReadWrite
}
