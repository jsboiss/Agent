export interface ClaudeTurnRequest {
  conversationId: string;
  userMessage: string;
  memoryContext: string;
  injectedMemoryIds: string[];
  availableTools: string[];
}

export interface ClaudeToolCall {
  id: string;
  name: string;
  arguments: Record<string, string>;
}

export interface ClaudeTurnResult {
  assistantMessage: string;
  toolCalls: ClaudeToolCall[];
  usageMetadata: Record<string, string>;
  error?: string;
}
