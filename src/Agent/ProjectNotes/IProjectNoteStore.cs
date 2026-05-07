using Agent.Workspaces;

namespace Agent.ProjectNotes;

public interface IProjectNoteStore
{
    Task<ProjectNoteContext> Load(AgentWorkspace workspace, CancellationToken cancellationToken);

    Task RecordActivity(
        AgentWorkspace workspace,
        string source,
        string content,
        CancellationToken cancellationToken);
}
