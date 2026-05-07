namespace Agent.Notifications;

public sealed class CompositeAgentNotifier(IEnumerable<IChannelNotifier> notifiers) : IAgentNotifier
{
    public async Task Send(
        string channel,
        string? target,
        string message,
        CancellationToken cancellationToken)
    {
        foreach (var notifier in notifiers)
        {
            if (notifier.CanNotify(channel))
            {
                await notifier.Send(target, message, cancellationToken);
                return;
            }
        }
    }
}

public interface IChannelNotifier
{
    bool CanNotify(string channel);

    Task Send(string? target, string message, CancellationToken cancellationToken);
}
