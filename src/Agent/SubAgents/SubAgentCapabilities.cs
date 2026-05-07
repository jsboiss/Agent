namespace Agent.SubAgents;

[Flags]
public enum SubAgentCapabilities
{
    None = 0,
    ReadOnly = 1,
    Code = 2,
    Web = 4,
    Memory = 8,
    ExternalActions = 16,
    CalendarRead = 32,
    EmailRead = 64,
    EmailDraft = 128,
    EmailSend = 256,
    ContactsRead = 512,
    ExternalWrite = 1024,
    Calendar = CalendarRead
}
