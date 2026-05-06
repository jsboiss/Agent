using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Agent.Memory;

public sealed class SqliteMemoryStore(IOptions<SqliteMemoryOptions> options) : IMemoryStore
{
    private SqliteMemoryOptions Options { get; } = options.Value;

    public async Task<MemoryRecord?> Get(string id, CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = new SqliteConnection(Options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Text, Tier, Segment, Lifecycle, Importance, Confidence, AccessCount,
                   CreatedAt, UpdatedAt, LastAccessedAt, SourceMessageId, Supersedes, EmbeddingReference
            FROM Memories
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? GetMemoryRecord(reader)
            : null;
    }

    public async Task<IReadOnlyList<MemoryRecord>> Search(
        MemorySearchRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = new SqliteConnection(Options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var limit = request.Limit <= 0 ? 5 : request.Limit;
        var lifecycles = request.IncludedLifecycles.Count == 0
            ? new HashSet<MemoryLifecycle> { MemoryLifecycle.Active }
            : request.IncludedLifecycles;

        await using var command = connection.CreateCommand();
        var ftsQuery = GetFtsQuery(request.Query);
        command.CommandText = string.IsNullOrWhiteSpace(ftsQuery)
            ? """
            SELECT Id, Text, Tier, Segment, Lifecycle, Importance, Confidence, AccessCount,
                   CreatedAt, UpdatedAt, LastAccessedAt, SourceMessageId, Supersedes, EmbeddingReference
            FROM Memories
            WHERE Lifecycle IN (SELECT value FROM json_each($lifecycles))
            ORDER BY Importance DESC, Confidence DESC, UpdatedAt DESC
            LIMIT $limit;
            """
            : """
            SELECT m.Id, m.Text, m.Tier, m.Segment, m.Lifecycle, m.Importance, m.Confidence, m.AccessCount,
                   m.CreatedAt, m.UpdatedAt, m.LastAccessedAt, m.SourceMessageId, m.Supersedes, m.EmbeddingReference
            FROM MemoriesFts f
            JOIN Memories m ON m.Id = f.Id
            WHERE MemoriesFts MATCH $query
              AND m.Lifecycle IN (SELECT value FROM json_each($lifecycles))
            ORDER BY bm25(MemoriesFts), m.Importance DESC, m.Confidence DESC, m.UpdatedAt DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$query", ftsQuery);
        command.Parameters.AddWithValue("$lifecycles", GetJsonArray(lifecycles.Select(x => x.ToString()).ToArray()));
        command.Parameters.AddWithValue("$limit", limit);

        List<MemoryRecord> memories = [];
        List<string> ids = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var memory = GetMemoryRecord(reader);
            memories.Add(memory);
            ids.Add(memory.Id);
        }

        await UpdateAccessMetadata(connection, ids, cancellationToken);

        return memories;
    }

    public async Task<MemoryRecord> Write(
        MemoryWriteRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        var timestamp = DateTimeOffset.UtcNow;
        var memory = new MemoryRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Text = request.Text,
            Tier = request.Tier,
            Segment = request.Segment,
            Lifecycle = MemoryLifecycle.Active,
            Importance = request.Importance,
            Confidence = request.Confidence,
            AccessCount = 0,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
            SourceMessageId = request.SourceMessageId
        };

        await using var connection = new SqliteConnection(Options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Memories (
                Id, Text, Tier, Segment, Lifecycle, Importance, Confidence, AccessCount,
                CreatedAt, UpdatedAt, LastAccessedAt, SourceMessageId, Supersedes, EmbeddingReference
            )
            VALUES (
                $id, $text, $tier, $segment, $lifecycle, $importance, $confidence, $accessCount,
                $createdAt, $updatedAt, NULL, $sourceMessageId, NULL, NULL
            );
            """;
        command.Parameters.AddWithValue("$id", memory.Id);
        command.Parameters.AddWithValue("$text", memory.Text);
        command.Parameters.AddWithValue("$tier", memory.Tier.ToString());
        command.Parameters.AddWithValue("$segment", memory.Segment.ToString());
        command.Parameters.AddWithValue("$lifecycle", memory.Lifecycle.ToString());
        command.Parameters.AddWithValue("$importance", memory.Importance);
        command.Parameters.AddWithValue("$confidence", memory.Confidence);
        command.Parameters.AddWithValue("$accessCount", memory.AccessCount);
        command.Parameters.AddWithValue("$createdAt", memory.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", memory.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$sourceMessageId", (object?)memory.SourceMessageId ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);

        return memory;
    }

    public async Task<MemoryRecord> UpdateLifecycle(
        string id,
        MemoryLifecycle lifecycle,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = new SqliteConnection(Options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Memories
            SET Lifecycle = $lifecycle,
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$lifecycle", lifecycle.ToString());
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));

        var updatedCount = await command.ExecuteNonQueryAsync(cancellationToken);

        if (updatedCount == 0)
        {
            throw new InvalidOperationException($"Memory '{id}' was not found.");
        }

        return await Get(id, cancellationToken)
            ?? throw new InvalidOperationException($"Memory '{id}' was not found after update.");
    }

    public async Task<MemoryRecord> Update(
        string id,
        string text,
        MemoryTier tier,
        MemorySegment segment,
        double importance,
        double confidence,
        string? supersedes,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = new SqliteConnection(Options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Memories
            SET Text = $text,
                Tier = $tier,
                Segment = $segment,
                Importance = $importance,
                Confidence = $confidence,
                Supersedes = $supersedes,
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$tier", tier.ToString());
        command.Parameters.AddWithValue("$segment", segment.ToString());
        command.Parameters.AddWithValue("$importance", importance);
        command.Parameters.AddWithValue("$confidence", confidence);
        command.Parameters.AddWithValue("$supersedes", (object?)supersedes ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));

        var updatedCount = await command.ExecuteNonQueryAsync(cancellationToken);

        if (updatedCount == 0)
        {
            throw new InvalidOperationException($"Memory '{id}' was not found.");
        }

        return await Get(id, cancellationToken)
            ?? throw new InvalidOperationException($"Memory '{id}' was not found after update.");
    }

    public async Task Delete(string id, CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = new SqliteConnection(Options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM Memories
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        await command.ExecuteNonQueryAsync(cancellationToken);
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

        await using var connection = new SqliteConnection(Options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Memories (
                Id TEXT PRIMARY KEY,
                Text TEXT NOT NULL,
                Tier TEXT NOT NULL,
                Segment TEXT NOT NULL,
                Lifecycle TEXT NOT NULL,
                Importance REAL NOT NULL,
                Confidence REAL NOT NULL,
                AccessCount INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                LastAccessedAt TEXT NULL,
                SourceMessageId TEXT NULL,
                Supersedes TEXT NULL,
                EmbeddingReference TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Memories_Lifecycle ON Memories (Lifecycle);
            CREATE INDEX IF NOT EXISTS IX_Memories_UpdatedAt ON Memories (UpdatedAt);

            CREATE VIRTUAL TABLE IF NOT EXISTS MemoriesFts USING fts5(
                Id UNINDEXED,
                Text,
                Tier UNINDEXED,
                Segment UNINDEXED,
                content='Memories',
                content_rowid='rowid'
            );

            CREATE TRIGGER IF NOT EXISTS Memories_AfterInsert_Fts
            AFTER INSERT ON Memories
            BEGIN
                INSERT INTO MemoriesFts(rowid, Id, Text, Tier, Segment)
                VALUES (new.rowid, new.Id, new.Text, new.Tier, new.Segment);
            END;

            CREATE TRIGGER IF NOT EXISTS Memories_AfterUpdate_Fts
            AFTER UPDATE ON Memories
            BEGIN
                INSERT INTO MemoriesFts(MemoriesFts, rowid, Id, Text, Tier, Segment)
                VALUES ('delete', old.rowid, old.Id, old.Text, old.Tier, old.Segment);
                INSERT INTO MemoriesFts(rowid, Id, Text, Tier, Segment)
                VALUES (new.rowid, new.Id, new.Text, new.Tier, new.Segment);
            END;

            CREATE TRIGGER IF NOT EXISTS Memories_AfterDelete_Fts
            AFTER DELETE ON Memories
            BEGIN
                INSERT INTO MemoriesFts(MemoriesFts, rowid, Id, Text, Tier, Segment)
                VALUES ('delete', old.rowid, old.Id, old.Text, old.Tier, old.Segment);
            END;

            INSERT INTO MemoriesFts(MemoriesFts) VALUES ('rebuild');
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static MemoryRecord GetMemoryRecord(SqliteDataReader reader)
    {
        return new MemoryRecord
        {
            Id = reader.GetString(0),
            Text = reader.GetString(1),
            Tier = Enum.Parse<MemoryTier>(reader.GetString(2)),
            Segment = Enum.Parse<MemorySegment>(reader.GetString(3)),
            Lifecycle = Enum.Parse<MemoryLifecycle>(reader.GetString(4)),
            Importance = reader.GetDouble(5),
            Confidence = reader.GetDouble(6),
            AccessCount = reader.GetInt32(7),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(8)),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(9)),
            LastAccessedAt = reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10)),
            SourceMessageId = reader.IsDBNull(11) ? null : reader.GetString(11),
            Supersedes = reader.IsDBNull(12) ? null : reader.GetString(12),
            EmbeddingReference = reader.IsDBNull(13) ? null : reader.GetString(13)
        };
    }

    private static async Task UpdateAccessMetadata(
        SqliteConnection connection,
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Memories
            SET AccessCount = AccessCount + 1,
                LastAccessedAt = $lastAccessedAt
            WHERE Id IN (SELECT value FROM json_each($ids));
            """;
        command.Parameters.AddWithValue("$lastAccessedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$ids", GetJsonArray(ids));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string GetJsonArray(IEnumerable<string> values)
    {
        return JsonSerializer.Serialize(values);
    }

    private static string GetFtsQuery(string query)
    {
        var terms = query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim('"', '\'', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}'))
            .Where(x => x.Length >= 3 && !MemorySearchStopWords.Contains(x))
            .Select(x => $"\"{x.Replace("\"", "\"\"")}\"")
            .ToArray();

        return terms.Length == 0
            ? string.Empty
            : string.Join(" OR ", terms);
    }

    private static ISet<string> MemorySearchStopWords { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "about",
        "after",
        "again",
        "also",
        "and",
        "are",
        "because",
        "been",
        "before",
        "being",
        "between",
        "can",
        "could",
        "does",
        "doing",
        "for",
        "explain",
        "from",
        "give",
        "going",
        "had",
        "has",
        "have",
        "help",
        "how",
        "into",
        "just",
        "know",
        "like",
        "make",
        "more",
        "need",
        "purpose",
        "should",
        "show",
        "some",
        "tell",
        "than",
        "that",
        "their",
        "there",
        "these",
        "they",
        "thing",
        "this",
        "those",
        "what",
        "when",
        "where",
        "which",
        "while",
        "who",
        "why",
        "with",
        "would",
        "was",
        "were",
        "yeah",
        "you",
        "your"
    };
}
