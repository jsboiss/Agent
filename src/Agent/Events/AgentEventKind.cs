namespace Agent.Events;

public enum AgentEventKind
{
    ChatMessage,
    MemoryRecall,
    MemoryWrite,
    MemoryExtraction,
    MemoryConsolidation,
    ToolCall,
    ClaudeRequest,
    ClaudeError
}
