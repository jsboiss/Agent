export type ProviderKind = "ClaudeCode" | "Codex";

export interface ProviderRequest {
  kind: ProviderKind;
  conversationId: string;
  userMessage: string;
  memoryContext: string;
  injectedMemoryIds: string[];
  availableTools: string[];
}

export interface ProviderToolCall {
  id: string;
  name: string;
  arguments: Record<string, string>;
}

export interface ProviderResult {
  assistantMessage: string;
  toolCalls: ProviderToolCall[];
  usageMetadata: Record<string, string>;
  error?: string;
}
