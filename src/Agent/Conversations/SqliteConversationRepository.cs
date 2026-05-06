using Agent.Workspaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Agent.Conversations;

public sealed class SqliteConversationRepository(IOptions<SqliteAgentStateOptions> options) : IConversationRepository
{
    private SqliteAgentStateOptions Options { get; } = options.Value;

    public async Task<Conversation?> Get(string conversationId, CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        return await GetConversation(connection, conversationId, cancellationToken);
    }

    public async Task<ConversationResolution> GetOrCreateMain(CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        var existingConversation = await GetConversation(connection, "main", cancellationToken);

        if (existingConversation is not null)
        {
            return new ConversationResolution(existingConversation, false);
        }

        var timestamp = DateTimeOffset.UtcNow;
        var conversation = new Conversation(
            "main",
            ConversationKind.Main,
            null,
            null,
            timestamp,
            timestamp);

        await InsertConversation(connection, conversation, cancellationToken);

        return new ConversationResolution(conversation, true);
    }

    public async Task<Conversation> CreateChild(
        ConversationKind kind,
        string parentConversationId,
        string parentEntryId,
        CancellationToken cancellationToken)
    {
        if (kind == ConversationKind.Main)
        {
            throw new ArgumentException("Child conversations must be Branch or SubAgent.", nameof(kind));
        }

        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        var parentConversation = await GetConversation(connection, parentConversationId, cancellationToken);

        if (parentConversation is null)
        {
            throw new InvalidOperationException($"Parent conversation '{parentConversationId}' was not found.");
        }

        var timestamp = DateTimeOffset.UtcNow;
        var conversation = new Conversation(
            Guid.NewGuid().ToString("N"),
            kind,
            parentConversationId,
            parentEntryId,
            timestamp,
            timestamp);

        await InsertConversation(connection, conversation, cancellationToken);

        return conversation;
    }

    public async Task<ConversationEntry> AddEntry(
        string conversationId,
        ConversationEntryRole role,
        string channel,
        string content,
        string? parentEntryId,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        var conversation = await GetConversation(connection, conversationId, cancellationToken)
            ?? throw new InvalidOperationException($"Conversation '{conversationId}' was not found.");
        var timestamp = DateTimeOffset.UtcNow;
        var entry = new ConversationEntry(
            Guid.NewGuid().ToString("N"),
            conversationId,
            role,
            channel,
            content,
            parentEntryId,
            timestamp);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO ConversationEntries (
                    Id, ConversationId, Role, Channel, Content, ParentEntryId, CreatedAt
                )
                VALUES (
                    $id, $conversationId, $role, $channel, $content, $parentEntryId, $createdAt
                );

                UPDATE Conversations
                SET UpdatedAt = $updatedAt
                WHERE Id = $conversationId;
                """;
            command.Parameters.AddWithValue("$id", entry.Id);
            command.Parameters.AddWithValue("$conversationId", entry.ConversationId);
            command.Parameters.AddWithValue("$role", entry.Role.ToString());
            command.Parameters.AddWithValue("$channel", entry.Channel);
            command.Parameters.AddWithValue("$content", entry.Content);
            command.Parameters.AddWithValue("$parentEntryId", (object?)entry.ParentEntryId ?? DBNull.Value);
            command.Parameters.AddWithValue("$createdAt", entry.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("$updatedAt", timestamp.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        _ = conversation;
        return entry;
    }

    public async Task<IReadOnlyList<ConversationEntry>> ListEntries(
        string conversationId,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ConversationId, Role, Channel, Content, ParentEntryId, CreatedAt
            FROM ConversationEntries
            WHERE ConversationId = $conversationId
            ORDER BY CreatedAt ASC;
            """;
        command.Parameters.AddWithValue("$conversationId", conversationId);

        List<ConversationEntry> entries = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new ConversationEntry(
                reader.GetString(0),
                reader.GetString(1),
                Enum.Parse<ConversationEntryRole>(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                DateTimeOffset.Parse(reader.GetString(6))));
        }

        return entries;
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
            CREATE TABLE IF NOT EXISTS Conversations (
                Id TEXT PRIMARY KEY,
                Kind TEXT NOT NULL,
                ParentConversationId TEXT NULL,
                ParentEntryId TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ConversationEntries (
                Id TEXT PRIMARY KEY,
                ConversationId TEXT NOT NULL,
                Role TEXT NOT NULL,
                Channel TEXT NOT NULL,
                Content TEXT NOT NULL,
                ParentEntryId TEXT NULL,
                CreatedAt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_ConversationEntries_Conversation_CreatedAt
            ON ConversationEntries (ConversationId, CreatedAt);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> Open(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(Options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        return connection;
    }

    private static async Task InsertConversation(
        SqliteConnection connection,
        Conversation conversation,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Conversations (
                Id, Kind, ParentConversationId, ParentEntryId, CreatedAt, UpdatedAt
            )
            VALUES (
                $id, $kind, $parentConversationId, $parentEntryId, $createdAt, $updatedAt
            );
            """;
        command.Parameters.AddWithValue("$id", conversation.Id);
        command.Parameters.AddWithValue("$kind", conversation.Kind.ToString());
        command.Parameters.AddWithValue("$parentConversationId", (object?)conversation.ParentConversationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$parentEntryId", (object?)conversation.ParentEntryId ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", conversation.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", conversation.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Conversation?> GetConversation(
        SqliteConnection connection,
        string conversationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Kind, ParentConversationId, ParentEntryId, CreatedAt, UpdatedAt
            FROM Conversations
            WHERE Id = $conversationId;
            """;
        command.Parameters.AddWithValue("$conversationId", conversationId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? new Conversation(
                reader.GetString(0),
                Enum.Parse<ConversationKind>(reader.GetString(1)),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4)),
                DateTimeOffset.Parse(reader.GetString(5)))
            : null;
    }
}
