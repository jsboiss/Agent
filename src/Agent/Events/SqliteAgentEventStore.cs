using System.Text.Json;
using Agent.Workspaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Agent.Events;

public sealed class SqliteAgentEventStore(IOptions<SqliteAgentStateOptions> options) : IAgentEventStore
{
    private SqliteAgentStateOptions Options { get; } = options.Value;

    public async Task Publish(AgentEvent agentEvent, CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AgentEvents (
                Id, Kind, ConversationId, CreatedAt, DataJson
            )
            VALUES (
                $id, $kind, $conversationId, $createdAt, $dataJson
            );
            """;
        command.Parameters.AddWithValue("$id", agentEvent.Id);
        command.Parameters.AddWithValue("$kind", agentEvent.Kind.ToString());
        command.Parameters.AddWithValue("$conversationId", agentEvent.ConversationId);
        command.Parameters.AddWithValue("$createdAt", agentEvent.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$dataJson", JsonSerializer.Serialize(agentEvent.Data));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AgentEvent>> List(
        string? conversationId,
        int limit,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = string.IsNullOrWhiteSpace(conversationId)
            ? """
            SELECT Id, Kind, ConversationId, CreatedAt, DataJson
            FROM AgentEvents
            ORDER BY CreatedAt DESC
            LIMIT $limit;
            """
            : """
            SELECT Id, Kind, ConversationId, CreatedAt, DataJson
            FROM AgentEvents
            WHERE ConversationId = $conversationId
            ORDER BY CreatedAt DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$conversationId", (object?)conversationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));

        List<AgentEvent> events = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(4))
                ?? new Dictionary<string, string>();
            events.Add(new AgentEvent(
                reader.GetString(0),
                Enum.Parse<AgentEventKind>(reader.GetString(1)),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3)),
                data));
        }

        return events;
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
            CREATE TABLE IF NOT EXISTS AgentEvents (
                Id TEXT PRIMARY KEY,
                Kind TEXT NOT NULL,
                ConversationId TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                DataJson TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_AgentEvents_Conversation_CreatedAt
            ON AgentEvents (ConversationId, CreatedAt);
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
