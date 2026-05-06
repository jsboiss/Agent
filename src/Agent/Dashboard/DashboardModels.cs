using Agent.Events;
using Agent.Memory;
using Agent.Memory.MemoryGraph;

namespace Agent.Dashboard;

public sealed record ChatDashboardSnapshot(
    string ConversationId,
    IReadOnlyList<ChatDashboardMessage> Messages,
    IReadOnlyList<RunEventRow> RecentEvents,
    IReadOnlyList<MemoryRow> InjectedMemories,
    IReadOnlyList<string> ActiveTools,
    string Provider,
    string Model,
    bool IsRunning,
    string? QueuedPrompt,
    WorkspaceStatus? Workspace,
    TokenUsageSummary Tokens);

public sealed record WorkspaceStatus(
    string Id,
    string Name,
    string RootPath,
    string? ChatThreadId,
    string? WorkThreadId,
    string? ActiveRunId,
    bool RemoteExecutionAllowed,
    string? ActiveRunStatus,
    string? ActiveRunKind);

public sealed record ChatDashboardMessage(
    string Id,
    string Role,
    string Content,
    string HtmlContent,
    DateTimeOffset CreatedAt);

public sealed record SendChatMessageRequest(string Prompt);

public sealed record SendChatMessageResponse(
    ChatDashboardSnapshot Snapshot,
    string? ErrorMessage);

public sealed record DebugTranscriptExport(
    string Path,
    string Content);

public sealed record MemorySearchFilter(
    string Query,
    string Lifecycle,
    string Segment,
    string Tier);

public sealed record MemoryWriteDto(
    string Text,
    string Tier,
    string Segment,
    double Importance,
    double Confidence);

public sealed record MemoryLifecycleUpdateDto(string Lifecycle);

public sealed record MemoryWorkspaceSnapshot(
    IReadOnlyList<MemoryRow> Memories,
    IReadOnlyList<string> Lifecycles,
    IReadOnlyList<string> Segments,
    IReadOnlyList<string> Tiers);

public sealed record MemoryRow(
    string Id,
    string Text,
    string Tier,
    string Segment,
    string Lifecycle,
    double Importance,
    double Confidence,
    int AccessCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastAccessedAt,
    string? SourceMessageId,
    string? Supersedes,
    bool HasDuplicateText);

public sealed record RunTimelineSnapshot(
    string? ConversationId,
    IReadOnlyList<RunTurnGroup> Turns,
    IReadOnlyList<RunEventRow> Events);

public sealed record SubAgentRunsSnapshot(
    IReadOnlyList<SubAgentRunRow> Runs,
    TokenUsageSummary Tokens);

public sealed record SubAgentRunRow(
    string Id,
    string WorkspaceId,
    string Status,
    string Kind,
    string Channel,
    string Prompt,
    string? CodexThreadId,
    string? ParentRunId,
    string? ParentCodexThreadId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? FinalResponse,
    string? Error,
    TokenUsageSummary Tokens);

public sealed record TokenUsageSummary(
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    int MainContextTokens,
    int ContextWindowTokens,
    int RemainingContextTokens,
    int CompactionThresholdTokens,
    int RemainingUntilCompactionTokens,
    string Source);

public sealed record RunTurnGroup(
    string Title,
    DateTimeOffset StartedAt,
    IReadOnlyList<RunEventRow> Events);

public sealed record RunEventRow(
    string Id,
    string Kind,
    string Phase,
    string ConversationId,
    DateTimeOffset CreatedAt,
    string Summary,
    IReadOnlyDictionary<string, string> Metadata,
    bool IsError);

public sealed record MemoryGraphSnapshot(
    IReadOnlyList<MemoryGraphNode> Nodes,
    IReadOnlyList<MemoryGraphEdge> Edges,
    string EmptyReason);

public sealed record SettingsDashboardSnapshot(
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<string> AppliedLayers,
    string MemoryConnectionString,
    WorkspaceStatus Workspace);

public sealed record WorkspacePermissionUpdateDto(bool RemoteExecutionAllowed);

