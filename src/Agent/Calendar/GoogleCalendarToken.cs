namespace Agent.Calendar;

public sealed record GoogleCalendarToken(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAt,
    string? AccountEmail,
    DateTimeOffset UpdatedAt);
