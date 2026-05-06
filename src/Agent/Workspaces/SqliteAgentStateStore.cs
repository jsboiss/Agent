using Agent.Conversations;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Agent.Workspaces;

public sealed class SqliteAgentStateStore(IOptions<SqliteAgentStateOptions> options) :
    IAgentWorkspaceStore,
    IAgentRunStore,
    IConversationMirrorStore
{
    private SqliteAgentStateOptions Options { get; } = options.Value;

    public async Task<WorkspaceResolveResult> GetOrCreateActive(
        string defaultRootPath,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        var activeWorkspaceId = await GetStateValue(connection, "ActiveWorkspaceId", cancellationToken);

        if (!string.IsNullOrWhiteSpace(activeWorkspaceId))
        {
            var existingWorkspace = await GetWorkspace(connection, activeWorkspaceId, cancellationToken);

            if (existingWorkspace is not null)
            {
                return new WorkspaceResolveResult(existingWorkspace, false);
            }
        }

        var timestamp = DateTimeOffset.UtcNow;
        var workspace = new AgentWorkspace(
            Guid.NewGuid().ToString("N"),
            GetWorkspaceName(defaultRootPath),
            defaultRootPath,
            null,
            null,
            null,
            false,
            timestamp,
            timestamp);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO AgentWorkspaces (
                    Id, Name, RootPath, ChatThreadId, WorkThreadId, ActiveRunId,
                    RemoteExecutionAllowed, CreatedAt, UpdatedAt
                )
                VALUES (
                    $id, $name, $rootPath, NULL, NULL, NULL,
                    0, $createdAt, $updatedAt
                );
                """;
            command.Parameters.AddWithValue("$id", workspace.Id);
            command.Parameters.AddWithValue("$name", workspace.Name);
            command.Parameters.AddWithValue("$rootPath", workspace.RootPath);
            command.Parameters.AddWithValue("$createdAt", workspace.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("$updatedAt", workspace.UpdatedAt.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await SetStateValue(connection, "ActiveWorkspaceId", workspace.Id, cancellationToken);

        return new WorkspaceResolveResult(workspace, true);
    }

    public async Task<AgentWorkspace> SetActiveRun(
        string workspaceId,
        string? runId,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE AgentWorkspaces
            SET ActiveRunId = $runId,
                UpdatedAt = $updatedAt
            WHERE Id = $workspaceId;
            """;
        command.Parameters.AddWithValue("$runId", (object?)runId ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await GetWorkspace(connection, workspaceId, cancellationToken)
            ?? throw new InvalidOperationException($"Workspace '{workspaceId}' was not found.");
    }

    public async Task<AgentWorkspace> SetThreadId(
        string workspaceId,
        AgentRouteKind routeKind,
        string threadId,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        var column = routeKind == AgentRouteKind.Chat ? "ChatThreadId" : "WorkThreadId";
        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE AgentWorkspaces
            SET {column} = $threadId,
                UpdatedAt = $updatedAt
            WHERE Id = $workspaceId;
            """;
        command.Parameters.AddWithValue("$threadId", threadId);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await GetWorkspace(connection, workspaceId, cancellationToken)
            ?? throw new InvalidOperationException($"Workspace '{workspaceId}' was not found.");
    }

    public async Task<AgentWorkspace> SetRemoteExecutionAllowed(
        string workspaceId,
        bool allowed,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE AgentWorkspaces
            SET RemoteExecutionAllowed = $allowed,
                UpdatedAt = $updatedAt
            WHERE Id = $workspaceId;
            """;
        command.Parameters.AddWithValue("$allowed", allowed ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await GetWorkspace(connection, workspaceId, cancellationToken)
            ?? throw new InvalidOperationException($"Workspace '{workspaceId}' was not found.");
    }

    public async Task<IReadOnlyList<AgentWorkspace>> List(CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, RootPath, ChatThreadId, WorkThreadId, ActiveRunId,
                   RemoteExecutionAllowed, CreatedAt, UpdatedAt
            FROM AgentWorkspaces
            ORDER BY UpdatedAt DESC;
            """;

        List<AgentWorkspace> workspaces = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            workspaces.Add(GetWorkspace(reader));
        }

        return workspaces;
    }

    public async Task<AgentRun> Create(
        string workspaceId,
        string prompt,
        AgentRunKind kind,
        string channel,
        string? parentRunId,
        string? parentCodexThreadId,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        var timestamp = DateTimeOffset.UtcNow;
        var run = new AgentRun(
            Guid.NewGuid().ToString("N"),
            workspaceId,
            prompt,
            null,
            AgentRunStatus.Created,
            kind,
            channel,
            parentRunId,
            parentCodexThreadId,
            timestamp,
            null,
            null,
            null);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AgentRuns (
                Id, WorkspaceId, Prompt, CodexThreadId, Status, Kind, Channel,
                ParentRunId, ParentCodexThreadId, StartedAt, CompletedAt, FinalResponse, Error
            )
            VALUES (
                $id, $workspaceId, $prompt, NULL, $status, $kind, $channel,
                $parentRunId, $parentCodexThreadId, $startedAt, NULL, NULL, NULL
            );
            """;
        command.Parameters.AddWithValue("$id", run.Id);
        command.Parameters.AddWithValue("$workspaceId", run.WorkspaceId);
        command.Parameters.AddWithValue("$prompt", run.Prompt);
        command.Parameters.AddWithValue("$status", run.Status.ToString());
        command.Parameters.AddWithValue("$kind", run.Kind.ToString());
        command.Parameters.AddWithValue("$channel", run.Channel);
        command.Parameters.AddWithValue("$parentRunId", (object?)run.ParentRunId ?? DBNull.Value);
        command.Parameters.AddWithValue("$parentCodexThreadId", (object?)run.ParentCodexThreadId ?? DBNull.Value);
        command.Parameters.AddWithValue("$startedAt", run.StartedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return run;
    }

    public async Task<AgentRun?> Get(string runId, CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        return await GetRun(connection, runId, cancellationToken);
    }

    public async Task<AgentRun?> GetActive(string workspaceId, CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        var workspace = await GetWorkspace(connection, workspaceId, cancellationToken);

        return string.IsNullOrWhiteSpace(workspace?.ActiveRunId)
            ? null
            : await GetRun(connection, workspace.ActiveRunId, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentRun>> List(
        AgentRunKind? kind,
        int limit,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = kind is null
            ? """
            SELECT Id, WorkspaceId, Prompt, CodexThreadId, Status, Kind, Channel,
                   ParentRunId, ParentCodexThreadId, StartedAt, CompletedAt, FinalResponse, Error
            FROM AgentRuns
            ORDER BY StartedAt DESC
            LIMIT $limit;
            """
            : """
            SELECT Id, WorkspaceId, Prompt, CodexThreadId, Status, Kind, Channel,
                   ParentRunId, ParentCodexThreadId, StartedAt, CompletedAt, FinalResponse, Error
            FROM AgentRuns
            WHERE Kind = $kind
            ORDER BY StartedAt DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$kind", kind?.ToString() ?? string.Empty);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 250));

        List<AgentRun> runs = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(GetRun(reader));
        }

        return runs;
    }

    public async Task<int> FailInterruptedSubAgentRuns(CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        var completedAt = DateTimeOffset.UtcNow.ToString("O");
        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE AgentRuns
            SET Status = $status,
                CompletedAt = $completedAt,
                Error = COALESCE(NULLIF(Error, ''), $error)
            WHERE Kind = $kind
              AND Status IN ($createdStatus, $runningStatus);
            """;
        command.Parameters.AddWithValue("$status", AgentRunStatus.Failed.ToString());
        command.Parameters.AddWithValue("$completedAt", completedAt);
        command.Parameters.AddWithValue("$error", "Run was interrupted because the app stopped while the sub-agent was running.");
        command.Parameters.AddWithValue("$kind", AgentRunKind.SubAgent.ToString());
        command.Parameters.AddWithValue("$createdStatus", AgentRunStatus.Created.ToString());
        command.Parameters.AddWithValue("$runningStatus", AgentRunStatus.Running.ToString());

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AgentRun> Update(
        string runId,
        AgentRunStatus status,
        string? codexThreadId,
        string? finalResponse,
        string? error,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        var completedAt = status is AgentRunStatus.Completed or AgentRunStatus.Failed or AgentRunStatus.Cancelled
            ? DateTimeOffset.UtcNow.ToString("O")
            : null;
        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE AgentRuns
            SET Status = $status,
                CodexThreadId = COALESCE($codexThreadId, CodexThreadId),
                CompletedAt = $completedAt,
                FinalResponse = $finalResponse,
                Error = $error
            WHERE Id = $runId;
            """;
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$codexThreadId", (object?)codexThreadId ?? DBNull.Value);
        command.Parameters.AddWithValue("$completedAt", (object?)completedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("$finalResponse", (object?)finalResponse ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("$runId", runId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await GetRun(connection, runId, cancellationToken)
            ?? throw new InvalidOperationException($"Run '{runId}' was not found.");
    }

    public async Task<ConversationMirrorEntry> Add(
        string workspaceId,
        string? runId,
        string codexThreadId,
        string channel,
        ConversationEntryRole role,
        string content,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        var entry = new ConversationMirrorEntry(
            Guid.NewGuid().ToString("N"),
            workspaceId,
            runId,
            codexThreadId,
            channel,
            role,
            content,
            DateTimeOffset.UtcNow);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ConversationMirrors (
                Id, WorkspaceId, RunId, CodexThreadId, Channel, Role, Content, CreatedAt
            )
            VALUES (
                $id, $workspaceId, $runId, $codexThreadId, $channel, $role, $content, $createdAt
            );
            """;
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$workspaceId", entry.WorkspaceId);
        command.Parameters.AddWithValue("$runId", (object?)entry.RunId ?? DBNull.Value);
        command.Parameters.AddWithValue("$codexThreadId", entry.CodexThreadId);
        command.Parameters.AddWithValue("$channel", entry.Channel);
        command.Parameters.AddWithValue("$role", entry.Role.ToString());
        command.Parameters.AddWithValue("$content", entry.Content);
        command.Parameters.AddWithValue("$createdAt", entry.CreatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return entry;
    }

    public async Task<IReadOnlyList<ConversationMirrorEntry>> ListRecent(
        string workspaceId,
        string codexThreadId,
        int limit,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, WorkspaceId, RunId, CodexThreadId, Channel, Role, Content, CreatedAt
            FROM ConversationMirrors
            WHERE WorkspaceId = $workspaceId
              AND CodexThreadId = $codexThreadId
            ORDER BY CreatedAt DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspaceId);
        command.Parameters.AddWithValue("$codexThreadId", codexThreadId);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 100));

        List<ConversationMirrorEntry> entries = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(GetMirrorEntry(reader));
        }

        entries.Reverse();
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
            CREATE TABLE IF NOT EXISTS AgentState (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AgentWorkspaces (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                RootPath TEXT NOT NULL,
                ChatThreadId TEXT NULL,
                WorkThreadId TEXT NULL,
                ActiveRunId TEXT NULL,
                RemoteExecutionAllowed INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AgentRuns (
                Id TEXT PRIMARY KEY,
                WorkspaceId TEXT NOT NULL,
                Prompt TEXT NOT NULL,
                CodexThreadId TEXT NULL,
                Status TEXT NOT NULL,
                Kind TEXT NOT NULL,
                Channel TEXT NOT NULL,
                ParentRunId TEXT NULL,
                ParentCodexThreadId TEXT NULL,
                StartedAt TEXT NOT NULL,
                CompletedAt TEXT NULL,
                FinalResponse TEXT NULL,
                Error TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS ConversationMirrors (
                Id TEXT PRIMARY KEY,
                WorkspaceId TEXT NOT NULL,
                RunId TEXT NULL,
                CodexThreadId TEXT NOT NULL,
                Channel TEXT NOT NULL,
                Role TEXT NOT NULL,
                Content TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_AgentWorkspaces_UpdatedAt ON AgentWorkspaces (UpdatedAt);
            CREATE INDEX IF NOT EXISTS IX_AgentRuns_Workspace_Status ON AgentRuns (WorkspaceId, Status);
            CREATE INDEX IF NOT EXISTS IX_ConversationMirrors_Workspace_Thread ON ConversationMirrors (WorkspaceId, CodexThreadId, CreatedAt);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> Open(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(Options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        return connection;
    }

    private static async Task<string?> GetStateValue(
        SqliteConnection connection,
        string key,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AgentState WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", key);

        var value = await command.ExecuteScalarAsync(cancellationToken);

        return value as string;
    }

    private static async Task SetStateValue(
        SqliteConnection connection,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AgentState (Key, Value)
            VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<AgentWorkspace?> GetWorkspace(
        SqliteConnection connection,
        string workspaceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, RootPath, ChatThreadId, WorkThreadId, ActiveRunId,
                   RemoteExecutionAllowed, CreatedAt, UpdatedAt
            FROM AgentWorkspaces
            WHERE Id = $workspaceId;
            """;
        command.Parameters.AddWithValue("$workspaceId", workspaceId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? GetWorkspace(reader)
            : null;
    }

    private static async Task<AgentRun?> GetRun(
        SqliteConnection connection,
        string runId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, WorkspaceId, Prompt, CodexThreadId, Status, Kind, Channel,
                   ParentRunId, ParentCodexThreadId, StartedAt, CompletedAt, FinalResponse, Error
            FROM AgentRuns
            WHERE Id = $runId;
            """;
        command.Parameters.AddWithValue("$runId", runId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? GetRun(reader)
            : null;
    }

    private static AgentWorkspace GetWorkspace(SqliteDataReader reader)
    {
        return new AgentWorkspace(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetInt32(6) == 1,
            DateTimeOffset.Parse(reader.GetString(7)),
            DateTimeOffset.Parse(reader.GetString(8)));
    }

    private static AgentRun GetRun(SqliteDataReader reader)
    {
        return new AgentRun(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            Enum.Parse<AgentRunStatus>(reader.GetString(4)),
            Enum.Parse<AgentRunKind>(reader.GetString(5)),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            DateTimeOffset.Parse(reader.GetString(9)),
            reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10)),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12));
    }

    private static ConversationMirrorEntry GetMirrorEntry(SqliteDataReader reader)
    {
        return new ConversationMirrorEntry(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            Enum.Parse<ConversationEntryRole>(reader.GetString(5)),
            reader.GetString(6),
            DateTimeOffset.Parse(reader.GetString(7)));
    }

    private static string GetWorkspaceName(string rootPath)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(rootPath));

        return string.IsNullOrWhiteSpace(name) ? "AgentHome" : name;
    }
}
