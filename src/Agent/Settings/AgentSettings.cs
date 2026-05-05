namespace Agent.Settings;

public sealed record AgentSettings(
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<string> AppliedLayers)
{
    public string? Get(string key)
    {
        return Values.GetValueOrDefault(key);
    }
}
