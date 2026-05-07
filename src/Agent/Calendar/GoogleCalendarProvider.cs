using System.Collections.Concurrent;

namespace Agent.Calendar;

public sealed class GoogleCalendarProvider(IGoogleCalendarClient googleCalendarClient) : ICalendarProvider
{
    private static TimeSpan CacheDuration => TimeSpan.FromMinutes(3);

    private ConcurrentDictionary<string, CalendarCacheEntry> EventCache { get; } = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<GoogleCalendarEvent>> ListEvents(
        GoogleCalendarEventQuery query,
        CancellationToken cancellationToken)
    {
        var cacheKey = GetCacheKey(query);

        if (EventCache.TryGetValue(cacheKey, out var cached)
            && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Events;
        }

        var events = await googleCalendarClient.ListEvents(query, cancellationToken);
        EventCache[cacheKey] = new CalendarCacheEntry(
            DateTimeOffset.UtcNow.Add(CacheDuration),
            events);

        PruneExpired();

        return events;
    }

    public async Task<IReadOnlyList<CalendarAvailabilityWindow>> GetAvailability(
        GoogleCalendarAvailabilityQuery query,
        CancellationToken cancellationToken)
    {
        var events = await ListEvents(
            new GoogleCalendarEventQuery(query.Start, query.End, null, query.CalendarId, 50),
            cancellationToken);
        List<CalendarAvailabilityWindow> windows = [];
        var cursor = query.Start;

        foreach (var calendarEvent in events.OrderBy(x => x.Start))
        {
            if (calendarEvent.End <= cursor)
            {
                continue;
            }

            if (calendarEvent.Start > cursor)
            {
                windows.Add(new CalendarAvailabilityWindow(cursor, calendarEvent.Start, false, null));
            }

            windows.Add(new CalendarAvailabilityWindow(
                Max(calendarEvent.Start, query.Start),
                Min(calendarEvent.End, query.End),
                true,
                calendarEvent.Title));
            cursor = Max(cursor, calendarEvent.End);
        }

        if (cursor < query.End)
        {
            windows.Add(new CalendarAvailabilityWindow(cursor, query.End, false, null));
        }

        return windows;
    }

    private void PruneExpired()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var x in EventCache.Where(x => x.Value.ExpiresAt <= now))
        {
            EventCache.TryRemove(x.Key, out _);
        }
    }

    private static string GetCacheKey(GoogleCalendarEventQuery query)
    {
        return string.Join(
            "|",
            query.CalendarId,
            query.Start.ToUniversalTime().ToString("O"),
            query.End.ToUniversalTime().ToString("O"),
            query.Query ?? string.Empty,
            query.Limit.ToString());
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b)
    {
        return a > b ? a : b;
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b)
    {
        return a < b ? a : b;
    }

    private sealed record CalendarCacheEntry(
        DateTimeOffset ExpiresAt,
        IReadOnlyList<GoogleCalendarEvent> Events);
}
