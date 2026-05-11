using Agent.SubAgents;
using Agent.Tools;

namespace Agent.Capabilities;

public sealed class AgentCapabilityRegistry : IAgentCapabilityRegistry
{
    public SubAgentCapabilities DefaultCapabilities => SubAgentCapabilities.ReadOnly | SubAgentCapabilities.Code;

    public IReadOnlyList<SubAgentCapabilities> AvailableCapabilities =>
    [
        SubAgentCapabilities.ReadOnly,
        SubAgentCapabilities.Code,
        SubAgentCapabilities.Web,
        SubAgentCapabilities.Memory,
        SubAgentCapabilities.ExternalActions,
        SubAgentCapabilities.CalendarRead,
        SubAgentCapabilities.EmailRead,
        SubAgentCapabilities.EmailDraft,
        SubAgentCapabilities.EmailSend,
        SubAgentCapabilities.ContactsRead,
        SubAgentCapabilities.ExternalWrite
    ];

    private static IReadOnlyList<AgentToolDefinition> DispatcherTools =>
    [
        new AgentToolDefinition(
            "search_memory",
            "Search durable agent memory for relevant records before answering. Read-only when Memory capability is available.",
            """
            {
              "type": "object",
              "properties": {
                "query": {
                  "type": "string",
                  "description": "Search text for memory lookup."
                },
                "limit": {
                  "type": "integer",
                  "description": "Maximum memory records to return.",
                  "minimum": 1,
                  "maximum": 20
                }
              },
              "required": ["query"]
            }
            """,
            "Matching memory records with ids, tiers, lifecycle state, and content."),
        new AgentToolDefinition(
            "write_memory",
            "Manage durable memory records when the user gives stable information worth preserving. Supports action add, replace, archive, remove.",
            """
            {
              "type": "object",
              "properties": {
                "action": {
                  "type": "string",
                  "description": "Memory action.",
                  "enum": ["add", "replace", "archive", "remove"]
                },
                "memoryId": {
                  "type": "string",
                  "description": "Existing memory id for replace, archive, or remove."
                },
                "match": {
                  "type": "string",
                  "description": "Unique text substring to find an existing memory when memoryId is not available."
                },
                "content": {
                  "type": "string",
                  "description": "Memory content to store or replacement content."
                },
                "tier": {
                  "type": "string",
                  "description": "Memory tier.",
                  "enum": ["Short", "Long", "Permanent"]
                },
                "segment": {
                  "type": "string",
                  "description": "Memory segment.",
                  "enum": ["Identity", "Preference", "Correction", "Relationship", "Project", "Knowledge", "Context"]
                },
                "importance": {
                  "type": "number",
                  "description": "Importance from 0 to 1."
                },
                "confidence": {
                  "type": "number",
                  "description": "Confidence from 0 to 1."
                }
              },
              "required": []
            }
            """,
            "The stored memory record id and metadata."),
        new AgentToolDefinition(
            "search_conversations",
            "Search prior conversation/session entries. Use for historical recall, not durable facts.",
            """
            {
              "type": "object",
              "properties": {
                "query": { "type": "string", "description": "Search text for session recall." },
                "limit": { "type": "integer", "minimum": 1, "maximum": 20 }
              },
              "required": ["query"]
            }
            """,
            "Matching conversation entries grouped with conversation ids, roles, timestamps, and snippets."),
        new AgentToolDefinition(
            "spawn_agent",
            "Create a child sub-agent conversation for delegated work. Use explicit permission capabilities for integrations and side effects.",
            """
            {
              "type": "object",
              "properties": {
                "task": {
                  "type": "string",
                  "description": "Self-contained task for the sub-agent."
                },
                "parentEntryId": {
                  "type": "string",
                  "description": "Conversation entry id where the child conversation branches from."
                },
                "capabilities": {
                  "type": "string",
                  "description": "Comma-separated capabilities: ReadOnly, Code, Web, Memory, ExternalActions, CalendarRead, EmailRead, EmailDraft, EmailSend, ContactsRead, ExternalWrite. Calendar is accepted as CalendarRead for compatibility."
                },
                "requiresConfirmation": {
                  "type": "boolean",
                  "description": "Whether risky actions require explicit user confirmation."
                },
                "notificationTarget": {
                  "type": "string",
                  "description": "Optional channel-specific notification target."
                }
              },
              "required": ["task", "parentEntryId"]
            }
            """,
            "A child conversation id and final sub-agent result summary."),
        new AgentToolDefinition(
            "send_ack",
            "Send a short acknowledgement before slow delegated work.",
            """
            {
              "type": "object",
              "properties": {
                "message": {
                  "type": "string",
                  "description": "Short acknowledgement text."
                },
                "target": {
                  "type": "string",
                  "description": "Optional channel target."
                }
              },
              "required": ["message"]
            }
            """,
            "Acknowledgement delivery status."),
        new AgentToolDefinition(
            "save_draft",
            "Stage a risky, write, send, or external action for later approval instead of applying it immediately.",
            """
            {
              "type": "object",
              "properties": {
                "kind": { "type": "string" },
                "summary": { "type": "string" },
                "payload": { "type": "string" },
                "sourceRunId": { "type": "string" }
              },
              "required": ["kind", "summary", "payload"]
            }
            """,
            "Pending draft id."),
        new AgentToolDefinition(
            "list_drafts",
            "List staged drafts pending approval or previously handled.",
            """
            {
              "type": "object",
              "properties": {
                "status": { "type": "string", "enum": ["Pending", "Approved", "Rejected", "Applied"] }
              }
            }
            """,
            "Draft summaries."),
        new AgentToolDefinition(
            "approve_draft",
            "Approve a pending draft after the user confirms it.",
            "{ \"type\": \"object\", \"properties\": { \"draftId\": { \"type\": \"string\" } }, \"required\": [\"draftId\"] }",
            "Draft approval status."),
        new AgentToolDefinition(
            "reject_draft",
            "Reject a pending draft after the user cancels it.",
            "{ \"type\": \"object\", \"properties\": { \"draftId\": { \"type\": \"string\" } }, \"required\": [\"draftId\"] }",
            "Draft rejection status."),
        new AgentToolDefinition(
            "automation",
            "Manage scheduled automations with one unified tool. Actions: create, list, edit, pause, resume, run_now, delete.",
            """
            {
              "type": "object",
              "properties": {
                "action": {
                  "type": "string",
                  "enum": ["create", "list", "edit", "pause", "resume", "run_now", "delete"]
                },
                "automationId": { "type": "string" },
                "name": { "type": "string" },
                "task": { "type": "string" },
                "schedule": { "type": "string", "description": "TimeSpan, 'every <TimeSpan>', or simple daily 5-field cron." },
                "mode": { "type": "string", "enum": ["Agent", "Deterministic"] },
                "capabilities": { "type": "string" },
                "workspaceRootPath": { "type": "string" },
                "skillIds": { "type": "string" },
                "notificationTarget": { "type": "string" }
              },
              "required": ["action"]
            }
            """,
            "Automation id, status, next run, last run, and latest output summary."),
        new AgentToolDefinition(
            "create_automation",
            "Create a scheduled task that spawns a sub-agent when due.",
            """
            {
              "type": "object",
              "properties": {
                "name": { "type": "string" },
                "task": { "type": "string" },
                "schedule": { "type": "string", "description": "TimeSpan, 'every <TimeSpan>', or simple daily 5-field cron." },
                "capabilities": { "type": "string", "description": "Comma-separated capabilities. Calendar is accepted as CalendarRead." },
                "notificationTarget": { "type": "string" }
              },
              "required": ["name", "task", "schedule"]
            }
            """,
            "Automation id and next run time."),
        new AgentToolDefinition(
            "list_automations",
            "List scheduled automations.",
            "{ \"type\": \"object\", \"properties\": {} }",
            "Automation summaries."),
        new AgentToolDefinition(
            "toggle_automation",
            "Enable or disable a scheduled automation.",
            "{ \"type\": \"object\", \"properties\": { \"automationId\": { \"type\": \"string\" }, \"enabled\": { \"type\": \"boolean\" } }, \"required\": [\"automationId\", \"enabled\"] }",
            "Automation status."),
        new AgentToolDefinition(
            "delete_automation",
            "Delete a scheduled automation.",
            "{ \"type\": \"object\", \"properties\": { \"automationId\": { \"type\": \"string\" } }, \"required\": [\"automationId\"] }",
            "Deletion status."),
        new AgentToolDefinition(
            "edit_automation",
            "Edit an existing scheduled automation while preserving its id and run history.",
            """
            {
              "type": "object",
              "properties": {
                "automationId": { "type": "string" },
                "name": { "type": "string" },
                "task": { "type": "string" },
                "schedule": { "type": "string" },
                "capabilities": { "type": "string" },
                "notificationTarget": { "type": "string" },
                "workspaceRootPath": { "type": "string" },
                "skillIds": { "type": "string" }
              },
              "required": ["automationId"]
            }
            """,
            "Updated automation summary."),
        new AgentToolDefinition(
            "run_automation",
            "Run an existing automation immediately without changing its normal schedule.",
            "{ \"type\": \"object\", \"properties\": { \"automationId\": { \"type\": \"string\" } }, \"required\": [\"automationId\"] }",
            "Triggered automation run summary."),
        new AgentToolDefinition(
            "cancel_run",
            "Cancel a queued or running agent run.",
            "{ \"type\": \"object\", \"properties\": { \"runId\": { \"type\": \"string\" } }, \"required\": [\"runId\"] }",
            "Cancellation status."),
        new AgentToolDefinition(
            "retry_run",
            "Retry a prior sub-agent run as a new run.",
            "{ \"type\": \"object\", \"properties\": { \"runId\": { \"type\": \"string\" } }, \"required\": [\"runId\"] }",
            "New run id.")
    ];

