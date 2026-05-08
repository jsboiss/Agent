namespace Agent.Email;

public interface IEmailProvider
{
    Task<EmailConnectionStatus> GetStatus(CancellationToken cancellationToken);

    Task<string> GetAuthorizationUrl(
        string state,
        string callbackUrl,
        CancellationToken cancellationToken);

    Task Connect(string connectedAccountId, CancellationToken cancellationToken);

    Task Disconnect(CancellationToken cancellationToken);

    Task<IReadOnlyList<GmailMessageSummary>> SearchMessages(
        GmailMessageQuery query,
        CancellationToken cancellationToken);

    Task<GmailMessageSummary> GetMessage(string messageId, CancellationToken cancellationToken);

    Task<string> CreateDraft(GmailDraftRequest request, CancellationToken cancellationToken);

    Task<string> SendDraft(string draftId, CancellationToken cancellationToken);
}
