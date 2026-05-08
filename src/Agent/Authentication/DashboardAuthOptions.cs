namespace Agent.Authentication;

public sealed class DashboardAuthOptions
{
    public const string SectionName = "Dashboard:Auth";

    public string Password { get; set; } = string.Empty;
}