    private static IReadOnlyList<AgentToolDefinition> CalendarReadTools =>
    [
        new AgentToolDefinition(
            "calendar_list_events",
            "List Google Calendar events within an explicit time range. CalendarRead only; executes directly and never writes calendar data.",
            """
            {
              "type": "object",
              "properties": {
                "start": { "type": "string", "description": "Inclusive ISO 8601 start date/time." },
                "end": { "type": "string", "description": "Exclusive ISO 8601 end date/time." },
                "calendarId": { "type": "string", "description": "Google Calendar id. Use primary unless the user names another calendar." },
                "limit": { "type": "integer", "minimum": 1, "maximum": 50 }
              },
              "required": ["start", "end"]
            }
            """,
            "Concise matching calendar events."),
        new AgentToolDefinition(
            "calendar_search_events",
            "Search Google Calendar events by text and optional explicit time range. CalendarRead only; executes directly and never writes calendar data.",
            """
            {
              "type": "object",
              "properties": {
                "query": { "type": "string", "description": "Search text." },
                "start": { "type": "string", "description": "Inclusive ISO 8601 start date/time." },
                "end": { "type": "string", "description": "Exclusive ISO 8601 end date/time." },
                "calendarId": { "type": "string", "description": "Google Calendar id. Use primary unless the user names another calendar." },
                "limit": { "type": "integer", "minimum": 1, "maximum": 50 }
              },
              "required": ["query"]
            }
            """,
            "Concise matching calendar events."),
        new AgentToolDefinition(
            "calendar_get_availability",
            "Return busy and free windows for a Google Calendar within an explicit time range. CalendarRead only; executes directly and never writes calendar data.",
            """
            {
              "type": "object",
              "properties": {
                "start": { "type": "string", "description": "Inclusive ISO 8601 start date/time." },
                "end": { "type": "string", "description": "Exclusive ISO 8601 end date/time." },
                "calendarId": { "type": "string", "description": "Google Calendar id. Use primary unless the user names another calendar." }
              },
              "required": ["start", "end"]
            }
            """,
            "Busy and free windows for the requested calendar range.")
    ];

