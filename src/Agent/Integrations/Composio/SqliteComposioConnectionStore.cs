using Agent.Workspaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Agent.Integrations.Composio;

public sealed class SqliteComposioConnectionStore(
    IOptions<SqliteAgentStateOptions> options) : IComposioConnectionStore
{
    private SqliteAgentStateOptions Options { get; } = options.Value;

    public async Task<ComposioConnection?> Get(string toolkit, CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Toolkit, ConnectedAccountId, Status, AccountEmail, UpdatedAt
            FROM ComposioConnections
            WHERE Toolkit = $toolkit;
            """;
        command.Parameters.AddWithValue("$toolkit", toolkit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ComposioConnection(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            DateTimeOffset.Parse(reader.GetString(4)));
    }

    public async Task Save(ComposioConnection connection, CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var sqliteConnection = await Open(cancellationToken);
        await using var command = sqliteConnection.CreateCommand();
        command.CommandText = """
            INSERT INTO ComposioConnections (
                Toolkit, ConnectedAccountId, Status, AccountEmail, UpdatedAt
            )
            VALUES (
                $toolkit, $connectedAccountId, $status, $accountEmail, $updatedAt
            )
            ON CONFLICT(Toolkit) DO UPDATE SET
                ConnectedAccountId = excluded.ConnectedAccountId,
                Status = excluded.Status,
                AccountEmail = excluded.AccountEmail,
                UpdatedAt = excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$toolkit", connection.Toolkit);
        command.Parameters.AddWithValue("$connectedAccountId", connection.ConnectedAccountId);
        command.Parameters.AddWithValue("$status", connection.Status);
        command.Parameters.AddWithValue("$accountEmail", (object?)connection.AccountEmail ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", connection.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task Clear(string toolkit, CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ComposioConnections WHERE Toolkit = $toolkit;";
        command.Parameters.AddWithValue("$toolkit", toolkit);
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

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ComposioConnections (
                Toolkit TEXT PRIMARY KEY,
                ConnectedAccountId TEXT NOT NULL,
                Status TEXT NOT NULL,
                AccountEmail TEXT NULL,
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
