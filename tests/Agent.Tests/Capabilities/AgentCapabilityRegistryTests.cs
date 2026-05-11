using Agent.Capabilities;
using Agent.SubAgents;
using Xunit;

namespace Agent.Tests.Capabilities;

public sealed class AgentCapabilityRegistryTests
{
    [Fact]
    public void GetToolDefinitions_UsesToolsetsForSessionRecallAndMemory()
    {
        var registry = new AgentCapabilityRegistry();

        var tools = registry.GetToolDefinitions(SubAgentCapabilities.ReadOnly);

        Assert.Contains(tools, x => x.Name == "search_memory");
        Assert.Contains(tools, x => x.Name == "write_memory");
        Assert.Contains(tools, x => x.Name == "search_conversations");
        Assert.Contains(tools, x => x.Name == "automation");
        Assert.DoesNotContain(tools, x => x.Name == "create_automation");
    }

    [Fact]
    public void GetToolDefinitionsForToolsets_FiltersCalendarWhenNotRequested()
    {
        var registry = new AgentCapabilityRegistry();

        var tools = registry.GetToolDefinitionsForToolsets(
            SubAgentCapabilities.None,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "safe", "memory" });

        Assert.Contains(tools, x => x.Name == "search_memory");
        Assert.DoesNotContain(tools, x => x.Name == "calendar_list_events");
    }

    [Fact]
    public void GetToolDefinitionsForProfile_BlocksAutomationCreationInAutomationRun()
    {
        var registry = new AgentCapabilityRegistry();

        var tools = registry.GetToolDefinitionsForProfile(
            SubAgentCapabilities.CalendarRead,
            ToolsetProfile.AutomationRun);

        Assert.Contains(tools, x => x.Name == "calendar_list_events");
        Assert.DoesNotContain(tools, x => x.Name == "automation");
        Assert.DoesNotContain(tools, x => x.Name == "create_automation");
    }
}
