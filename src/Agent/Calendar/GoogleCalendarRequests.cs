namespace Agent.Calendar;

public sealed record GoogleCalendarEventQuery(
    DateTimeOffset Start,
    DateTimeOffset End,
    string? Query,
    string CalendarId,
    int Limit);

public sealed record GoogleCalendarAvailabilityQuery(
    DateTimeOffset Start,
    DateTimeOffset End,
    string CalendarId);
