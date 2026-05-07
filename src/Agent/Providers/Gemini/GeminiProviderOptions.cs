namespace Agent.Providers.Gemini;

public sealed class GeminiProviderOptions
{
    public const string SectionName = "Providers:Gemini";

    public Uri BaseUri { get; init; } = new("https://generativelanguage.googleapis.com/v1beta/");

    public string Model { get; init; } = "gemini-2.5-flash-lite";

    public string? ApiKey { get; init; }
}
