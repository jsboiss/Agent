using Agent.AgentFlow;

namespace Agent.Adapters.Chat;

public interface IChatAdapter
{
    string Channel { get; }

    Task Start(IAgentDispatcher dispatcher, CancellationToken cancellationToken);
}
