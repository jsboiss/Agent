namespace Agent.Calendar;

public interface IGoogleCalendarClient
{
    Task<GoogleCalendarConnectionStatus> GetStatus(CancellationToken cancellationToken);

    string GetAuthorizationUrl(string state);

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
