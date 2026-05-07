namespace Agent.Workspaces;

public enum AgentRunStatus
{
    Created,
    Running,
    WaitingForConfirmation,
    Completed,
    Failed,
    Cancelled
}
