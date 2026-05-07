using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Agent.Context;

public sealed partial class RuleBasedContextPlanner(IOptions<ContextPlannerOptions> options) : IContextPlanner
{
    private ContextPlannerOptions Options { get; } = options.Value;

    public Task<ContextPlan> Plan(ContextPlanningRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserMessage)
            || request.UserMessage.Contains("```", StringComparison.Ordinal))
        {
            return Task.FromResult(ContextPlan.Empty);
        }

        List<ContextProviderPlan> providers = [];
        var enabled = Options.EnabledProviders.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (enabled.Contains("Memory") && IsMemoryRelevant(request.UserMessage))
        {
            providers.Add(new ContextProviderPlan("memory", request.UserMessage, null, null, null, false, 0.65));
        }

        if (enabled.Contains("Calendar") && IsCalendarRelevant(request.UserMessage))
        {
            var hasWindow = TryResolveDateWindow(request.UserMessage, request.ReceivedAt, out var start, out var end, out var label);
            providers.Add(new ContextProviderPlan(
                "calendar",
                request.UserMessage,
                hasWindow ? start : null,
                hasWindow ? end : null,
                label,
                IsExplicitCalendarRequest(request.UserMessage),
                hasWindow ? 0.75 : 0.45));
        }

        return Task.FromResult(providers.Count == 0
            ? ContextPlan.Empty
            : new ContextPlan(true, providers, providers.Max(x => x.Confidence), providers.Any(x => x.Required), null));
    }

    private static bool IsMemoryRelevant(string message)
    {
        return MemoryWords().IsMatch(message)
            || PreferenceWords().IsMatch(message)
            || HistoryWords().IsMatch(message);
    }

    private static bool IsCalendarRelevant(string message)
    {
        return IsExplicitCalendarRequest(message)
            || PlanningWords().IsMatch(message)
            || DateWords().IsMatch(message)
            || WeekdayWords().IsMatch(message);
    }

    private static bool IsExplicitCalendarRequest(string message)
    {
        return ExplicitCalendarWords().IsMatch(message);
    }

    private static bool TryResolveDateWindow(
        string message,
        DateTimeOffset now,
        out DateTimeOffset start,
        out DateTimeOffset end,
        out string? label)
    {
        var normalized = message.ToLowerInvariant();
        var localNow = now;
        var today = DateOnly.FromDateTime(localNow.Date);

        if (normalized.Contains("tomorrow", StringComparison.OrdinalIgnoreCase))
        {
            label = "tomorrow";
            start = AtStartOfDay(today.AddDays(1), localNow.Offset);
            end = start.AddDays(1);
            return true;
        }

        if (normalized.Contains("today", StringComparison.OrdinalIgnoreCase))
        {
            label = "today";
            start = AtStartOfDay(today, localNow.Offset);
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

            if (days == 0 && !normalized.Contains("today", StringComparison.OrdinalIgnoreCase))
            {
                days = 7;
            }

            label = x.Name;
            start = AtStartOfDay(today.AddDays(days), localNow.Offset);
            end = start.AddDays(1);
            return true;
        }

        start = default;
        end = default;
        label = null;
        return false;
    }

    private static DateTimeOffset AtStartOfDay(DateOnly date, TimeSpan offset)
    {
        return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), offset);
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

    [GeneratedRegex(@"\b(memory|remember|preference|prefer|usually|last time|before|history|know about me|my)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MemoryWords();

    [GeneratedRegex(@"\b(like|likes|prefer|prefers|favorite|favourite|avoid|hate|love)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PreferenceWords();

    [GeneratedRegex(@"\b(what did|when did|have i|did i|previously|earlier)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HistoryWords();

    [GeneratedRegex(@"\b(calendar|schedule|agenda|what'?s on|free/busy|availability|available|meeting|appointment|event)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitCalendarWords();

    [GeneratedRegex(@"\b(plan|plans|planning|fit|before|after|evening|lunch|dinner|work|gym|meet|catch up)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PlanningWords();

    [GeneratedRegex(@"\b(today|tomorrow|weekend|tonight|morning|afternoon|evening|lunch|dinner)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DateWords();

    [GeneratedRegex(@"\b(monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WeekdayWords();
}