public sealed record ManualCompactionResponse(
    string ConversationId,
    string? ThroughEntryId,
    int ExactEntryCount,
    int NewlyCompactedEntryCount,
    int MemoryExtractionEntryCount,
    int ProposedMemoryCount,
    int WrittenMemoryCount,
    int SkippedMemoryCount,
    DateTimeOffset UpdatedAt);

public sealed record TelegramStatusResponse(
    bool Enabled,
    int TrustedChatCount);

public sealed record RunActionResponse(
    string RunId,
    string Status,
    string Message);

public sealed record DraftRow(
    string Id,
    string Kind,
    string Summary,
    string Payload,
    string? SourceRunId,
    string ConversationId,
    string Channel,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AutomationRow(
    string Id,
    string Name,
    string Task,
    string Schedule,
    string Status,
    string ConversationId,
    string Channel,
    string? NotificationTarget,
    string Capabilities,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastRunAt,
    string? LastRunId,
    string? LastResult);

public sealed record AutomationCreateDto(
    string Name,
    string Task,
    string Schedule,
    string Channel,
    string? ConversationId,
    string? NotificationTarget,
    string? Capabilities);

public sealed record AutomationToggleDto(bool Enabled);

public sealed record MemoryMaintenanceResponse(
    int Scanned,
    int Archived,
    int Pruned,
    int Merged,
    int Superseded,
    string Summary);

public interface IChatDashboardService
{
    Task<ChatDashboardSnapshot> LoadMain(CancellationToken cancellationToken);

    Task<SendChatMessageResponse> SendPrompt(
        SendChatMessageRequest request,
        CancellationToken cancellationToken);

    Task StreamPrompt(
        SendChatMessageRequest request,
        Stream responseStream,
        CancellationToken cancellationToken);

    Task<DebugTranscriptExport> ExportMainTranscript(CancellationToken cancellationToken);
}

public interface IMemoryDashboardService
{
    Task<MemoryWorkspaceSnapshot> Search(
        MemorySearchFilter filter,
        CancellationToken cancellationToken);

    Task<MemoryRow> Write(
        MemoryWriteDto request,
        CancellationToken cancellationToken);

    Task<MemoryRow> UpdateLifecycle(
        string id,
        MemoryLifecycleUpdateDto request,
        CancellationToken cancellationToken);

    Task Delete(string id, CancellationToken cancellationToken);
}

public interface IRunTimelineService
{
    Task<RunTimelineSnapshot> List(
        string? conversationId,
        string filter,
        CancellationToken cancellationToken);
}

public interface ISubAgentDashboardService
{
    Task<SubAgentRunsSnapshot> List(CancellationToken cancellationToken);
}

public interface IMemoryGraphService
{
    Task<MemoryGraphSnapshot> Build(CancellationToken cancellationToken);
}

public interface ISettingsDashboardService
{
    Task<SettingsDashboardSnapshot> Load(CancellationToken cancellationToken);

    Task<WorkspaceStatus> UpdateWorkspacePermissions(
        WorkspacePermissionUpdateDto request,
        CancellationToken cancellationToken);
}

public interface ICompactionDashboardService
{
    Task<ManualCompactionResponse> CompactMain(CancellationToken cancellationToken);
}

public interface IOperationsDashboardService
{
    TelegramStatusResponse GetTelegramStatus();

    Task<RunActionResponse> CancelRun(string runId, CancellationToken cancellationToken);

    Task<RunActionResponse> RetryRun(string runId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DraftRow>> ListDrafts(string? status, CancellationToken cancellationToken);

    Task<DraftRow> ApproveDraft(string id, CancellationToken cancellationToken);

    Task<DraftRow> RejectDraft(string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<AutomationRow>> ListAutomations(CancellationToken cancellationToken);

    Task<AutomationRow> CreateAutomation(AutomationCreateDto request, CancellationToken cancellationToken);

    Task<AutomationRow> ToggleAutomation(string id, AutomationToggleDto request, CancellationToken cancellationToken);

    Task DeleteAutomation(string id, CancellationToken cancellationToken);

    Task<MemoryMaintenanceResponse> CleanupMemory(CancellationToken cancellationToken);

    Task<MemoryMaintenanceResponse> ConsolidateMemory(CancellationToken cancellationToken);
}
