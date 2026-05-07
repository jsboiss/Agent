using Agent.Workspaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Agent.Calendar;

public sealed class SqliteGoogleCalendarAuthStore(
    IOptions<SqliteAgentStateOptions> options,
    IDataProtectionProvider dataProtectionProvider) : IGoogleCalendarAuthStore
{
    private SqliteAgentStateOptions Options { get; } = options.Value;

    private IDataProtector Protector { get; } = dataProtectionProvider.CreateProtector("MainAgent.GoogleCalendarToken.v1");

    public async Task<GoogleCalendarToken?> Get(CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT AccessToken, RefreshToken, ExpiresAt, AccountEmail, UpdatedAt
            FROM GoogleCalendarTokens
            WHERE Id = 'primary';
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new GoogleCalendarToken(
            Unprotect(reader.GetString(0)),
            reader.IsDBNull(1) ? null : Unprotect(reader.GetString(1)),
            DateTimeOffset.Parse(reader.GetString(2)),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            DateTimeOffset.Parse(reader.GetString(4)));
    }

    public async Task Save(GoogleCalendarToken token, CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO GoogleCalendarTokens (
                Id, AccessToken, RefreshToken, ExpiresAt, AccountEmail, UpdatedAt
            )
            VALUES (
                'primary', $accessToken, $refreshToken, $expiresAt, $accountEmail, $updatedAt
            )
            ON CONFLICT(Id) DO UPDATE SET
                AccessToken = excluded.AccessToken,
                RefreshToken = COALESCE(excluded.RefreshToken, GoogleCalendarTokens.RefreshToken),
                ExpiresAt = excluded.ExpiresAt,
                AccountEmail = excluded.AccountEmail,
                UpdatedAt = excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$accessToken", Protect(token.AccessToken));
        command.Parameters.AddWithValue("$refreshToken", string.IsNullOrWhiteSpace(token.RefreshToken) ? DBNull.Value : Protect(token.RefreshToken));
        command.Parameters.AddWithValue("$expiresAt", token.ExpiresAt.ToString("O"));
        command.Parameters.AddWithValue("$accountEmail", (object?)token.AccountEmail ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", token.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task Clear(CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM GoogleCalendarTokens WHERE Id = 'primary';";
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
            CREATE TABLE IF NOT EXISTS GoogleCalendarTokens (
                Id TEXT PRIMARY KEY,
                AccessToken TEXT NOT NULL,
                RefreshToken TEXT NULL,
                ExpiresAt TEXT NOT NULL,
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

    private string Protect(string value)
    {
        return Protector.Protect(value);
    }

    private string Unprotect(string value)
    {
        return Protector.Unprotect(value);
    }
}
