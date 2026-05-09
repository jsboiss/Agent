using Agent.Context;
using Agent.Email;
using Xunit;

namespace Agent.Tests.Context;

public sealed class EmailContextProviderTests
{
    [Theory]
    [InlineData("Did I get an email from Stripe about an invoice?", "from:Stripe invoice")]
    [InlineData("What did Sarah email me about the booking?", "from:Sarah booking")]
    [InlineData("Do I have a receipt from Uber?", "from:Uber receipt")]
    [InlineData("Check if billing@example.com sent me anything about renewal", "from:billing@example.com renewal")]
    public void BuildGmailSearchQuery_ExtractsTargetedQueries(string message, string expected)
    {
        Assert.Equal(expected, EmailContextProvider.BuildGmailSearchQuery(message));
    }

    [Fact]
    public async Task Gather_UsesTargetedQuery_AndInjectsSnippetMetadata()
    {
        var provider = new CapturingEmailProvider([
            new GmailMessageSummary(
                "message-1",
                "thread-1",
                "Stripe <billing@stripe.com>",
                "owner@example.com",
                "Invoice",
                new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero),
                "Your invoice is ready.")
        ]);
        var contextProvider = new EmailContextProvider(provider);

        var result = await contextProvider.Gather(
            new ContextProviderRequest(
                "main",
                "local-web",
                "Did I get an email from Stripe about an invoice?",
                new ContextProviderPlan("email", null, null, null, null, false, 0.7),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("from:Stripe invoice", provider.Query?.Query);
        var item = Assert.Single(result.Items);
        Assert.Contains("Snippet: Your invoice is ready.", item.Text);
        Assert.Equal("message-1", item.Metadata["id"]);
        Assert.Equal("thread-1", item.Metadata["threadId"]);
        Assert.Equal("from:Stripe invoice", item.Metadata["query"]);
    }

    [Fact]
    public async Task Gather_ReturnsProviderFailure_WhenEmailProviderFails()
    {
        var contextProvider = new EmailContextProvider(new FailingEmailProvider());

        var result = await contextProvider.Gather(
            new ContextProviderRequest(
                "main",
                "local-web",
                "Did I get an email from Stripe about an invoice?",
                new ContextProviderPlan("email", null, null, null, null, false, 0.7),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Items);
        Assert.Equal("Gmail is not connected.", result.Error);
    }

    private sealed class CapturingEmailProvider(IReadOnlyList<GmailMessageSummary> messages) : IEmailProvider
    {
        public GmailMessageQuery? Query { get; private set; }

        public Task<EmailConnectionStatus> GetStatus(CancellationToken cancellationToken)
        {
            return Task.FromResult(new EmailConnectionStatus(true, true, "owner@example.com", DateTimeOffset.UtcNow));
        }

        public Task<string> GetAuthorizationUrl(string state, string callbackUrl, CancellationToken cancellationToken)
        {
            return Task.FromResult("https://example.com/connect");
        }

        public Task Connect(string connectedAccountId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task Disconnect(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<GmailMessageSummary>> SearchMessages(GmailMessageQuery query, CancellationToken cancellationToken)
        {
            Query = query;

            return Task.FromResult(messages);
        }

        public Task<GmailMessageSummary> GetMessage(string messageId, CancellationToken cancellationToken)
        {
            return Task.FromResult(messages.Single(x => x.Id == messageId));
        }

        public Task<string> CreateDraft(GmailDraftRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult("draft-1");
        }

        public Task<string> SendDraft(string draftId, CancellationToken cancellationToken)
        {
            return Task.FromResult("message-1");
        }
    }

    private sealed class FailingEmailProvider : IEmailProvider
    {
        public Task<EmailConnectionStatus> GetStatus(CancellationToken cancellationToken)
        {
            return Task.FromResult(new EmailConnectionStatus(true, false, null, null));
        }

        public Task<string> GetAuthorizationUrl(string state, string callbackUrl, CancellationToken cancellationToken)
        {
            return Task.FromResult("https://example.com/connect");
        }

        public Task Connect(string connectedAccountId, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task Disconnect(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<GmailMessageSummary>> SearchMessages(GmailMessageQuery query, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Gmail is not connected.");
        }

        public Task<GmailMessageSummary> GetMessage(string messageId, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Gmail is not connected.");
        }

        public Task<string> CreateDraft(GmailDraftRequest request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Gmail is not connected.");
        }

        public Task<string> SendDraft(string draftId, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Gmail is not connected.");
        }
    }
}
