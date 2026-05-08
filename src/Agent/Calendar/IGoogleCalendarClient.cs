namespace Agent.Calendar;

public interface IGoogleCalendarClient
{
    Task<GoogleCalendarConnectionStatus> GetStatus(CancellationToken cancellationToken);

    Task<string> GetAuthorizationUrl(
        string state,
        string callbackUrl,
        CancellationToken cancellationToken);

    Task Connect(string code, CancellationToken cancellationToken);

    Task Disconnect(CancellationToken cancellationToken);

    Task<IReadOnlyList<GoogleCalendarEvent>> ListEvents(
        GoogleCalendarEventQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CalendarAvailabilityWindow>> GetAvailability(
        GoogleCalendarAvailabilityQuery query,
        CancellationToken cancellationToken);
}

public sealed record GoogleCalendarConnectionStatus(
    bool Configured,
    bool Connected,
    string? AccountEmail,
    DateTimeOffset? UpdatedAt);
