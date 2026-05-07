using Agent.Workspaces;

namespace Agent.ProjectNotes;

public interface IProjectNoteDistiller
{
    Task Distill(
        AgentWorkspace workspace,
        ProjectNoteDistillationRequest request,
        CancellationToken cancellationToken);
}