    private static IReadOnlyList<AgentToolDefinition> EmailReadTools =>
    [
        new AgentToolDefinition(
            "gmail_search_messages",
            "Search Gmail messages. EmailRead only; executes directly and never modifies email.",
            """
            {
              "type": "object",
              "properties": {
                "query": { "type": "string", "description": "Gmail search query." },
                "limit": { "type": "integer", "minimum": 1, "maximum": 20 }
              },
              "required": ["query"]
            }
            """,
            "Concise matching Gmail message metadata."),
        new AgentToolDefinition(
            "gmail_get_message",
            "Get Gmail message metadata and snippet by message id. EmailRead only; executes directly and never modifies email.",
            """
            {
              "type": "object",
              "properties": {
                "messageId": { "type": "string", "description": "Gmail message id." }
              },
              "required": ["messageId"]
            }
            """,
            "Gmail message metadata and snippet.")
    ];

    private static IReadOnlyList<AgentToolDefinition> EmailDraftTools =>
    [
        new AgentToolDefinition(
            "gmail_create_draft",
            "Create a Gmail draft. Requires EmailDraft and should only be used when the user asked to draft an email.",
            """
            {
              "type": "object",
              "properties": {
                "to": { "type": "string" },
                "cc": { "type": "string" },
                "bcc": { "type": "string" },
                "subject": { "type": "string" },
                "body": { "type": "string" }
              },
              "required": ["to", "subject", "body"]
            }
            """,
            "Created Gmail draft id.")
    ];

