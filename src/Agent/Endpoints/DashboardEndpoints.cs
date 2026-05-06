using Agent.Dashboard;

namespace Agent.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/dashboard");

        group.MapGet(
            "/chat/main",
            async (IChatDashboardService service, CancellationToken cancellationToken) =>
                await service.LoadMain(cancellationToken));
        group.MapPost(
            "/chat/main/messages",
            async (SendChatMessageRequest request, IChatDashboardService service, CancellationToken cancellationToken) =>
                await service.SendPrompt(request, cancellationToken));
        group.MapGet(
            "/runs",
            async (string? conversationId, string? filter, IRunTimelineService service, CancellationToken cancellationToken) =>
                await service.List(conversationId, filter ?? "All", cancellationToken));
        group.MapGet(
            "/memories",
            async (string? query, string? lifecycle, string? segment, string? tier, IMemoryDashboardService service, CancellationToken cancellationToken) =>
                await service.Search(
                    new MemorySearchFilter(
                        query ?? string.Empty,
                        lifecycle ?? "Active",
                        segment ?? "All",
                        tier ?? "All"),
                    cancellationToken));
        group.MapPost(
            "/memories",
            async (MemoryWriteDto request, IMemoryDashboardService service, CancellationToken cancellationToken) =>
                await service.Write(request, cancellationToken));
        group.MapPatch(
            "/memories/{id}/lifecycle",
            async (string id, MemoryLifecycleUpdateDto request, IMemoryDashboardService service, CancellationToken cancellationToken) =>
                await service.UpdateLifecycle(id, request, cancellationToken));
        group.MapDelete(
            "/memories/{id}",
            async (string id, IMemoryDashboardService service, CancellationToken cancellationToken) =>
            {
                await service.Delete(id, cancellationToken);

                return Results.NoContent();
            });
        group.MapGet(
            "/graph",
            async (IMemoryGraphService service, CancellationToken cancellationToken) =>
                await service.Build(cancellationToken));

        return endpoints;
    }
}
