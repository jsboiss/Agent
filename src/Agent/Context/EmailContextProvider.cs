using Agent.Email;

namespace Agent.Context;

public sealed class EmailContextProvider(IEmailProvider emailProvider) : IContextProvider
{
    public string Id => "email";

    public IReadOnlySet<string> Capabilities { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "gmail",
        "message-search",
        "read-only"
    };

    public ContextProviderCost Cost => ContextProviderCost.Low;

    public ContextProviderLatency Latency => ContextProviderLatency.Medium;

    public ContextProviderSafety Safety => ContextProviderSafety.ReadOnly;

    public async Task<ContextProviderResult> Gather(
        ContextProviderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var messages = await emailProvider.SearchMessages(
                new GmailMessageQuery(request.Plan.Query ?? request.UserMessage, 5),
                cancellationToken);
            var items = messages
                .Select(x => new EvidenceItem(
                    Id,
                    string.IsNullOrWhiteSpace(x.Subject) ? $"gmail:{x.Id}" : x.Subject,
                    FormatMessage(x),
                    x.Date,
                    null,
                    0.8,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["id"] = x.Id,
                        ["threadId"] = x.ThreadId,
                        ["from"] = x.From,
                        ["to"] = x.To
                    }))
                .ToArray();

            if (items.Length == 0)
            {
                items =
                [
                    new EvidenceItem(
                        Id,
                        "gmail-search",
                        $"No Gmail messages found for query: {request.Plan.Query ?? request.UserMessage}.",
                        null,
                        null,
                        0.7,
                        new Dictionary<string, string>())
                ];
            }

            return new ContextProviderResult(Id, items, true, null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            return new ContextProviderResult(Id, [], false, exception.Message);
        }
    }

    private static string FormatMessage(GmailMessageSummary message)
    {
        var date = message.Date is null ? string.Empty : $" Date: {message.Date:O}.";
        var from = string.IsNullOrWhiteSpace(message.From) ? string.Empty : $" From: {message.From}.";
        var to = string.IsNullOrWhiteSpace(message.To) ? string.Empty : $" To: {message.To}.";
        var snippet = string.IsNullOrWhiteSpace(message.Snippet) ? string.Empty : $" Snippet: {message.Snippet}";

        return $"Subject: {message.Subject}.{date}{from}{to} Message id: {message.Id}. Thread id: {message.ThreadId}.{snippet}";
    }
}
