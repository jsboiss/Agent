using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Agent.Calendar;

public sealed class GoogleCalendarClient(
    HttpClient httpClient,
    IOptions<GoogleCalendarOptions> options,
    IGoogleCalendarAuthStore authStore) : IGoogleCalendarClient
{
    private const string CalendarReadonlyScope = "https://www.googleapis.com/auth/calendar.readonly";

    private GoogleCalendarOptions Options { get; } = options.Value;

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<GoogleCalendarConnectionStatus> GetStatus(CancellationToken cancellationToken)
    {
        var token = await authStore.Get(cancellationToken);

        return new GoogleCalendarConnectionStatus(
            IsConfigured(),
            token is not null,
            token?.AccountEmail,
            token?.UpdatedAt);
    }

    public string GetAuthorizationUrl(string state)
    {
        EnsureConfigured();

        var values = new Dictionary<string, string?>
        {
            ["client_id"] = Options.ClientId,
            ["redirect_uri"] = Options.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = CalendarReadonlyScope,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = state
        };

        return "https://accounts.google.com/o/oauth2/v2/auth?" + string.Join(
            "&",
            values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value ?? string.Empty)}"));
    }

    public async Task Connect(string code, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var response = await httpClient.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = Options.ClientId,
                ["client_secret"] = Options.ClientSecret,
                ["code"] = code,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = Options.RedirectUri
            }),
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google OAuth token exchange failed: {(int)response.StatusCode} {body}");
        }

        var token = JsonSerializer.Deserialize<GoogleTokenResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Google OAuth token exchange returned no token body.");
        var accountEmail = await GetAccountEmail(token.AccessToken, cancellationToken);
        await authStore.Save(
            new GoogleCalendarToken(
                token.AccessToken,
                token.RefreshToken,
                DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn - 60)),
                accountEmail,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    public async Task Disconnect(CancellationToken cancellationToken)
    {
        await authStore.Clear(cancellationToken);
    }

    public async Task<IReadOnlyList<GoogleCalendarEvent>> ListEvents(
        GoogleCalendarEventQuery query,
        CancellationToken cancellationToken)
    {
        ValidateRange(query.Start, query.End);

        var accessToken = await GetAccessToken(cancellationToken);
        var parameters = new Dictionary<string, string?>
        {
            ["timeMin"] = query.Start.ToUniversalTime().ToString("O"),
            ["timeMax"] = query.End.ToUniversalTime().ToString("O"),
            ["singleEvents"] = "true",
            ["orderBy"] = "startTime",
            ["maxResults"] = Math.Clamp(query.Limit, 1, 50).ToString()
        };

        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            parameters["q"] = query.Query;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(query.CalendarId)}/events?{ToQueryString(parameters)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google Calendar events request failed: {(int)response.StatusCode} {body}");
        }

        var result = JsonSerializer.Deserialize<GoogleEventsResponse>(body, JsonOptions);

        return result?.Items?
            .Where(x => x.Start is not null && x.End is not null)
            .Select(x => ToEvent(query.CalendarId, x))
            .ToArray() ?? [];
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

    private async Task<string> GetAccessToken(CancellationToken cancellationToken)
    {
        var token = await authStore.Get(cancellationToken);

        if (token is null)
        {
            throw new InvalidOperationException("Google Calendar is not connected. Connect it from Settings first.");
        }

        if (token.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return token.AccessToken;
        }

        if (string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            throw new InvalidOperationException("Google Calendar access expired and no refresh token is stored. Reconnect calendar from Settings.");
        }

        EnsureConfigured();

        var response = await httpClient.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = Options.ClientId,
                ["client_secret"] = Options.ClientSecret,
                ["refresh_token"] = token.RefreshToken,
                ["grant_type"] = "refresh_token"
            }),
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google OAuth refresh failed: {(int)response.StatusCode} {body}");
        }

        var refreshed = JsonSerializer.Deserialize<GoogleTokenResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Google OAuth refresh returned no token body.");
        var updated = token with
        {
            AccessToken = refreshed.AccessToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, refreshed.ExpiresIn - 60)),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await authStore.Save(updated, cancellationToken);

        return updated.AccessToken;
    }

    private async Task<string?> GetAccountEmail(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var user = JsonSerializer.Deserialize<GoogleUserInfoResponse>(body, JsonOptions);

        return user?.Email;
    }

    private bool IsConfigured()
    {
        return !string.IsNullOrWhiteSpace(Options.ClientId)
            && !string.IsNullOrWhiteSpace(Options.ClientSecret)
            && !string.IsNullOrWhiteSpace(Options.RedirectUri);
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured())
        {
            throw new InvalidOperationException("Google Calendar OAuth is not configured. Set Integrations:GoogleCalendar:ClientId, ClientSecret, and RedirectUri.");
        }
    }

    private static GoogleCalendarEvent ToEvent(string calendarId, GoogleEventItem item)
    {
        var start = GetDateTime(item.Start);
        var end = GetDateTime(item.End);

        return new GoogleCalendarEvent(
            item.Id ?? string.Empty,
            calendarId,
            string.IsNullOrWhiteSpace(item.Summary) ? "(No title)" : item.Summary,
            start,
            end,
            item.Start?.TimeZone ?? item.End?.TimeZone ?? TimeZoneInfo.Local.Id,
            item.Location,
            item.Attendees?.Select(x => x.Email ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray() ?? [],
            item.HangoutLink ?? item.ConferenceData?.EntryPoints?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Uri))?.Uri);
    }

    private static DateTimeOffset GetDateTime(GoogleEventDateTime? value)
    {
        if (!string.IsNullOrWhiteSpace(value?.DateTime))
        {
            return DateTimeOffset.Parse(value.DateTime);
        }

        if (!string.IsNullOrWhiteSpace(value?.Date))
        {
            return DateTimeOffset.Parse(value.Date);
        }

        throw new InvalidOperationException("Google Calendar event was missing start or end time.");
    }

    private static void ValidateRange(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            throw new InvalidOperationException("Calendar end time must be after start time.");
        }
    }

    private static string ToQueryString(IReadOnlyDictionary<string, string?> values)
    {
        return string.Join(
            "&",
            values
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value ?? string.Empty)}"));
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b)
    {
        return a > b ? a : b;
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b)
    {
        return a < b ? a : b;
    }

    private sealed record GoogleTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record GoogleUserInfoResponse(string? Email);

    private sealed record GoogleEventsResponse(IReadOnlyList<GoogleEventItem>? Items);

    private sealed record GoogleEventItem(
        string? Id,
        string? Summary,
        GoogleEventDateTime? Start,
        GoogleEventDateTime? End,
        string? Location,
        IReadOnlyList<GoogleEventAttendee>? Attendees,
        string? HangoutLink,
        GoogleConferenceData? ConferenceData);

    private sealed record GoogleEventDateTime(
        string? Date,
        string? DateTime,
        string? TimeZone);

    private sealed record GoogleEventAttendee(string? Email);

    private sealed record GoogleConferenceData(IReadOnlyList<GoogleConferenceEntryPoint>? EntryPoints);

    private sealed record GoogleConferenceEntryPoint(string? Uri);
}
