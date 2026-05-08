using System.Text.Json;
using Agent.Integrations.Composio;

namespace Agent.Calendar;

public sealed class GoogleCalendarClient(IComposioClient composioClient) : IGoogleCalendarClient
{
    public async Task<GoogleCalendarConnectionStatus> GetStatus(CancellationToken cancellationToken)
    {
        var status = await composioClient.GetStatus(ComposioToolkits.GoogleCalendar, cancellationToken);

        return new GoogleCalendarConnectionStatus(
            status.Configured,
            status.Connected,
            status.AccountEmail,
            status.UpdatedAt);
    }

    public async Task<string> GetAuthorizationUrl(
        string state,
        string callbackUrl,
        CancellationToken cancellationToken)
    {
        var separator = callbackUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var request = await composioClient.CreateConnectLink(
            ComposioToolkits.GoogleCalendar,
            $"{callbackUrl}{separator}state={Uri.EscapeDataString(state)}",
            cancellationToken);

        return request.RedirectUrl;
    }

    public async Task Connect(string code, CancellationToken cancellationToken)
    {
        await composioClient.CompleteConnect(ComposioToolkits.GoogleCalendar, code, cancellationToken);
    }

    public async Task Disconnect(CancellationToken cancellationToken)
    {
        await composioClient.Disconnect(ComposioToolkits.GoogleCalendar, cancellationToken);
    }

    public async Task<IReadOnlyList<GoogleCalendarEvent>> ListEvents(
        GoogleCalendarEventQuery query,
        CancellationToken cancellationToken)
    {
        ValidateRange(query.Start, query.End);

        var arguments = new Dictionary<string, object?>
        {
            ["calendarId"] = query.CalendarId,
            ["timeMin"] = ToComposioDateTime(query.Start),
            ["timeMax"] = ToComposioDateTime(query.End),
            ["singleEvents"] = true,
            ["orderBy"] = "startTime",
            ["maxResults"] = Math.Clamp(query.Limit, 1, 50)
        };

        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            arguments["q"] = query.Query;
        }

        var result = await composioClient.ExecuteTool(
            ComposioToolkits.GoogleCalendar,
            "GOOGLECALENDAR_EVENTS_LIST",
            arguments,
            cancellationToken);

        if (!result.Successful)
        {
            throw new InvalidOperationException(result.Content);
        }

        using var document = JsonDocument.Parse(result.Content);
        var root = UnwrapData(document.RootElement);

        var items = FindArray(root, "items");

        if (items is null)
        {
            return [];
        }

        return items.Value
            .EnumerateArray()
            .Where(x => x.TryGetProperty("start", out _) && x.TryGetProperty("end", out _))
            .Select(x => ToEvent(query.CalendarId, x))
            .ToArray();
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

    private static GoogleCalendarEvent ToEvent(string calendarId, JsonElement item)
    {
        var start = GetDateTime(item.GetProperty("start"));
        var end = GetDateTime(item.GetProperty("end"));
        var attendees = item.TryGetProperty("attendees", out var attendeesElement) && attendeesElement.ValueKind == JsonValueKind.Array
            ? attendeesElement
                .EnumerateArray()
                .Select(x => GetString(x, "email") ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray()
            : [];
        var meetingLink = GetString(item, "hangoutLink") ?? FindConferenceLink(item);

        return new GoogleCalendarEvent(
            GetString(item, "id") ?? string.Empty,
            calendarId,
            string.IsNullOrWhiteSpace(GetString(item, "summary")) ? "(No title)" : GetString(item, "summary")!,
            start,
            end,
            GetString(item.GetProperty("start"), "timeZone") ?? GetString(item.GetProperty("end"), "timeZone") ?? TimeZoneInfo.Local.Id,
            GetString(item, "location"),
            attendees,
            meetingLink);
    }

    private static JsonElement UnwrapData(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
                ? data
                : root;
    }

    private static DateTimeOffset GetDateTime(JsonElement value)
    {
        var dateTime = GetString(value, "dateTime");

        if (!string.IsNullOrWhiteSpace(dateTime))
        {
            return DateTimeOffset.Parse(dateTime);
        }

        var date = GetString(value, "date");

        if (!string.IsNullOrWhiteSpace(date))
        {
            return DateTimeOffset.Parse(date);
        }

        throw new InvalidOperationException("Google Calendar event was missing start or end time.");
    }

    private static JsonElement? FindArray(JsonElement root, string propertyName)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.Array)
                {
                    return property.Value;
                }

                var nested = FindArray(property.Value, propertyName);

                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string? FindConferenceLink(JsonElement item)
    {
        if (!item.TryGetProperty("conferenceData", out var conferenceData)
            || !conferenceData.TryGetProperty("entryPoints", out var entryPoints)
            || entryPoints.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return entryPoints
            .EnumerateArray()
            .Select(x => GetString(x, "uri"))
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    }

    private static string? GetString(JsonElement root, string name)
    {
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static void ValidateRange(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            throw new InvalidOperationException("Calendar end time must be after start time.");
        }
    }

    private static string ToComposioDateTime(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b)
    {
        return a > b ? a : b;
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b)
    {
        return a < b ? a : b;
    }
}
