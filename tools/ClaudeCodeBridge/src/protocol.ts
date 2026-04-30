export interface BridgeTurnRequest {
  conversationId: string;
  userMessage: string;
  memoryContext: string;
  injectedMemoryIds: string[];
  availableTools: string[];
}

export interface BridgeToolCall {
  id: string;
  name: string;
  arguments: Record<string, string>;
}

export interface BridgeTurnResult {
  assistantMessage: string;
  toolCalls: BridgeToolCall[];
  usageMetadata: Record<string, string>;
  error?: string;
}
