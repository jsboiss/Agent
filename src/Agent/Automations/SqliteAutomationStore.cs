using Agent.SubAgents;
using Agent.Workspaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Agent.Automations;

public sealed class SqliteAutomationStore(
    IOptions<SqliteAgentStateOptions> options,
    IAutomationScheduler scheduler) : IAutomationStore
{
    private SqliteAgentStateOptions Options { get; } = options.Value;

    public async Task<AgentAutomation> Create(
        AutomationWriteRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        var timestamp = DateTimeOffset.UtcNow;
        var automation = new AgentAutomation(
            Guid.NewGuid().ToString("N"),
            request.Name,
            request.Task,
            request.Schedule,
            AutomationStatus.Enabled,
            request.Mode,
            request.ConversationId,
            request.Channel,
            request.NotificationTarget,
            request.Capabilities,
            scheduler.GetNextRun(request.Schedule, timestamp),
            null,
            null,
            null,
            request.WorkspaceRootPath,
            request.SkillIds,
            timestamp,
            timestamp);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AgentAutomations (
                Id, Name, Task, Schedule, Status, Mode, ConversationId, Channel, NotificationTarget,
                Capabilities, NextRunAt, LastRunAt, LastRunId, LastResult, WorkspaceRootPath, SkillIds, CreatedAt, UpdatedAt
            )
            VALUES (
                $id, $name, $task, $schedule, $status, $mode, $conversationId, $channel, $notificationTarget,
                $capabilities, $nextRunAt, NULL, NULL, NULL, $workspaceRootPath, $skillIds, $createdAt, $updatedAt
            );
            """;
        AddParameters(command, automation);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return automation;
    }

    public async Task<AgentAutomation?> Get(string id, CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Task, Schedule, Status, Mode, ConversationId, Channel, NotificationTarget,
                   Capabilities, NextRunAt, LastRunAt, LastRunId, LastResult, WorkspaceRootPath, SkillIds, CreatedAt, UpdatedAt
            FROM AgentAutomations
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? GetAutomation(reader) : null;
    }

    public async Task<IReadOnlyList<AgentAutomation>> List(CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Task, Schedule, Status, Mode, ConversationId, Channel, NotificationTarget,
                   Capabilities, NextRunAt, LastRunAt, LastRunId, LastResult, WorkspaceRootPath, SkillIds, CreatedAt, UpdatedAt
            FROM AgentAutomations
            ORDER BY CreatedAt DESC;
            """;

        List<AgentAutomation> automations = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            automations.Add(GetAutomation(reader));
        }

        return automations;
    }

    public async Task<AgentAutomation> SetStatus(
        string id,
        AutomationStatus status,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var existing = await Get(id, cancellationToken)
            ?? throw new InvalidOperationException($"Automation '{id}' was not found.");
        var nextRunAt = status == AutomationStatus.Enabled
            ? scheduler.GetNextRun(existing.Schedule, now)
            : null;
        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE AgentAutomations
            SET Status = $status,
                NextRunAt = $nextRunAt,
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$nextRunAt", (object?)nextRunAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await Get(id, cancellationToken)
            ?? throw new InvalidOperationException($"Automation '{id}' was not found.");
    }

    public async Task<AgentAutomation> UpdateRunResult(
        string id,
        DateTimeOffset? nextRunAt,
        string? runId,
        string? result,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE AgentAutomations
            SET LastRunAt = $lastRunAt,
                LastRunId = $lastRunId,
                LastResult = $lastResult,
                NextRunAt = $nextRunAt,
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$lastRunAt", now.ToString("O"));
        command.Parameters.AddWithValue("$lastRunId", (object?)runId ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastResult", (object?)result ?? DBNull.Value);
        command.Parameters.AddWithValue("$nextRunAt", (object?)nextRunAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await Get(id, cancellationToken)
            ?? throw new InvalidOperationException($"Automation '{id}' was not found.");
    }

    public async Task<AgentAutomation> Update(
        string id,
        AutomationWriteRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        var existing = await Get(id, cancellationToken)
            ?? throw new InvalidOperationException($"Automation '{id}' was not found.");
        var now = DateTimeOffset.UtcNow;
        var nextRunAt = existing.Status == AutomationStatus.Enabled
            ? scheduler.GetNextRun(request.Schedule, now)
            : null;

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE AgentAutomations
            SET Name = $name,
                Task = $task,
                Schedule = $schedule,
                Mode = $mode,
                ConversationId = $conversationId,
                Channel = $channel,
                NotificationTarget = $notificationTarget,
                Capabilities = $capabilities,
                WorkspaceRootPath = $workspaceRootPath,
                SkillIds = $skillIds,
                NextRunAt = $nextRunAt,
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", request.Name);
        command.Parameters.AddWithValue("$task", request.Task);
        command.Parameters.AddWithValue("$schedule", request.Schedule);
        command.Parameters.AddWithValue("$mode", request.Mode.ToString());
        command.Parameters.AddWithValue("$conversationId", request.ConversationId);
        command.Parameters.AddWithValue("$channel", request.Channel);
        command.Parameters.AddWithValue("$notificationTarget", (object?)request.NotificationTarget ?? DBNull.Value);
        command.Parameters.AddWithValue("$capabilities", (int)request.Capabilities);
        command.Parameters.AddWithValue("$workspaceRootPath", (object?)request.WorkspaceRootPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$skillIds", (object?)request.SkillIds ?? DBNull.Value);
        command.Parameters.AddWithValue("$nextRunAt", (object?)nextRunAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await Get(id, cancellationToken)
            ?? throw new InvalidOperationException($"Automation '{id}' was not found.");
    }

    public async Task Delete(string id, CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM AgentAutomations WHERE Id = $id;";
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

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS AgentAutomations (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Task TEXT NOT NULL,
                Schedule TEXT NOT NULL,
                Status TEXT NOT NULL,
                Mode TEXT NOT NULL DEFAULT 'Agent',
                ConversationId TEXT NOT NULL,
                Channel TEXT NOT NULL,
                NotificationTarget TEXT NULL,
                Capabilities INTEGER NOT NULL,
                NextRunAt TEXT NULL,
                LastRunAt TEXT NULL,
                LastRunId TEXT NULL,
                LastResult TEXT NULL,
                WorkspaceRootPath TEXT NULL,
                SkillIds TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_AgentAutomations_Status_NextRunAt ON AgentAutomations (Status, NextRunAt);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureColumn(connection, "Mode", "TEXT NOT NULL DEFAULT 'Agent'", cancellationToken);
        await EnsureColumn(connection, "WorkspaceRootPath", cancellationToken);
        await EnsureColumn(connection, "SkillIds", cancellationToken);
    }

    private static Task EnsureColumn(
        SqliteConnection connection,
        string column,
        CancellationToken cancellationToken)
    {
        return EnsureColumn(connection, column, "TEXT NULL", cancellationToken);
    }

    private static async Task EnsureColumn(
        SqliteConnection connection,
        string column,
        string definition,
        CancellationToken cancellationToken)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA table_info(AgentAutomations);";
        await using var reader = await check.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE AgentAutomations ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> Open(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(Options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void AddParameters(SqliteCommand command, AgentAutomation automation)
    {
        command.Parameters.AddWithValue("$id", automation.Id);
        command.Parameters.AddWithValue("$name", automation.Name);
        command.Parameters.AddWithValue("$task", automation.Task);
        command.Parameters.AddWithValue("$schedule", automation.Schedule);
        command.Parameters.AddWithValue("$status", automation.Status.ToString());
        command.Parameters.AddWithValue("$mode", automation.Mode.ToString());
        command.Parameters.AddWithValue("$conversationId", automation.ConversationId);
        command.Parameters.AddWithValue("$channel", automation.Channel);
        command.Parameters.AddWithValue("$notificationTarget", (object?)automation.NotificationTarget ?? DBNull.Value);
        command.Parameters.AddWithValue("$capabilities", (int)automation.Capabilities);
        command.Parameters.AddWithValue("$nextRunAt", (object?)automation.NextRunAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$workspaceRootPath", (object?)automation.WorkspaceRootPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$skillIds", (object?)automation.SkillIds ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", automation.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", automation.UpdatedAt.ToString("O"));
    }

    private static AgentAutomation GetAutomation(SqliteDataReader reader)
    {
        return new AgentAutomation(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            Enum.Parse<AutomationStatus>(reader.GetString(4)),
            Enum.Parse<AutomationExecutionMode>(reader.GetString(5)),
            reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            (SubAgentCapabilities)reader.GetInt32(9),
            reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10)),
            reader.IsDBNull(11) ? null : DateTimeOffset.Parse(reader.GetString(11)),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.IsDBNull(15) ? null : reader.GetString(15),
            DateTimeOffset.Parse(reader.GetString(16)),
            DateTimeOffset.Parse(reader.GetString(17)));
    }
}
