namespace Agent.Events;

public enum AgentEventKind
{
    ChatTurn,
    MemoryRecall,
    MemoryWrite,
    MemoryExtraction,
    MemoryConsolidation,
    ToolCall,
    BridgeRequest,
    BridgeError
}
