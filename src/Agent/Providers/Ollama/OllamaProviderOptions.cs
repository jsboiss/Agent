namespace Agent.Providers.Ollama;

public sealed record OllamaProviderOptions : AgentProviderOptions
{
    public const string SectionName = "Providers:Ollama";

    public OllamaProviderOptions()
    {
        Kind = AgentProviderType.Ollama;
        Command = "ollama";
    }

    public Uri BaseUri { get; init; } = new("http://localhost:11434/v1/");

    public string Model { get; init; } = "qwen3.5:latest";
}
