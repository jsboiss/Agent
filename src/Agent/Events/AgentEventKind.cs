namespace Agent.Events;

public enum AgentEventKind
{
    MessageReceived,
    MessagePersisted,
    ProviderRequestStarted,
    ProviderTextDelta,
    ProviderTurnCompleted,
    ProviderRetryStarted,
    ProviderRetryCompleted,
    ToolCallStarted,
    ToolCallOutput,
    ToolCallCompleted,
    MemoryScoutStarted,
    MemoryScoutCompleted,
    MemoryInjected,
    MemoryExtractionStarted,
    MemoryExtractionCompleted,
    MemoryConsolidationStarted,
    MemoryConsolidationCompleted,
    CompactionStarted,
    CompactionCompleted,
    ProviderError,
    ChatMessage,
    MemoryRecall,
    MemoryWrite,
    MemoryExtraction,
    MemoryConsolidation,
    ToolCall,
    ProviderRequest
}
