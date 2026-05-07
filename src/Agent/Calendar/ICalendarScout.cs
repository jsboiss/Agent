namespace Agent.Calendar;

public interface ICalendarScout
{
    Task<CalendarScoutResult> Prefetch(
        CalendarScoutRequest request,
        CancellationToken cancellationToken);
}

public sealed record CalendarScoutRequest(
    string ConversationId,
    string UserMessage,
    IReadOnlyDictionary<string, string> Hints);

public sealed record CalendarScoutResult(
    bool IsCalendarRelevant,
    bool IsExplicitCalendarRequest,
    string CompactContext,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    string? Error);
