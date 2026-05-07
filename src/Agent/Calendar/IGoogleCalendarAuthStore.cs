namespace Agent.Calendar;

public interface IGoogleCalendarAuthStore
{
    Task<GoogleCalendarToken?> Get(CancellationToken cancellationToken);

    Task Save(GoogleCalendarToken token, CancellationToken cancellationToken);

    Task Clear(CancellationToken cancellationToken);
}
