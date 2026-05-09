using Agent.Email;
using System.Text.RegularExpressions;

namespace Agent.Context;

public sealed partial class EmailContextProvider(IEmailProvider emailProvider) : IContextProvider
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
            var query = BuildGmailSearchQuery(request.Plan.Query ?? request.UserMessage);
            var messages = await emailProvider.SearchMessages(
                new GmailMessageQuery(query, 5),
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
                        ["to"] = x.To,
                        ["query"] = query
                    }))
                .ToArray();

            if (items.Length == 0)
            {
                items =
                [
                    new EvidenceItem(
                        Id,
                        "gmail-search",
                        $"No Gmail messages found for query: {query}.",
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

    public static string BuildGmailSearchQuery(string value)
    {
        var message = NormalizeQueryText(value);

        if (string.IsNullOrWhiteSpace(message))
        {
            return value;
        }

        var emailAddress = EmailAddressWords().Match(message);

        if (emailAddress.Success)
        {
            return $"from:{emailAddress.Value} {GetTopic(message, emailAddress.Value)}".Trim();
        }

        var fromAbout = FromAboutWords().Match(message);

        if (fromAbout.Success)
        {
            return $"from:{CleanTerm(fromAbout.Groups["sender"].Value)} {CleanTerm(fromAbout.Groups["topic"].Value)}".Trim();
        }

        var personEmailedAbout = PersonEmailedAboutWords().Match(message);

        if (personEmailedAbout.Success)
        {
            return $"from:{CleanTerm(personEmailedAbout.Groups["sender"].Value)} {CleanTerm(personEmailedAbout.Groups["topic"].Value)}".Trim();
        }

        var documentFrom = DocumentFromWords().Match(message);

        if (documentFrom.Success)
        {
            return $"from:{CleanTerm(documentFrom.Groups["sender"].Value)} {CleanTerm(documentFrom.Groups["kind"].Value)}".Trim();
        }

        return CleanTerm(message);
    }

    private static string FormatMessage(GmailMessageSummary message)
    {
        var date = message.Date is null ? string.Empty : $" Date: {message.Date:O}.";
        var from = string.IsNullOrWhiteSpace(message.From) ? string.Empty : $" From: {message.From}.";
        var to = string.IsNullOrWhiteSpace(message.To) ? string.Empty : $" To: {message.To}.";
        var snippet = string.IsNullOrWhiteSpace(message.Snippet) ? string.Empty : $" Snippet: {message.Snippet}";

        return $"Subject: {message.Subject}.{date}{from}{to} Message id: {message.Id}. Thread id: {message.ThreadId}.{snippet}";
    }

    private static string GetTopic(string message, string matchedAddress)
    {
        var withoutAddress = message.Replace(matchedAddress, string.Empty, StringComparison.OrdinalIgnoreCase);
        var about = AboutWords().Match(withoutAddress);

        return about.Success
            ? CleanTerm(about.Groups["topic"].Value)
            : CleanTerm(withoutAddress);
    }

    private static string NormalizeQueryText(string value)
    {
        return WhitespaceWords().Replace(value
            .Replace("?", " ", StringComparison.Ordinal)
            .Replace(",", " ", StringComparison.Ordinal)
            .Replace("'", string.Empty, StringComparison.Ordinal), " ")
            .Trim();
    }

    private static string CleanTerm(string value)
    {
        var cleaned = FillerWords()
            .Replace(value, " ")
            .Trim();

        return WhitespaceWords().Replace(cleaned, " ");
    }

    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex EmailAddressWords();

    [GeneratedRegex(@"\bfrom\s+(?<sender>[A-Z0-9._%+-][A-Z0-9._%+\-\s]{1,80}?)\s+about\s+(?<topic>.+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FromAboutWords();

    [GeneratedRegex(@"\b(?:(what|when)\s+did\s+|did\s+|has\s+|have\s+)?(?<sender>[A-Z][A-Z0-9._%+\-\s]{1,80}?)\s+(email|emailed|sent|messaged)\s+(me\s+)?about\s+(?<topic>.+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PersonEmailedAboutWords();

    [GeneratedRegex(@"\b(?<kind>receipt|invoice|booking|confirmation|confirmations|attachment|newsletter)\s+from\s+(?<sender>[A-Z0-9._%+-][A-Z0-9._%+\-\s]{1,80})\b", RegexOptions.IgnoreCase)]
    private static partial Regex DocumentFromWords();

    [GeneratedRegex(@"\babout\s+(?<topic>.+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AboutWords();

    [GeneratedRegex(@"\b(did|do|does|can|could|check|my|the|an|a|email|emails|gmail|inbox|message|messages|mail|me|i|get|got|have|has|received|sent|was|were|any|anything|if|whether|please)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FillerWords();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceWords();
}
