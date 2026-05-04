namespace Agent.Messages;

public interface IMessageProcessor
{
    Task<MessageResult> Process(MessageRequest request, CancellationToken cancellationToken);
}
