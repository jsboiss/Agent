namespace Agent.Messages;

public interface IAgentPostResponseProcessor
{
    Task Process(AgentPostResponseWorkItem item, CancellationToken cancellationToken);
}

