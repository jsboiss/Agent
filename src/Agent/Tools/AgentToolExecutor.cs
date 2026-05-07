using Agent.Memory;
using Agent.Automations;
using Agent.Calendar;
using Agent.Drafts;
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
    IAgentRunStore runStore) : IAgentToolExecutor
{
    public async Task<AgentToolResult> Execute(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        return request.Name switch
        {
            "search_memory" => await SearchMemory(request, cancellationToken),
            "write_memory" => await WriteMemory(request, cancellationToken),
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
            "calendar_list_events" => await ListCalendarEvents(request, false, cancellationToken),
            "calendar_search_events" => await ListCalendarEvents(request, true, cancellationToken),
            "calendar_get_availability" => await GetCalendarAvailability(request, cancellationToken),
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

    private async Task<AgentToolResult> SpawnAgent(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var task = request.Arguments.GetValueOrDefault("task") ?? string.Empty;
        var parentEntryId = request.Arguments.GetValueOrDefault("parentEntryId") ?? request.ParentEntryId;
        var capabilities = GetCapabilities(request.Arguments.GetValueOrDefault("capabilities"));
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

        if (automationScheduler.GetNextRun(schedule, DateTimeOffset.UtcNow) is null)
        {
            return new AgentToolResult(request.Name, false, "Schedule must be a TimeSpan, 'every <TimeSpan>', or a simple daily 5-field cron with numeric minute and hour.", new Dictionary<string, string>());
        }

        var automation = await automationStore.Create(
            new AutomationWriteRequest(
                name,
                task,
                schedule,
                request.ConversationId,
                request.Channel,
                request.Arguments.GetValueOrDefault("notificationTarget"),
                GetCapabilities(request.Arguments.GetValueOrDefault("capabilities"))),
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
                SubAgentCapabilities.ReadOnly | SubAgentCapabilities.Code,
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

    private async Task<AgentToolResult> WriteMemory(
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var content = request.Arguments.GetValueOrDefault("content") ?? string.Empty;

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

        var memory = await memoryStore.Write(
            new MemoryWriteRequest(
                content,
                string.IsNullOrWhiteSpace(request.Arguments.GetValueOrDefault("tier")) ? defaults.Tier : tier,
                segment,
                GetDouble(request.Arguments.GetValueOrDefault("importance"), defaults.Importance),
                GetDouble(request.Arguments.GetValueOrDefault("confidence"), defaults.Confidence),
                request.Arguments.GetValueOrDefault("sourceMessageId")),
            cancellationToken);

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

    private static SubAgentCapabilities GetCapabilities(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return SubAgentCapabilities.ReadOnly | SubAgentCapabilities.Code;
        }

        SubAgentCapabilities result = SubAgentCapabilities.None;

        foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<SubAgentCapabilities>(item, true, out var parsed))
            {
                result |= parsed;
            }
        }

        return result == SubAgentCapabilities.None
            ? SubAgentCapabilities.ReadOnly
            : result;
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
}
