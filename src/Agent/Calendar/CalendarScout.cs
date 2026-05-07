using System.Globalization;
using System.Text.RegularExpressions;

namespace Agent.Calendar;

public sealed partial class CalendarScout(ICalendarProvider calendarProvider) : ICalendarScout
{
    public async Task<CalendarScoutResult> Prefetch(
        CalendarScoutRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserMessage)
            || !IsRelevant(request.UserMessage))
        {
            return new CalendarScoutResult(false, false, string.Empty, null, null, null);
        }

        var explicitRequest = IsExplicitCalendarRequest(request.UserMessage);

        if (!TryResolveRange(request.UserMessage, out var start, out var end))
        {
            return new CalendarScoutResult(false, explicitRequest, string.Empty, null, null, null);
        }

        try
        {
            var events = await calendarProvider.ListEvents(
                new GoogleCalendarEventQuery(start, end, null, "primary", 20),
                cancellationToken);

            return new CalendarScoutResult(
                true,
                explicitRequest,
                FormatContext(start, end, events),
                start,
                end,
                null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            return new CalendarScoutResult(
                explicitRequest,
                explicitRequest,
                explicitRequest ? $"Calendar context unavailable: {exception.Message}" : string.Empty,
                start,
                end,
                exception.Message);
        }
    }

    private static bool IsRelevant(string message)
    {
        if (ContainsCodeFence(message))
        {
            return false;
        }

        return IsExplicitCalendarRequest(message)
            || PlanningWords().IsMatch(message)
            || DateWords().IsMatch(message)
            || WeekdayWords().IsMatch(message);
    }

    private static bool IsExplicitCalendarRequest(string message)
    {
        return ExplicitCalendarWords().IsMatch(message);
    }

    private static bool TryResolveRange(
        string message,
        out DateTimeOffset start,
        out DateTimeOffset end)
    {
        var timeZone = GetBrisbaneTimeZone();
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        var today = DateOnly.FromDateTime(now.Date);
        var normalized = message.ToLowerInvariant();

        if (normalized.Contains("weekend", StringComparison.OrdinalIgnoreCase))
        {
            var daysUntilSaturday = ((int)DayOfWeek.Saturday - (int)today.DayOfWeek + 7) % 7;

            if (daysUntilSaturday == 0 && now.DayOfWeek == DayOfWeek.Sunday)
            {
                daysUntilSaturday = 6;
            }

            start = AtStartOfDay(today.AddDays(daysUntilSaturday), timeZone);
            end = start.AddDays(2);
            return true;
        }

        if (normalized.Contains("tomorrow", StringComparison.OrdinalIgnoreCase))
        {
            start = AtStartOfDay(today.AddDays(1), timeZone);
            end = start.AddDays(1);
            return true;
        }

        if (normalized.Contains("today", StringComparison.OrdinalIgnoreCase))
        {
            start = AtStartOfDay(today, timeZone);
            end = start.AddDays(1);
            return true;
        }

        foreach (var x in GetWeekdays())
        {
            if (!Regex.IsMatch(message, $@"\b{x.Name}\b", RegexOptions.IgnoreCase))
            {
                continue;
            }

            var days = ((int)x.Day - (int)today.DayOfWeek + 7) % 7;

            if (days == 0 && !IsExplicitToday(message))
            {
                days = 7;
            }

            start = AtStartOfDay(today.AddDays(days), timeZone);
            end = start.AddDays(1);
            return true;
        }

        var dateMatch = AbsoluteDate().Match(message);

        if (dateMatch.Success
            && TryParseAbsoluteDate(dateMatch.Value, out var date))
        {
            start = AtStartOfDay(date, timeZone);
            end = start.AddDays(1);
            return true;
        }

        start = default;
        end = default;
        return false;
    }

    private static bool TryParseAbsoluteDate(string value, out DateOnly date)
    {
        var year = DateTimeOffset.UtcNow.Year;
        var formats = new[]
        {
            "MMMM d yyyy",
            "MMM d yyyy",
            "d MMMM yyyy",
            "d MMM yyyy",
            "MMMM d",
            "MMM d",
            "d MMMM",
            "d MMM"
        };
        var candidate = Regex.Replace(value, @",", string.Empty);

        if (!Regex.IsMatch(candidate, @"\b\d{4}\b"))
        {
            candidate += $" {year}";
        }

        if (DateTime.TryParseExact(
            candidate,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed))
        {
            date = DateOnly.FromDateTime(parsed.Date);
            return true;
        }

        date = default;
        return false;
    }

    private static string FormatContext(
        DateTimeOffset start,
        DateTimeOffset end,
        IReadOnlyList<GoogleCalendarEvent> events)
    {
        var label = end - start <= TimeSpan.FromDays(1)
            ? start.ToString("dddd, MMMM d", CultureInfo.InvariantCulture)
            : $"{start:dddd, MMMM d} to {end.AddDays(-1):dddd, MMMM d}";

        if (events.Count == 0)
        {
            return $"Calendar context for {label}: no events found.";
        }

        return $"Calendar context for {label}: "
            + string.Join("; ", events.Select(x => $"{x.Start:HH:mm} {x.Title}")) + ".";
    }

    private static DateTimeOffset AtStartOfDay(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
    }

    private static TimeZoneInfo GetBrisbaneTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Australia/Brisbane");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. Australia Standard Time");
        }
    }

    private static bool ContainsCodeFence(string value)
    {
        return value.Contains("```", StringComparison.Ordinal);
    }

    private static bool IsExplicitToday(string value)
    {
        return value.Contains("today", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<(string Name, DayOfWeek Day)> GetWeekdays() =>
    [
        ("monday", DayOfWeek.Monday),
        ("tuesday", DayOfWeek.Tuesday),
        ("wednesday", DayOfWeek.Wednesday),
        ("thursday", DayOfWeek.Thursday),
        ("friday", DayOfWeek.Friday),
        ("saturday", DayOfWeek.Saturday),
        ("sunday", DayOfWeek.Sunday)
    ];

    [GeneratedRegex(@"\b(calendar|schedule|agenda|what'?s on|free/busy|availability|available|meeting|appointment|event)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitCalendarWords();

    [GeneratedRegex(@"\b(plan|plans|planning|fit|before|after|evening|lunch|dinner|work|gym|meet|catch up)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PlanningWords();

    [GeneratedRegex(@"\b(today|tomorrow|weekend|tonight|morning|afternoon|evening|lunch|dinner)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DateWords();

    [GeneratedRegex(@"\b(monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WeekdayWords();

    [GeneratedRegex(@"\b((January|February|March|April|May|June|July|August|September|October|November|December|Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)\.?\s+\d{1,2}(,\s*\d{4})?|\d{1,2}\s+(January|February|March|April|May|June|July|August|September|October|November|December|Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)\.?(,\s*\d{4})?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AbsoluteDate();
}
