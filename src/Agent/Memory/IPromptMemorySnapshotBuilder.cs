namespace Agent.Memory;

public interface IPromptMemorySnapshotBuilder
{
    Task<PromptMemorySnapshot> Build(
        IReadOnlyDictionary<string, string> settings,
        CancellationToken cancellationToken);
}
