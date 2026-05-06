namespace Agent.Drafts;

public interface IAgentDraftStore
{
    Task<AgentDraft> Create(DraftWriteRequest request, CancellationToken cancellationToken);

    Task<AgentDraft?> Get(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentDraft>> List(DraftStatus? status, int limit, CancellationToken cancellationToken);

    Task<AgentDraft> UpdateStatus(string id, DraftStatus status, CancellationToken cancellationToken);
}
