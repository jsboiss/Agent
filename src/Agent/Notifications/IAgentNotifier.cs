namespace Agent.Notifications;

public interface IAgentNotifier
{
    Task Send(
        string channel,
        string? target,
        string message,
        CancellationToken cancellationToken);
}
