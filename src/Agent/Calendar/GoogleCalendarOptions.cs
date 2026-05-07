namespace Agent.Calendar;

public sealed class GoogleCalendarOptions
{
    public const string SectionName = "Integrations:GoogleCalendar";

    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    public string RedirectUri { get; init; } = "https://localhost:5001/api/dashboard/calendar/oauth-callback";
}
