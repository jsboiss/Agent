using Agent.Workspaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Agent.Automations;

public sealed class SqliteAutomationRunStore(IOptions<SqliteAgentStateOptions> options) : IAutomationRunStore
{
    private SqliteAgentStateOptions Options { get; } = options.Value;

    public async Task<AgentAutomationRun?> Get(string id, CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, AutomationId, SubAgentRunId, Status, TriggerKind, OutputSummary, Error,
                   WorkspaceRootPath, SkillIds, StartedAt, CompletedAt, CreatedAt, UpdatedAt
            FROM AgentAutomationRuns
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? GetRun(reader) : null;
    }

    public async Task<IReadOnlyList<AgentAutomationRun>> List(
        string? automationId,
        int limit,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = string.IsNullOrWhiteSpace(automationId)
            ? """
            SELECT Id, AutomationId, SubAgentRunId, Status, TriggerKind, OutputSummary, Error,
                   WorkspaceRootPath, SkillIds, StartedAt, CompletedAt, CreatedAt, UpdatedAt
            FROM AgentAutomationRuns
            ORDER BY StartedAt DESC
            LIMIT $limit;
            """
            : """
            SELECT Id, AutomationId, SubAgentRunId, Status, TriggerKind, OutputSummary, Error,
                   WorkspaceRootPath, SkillIds, StartedAt, CompletedAt, CreatedAt, UpdatedAt
            FROM AgentAutomationRuns
            WHERE AutomationId = $automationId
            ORDER BY StartedAt DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$automationId", (object?)automationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", Math.Max(1, limit));

        List<AgentAutomationRun> runs = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(GetRun(reader));
        }

        return runs;
    }

    public async Task<AgentAutomationRun?> TryStart(
        AgentAutomation automation,
        AutomationRunTrigger trigger,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        await using var connection = await Open(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = connection.CreateCommand())
        {
            lockCommand.Transaction = (SqliteTransaction)transaction;
            lockCommand.CommandText = """
                SELECT 1
                FROM AgentAutomationRuns
                WHERE AutomationId = $automationId
                  AND Status = $status
                LIMIT 1;
                """;
            lockCommand.Parameters.AddWithValue("$automationId", automation.Id);
            lockCommand.Parameters.AddWithValue("$status", AutomationRunStatus.Running.ToString());

            if (await lockCommand.ExecuteScalarAsync(cancellationToken) is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }
        }

        var now = DateTimeOffset.UtcNow;
        var run = new AgentAutomationRun(
            Guid.NewGuid().ToString("N"),
            automation.Id,
            null,
            AutomationRunStatus.Running,
            trigger,
            null,
            null,
            automation.WorkspaceRootPath,
            automation.SkillIds,
            now,
            null,
            now,
            now);

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = """
                INSERT INTO AgentAutomationRuns (
                    Id, AutomationId, SubAgentRunId, Status, TriggerKind, OutputSummary, Error,
                    WorkspaceRootPath, SkillIds, StartedAt, CompletedAt, CreatedAt, UpdatedAt
                )
                VALUES (
                    $id, $automationId, NULL, $status, $triggerKind, NULL, NULL,
                    $workspaceRootPath, $skillIds, $startedAt, NULL, $createdAt, $updatedAt
                );
                """;
            AddParameters(insert, run);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return run;
    }

    public async Task<AgentAutomationRun> Complete(
        string id,
        AutomationRunStatus status,
        string? subAgentRunId,
        string? outputSummary,
        string? error,
        CancellationToken cancellationToken)
    {
        await EnsureDatabase(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        await using var connection = await Open(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE AgentAutomationRuns
            SET Status = $status,
                SubAgentRunId = $subAgentRunId,
                OutputSummary = $outputSummary,
                Error = $error,
                CompletedAt = $completedAt,
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$subAgentRunId", (object?)subAgentRunId ?? DBNull.Value);
        command.Parameters.AddWithValue("$outputSummary", (object?)outputSummary ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("$completedAt", now.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await Get(id, cancellationToken)
            ?? throw new InvalidOperationException($"Automation run '{id}' was not found.");
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
            CREATE TABLE IF NOT EXISTS AgentAutomationRuns (
                Id TEXT PRIMARY KEY,
                AutomationId TEXT NOT NULL,
                SubAgentRunId TEXT NULL,
                Status TEXT NOT NULL,
                TriggerKind TEXT NOT NULL,
                OutputSummary TEXT NULL,
                Error TEXT NULL,
                WorkspaceRootPath TEXT NULL,
                SkillIds TEXT NULL,
                StartedAt TEXT NOT NULL,
                CompletedAt TEXT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_AgentAutomationRuns_Automation_StartedAt
            ON AgentAutomationRuns (AutomationId, StartedAt);

            CREATE UNIQUE INDEX IF NOT EXISTS IX_AgentAutomationRuns_OneRunning
            ON AgentAutomationRuns (AutomationId)
            WHERE Status = 'Running';
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> Open(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(Options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void AddParameters(SqliteCommand command, AgentAutomationRun run)
    {
        command.Parameters.AddWithValue("$id", run.Id);
        command.Parameters.AddWithValue("$automationId", run.AutomationId);
        command.Parameters.AddWithValue("$status", run.Status.ToString());
        command.Parameters.AddWithValue("$triggerKind", run.Trigger.ToString());
        command.Parameters.AddWithValue("$workspaceRootPath", (object?)run.WorkspaceRootPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$skillIds", (object?)run.SkillIds ?? DBNull.Value);
        command.Parameters.AddWithValue("$startedAt", run.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$createdAt", run.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", run.UpdatedAt.ToString("O"));
    }

    private static AgentAutomationRun GetRun(SqliteDataReader reader)
    {
        return new AgentAutomationRun(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            Enum.Parse<AutomationRunStatus>(reader.GetString(3)),
            Enum.Parse<AutomationRunTrigger>(reader.GetString(4)),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            DateTimeOffset.Parse(reader.GetString(9)),
            reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10)),
            DateTimeOffset.Parse(reader.GetString(11)),
            DateTimeOffset.Parse(reader.GetString(12)));
    }
}
