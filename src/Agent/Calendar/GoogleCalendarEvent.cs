namespace Agent.Calendar;

public sealed record GoogleCalendarEvent(
    string Id,
    string CalendarId,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    string TimeZone,
    string? Location,
    IReadOnlyList<string> Attendees,
    string? MeetingLink);

public sealed record CalendarAvailabilityWindow(
    DateTimeOffset Start,
    DateTimeOffset End,
    bool Busy,
    string? Title);
