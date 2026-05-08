namespace Agent.Email;

public sealed record EmailConnectionStatus(
    bool Configured,
    bool Connected,
    string? AccountEmail,
    DateTimeOffset? UpdatedAt);

public sealed record GmailMessageQuery(
    string Query,
    int Limit);

public sealed record GmailMessageSummary(
    string Id,
    string ThreadId,
    string From,
    string To,
    string Subject,
    DateTimeOffset? Date,
    string Snippet);

public sealed record GmailDraftRequest(
    string To,
    string Subject,
    string Body,
    string? Cc,
    string? Bcc);
