namespace Agent.Automations;

public sealed class SimpleAutomationScheduler : IAutomationScheduler
{
    public DateTimeOffset? GetNextRun(string schedule, DateTimeOffset from)
    {
        if (string.IsNullOrWhiteSpace(schedule))
        {
            return null;
        }

        var value = schedule.Trim();

        if (TimeSpan.TryParse(value, out var interval))
        {
            return from.Add(interval);
        }

        if (value.StartsWith("every ", StringComparison.OrdinalIgnoreCase)
            && TimeSpan.TryParse(value[6..].Trim(), out interval))
        {
            return from.Add(interval);
        }

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 5
            && int.TryParse(parts[0], out var minute)
            && int.TryParse(parts[1], out var hour))
        {
            minute = Math.Clamp(minute, 0, 59);
            hour = Math.Clamp(hour, 0, 23);
            var next = new DateTimeOffset(
                from.Year,
                from.Month,
                from.Day,
                hour,
                minute,
                0,
                from.Offset);

            if (next <= from)
            {
                next = next.AddDays(1);
            }

            return next;
        }

        return null;
    }
}
