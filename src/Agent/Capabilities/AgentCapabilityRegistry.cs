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
            "Write a durable memory record when the user gives stable information worth preserving.",
            """
            {
              "type": "object",
              "properties": {
                "content": {
                  "type": "string",
                  "description": "Memory content to store."
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
              "required": ["content"]
            }
            """,
            "The stored memory record id and metadata."),
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
        List<AgentToolDefinition> tools = [.. DispatcherTools];

        if (capabilities == SubAgentCapabilities.None
            || capabilities.HasFlag(SubAgentCapabilities.CalendarRead))
        {
            tools.AddRange(CalendarReadTools);
        }

        if (capabilities == SubAgentCapabilities.None
            || capabilities.HasFlag(SubAgentCapabilities.EmailRead))
        {
            tools.AddRange(EmailReadTools);
        }

        if (capabilities.HasFlag(SubAgentCapabilities.EmailDraft))
        {
            tools.AddRange(EmailDraftTools);
        }

        if (capabilities.HasFlag(SubAgentCapabilities.EmailSend))
        {
            tools.AddRange(EmailSendTools);
        }

        return tools;
    }

    public bool RequiresDraftForExternalSideEffects(SubAgentCapabilities capabilities)
    {
        return capabilities.HasFlag(SubAgentCapabilities.EmailDraft)
            || capabilities.HasFlag(SubAgentCapabilities.EmailSend)
            || capabilities.HasFlag(SubAgentCapabilities.ExternalWrite);
    }
}
