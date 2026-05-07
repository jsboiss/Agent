using Agent.Calendar;

namespace Agent.Context;

public sealed class CalendarContextProvider(ICalendarProvider calendarProvider) : IContextProvider
{
    public string Id => "calendar";

    public IReadOnlySet<string> Capabilities { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "events",
        "availability",
        "read-only"
    };

    public ContextProviderCost Cost => ContextProviderCost.Low;

    public ContextProviderLatency Latency => ContextProviderLatency.Medium;

    public ContextProviderSafety Safety => ContextProviderSafety.ReadOnly;

    public async Task<ContextProviderResult> Gather(
        ContextProviderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Plan.Start is null || request.Plan.End is null)
        {
            return new ContextProviderResult(Id, [], false, "Calendar provider requires a date window.");
        }

        try
        {
            var events = await calendarProvider.ListEvents(
                new GoogleCalendarEventQuery(
                    request.Plan.Start.Value,
                    request.Plan.End.Value,
                    request.Plan.Query,
                    "primary",
                    20),
                cancellationToken);
            var items = events
                .Select(x => new EvidenceItem(
                    Id,
                    x.Title,
                    $"{x.Start:O} to {x.End:O}: {x.Title}{GetLocation(x)}",
                    x.Start,
                    x.End,
                    0.9,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["id"] = x.Id,
                        ["calendarId"] = x.CalendarId,
                        ["timeZone"] = x.TimeZone
                    }))
                .ToArray();

            if (items.Length == 0)
            {
                items =
                [
                    new EvidenceItem(
                        Id,
                        request.Plan.DateWindowLabel ?? "calendar-window",
                        $"No calendar events found from {request.Plan.Start:O} to {request.Plan.End:O}.",
                        request.Plan.Start,
                        request.Plan.End,
                        0.85,
                        new Dictionary<string, string>())
                ];
            }

            return new ContextProviderResult(Id, items, true, null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            return new ContextProviderResult(Id, [], false, exception.Message);
        }
    }

    private static string GetLocation(GoogleCalendarEvent calendarEvent)
    {
        return string.IsNullOrWhiteSpace(calendarEvent.Location)
            ? string.Empty
            : $" at {calendarEvent.Location}";
    }
}
