using Agent.Dashboard;

namespace Agent.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/dashboard")
            .WithTags("Dashboard");

        endpoints.MapGet(
            "/api/health",
            () => Results.Ok(new
            {
                status = "ok",
                service = "MainAgent",
                timestamp = DateTimeOffset.UtcNow
            }))
            .WithName("GetApiHealth");

        group.MapGet(
            "/chat/main",
            async (IChatDashboardService service, CancellationToken cancellationToken) =>
                await service.LoadMain(cancellationToken))
            .WithName("GetMainChat");
        group.MapPost(
            "/chat/main/messages",
            async (SendChatMessageRequest request, IChatDashboardService service, CancellationToken cancellationToken) =>
                await service.SendPrompt(request, cancellationToken))
            .WithName("SendMainChatMessage");
        group.MapPost(
            "/chat/main/stream",
            async (SendChatMessageRequest request, IChatDashboardService service, HttpResponse response, CancellationToken cancellationToken) =>
            {
                response.ContentType = "text/plain; charset=utf-8";
                await service.StreamPrompt(request, response.Body, cancellationToken);
            })
            .WithName("StreamMainChatMessage");
        group.MapGet(
            "/debug/main-transcript",
            async (IChatDashboardService service, CancellationToken cancellationToken) =>
                await service.ExportMainTranscript(cancellationToken))
            .WithName("ExportMainChatTranscript");
        group.MapGet(
            "/runs",
            async (string? conversationId, string? filter, IRunTimelineService service, CancellationToken cancellationToken) =>
                await service.List(conversationId, filter ?? "All", cancellationToken))
            .WithName("GetRuns");
        group.MapGet(
            "/subagents",
            async (ISubAgentDashboardService service, CancellationToken cancellationToken) =>
                await service.List(cancellationToken))
            .WithName("GetSubAgents");
        group.MapGet(
            "/subagents/{id}",
            async (string id, ISubAgentDashboardService service, CancellationToken cancellationToken) =>
                await service.GetDetail(id, cancellationToken))
            .WithName("GetSubAgentDetail");
        group.MapGet(
            "/subagents/{id}/stream",
            async (string id, ISubAgentDashboardService service, HttpResponse response, CancellationToken cancellationToken) =>
            {
                response.ContentType = "text/event-stream; charset=utf-8";
                response.Headers.CacheControl = "no-cache";
                await service.StreamDetail(id, response.Body, cancellationToken);
            })
            .WithName("StreamSubAgentDetail");
        group.MapGet(
            "/memories",
            async (string? query, string? lifecycle, string? segment, string? tier, IMemoryDashboardService service, CancellationToken cancellationToken) =>
                await service.Search(
                    new MemorySearchFilter(
                        query ?? string.Empty,
                        lifecycle ?? "Active",
                        segment ?? "All",
                        tier ?? "All"),
                    cancellationToken))
            .WithName("GetMemories");
        group.MapPost(
            "/memories",
            async (MemoryWriteDto request, IMemoryDashboardService service, CancellationToken cancellationToken) =>
                await service.Write(request, cancellationToken))
            .WithName("WriteMemory");
        group.MapPatch(
            "/memories/{id}/lifecycle",
            async (string id, MemoryLifecycleUpdateDto request, IMemoryDashboardService service, CancellationToken cancellationToken) =>
                await service.UpdateLifecycle(id, request, cancellationToken))
            .WithName("UpdateMemoryLifecycle");
        group.MapDelete(
            "/memories/{id}",
            async (string id, IMemoryDashboardService service, CancellationToken cancellationToken) =>
            {
                await service.Delete(id, cancellationToken);

                return Results.NoContent();
            })
            .WithName("DeleteMemory");
        group.MapGet(
            "/graph",
            async (IMemoryGraphService service, CancellationToken cancellationToken) =>
                await service.Build(cancellationToken))
            .WithName("GetGraph");
        group.MapGet(
            "/settings",
            async (ISettingsDashboardService service, CancellationToken cancellationToken) =>
                await service.Load(cancellationToken))
            .WithName("GetSettings");
        group.MapGet(
            "/calendar/status",
            async (ICalendarDashboardService service, CancellationToken cancellationToken) =>
                await service.GetStatus(cancellationToken))
            .WithName("GetCalendarStatus");
        group.MapGet(
            "/calendar/connect",
            async (ICalendarDashboardService service, HttpContext context, CancellationToken cancellationToken) =>
                Results.Redirect(await service.GetConnectUrl(context, cancellationToken)))
            .WithName("ConnectCalendar");
        group.MapGet(
            "/calendar/oauth-callback",
            async (string? connected_account_id, string? code, string? state, ICalendarDashboardService service, HttpContext context, CancellationToken cancellationToken) =>
            {
                var expectedState = context.Session.GetString("google-calendar-oauth-state");
                var connectedAccountId = connected_account_id ?? code;

                if (string.IsNullOrWhiteSpace(connectedAccountId)
                    || string.IsNullOrWhiteSpace(state)
                    || !string.Equals(expectedState, state, StringComparison.Ordinal))
                {
                    return Results.BadRequest("Invalid Google Calendar Composio callback.");
                }

                await service.CompleteConnect(connectedAccountId, cancellationToken);
                context.Session.Remove("google-calendar-oauth-state");

                return Results.Redirect("/settings");
            })
            .WithName("CompleteCalendarOAuth");
        group.MapPost(
            "/calendar/disconnect",
            async (ICalendarDashboardService service, CancellationToken cancellationToken) =>
            {
                await service.Disconnect(cancellationToken);

                return Results.NoContent();
            })
            .WithName("DisconnectCalendar");
        group.MapGet(
            "/email/status",
            async (IEmailDashboardService service, CancellationToken cancellationToken) =>
                await service.GetStatus(cancellationToken))
            .WithName("GetEmailStatus");
        group.MapGet(
            "/email/connect",
            async (IEmailDashboardService service, HttpContext context, CancellationToken cancellationToken) =>
                Results.Redirect(await service.GetConnectUrl(context, cancellationToken)))
            .WithName("ConnectEmail");
        group.MapGet(
            "/email/oauth-callback",
            async (string? connected_account_id, string? code, string? state, IEmailDashboardService service, HttpContext context, CancellationToken cancellationToken) =>
            {
                var expectedState = context.Session.GetString("gmail-oauth-state");
                var connectedAccountId = connected_account_id ?? code;

                if (string.IsNullOrWhiteSpace(connectedAccountId)
                    || string.IsNullOrWhiteSpace(state)
                    || !string.Equals(expectedState, state, StringComparison.Ordinal))
                {
                    return Results.BadRequest("Invalid Gmail Composio callback.");
                }

                await service.CompleteConnect(connectedAccountId, cancellationToken);
                context.Session.Remove("gmail-oauth-state");

                return Results.Redirect("/settings");
            })
            .WithName("CompleteEmailOAuth");
        group.MapPost(
            "/email/disconnect",
            async (IEmailDashboardService service, CancellationToken cancellationToken) =>
            {
                await service.Disconnect(cancellationToken);

                return Results.NoContent();
            })
            .WithName("DisconnectEmail");
        group.MapPost(
            "/settings/workspace-permissions",
            async (WorkspacePermissionUpdateDto request, ISettingsDashboardService service, CancellationToken cancellationToken) =>
                await service.UpdateWorkspacePermissions(request, cancellationToken))
            .WithName("UpdateWorkspacePermissions");
        group.MapPost(
            "/settings/workspace-root",
            async (WorkspaceRootPathUpdateDto request, ISettingsDashboardService service, CancellationToken cancellationToken) =>
                await service.UpdateWorkspaceRootPath(request, cancellationToken))
            .WithName("UpdateWorkspaceRootPath");
        group.MapPost(
            "/compaction/main",
            async (ICompactionDashboardService service, CancellationToken cancellationToken) =>
                await service.CompactMain(cancellationToken))
            .WithName("CompactMainConversation");
        group.MapGet(
            "/telegram/status",
            (IOperationsDashboardService service) => service.GetTelegramStatus())
            .WithName("GetTelegramStatus");
        group.MapPost(
            "/runs/{id}/cancel",
            async (string id, IOperationsDashboardService service, CancellationToken cancellationToken) =>
                await service.CancelRun(id, cancellationToken))
            .WithName("CancelRun");
        group.MapPost(
            "/runs/{id}/retry",
            async (string id, IOperationsDashboardService service, CancellationToken cancellationToken) =>
                await service.RetryRun(id, cancellationToken))
            .WithName("RetryRun");
        group.MapGet(
            "/drafts",
            async (string? status, IOperationsDashboardService service, CancellationToken cancellationToken) =>
                await service.ListDrafts(status, cancellationToken))
            .WithName("GetDrafts");
        group.MapPost(
            "/drafts/{id}/approve",
            async (string id, IOperationsDashboardService service, CancellationToken cancellationToken) =>
                await service.ApproveDraft(id, cancellationToken))
            .WithName("ApproveDraft");
        group.MapPost(
            "/drafts/{id}/reject",
            async (string id, IOperationsDashboardService service, CancellationToken cancellationToken) =>
                await service.RejectDraft(id, cancellationToken))
            .WithName("RejectDraft");
        group.MapGet(
            "/automations",
            async (IOperationsDashboardService service, CancellationToken cancellationToken) =>
                await service.ListAutomations(cancellationToken))
            .WithName("GetAutomations");
        group.MapPost(
            "/automations",
            async (AutomationCreateDto request, IOperationsDashboardService service, CancellationToken cancellationToken) =>
                await service.CreateAutomation(request, cancellationToken))
            .WithName("CreateAutomation");
        group.MapPost(
            "/automations/{id}/toggle",
            async (string id, AutomationToggleDto request, IOperationsDashboardService service, CancellationToken cancellationToken) =>
                await service.ToggleAutomation(id, request, cancellationToken))
            .WithName("ToggleAutomation");
        group.MapDelete(
            "/automations/{id}",
            async (string id, IOperationsDashboardService service, CancellationToken cancellationToken) =>
            {
                await service.DeleteAutomation(id, cancellationToken);

                return Results.NoContent();
            })
            .WithName("DeleteAutomation");
        group.MapPost(
            "/memory/cleanup",
            async (IOperationsDashboardService service, CancellationToken cancellationToken) =>
                await service.CleanupMemory(cancellationToken))
            .WithName("CleanupMemory");
        group.MapPost(
            "/memory/consolidate",
            async (IOperationsDashboardService service, CancellationToken cancellationToken) =>
                await service.ConsolidateMemory(cancellationToken))
            .WithName("ConsolidateMemory");

        return endpoints;
    }
}
