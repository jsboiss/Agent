using Agent.Workspaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Agent.Drafts;

public sealed class SqliteAgentDraftStore(IOptions<SqliteAgentStateOptions> options) : IAgentDraftStore
{
    private SqliteAgentStateOptions Options { get; } = options.Value;

    public async Task<AgentDraft> Create(DraftWriteRequest request, CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        var timestamp = DateTimeOffset.UtcNow;
        var draft = new AgentDraft(
            Guid.NewGuid().ToString("N"),
            request.Kind,
            request.Summary,
            request.Payload,
            request.SourceRunId,
            request.ConversationId,
            request.Channel,
            DraftStatus.Pending,
            timestamp,
            timestamp);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AgentDrafts (
                Id, Kind, Summary, Payload, SourceRunId, ConversationId, Channel, Status, CreatedAt, UpdatedAt
            )
            VALUES (
                $id, $kind, $summary, $payload, $sourceRunId, $conversationId, $channel, $status, $createdAt, $updatedAt
            );
            """;
        AddParameters(command, draft);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return draft;
    }

    public async Task<AgentDraft?> Get(string id, CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Kind, Summary, Payload, SourceRunId, ConversationId, Channel, Status, CreatedAt, UpdatedAt
            FROM AgentDrafts
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? GetDraft(reader) : null;
    }

    public async Task<IReadOnlyList<AgentDraft>> List(
        DraftStatus? status,
        int limit,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = status is null
            ? """
            SELECT Id, Kind, Summary, Payload, SourceRunId, ConversationId, Channel, Status, CreatedAt, UpdatedAt
            FROM AgentDrafts
            ORDER BY CreatedAt DESC
            LIMIT $limit;
            """
            : """
            SELECT Id, Kind, Summary, Payload, SourceRunId, ConversationId, Channel, Status, CreatedAt, UpdatedAt
            FROM AgentDrafts
            WHERE Status = $status
            ORDER BY CreatedAt DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$status", status?.ToString() ?? string.Empty);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 250));

        List<AgentDraft> drafts = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            drafts.Add(GetDraft(reader));
        }

        return drafts;
    }

    public async Task<AgentDraft> UpdateStatus(
        string id,
        DraftStatus status,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE AgentDrafts
            SET Status = $status,
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await Get(id, cancellationToken)
            ?? throw new InvalidOperationException($"Draft '{id}' was not found.");
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
            CREATE TABLE IF NOT EXISTS AgentDrafts (
                Id TEXT PRIMARY KEY,
                Kind TEXT NOT NULL,
                Summary TEXT NOT NULL,
                Payload TEXT NOT NULL,
                SourceRunId TEXT NULL,
                ConversationId TEXT NOT NULL,
                Channel TEXT NOT NULL,
                Status TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_AgentDrafts_Status_CreatedAt ON AgentDrafts (Status, CreatedAt);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> Open(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(Options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void AddParameters(SqliteCommand command, AgentDraft draft)
    {
        command.Parameters.AddWithValue("$id", draft.Id);
        command.Parameters.AddWithValue("$kind", draft.Kind);
        command.Parameters.AddWithValue("$summary", draft.Summary);
        command.Parameters.AddWithValue("$payload", draft.Payload);
        command.Parameters.AddWithValue("$sourceRunId", (object?)draft.SourceRunId ?? DBNull.Value);
        command.Parameters.AddWithValue("$conversationId", draft.ConversationId);
        command.Parameters.AddWithValue("$channel", draft.Channel);
        command.Parameters.AddWithValue("$status", draft.Status.ToString());
        command.Parameters.AddWithValue("$createdAt", draft.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", draft.UpdatedAt.ToString("O"));
    }

    private static AgentDraft GetDraft(SqliteDataReader reader)
    {
        return new AgentDraft(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            Enum.Parse<DraftStatus>(reader.GetString(7)),
            DateTimeOffset.Parse(reader.GetString(8)),
            DateTimeOffset.Parse(reader.GetString(9)));
    }
}
