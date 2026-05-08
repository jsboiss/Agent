namespace Agent.Integrations.Composio;

public interface IComposioConnectionStore
{
    Task<ComposioConnection?> Get(string toolkit, CancellationToken cancellationToken);

    Task Save(ComposioConnection connection, CancellationToken cancellationToken);

    Task Clear(string toolkit, CancellationToken cancellationToken);
}
