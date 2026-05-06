namespace Agent.Automations;

public interface IAutomationScheduler
{
    DateTimeOffset? GetNextRun(string schedule, DateTimeOffset from);
}
