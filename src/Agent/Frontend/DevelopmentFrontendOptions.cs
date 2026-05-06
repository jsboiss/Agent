namespace Agent.Frontend;

public sealed class DevelopmentFrontendOptions
{
    public const string SectionName = "DevelopmentFrontend";

    public bool Enabled { get; set; } = true;

    public string ClientAppPath { get; set; } = "ClientApp";

    public string Url { get; set; } = "http://127.0.0.1:5173";

    public string ApiTarget { get; set; } = "http://localhost:5213";
}
