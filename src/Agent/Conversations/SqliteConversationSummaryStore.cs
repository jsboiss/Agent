using Agent.Workspaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Agent.Conversations;

public sealed class SqliteConversationSummaryStore(IOptions<SqliteAgentStateOptions> options) : IConversationSummaryStore
{
    private SqliteAgentStateOptions Options { get; } = options.Value;

    public async Task<ConversationSummary?> Get(string conversationId, CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ConversationId, Content, ThroughEntryId, UpdatedAt
            FROM ConversationSummaries
            WHERE ConversationId = $conversationId;
            """;
        command.Parameters.AddWithValue("$conversationId", conversationId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? new ConversationSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3)))
            : null;
    }

    public async Task<ConversationSummary> Upsert(
        string conversationId,
        string content,
        string? throughEntryId,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        var summary = new ConversationSummary(
            conversationId,
            content,
            throughEntryId,
            DateTimeOffset.UtcNow);
        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ConversationSummaries (
                ConversationId, Content, ThroughEntryId, UpdatedAt
            )
            VALUES (
                $conversationId, $content, $throughEntryId, $updatedAt
            )
            ON CONFLICT(ConversationId) DO UPDATE SET
                Content = excluded.Content,
                ThroughEntryId = excluded.ThroughEntryId,
                UpdatedAt = excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$conversationId", summary.ConversationId);
        command.Parameters.AddWithValue("$content", summary.Content);
        command.Parameters.AddWithValue("$throughEntryId", (object?)summary.ThroughEntryId ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", summary.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return summary;
    }

    private async Task EnsureDatabase(CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder(Options.ConnectionString);

        if (!string.IsNullOrWhiteSpace(builder.DataSource))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(builder.DataSource));

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ConversationSummaries (
                ConversationId TEXT PRIMARY KEY,
                Content TEXT NOT NULL,
                ThroughEntryId TEXT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> Open(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(Options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        return connection;
    }
}
