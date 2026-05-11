using Agent.Memory;
using Agent.Automations;
using Agent.Calendar;
using Agent.Capabilities;
using Agent.Conversations;
using Agent.Drafts;
using Agent.Email;
using Agent.Notifications;
using Agent.SubAgents;
using Agent.Workspaces;

namespace Agent.Tools;

public sealed class AgentToolExecutor(
    IMemoryStore memoryStore,
    ISubAgentCoordinator subAgentCoordinator,
    IAgentNotifier notifier,
    IAgentDraftStore draftStore,
    IAutomationStore automationStore,
    IAutomationScheduler automationScheduler,
    ICalendarProvider calendarProvider,
    IEmailProvider emailProvider,
    IAgentRunStore runStore,
    IAgentCapabilityRegistry capabilityRegistry,
    IConversationRepository conversationRepository,
    IExternalMemoryProvider externalMemoryProvider,
    IAutomationRunStore automationRunStore) : IAgentToolExecutor
{
    public async Task<AgentToolResult> Execute(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        return request.Name switch
        {
            "search_memory" => await SearchMemory(request, cancellationToken),
            "write_memory" => await WriteMemory(request, cancellationToken),
            "search_conversations" => await SearchConversations(request, cancellationToken),
            "automation" => await Automation(request, cancellationToken),
            "spawn_agent" => await SpawnAgent(request, cancellationToken),
            "send_ack" => await SendAck(request, cancellationToken),
            "save_draft" => await SaveDraft(request, cancellationToken),
            "list_drafts" => await ListDrafts(request, cancellationToken),
            "approve_draft" => await UpdateDraft(request, DraftStatus.Approved, cancellationToken),
            "reject_draft" => await UpdateDraft(request, DraftStatus.Rejected, cancellationToken),
            "create_automation" => await CreateAutomation(request, cancellationToken),
            "list_automations" => await ListAutomations(request, cancellationToken),
            "toggle_automation" => await ToggleAutomation(request, cancellationToken),
            "delete_automation" => await DeleteAutomation(request, cancellationToken),
            "edit_automation" => await EditAutomation(request, cancellationToken),
            "run_automation" => await RunAutomation(request, cancellationToken),
            "calendar_list_events" => await ListCalendarEvents(request, false, cancellationToken),
            "calendar_search_events" => await ListCalendarEvents(request, true, cancellationToken),
            "calendar_get_availability" => await GetCalendarAvailability(request, cancellationToken),
            "gmail_search_messages" => await SearchGmailMessages(request, cancellationToken),
            "gmail_get_message" => await GetGmailMessage(request, cancellationToken),
            "gmail_create_draft" => await CreateGmailDraft(request, cancellationToken),
            "gmail_send_draft" => await SendGmailDraft(request, cancellationToken),
            "cancel_run" => await CancelRun(request, cancellationToken),
            "retry_run" => await RetryRun(request, cancellationToken),
            _ => new AgentToolResult(
                request.Name,
                false,
                $"Unknown tool '{request.Name}'.",
                new Dictionary<string, string>())
        };
    }

    private async Task<AgentToolResult> ListCalendarEvents(
        AgentToolRequest request,
        bool requiresQuery,
        CancellationToken cancellationToken)
    {
        if (!TryGetRange(request, !requiresQuery, out var start, out var end, out var error))
        {
            return new AgentToolResult(request.Name, false, error, new Dictionary<string, string>());
        }

        var query = request.Arguments.GetValueOrDefault("query");

        if (requiresQuery && string.IsNullOrWhiteSpace(query))
        {
            return new AgentToolResult(request.Name, false, "Missing required argument 'query'.", new Dictionary<string, string>());
        }

        try
        {
            var events = await calendarProvider.ListEvents(
                new GoogleCalendarEventQuery(
                    start,
                    end,
                    query,
                    GetCalendarId(request),
                    GetInt(request.Arguments.GetValueOrDefault("limit"), 20)),
                cancellationToken);
            var content = events.Count == 0
                ? "No calendar events found."
                : string.Join(Environment.NewLine, events.Select(FormatEvent));

            return new AgentToolResult(
                request.Name,
                true,
                content,
                new Dictionary<string, string> { ["count"] = events.Count.ToString() });
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            return new AgentToolResult(request.Name, false, exception.Message, new Dictionary<string, string>());
        }
    }

    private async Task<AgentToolResult> GetCalendarAvailability(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetRange(request, true, out var start, out var end, out var error))
        {
            return new AgentToolResult(request.Name, false, error, new Dictionary<string, string>());
        }

        try
        {
            var windows = await calendarProvider.GetAvailability(
                new GoogleCalendarAvailabilityQuery(start, end, GetCalendarId(request)),
                cancellationToken);
            var content = windows.Count == 0
                ? "No availability windows found."
                : string.Join(Environment.NewLine, windows.Select(x =>
                    $"- {(x.Busy ? "Busy" : "Free")}: {x.Start:O} to {x.End:O}{(string.IsNullOrWhiteSpace(x.Title) ? string.Empty : $" - {x.Title}")}"));

            return new AgentToolResult(
                request.Name,
                true,
                content,
                new Dictionary<string, string> { ["count"] = windows.Count.ToString() });
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            return new AgentToolResult(request.Name, false, exception.Message, new Dictionary<string, string>());
        }
    }

    private static bool TryGetRange(
        AgentToolRequest request,
        bool required,
        out DateTimeOffset start,
        out DateTimeOffset end,
        out string error)
    {
        start = default;
        end = default;
        error = string.Empty;

        var startValue = request.Arguments.GetValueOrDefault("start");
        var endValue = request.Arguments.GetValueOrDefault("end");

        if (!required && string.IsNullOrWhiteSpace(startValue) && string.IsNullOrWhiteSpace(endValue))
        {
            start = DateTimeOffset.Now.AddYears(-1);
            end = DateTimeOffset.Now.AddYears(1);
            return true;
        }

        if (!DateTimeOffset.TryParse(startValue, out start))
        {
            error = "Missing or invalid required argument 'start'. Use ISO 8601 date/time.";
            return false;
        }

        if (!DateTimeOffset.TryParse(endValue, out end))
        {
            error = "Missing or invalid required argument 'end'. Use ISO 8601 date/time.";
            return false;
        }

        if (end <= start)
        {
            error = "Calendar end time must be after start time.";
            return false;
        }

        return true;
    }

    private static string GetCalendarId(AgentToolRequest request)
    {
        var calendarId = request.Arguments.GetValueOrDefault("calendarId");

        return string.IsNullOrWhiteSpace(calendarId) ? "primary" : calendarId;
    }

    private static int GetInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed)
            ? parsed
            : fallback;
    }

    private static string FormatEvent(GoogleCalendarEvent calendarEvent)
    {
        var attendees = calendarEvent.Attendees.Count == 0
            ? string.Empty
            : $" attendees={string.Join(", ", calendarEvent.Attendees)}";
        var location = string.IsNullOrWhiteSpace(calendarEvent.Location)
            ? string.Empty
            : $" location={calendarEvent.Location}";
        var link = string.IsNullOrWhiteSpace(calendarEvent.MeetingLink)
            ? string.Empty
            : $" link={calendarEvent.MeetingLink}";

        return $"- {calendarEvent.Title}: {calendarEvent.Start:O} to {calendarEvent.End:O} timezone={calendarEvent.TimeZone} id={calendarEvent.Id} calendar={calendarEvent.CalendarId}{location}{attendees}{link}";
    }

    private async Task<AgentToolResult> SearchGmailMessages(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var query = request.Arguments.GetValueOrDefault("query") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(query))
        {
            return new AgentToolResult(request.Name, false, "Missing required argument 'query'.", new Dictionary<string, string>());
        }

        try
        {
            var messages = await emailProvider.SearchMessages(
                new GmailMessageQuery(query, GetInt(request.Arguments.GetValueOrDefault("limit"), 10)),
                cancellationToken);
            var content = messages.Count == 0
                ? "No Gmail messages found."
                : string.Join(Environment.NewLine, messages.Select(FormatMessage));

            return new AgentToolResult(
                request.Name,
                true,
                content,
                new Dictionary<string, string> { ["count"] = messages.Count.ToString() });
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            return new AgentToolResult(request.Name, false, exception.Message, new Dictionary<string, string>());
        }
    }

    private async Task<AgentToolResult> GetGmailMessage(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var messageId = request.Arguments.GetValueOrDefault("messageId") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(messageId))
        {
            return new AgentToolResult(request.Name, false, "Missing required argument 'messageId'.", new Dictionary<string, string>());
        }

        try
        {
            var message = await emailProvider.GetMessage(messageId, cancellationToken);

            return new AgentToolResult(
                request.Name,
                true,
                FormatMessage(message),
                new Dictionary<string, string> { ["messageId"] = message.Id, ["threadId"] = message.ThreadId });
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            return new AgentToolResult(request.Name, false, exception.Message, new Dictionary<string, string>());
        }
    }

    private async Task<AgentToolResult> CreateGmailDraft(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var to = request.Arguments.GetValueOrDefault("to") ?? string.Empty;
        var subject = request.Arguments.GetValueOrDefault("subject") ?? string.Empty;
        var body = request.Arguments.GetValueOrDefault("body") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(to) || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body))
        {
            return new AgentToolResult(request.Name, false, "Missing required Gmail draft recipient, subject, or body.", new Dictionary<string, string>());
        }

        try
        {
            var draftId = await emailProvider.CreateDraft(
                new GmailDraftRequest(
                    to,
                    subject,
                    body,
                    request.Arguments.GetValueOrDefault("cc"),
                    request.Arguments.GetValueOrDefault("bcc")),
                cancellationToken);

            return new AgentToolResult(
                request.Name,
                true,
                $"Gmail draft created: {draftId}",
                new Dictionary<string, string> { ["draftId"] = draftId });
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            return new AgentToolResult(request.Name, false, exception.Message, new Dictionary<string, string>());
        }
    }

    private async Task<AgentToolResult> SendGmailDraft(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var draftId = request.Arguments.GetValueOrDefault("draftId") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(draftId))
        {
            return new AgentToolResult(request.Name, false, "Missing required argument 'draftId'.", new Dictionary<string, string>());
        }

        try
        {
            var messageId = await emailProvider.SendDraft(draftId, cancellationToken);

            return new AgentToolResult(
                request.Name,
                true,
                $"Gmail draft sent: {messageId}",
                new Dictionary<string, string> { ["messageId"] = messageId });
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            return new AgentToolResult(request.Name, false, exception.Message, new Dictionary<string, string>());
        }
    }

    private static string FormatMessage(GmailMessageSummary message)
    {
        var date = message.Date is null ? string.Empty : $" date={message.Date:O}";

        return $"- {message.Subject}: from={message.From} to={message.To}{date} id={message.Id} thread={message.ThreadId} snippet={message.Snippet}";
    }

    private async Task<AgentToolResult> SpawnAgent(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var task = request.Arguments.GetValueOrDefault("task") ?? string.Empty;
        var parentEntryId = request.Arguments.GetValueOrDefault("parentEntryId") ?? request.ParentEntryId;
        var capabilities = capabilityRegistry.Parse(request.Arguments.GetValueOrDefault("capabilities"));
        var requiresConfirmation = GetBool(request.Arguments.GetValueOrDefault("requiresConfirmation"), IsMobileChannel(request.Channel));
        var notificationTarget = request.Arguments.GetValueOrDefault("notificationTarget");

        if (string.IsNullOrWhiteSpace(task))
        {
            return new AgentToolResult(
                request.Name,
                false,
                "Missing required argument 'task'.",
                new Dictionary<string, string>());
        }

        if (IsSimplePersonalContextLookup(task))
        {
            return new AgentToolResult(
                request.Name,
                false,
                "Do not spawn a sub-agent for simple read-only Gmail, email, calendar, schedule, event, availability, receipt, invoice, booking, confirmation, or ticket lookups. Use the direct Gmail/calendar tools or prefetched evidence, then answer the user in this same turn.",
                new Dictionary<string, string>
                {
                    ["blockedDelegation"] = "personal-context-read"
                });
        }

        var result = await subAgentCoordinator.CreateAndReport(
            new SubAgentRunRequest(
                request.ConversationId,
                parentEntryId,
                task,
                request.Channel,
                capabilities,
                requiresConfirmation,
                notificationTarget),
            cancellationToken);

        return new AgentToolResult(
            request.Name,
            true,
            result.Summary,
            new Dictionary<string, string>
            {
                ["conversationId"] = result.ConversationId,
                ["resultEntryId"] = result.ResultEntryId,
                ["runId"] = result.RunId ?? string.Empty,
                ["codexThreadId"] = result.CodexThreadId ?? string.Empty,
                ["status"] = result.Status
            });
    }

    private static bool IsSimplePersonalContextLookup(string task)
    {
        if (ContainsAny(
            task,
            "code",
            "file",
            "build",
            "test",
            "fix",
            "implement",
            "refactor",
            "debug",
            "shell",
            "command",
            "run ",
            "launch",
            "open ",
            "start ",
            "automation",
            "scheduled task"))
        {
            return false;
        }

        return ContainsAny(
            task,
            "gmail",
            "email",
            "inbox",
            "mail",
            "calendar",
            "schedule",
            "agenda",
            "event",
            "availability",
            "available",
            "receipt",
            "invoice",
            "booking",
            "confirmation",
            "ticket");
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(x => value.Contains(x, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<AgentToolResult> SendAck(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var message = request.Arguments.GetValueOrDefault("message") ?? "Working on it.";
        var target = request.Arguments.GetValueOrDefault("target");
        await notifier.Send(request.Channel, target, message, cancellationToken);

        return new AgentToolResult(request.Name, true, "Acknowledgement sent.", new Dictionary<string, string>());
    }

    private async Task<AgentToolResult> SaveDraft(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var kind = request.Arguments.GetValueOrDefault("kind") ?? "action";
        var summary = request.Arguments.GetValueOrDefault("summary") ?? string.Empty;
        var payload = request.Arguments.GetValueOrDefault("payload") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(summary) || string.IsNullOrWhiteSpace(payload))
        {
            return new AgentToolResult(request.Name, false, "Missing required draft summary or payload.", new Dictionary<string, string>());
        }

        var draft = await draftStore.Create(
            new DraftWriteRequest(
                kind,
                summary,
                payload,
                request.Arguments.GetValueOrDefault("sourceRunId"),
                request.ConversationId,
                request.Channel),
            cancellationToken);

        return new AgentToolResult(
            request.Name,
            true,
            $"Draft saved: {draft.Id}. Ask the user to approve or reject it.",
            new Dictionary<string, string>
            {
                ["draftId"] = draft.Id,
                ["status"] = draft.Status.ToString()
            });
    }

    private async Task<AgentToolResult> ListDrafts(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var status = GetNullableEnum<DraftStatus>(request.Arguments.GetValueOrDefault("status"));
        var drafts = await draftStore.List(status, 20, cancellationToken);
        var content = drafts.Count == 0
            ? "No drafts found."
            : string.Join(Environment.NewLine, drafts.Select(x => $"- {x.Id} [{x.Status}] {x.Kind}: {x.Summary}"));

        return new AgentToolResult(request.Name, true, content, new Dictionary<string, string> { ["count"] = drafts.Count.ToString() });
    }

    private async Task<AgentToolResult> UpdateDraft(
        AgentToolRequest request,
        DraftStatus status,
        CancellationToken cancellationToken)
    {
        var id = request.Arguments.GetValueOrDefault("draftId") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(id))
        {
            return new AgentToolResult(request.Name, false, "Missing required argument 'draftId'.", new Dictionary<string, string>());
        }

        var draft = await draftStore.UpdateStatus(id, status, cancellationToken);

        return new AgentToolResult(request.Name, true, $"Draft {draft.Id} marked {draft.Status}.", new Dictionary<string, string> { ["draftId"] = draft.Id });
    }

    private async Task<AgentToolResult> CreateAutomation(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Arguments.GetValueOrDefault("name") ?? string.Empty;
        var task = request.Arguments.GetValueOrDefault("task") ?? string.Empty;
        var schedule = request.Arguments.GetValueOrDefault("schedule") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(task) || string.IsNullOrWhiteSpace(schedule))
        {
            return new AgentToolResult(request.Name, false, "Missing required automation name, task, or schedule.", new Dictionary<string, string>());
        }

        if (string.Equals(request.Channel, "automation", StringComparison.OrdinalIgnoreCase))
        {
            return new AgentToolResult(request.Name, false, "Automations cannot create additional automations.", new Dictionary<string, string>());
        }

        if (automationScheduler.GetNextRun(schedule, DateTimeOffset.UtcNow) is null)
        {
            return new AgentToolResult(request.Name, false, "Schedule must be a TimeSpan, 'every <TimeSpan>', or a simple daily 5-field cron with numeric minute and hour.", new Dictionary<string, string>());
        }

        var automation = await automationStore.Create(
            new AutomationWriteRequest(
                name,
                task,
                schedule,
                GetEnum(request.Arguments.GetValueOrDefault("mode"), AutomationExecutionMode.Agent),
                request.ConversationId,
                request.Channel,
                request.Arguments.GetValueOrDefault("notificationTarget"),
                capabilityRegistry.Parse(request.Arguments.GetValueOrDefault("capabilities")),
                GetValidatedWorkspaceRootPath(request.Arguments.GetValueOrDefault("workspaceRootPath")),
                request.Arguments.GetValueOrDefault("skillIds")),
            cancellationToken);

        return new AgentToolResult(
            request.Name,
            true,
            $"Automation created: {automation.Id}. Next run: {automation.NextRunAt:O}.",
            new Dictionary<string, string> { ["automationId"] = automation.Id });
    }

    private async Task<AgentToolResult> ListAutomations(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var automations = await automationStore.List(cancellationToken);
        var content = automations.Count == 0
            ? "No automations found."
            : string.Join(Environment.NewLine, automations.Select(x => $"- {x.Id} [{x.Status}] {x.Name}: {x.Schedule}, next {x.NextRunAt:O}"));

        return new AgentToolResult(request.Name, true, content, new Dictionary<string, string> { ["count"] = automations.Count.ToString() });
    }

    private async Task<AgentToolResult> ToggleAutomation(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var id = request.Arguments.GetValueOrDefault("automationId") ?? string.Empty;
        var enabled = GetBool(request.Arguments.GetValueOrDefault("enabled"), true);

        if (string.IsNullOrWhiteSpace(id))
        {
            return new AgentToolResult(request.Name, false, "Missing required argument 'automationId'.", new Dictionary<string, string>());
        }

        var automation = await automationStore.SetStatus(id, enabled ? AutomationStatus.Enabled : AutomationStatus.Disabled, cancellationToken);
        return new AgentToolResult(request.Name, true, $"Automation {automation.Id} is {automation.Status}.", new Dictionary<string, string>());
    }

    private async Task<AgentToolResult> DeleteAutomation(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var id = request.Arguments.GetValueOrDefault("automationId") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(id))
        {
            return new AgentToolResult(request.Name, false, "Missing required argument 'automationId'.", new Dictionary<string, string>());
        }

        await automationStore.Delete(id, cancellationToken);
        return new AgentToolResult(request.Name, true, $"Automation deleted: {id}.", new Dictionary<string, string>());
    }

    private async Task<AgentToolResult> EditAutomation(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var id = request.Arguments.GetValueOrDefault("automationId") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(id))
        {
            return new AgentToolResult(request.Name, false, "Missing required argument 'automationId'.", new Dictionary<string, string>());
        }

        var existing = await automationStore.Get(id, cancellationToken);

        if (existing is null)
        {
            return new AgentToolResult(request.Name, false, $"Automation '{id}' was not found.", new Dictionary<string, string>());
        }

        var schedule = request.Arguments.GetValueOrDefault("schedule") ?? existing.Schedule;

        if (automationScheduler.GetNextRun(schedule, DateTimeOffset.UtcNow) is null)
        {
            return new AgentToolResult(request.Name, false, "Schedule must be a TimeSpan, 'every <TimeSpan>', or a simple daily 5-field cron with numeric minute and hour.", new Dictionary<string, string>());
        }

        var automation = await automationStore.Update(
            id,
            new AutomationWriteRequest(
                request.Arguments.GetValueOrDefault("name") ?? existing.Name,
                request.Arguments.GetValueOrDefault("task") ?? existing.Task,
                schedule,
                GetEnum(request.Arguments.GetValueOrDefault("mode"), existing.Mode),
                existing.ConversationId,
                existing.Channel,
                request.Arguments.GetValueOrDefault("notificationTarget") ?? existing.NotificationTarget,
                capabilityRegistry.Parse(request.Arguments.GetValueOrDefault("capabilities"), existing.Capabilities),
                GetValidatedWorkspaceRootPath(request.Arguments.GetValueOrDefault("workspaceRootPath")) ?? existing.WorkspaceRootPath,
                request.Arguments.GetValueOrDefault("skillIds") ?? existing.SkillIds),
            cancellationToken);

        return new AgentToolResult(request.Name, true, $"Automation updated: {automation.Id}. Next run: {automation.NextRunAt:O}.", new Dictionary<string, string> { ["automationId"] = automation.Id });
    }

    private async Task<AgentToolResult> RunAutomation(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var id = request.Arguments.GetValueOrDefault("automationId") ?? string.Empty;
        var automation = string.IsNullOrWhiteSpace(id) ? null : await automationStore.Get(id, cancellationToken);

        if (automation is null)
        {
            return new AgentToolResult(request.Name, false, $"Automation '{id}' was not found.", new Dictionary<string, string>());
        }

        var automationRun = await automationRunStore.TryStart(automation, AutomationRunTrigger.Manual, cancellationToken);

        if (automationRun is null)
        {
            return new AgentToolResult(request.Name, false, $"Automation '{automation.Id}' is already running.", new Dictionary<string, string> { ["automationId"] = automation.Id });
        }

        if (automation.Mode == AutomationExecutionMode.Deterministic)
        {
            var deterministic = await RunDeterministicAutomation(automation, request, cancellationToken);
            await automationRunStore.Complete(
                automationRun.Id,
                deterministic.Succeeded ? AutomationRunStatus.Completed : AutomationRunStatus.Failed,
                null,
                deterministic.Content,
                deterministic.Succeeded ? null : deterministic.Content,
                cancellationToken);
            await automationStore.UpdateRunResult(
                automation.Id,
                automation.NextRunAt,
                null,
                deterministic.Content,
                cancellationToken);

            return deterministic;
        }

        var result = await subAgentCoordinator.CreateAndReport(
            new SubAgentRunRequest(
                automation.ConversationId,
                automation.LastRunId ?? automation.Id,
                GetAutomationTask(automation),
                "automation",
                automation.Capabilities,
                true,
                automation.NotificationTarget,
                automationRun.Id),
            cancellationToken);
        if (string.IsNullOrWhiteSpace(result.RunId))
        {
            await automationRunStore.Complete(
                automationRun.Id,
                AutomationRunStatus.Failed,
                null,
                result.Summary,
                result.Summary,
                cancellationToken);
        }
        await automationStore.UpdateRunResult(
            automation.Id,
            automation.NextRunAt,
            result.RunId,
            result.Summary,
            cancellationToken);

        return new AgentToolResult(request.Name, true, result.Summary, new Dictionary<string, string> { ["automationId"] = automation.Id, ["runId"] = result.RunId ?? string.Empty });
    }

    private async Task<AgentToolResult> Automation(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var action = request.Arguments.GetValueOrDefault("action") ?? "list";

        return action.ToLowerInvariant() switch
        {
            "create" => await CreateAutomation(request, cancellationToken),
            "list" => await ListAutomations(request, cancellationToken),
            "edit" => await EditAutomation(request, cancellationToken),
            "pause" => await ToggleAutomation(request with
            {
                Arguments = AddOrReplace(request.Arguments, "enabled", "false")
            }, cancellationToken),
            "resume" => await ToggleAutomation(request with
            {
                Arguments = AddOrReplace(request.Arguments, "enabled", "true")
            }, cancellationToken),
            "run_now" => await RunAutomation(request, cancellationToken),
            "delete" => await DeleteAutomation(request, cancellationToken),
            _ => new AgentToolResult(request.Name, false, $"Unsupported automation action '{action}'.", new Dictionary<string, string>())
        };
    }

    private async Task<AgentToolResult> CancelRun(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var id = request.Arguments.GetValueOrDefault("runId") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(id))
        {
            return new AgentToolResult(request.Name, false, "Missing required argument 'runId'.", new Dictionary<string, string>());
        }

        var run = await runStore.Get(id, cancellationToken);

        if (run is null)
        {
            return new AgentToolResult(request.Name, false, $"Run '{id}' was not found.", new Dictionary<string, string>());
        }

        await runStore.Update(id, AgentRunStatus.Cancelled, run.CodexThreadId, run.FinalResponse, "Cancelled by user request.", cancellationToken);
        return new AgentToolResult(request.Name, true, $"Run cancelled: {id}.", new Dictionary<string, string>());
    }

    private async Task<AgentToolResult> RetryRun(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var id = request.Arguments.GetValueOrDefault("runId") ?? string.Empty;
        var run = string.IsNullOrWhiteSpace(id) ? null : await runStore.Get(id, cancellationToken);

        if (run is null)
        {
            return new AgentToolResult(request.Name, false, $"Run '{id}' was not found.", new Dictionary<string, string>());
        }

        var result = await subAgentCoordinator.CreateAndReport(
            new SubAgentRunRequest(
                request.ConversationId,
                request.ParentEntryId,
                run.Prompt,
                request.Channel,
                capabilityRegistry.DefaultCapabilities,
                IsMobileChannel(request.Channel),
                null),
            cancellationToken);

        return new AgentToolResult(request.Name, true, result.Summary, new Dictionary<string, string> { ["runId"] = result.RunId ?? string.Empty });
    }

    private async Task<AgentToolResult> SearchMemory(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var query = request.Arguments.GetValueOrDefault("query") ?? string.Empty;
        var limit = int.TryParse(request.Arguments.GetValueOrDefault("limit"), out var parsedLimit)
            ? parsedLimit
            : 5;
        var memories = await memoryStore.Search(
            new MemorySearchRequest(
                query,
                limit,
                new HashSet<MemoryLifecycle> { MemoryLifecycle.Active },
                new Dictionary<string, string>
                {
                    ["conversationId"] = request.ConversationId
                }),
            cancellationToken);

        var content = memories.Count == 0
            ? "No matching memories found."
            : string.Join(Environment.NewLine, memories.Select(x => $"- {x.Id}: {x.Text}"));

        return new AgentToolResult(
            request.Name,
            true,
            content,
            new Dictionary<string, string>
            {
                ["count"] = memories.Count.ToString()
            });
    }

    private async Task<AgentToolResult> RunDeterministicAutomation(
        AgentAutomation automation,
        AgentToolRequest parentRequest,
        CancellationToken cancellationToken)
    {
        var deterministicRequest = GetDeterministicToolRequest(automation, parentRequest);

        if (deterministicRequest is null)
        {
            return new AgentToolResult(
                parentRequest.Name,
                false,
                "Deterministic automation task must start with one of: search_memory, search_conversations, gmail_search_messages, calendar_search_events, calendar_list_events, notify_summary.",
                new Dictionary<string, string> { ["automationId"] = automation.Id });
        }

        var result = deterministicRequest.Name switch
        {
            "search_memory" => await SearchMemory(deterministicRequest, cancellationToken),
            "search_conversations" => await SearchConversations(deterministicRequest, cancellationToken),
            "gmail_search_messages" => await SearchGmailMessages(deterministicRequest, cancellationToken),
            "calendar_search_events" => await ListCalendarEvents(deterministicRequest, true, cancellationToken),
            "calendar_list_events" => await ListCalendarEvents(deterministicRequest, false, cancellationToken),
            "notify_summary" => await SendAck(deterministicRequest, cancellationToken),
            _ => new AgentToolResult(parentRequest.Name, false, $"Unsupported deterministic action '{deterministicRequest.Name}'.", new Dictionary<string, string>())
        };

        if (automation.NotificationTarget is not null && deterministicRequest.Name is not "notify_summary")
        {
            await notifier.Send("automation", automation.NotificationTarget, Shorten(result.Content, 1800), cancellationToken);
        }

        return result with
        {
            Name = parentRequest.Name,
            Metadata = new Dictionary<string, string>(result.Metadata, StringComparer.OrdinalIgnoreCase)
            {
                ["automationId"] = automation.Id,
                ["mode"] = automation.Mode.ToString()
            }
        };
    }

    private async Task<AgentToolResult> SearchConversations(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var query = request.Arguments.GetValueOrDefault("query") ?? string.Empty;
        var limit = int.TryParse(request.Arguments.GetValueOrDefault("limit"), out var parsedLimit)
            ? parsedLimit
            : 8;

        if (string.IsNullOrWhiteSpace(query))
        {
            return new AgentToolResult(request.Name, false, "Missing required argument 'query'.", new Dictionary<string, string>());
        }

        var results = await conversationRepository.SearchEntries(query, Math.Clamp(limit, 1, 20), cancellationToken);
        var content = results.Count == 0
            ? "No matching conversation entries found."
            : string.Join(Environment.NewLine, results.Select(x =>
                $"- conversation={x.Conversation.Id} kind={x.Conversation.Kind} entry={x.Entry.Id} role={x.Entry.Role} at={x.Entry.CreatedAt:O}: {Shorten(x.Entry.Content, 500)}"));

        return new AgentToolResult(
            request.Name,
            true,
            content,
            new Dictionary<string, string> { ["count"] = results.Count.ToString() });
    }

    private async Task<AgentToolResult> WriteMemory(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var action = request.Arguments.GetValueOrDefault("action") ?? "add";
        var content = request.Arguments.GetValueOrDefault("content") ?? string.Empty;

        if (!string.Equals(action, "add", StringComparison.OrdinalIgnoreCase))
        {
            return await MutateExistingMemory(request, action, content, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return new AgentToolResult(
                request.Name,
                false,
                "Missing required argument 'content'.",
                new Dictionary<string, string>());
        }

        var tier = GetEnum(request.Arguments.GetValueOrDefault("tier"), MemoryTier.Long);
        var segment = GetEnum(request.Arguments.GetValueOrDefault("segment"), MemorySegment.Context);
        var defaults = MemorySegmentDefaults.Get(segment);
        var existingMemories = await memoryStore.Search(
            new MemorySearchRequest(
                content,
                10,
                new HashSet<MemoryLifecycle> { MemoryLifecycle.Active },
                new Dictionary<string, string>
                {
                    ["conversationId"] = request.ConversationId,
                    ["source"] = "write-memory-dedupe"
                }),
            cancellationToken);
        var existingMemory = existingMemories.FirstOrDefault(x => string.Equals(
            Normalize(x.Text),
            Normalize(content),
            StringComparison.OrdinalIgnoreCase));

        if (existingMemory is not null)
        {
            return new AgentToolResult(
                request.Name,
                true,
                $"Memory already exists: {existingMemory.Id}",
                new Dictionary<string, string>
                {
                    ["memoryId"] = existingMemory.Id,
                    ["duplicate"] = "true"
                });
        }

        var nearDuplicate = existingMemories.FirstOrDefault(x => GetSimilarity(Normalize(x.Text), Normalize(content)) >= 0.82);

        if (nearDuplicate is not null)
        {
            return new AgentToolResult(
                request.Name,
                false,
                $"Near-duplicate memory found: {nearDuplicate.Id}. Use action=replace with memoryId or match if this updates it.",
                new Dictionary<string, string>
                {
                    ["memoryId"] = nearDuplicate.Id,
                    ["nearDuplicate"] = "true"
                });
        }

        var memory = await memoryStore.Write(
            new MemoryWriteRequest(
                content,
                string.IsNullOrWhiteSpace(request.Arguments.GetValueOrDefault("tier")) ? defaults.Tier : tier,
                segment,
                GetDouble(request.Arguments.GetValueOrDefault("importance"), defaults.Importance),
                GetDouble(request.Arguments.GetValueOrDefault("confidence"), defaults.Confidence),
                request.Arguments.GetValueOrDefault("sourceMessageId")),
            cancellationToken);
        await externalMemoryProvider.MirrorWrite(memory, cancellationToken);

        return new AgentToolResult(
            request.Name,
            true,
            $"Memory written: {memory.Id}",
            new Dictionary<string, string>
            {
                ["memoryId"] = memory.Id,
                ["tier"] = memory.Tier.ToString(),
                ["segment"] = memory.Segment.ToString()
            });
    }

    private async Task<AgentToolResult> MutateExistingMemory(
        AgentToolRequest request,
        string action,
        string content,
        CancellationToken cancellationToken)
    {
        var existing = await ResolveMemory(request, cancellationToken);

        if (existing is null)
        {
            return new AgentToolResult(request.Name, false, "Existing memory was not found or match was ambiguous.", new Dictionary<string, string>());
        }

        if (string.Equals(action, "archive", StringComparison.OrdinalIgnoreCase))
        {
            var archived = await memoryStore.UpdateLifecycle(existing.Id, MemoryLifecycle.Archived, cancellationToken);
            return new AgentToolResult(request.Name, true, $"Memory archived: {archived.Id}", new Dictionary<string, string> { ["memoryId"] = archived.Id });
        }

        if (string.Equals(action, "remove", StringComparison.OrdinalIgnoreCase))
        {
            await memoryStore.Delete(existing.Id, cancellationToken);
            return new AgentToolResult(request.Name, true, $"Memory removed: {existing.Id}", new Dictionary<string, string> { ["memoryId"] = existing.Id });
        }

        if (string.Equals(action, "replace", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return new AgentToolResult(request.Name, false, "Replacement memory content is required.", new Dictionary<string, string>());
            }

            var segment = GetEnum(request.Arguments.GetValueOrDefault("segment"), existing.Segment);
            var defaults = MemorySegmentDefaults.Get(segment);
            var updated = await memoryStore.Update(
                existing.Id,
                content,
                GetEnum(request.Arguments.GetValueOrDefault("tier"), existing.Tier),
                segment,
                GetDouble(request.Arguments.GetValueOrDefault("importance"), Math.Max(existing.Importance, defaults.Importance)),
                GetDouble(request.Arguments.GetValueOrDefault("confidence"), Math.Max(existing.Confidence, defaults.Confidence)),
                request.Arguments.GetValueOrDefault("supersedes") ?? existing.Supersedes,
                cancellationToken);
            await externalMemoryProvider.MirrorWrite(updated, cancellationToken);

            return new AgentToolResult(request.Name, true, $"Memory replaced: {updated.Id}", new Dictionary<string, string> { ["memoryId"] = updated.Id });
        }

        return new AgentToolResult(request.Name, false, $"Unsupported memory action '{action}'.", new Dictionary<string, string>());
    }

    private async Task<MemoryRecord?> ResolveMemory(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var memoryId = request.Arguments.GetValueOrDefault("memoryId");

        if (!string.IsNullOrWhiteSpace(memoryId))
        {
            return await memoryStore.Get(memoryId, cancellationToken);
        }

        var match = request.Arguments.GetValueOrDefault("match");

        if (string.IsNullOrWhiteSpace(match))
        {
            return null;
        }

        var memories = await memoryStore.Search(
            new MemorySearchRequest(
                match,
                20,
                new HashSet<MemoryLifecycle> { MemoryLifecycle.Active },
                new Dictionary<string, string> { ["source"] = "write-memory-match" }),
            cancellationToken);
        var matches = memories
            .Where(x => x.Text.Contains(match, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    private static T GetEnum<T>(string? value, T fallback)
        where T : struct
    {
        return Enum.TryParse<T>(value, true, out var parsed)
            ? parsed
            : fallback;
    }

    private static T? GetNullableEnum<T>(string? value)
        where T : struct
    {
        return Enum.TryParse<T>(value, true, out var parsed)
            ? parsed
            : null;
    }

    private static bool GetBool(string? value, bool fallback)
    {
        return bool.TryParse(value, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool IsMobileChannel(string channel)
    {
        return string.Equals(channel, "telegram", StringComparison.OrdinalIgnoreCase)
            || string.Equals(channel, "imessage", StringComparison.OrdinalIgnoreCase);
    }

    private static double GetDouble(string? value, double fallback)
    {
        return double.TryParse(value, out var parsed)
            ? parsed
            : fallback;
    }

    private static string Normalize(string value)
    {
        return string.Join(
            " ",
            value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.Trim('.', ',', ';', ':', '!', '?').ToLowerInvariant()));
    }

    private static double GetSimilarity(string a, string b)
    {
        var left = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var right = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (left.Count == 0 || right.Count == 0)
        {
            return 0;
        }

        var intersection = left.Intersect(right, StringComparer.OrdinalIgnoreCase).Count();
        var union = left.Union(right, StringComparer.OrdinalIgnoreCase).Count();

        return union == 0 ? 0 : (double)intersection / union;
    }

    private static string Shorten(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..maxLength] + "...";
    }

    private static IReadOnlyDictionary<string, string> AddOrReplace(
        IReadOnlyDictionary<string, string> source,
        string key,
        string value)
    {
        var result = new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase)
        {
            [key] = value
        };

        return result;
    }

    private static string GetAutomationTask(AgentAutomation automation)
    {
        List<string> lines = [];

        if (!string.IsNullOrWhiteSpace(automation.WorkspaceRootPath))
        {
            lines.Add($"Workspace root: {automation.WorkspaceRootPath}");
        }

        if (!string.IsNullOrWhiteSpace(automation.SkillIds))
        {
            lines.Add($"Required skills: {automation.SkillIds}");
        }

        lines.Add(automation.Task);

        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    private static AgentToolRequest? GetDeterministicToolRequest(
        AgentAutomation automation,
        AgentToolRequest parentRequest)
    {
        var parts = automation.Task.Split(':', 2, StringSplitOptions.TrimEntries);
        var action = parts[0].Trim();
        var body = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        var arguments = ParseKeyValues(body);

        if (!arguments.ContainsKey("query") && !string.IsNullOrWhiteSpace(body))
        {
            arguments["query"] = body;
        }

        if (action.Equals("notify_summary", StringComparison.OrdinalIgnoreCase))
        {
            arguments["message"] = string.IsNullOrWhiteSpace(body) ? automation.Name : body;
            arguments["target"] = automation.NotificationTarget ?? string.Empty;
        }

        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "search_memory",
            "search_conversations",
            "gmail_search_messages",
            "calendar_search_events",
            "calendar_list_events",
            "notify_summary"
        };

        if (!supported.Contains(action))
        {
            return null;
        }

        return new AgentToolRequest(
            action,
            arguments,
            automation.ConversationId,
            "automation",
            parentRequest.ParentEntryId);
    }

    private static Dictionary<string, string> ParseKeyValues(string value)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);

        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);

            if (pair.Length == 2)
            {
                result[pair[0]] = pair[1];
            }
        }

        return result;
    }

    private static string? GetValidatedWorkspaceRootPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!Path.IsPathRooted(path))
        {
            throw new InvalidOperationException("Automation workspaceRootPath must be an absolute path.");
        }

        var fullPath = Path.GetFullPath(path);

        if (!Directory.Exists(fullPath))
        {
            throw new InvalidOperationException("Automation workspaceRootPath does not exist.");
        }

        return fullPath;
    }
}
