namespace Agent.Providers.Gemini;

public sealed class GeminiProviderOptions
{
    public const string SectionName = "Providers:Gemini";

    public Uri BaseUri { get; init; } = new("https://generativelanguage.googleapis.com/v1beta/");

    public string Model { get; init; } = "gemini-3.1-flash-lite";

    public string? ApiKey { get; init; }
}