    private static IReadOnlyList<AgentToolDefinition> EmailSendTools =>
    [
        new AgentToolDefinition(
            "gmail_send_draft",
            "Send an existing Gmail draft. Requires EmailSend and explicit user approval.",
            """
            {
              "type": "object",
              "properties": {
                "draftId": { "type": "string" }
              },
              "required": ["draftId"]
            }
            """,
            "Sent Gmail message id.")
    ];

    public SubAgentCapabilities Parse(string? value, SubAgentCapabilities? fallback = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback ?? DefaultCapabilities;
        }

        SubAgentCapabilities result = SubAgentCapabilities.None;

        foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(item, "Calendar", StringComparison.OrdinalIgnoreCase))
            {
                result |= SubAgentCapabilities.CalendarRead;
                continue;
            }

            if (Enum.TryParse<SubAgentCapabilities>(item, true, out var parsed))
            {
                result |= parsed;
            }
        }

        return result == SubAgentCapabilities.None
            ? fallback ?? SubAgentCapabilities.ReadOnly
            : result;
    }

    public IReadOnlyList<AgentToolDefinition> GetToolDefinitions(SubAgentCapabilities capabilities)
    {
        return GetToolDefinitionsForToolsets(capabilities, GetDefaultToolsets(capabilities));
    }

    public IReadOnlyList<AgentToolDefinition> GetToolDefinitionsForToolsets(
        SubAgentCapabilities capabilities,
        IReadOnlySet<string> toolsets)
    {
        List<AgentToolDefinition> tools = [.. DispatcherTools];

        if (toolsets.Contains("calendar")
            && (capabilities == SubAgentCapabilities.None
            || capabilities.HasFlag(SubAgentCapabilities.CalendarRead)))
        {
            tools.AddRange(CalendarReadTools);
        }

        if (toolsets.Contains("email")
            && (capabilities == SubAgentCapabilities.None
            || capabilities.HasFlag(SubAgentCapabilities.EmailRead)))
        {
            tools.AddRange(EmailReadTools);
        }

        if (toolsets.Contains("email")
            && capabilities.HasFlag(SubAgentCapabilities.EmailDraft))
        {
            tools.AddRange(EmailDraftTools);
        }

        if (toolsets.Contains("email")
            && capabilities.HasFlag(SubAgentCapabilities.EmailSend))
        {
            tools.AddRange(EmailSendTools);
        }

        return tools
            .Where(x => IsInToolset(x.Name, toolsets))
            .DistinctBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<AgentToolDefinition> GetToolDefinitionsForProfile(
        SubAgentCapabilities capabilities,
        ToolsetProfile profile)
    {
        return GetToolDefinitionsForToolsets(capabilities, GetToolsets(profile, capabilities));
    }

    private static IReadOnlySet<string> GetDefaultToolsets(SubAgentCapabilities capabilities)
    {
        HashSet<string> toolsets = new(StringComparer.OrdinalIgnoreCase)
        {
            "safe",
            "memory",
            "session_recall",
            "subagents",
            "drafts",
            "automation"
        };

        if (capabilities == SubAgentCapabilities.None || capabilities.HasFlag(SubAgentCapabilities.CalendarRead))
        {
            toolsets.Add("calendar");
        }

        if (capabilities == SubAgentCapabilities.None
            || capabilities.HasFlag(SubAgentCapabilities.EmailRead)
            || capabilities.HasFlag(SubAgentCapabilities.EmailDraft)
            || capabilities.HasFlag(SubAgentCapabilities.EmailSend))
        {
            toolsets.Add("email");
        }

        return toolsets;
    }

    private static bool IsInToolset(string toolName, IReadOnlySet<string> toolsets)
    {
        var toolset = toolName switch
        {
            "search_memory" or "write_memory" => "memory",
            "search_conversations" => "session_recall",
            "spawn_agent" or "cancel_run" or "retry_run" => "subagents",
            "send_ack" => "safe",
            "save_draft" or "list_drafts" or "approve_draft" or "reject_draft" => "drafts",
            "automation" => "automation",
            "create_automation" or "list_automations" or "toggle_automation" or "delete_automation" or "edit_automation" or "run_automation" => "legacy_automation",
            "calendar_list_events" or "calendar_search_events" or "calendar_get_availability" => "calendar",
            "gmail_search_messages" or "gmail_get_message" or "gmail_create_draft" or "gmail_send_draft" => "email",
            _ => "safe"
        };

        return toolsets.Contains(toolset);
    }

    private static IReadOnlySet<string> GetToolsets(
        ToolsetProfile profile,
        SubAgentCapabilities capabilities)
    {
        HashSet<string> toolsets = new(StringComparer.OrdinalIgnoreCase)
        {
            "safe",
            "memory",
            "session_recall"
        };

        if (profile is ToolsetProfile.DashboardChat or ToolsetProfile.MobileChat)
        {
            toolsets.Add("drafts");
            toolsets.Add("automation");
            toolsets.Add("calendar");
            toolsets.Add("email");
        }

        if (profile == ToolsetProfile.SubAgentWork)
        {
            toolsets.Add("subagents");
            AddCapabilityToolsets(toolsets, capabilities);
        }

        if (profile == ToolsetProfile.AutomationRun)
        {
            AddCapabilityToolsets(toolsets, capabilities);
        }

        return toolsets;
    }

    private static void AddCapabilityToolsets(
        ISet<string> toolsets,
        SubAgentCapabilities capabilities)
    {
        if (capabilities == SubAgentCapabilities.None
            || capabilities.HasFlag(SubAgentCapabilities.CalendarRead))
        {
            toolsets.Add("calendar");
        }

        if (capabilities == SubAgentCapabilities.None
            || capabilities.HasFlag(SubAgentCapabilities.EmailRead)
            || capabilities.HasFlag(SubAgentCapabilities.EmailDraft)
            || capabilities.HasFlag(SubAgentCapabilities.EmailSend))
        {
            toolsets.Add("email");
        }
    }

    public bool RequiresDraftForExternalSideEffects(SubAgentCapabilities capabilities)
    {
        return capabilities.HasFlag(SubAgentCapabilities.EmailDraft)
            || capabilities.HasFlag(SubAgentCapabilities.EmailSend)
            || capabilities.HasFlag(SubAgentCapabilities.ExternalWrite);
    }
}
