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
    string? QueuedPrompt);

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
    string MemoryConnectionString);

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

public interface IMemoryGraphService
{
    Task<MemoryGraphSnapshot> Build(CancellationToken cancellationToken);
}

public interface ISettingsDashboardService
{
    Task<SettingsDashboardSnapshot> Load(CancellationToken cancellationToken);
}
