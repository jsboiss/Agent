using Agent.Context;
using Agent.Settings;
using Microsoft.Extensions.Options;
using Xunit;

namespace Agent.Tests.Context;

public sealed class RuleBasedContextPlannerTests
{
    [Fact]
    public async Task Plan_SelectsEmail_ForInvoiceQuestion()
    {
        var planner = CreatePlanner();

        var plan = await planner.Plan(
            new ContextPlanningRequest(
                "main",
                "local-web",
                "Did I get an email from Stripe about an invoice?",
                GetSettings(),
                new DateTimeOffset(2026, 5, 9, 10, 0, 0, TimeSpan.FromHours(10))),
            CancellationToken.None);

        Assert.True(plan.NeedsContext);
        Assert.Contains(plan.Providers, x => string.Equals(x.ProviderId, "email", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Plan_SelectsCalendar_ForProactiveTemporalPlanning()
    {
        var planner = CreatePlanner();

        var plan = await planner.Plan(
            new ContextPlanningRequest(
                "main",
                "local-web",
                "Can I fit this in tomorrow afternoon?",
                GetSettings(),
                new DateTimeOffset(2026, 5, 9, 10, 0, 0, TimeSpan.FromHours(10))),
            CancellationToken.None);

        var provider = Assert.Single(plan.Providers, x => string.Equals(x.ProviderId, "calendar", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("tomorrow", provider.DateWindowLabel);
        Assert.NotNull(provider.Start);
        Assert.NotNull(provider.End);
    }

    [Fact]
    public async Task Plan_DoesNotSelectCalendarOrEmail_ForCodeFence()
    {
        var planner = CreatePlanner();

        var plan = await planner.Plan(
            new ContextPlanningRequest(
                "main",
                "local-web",
                "Update this code:\n```csharp\nvar email = input;\n```",
                GetSettings(),
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.False(plan.NeedsContext);
        Assert.Empty(plan.Providers);
    }

    private static RuleBasedContextPlanner CreatePlanner()
    {
        return new RuleBasedContextPlanner(Options.Create(new ContextPlannerOptions
        {
            EnabledProviders = ["Memory", "Calendar", "Email"]
        }));
    }

    private static AgentSettings GetSettings()
    {
        return new AgentSettings(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            []);
    }
}
