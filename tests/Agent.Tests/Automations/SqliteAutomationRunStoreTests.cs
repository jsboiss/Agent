using Agent.Automations;
using Agent.SubAgents;
using Agent.Workspaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Xunit;

namespace Agent.Tests.Automations;

public sealed class SqliteAutomationRunStoreTests
{
    [Fact]
    public async Task TryStart_ReturnsNull_WhenAutomationAlreadyRunning()
    {
        var store = CreateStore();
        var automation = GetAutomation();

        var first = await store.TryStart(automation, AutomationRunTrigger.Scheduled, CancellationToken.None);
        var second = await store.TryStart(automation, AutomationRunTrigger.Scheduled, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public async Task Complete_RecordsOutputAndUnlocksAutomation()
    {
        var store = CreateStore();
        var automation = GetAutomation();
        var first = await store.TryStart(automation, AutomationRunTrigger.Manual, CancellationToken.None);

        Assert.NotNull(first);

        await store.Complete(first.Id, AutomationRunStatus.Completed, "run-1", "done", null, CancellationToken.None);
        var second = await store.TryStart(automation, AutomationRunTrigger.Manual, CancellationToken.None);
        var runs = await store.List(automation.Id, 10, CancellationToken.None);

        Assert.NotNull(second);
        Assert.Contains(runs, x => x.OutputSummary == "done" && x.SubAgentRunId == "run-1");
    }

    private static SqliteAutomationRunStore CreateStore()
    {
        var path = Path.Combine(Path.GetTempPath(), "mainagent-tests", Guid.NewGuid().ToString("N"), "agent.db");

        return new SqliteAutomationRunStore(Options.Create(new SqliteAgentStateOptions
        {
            ConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path
            }.ToString()
        }));
    }

    private static AgentAutomation GetAutomation()
    {
        var now = DateTimeOffset.UtcNow;

        return new AgentAutomation(
            "automation-1",
            "Test",
            "search_memory: query=test",
            "every 01:00:00",
            AutomationStatus.Enabled,
            AutomationExecutionMode.Deterministic,
            "main",
            "local-web",
            null,
            SubAgentCapabilities.ReadOnly,
            now.AddHours(1),
            null,
            null,
            null,
            null,
            null,
            now,
            now);
    }
}
