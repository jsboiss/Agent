namespace Agent.Calendar;

public interface ICalendarProvider
{
    Task<IReadOnlyList<GoogleCalendarEvent>> ListEvents(
        GoogleCalendarEventQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CalendarAvailabilityWindow>> GetAvailability(
        GoogleCalendarAvailabilityQuery query,
        CancellationToken cancellationToken);
}
