using Agent.Conversations;
using Agent.Workspaces;
using Microsoft.Extensions.Options;
using Xunit;

namespace Agent.Tests.Conversations;

public sealed class SqliteConversationRepositoryTests
{
    [Fact]
    public async Task SearchEntries_FindsPriorConversationEntries()
    {
        var path = Path.Combine(Path.GetTempPath(), "mainagent-tests", Guid.NewGuid().ToString("N"), "agent.db");
        var repository = new SqliteConversationRepository(Options.Create(new SqliteAgentStateOptions
        {
            ConnectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = path
            }.ToString()
        }));
        var conversation = (await repository.GetOrCreateMain(CancellationToken.None)).Conversation;
        await repository.AddEntry(conversation.Id, ConversationEntryRole.User, "local-web", "We discussed Hermes session recall.", null, CancellationToken.None);

        var results = await repository.SearchEntries("Hermes recall", 5, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(conversation.Id, result.Conversation.Id);
        Assert.Contains("Hermes session recall", result.Entry.Content);
    }
}
