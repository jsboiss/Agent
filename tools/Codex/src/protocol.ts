export type ProviderKind = "ClaudeCode" | "Codex";

export interface ProviderRequest {
  kind: ProviderKind;
  conversationId: string;
  userMessage: string;
  systemPrompt: string;
  workspace: WorkspaceContext;
  memoryContext: string;
  injectedMemoryIds: string[];
  availableTools: AgentToolDefinition[];
  priorToolCalls: ProviderToolCall[];
  toolResults: ProviderToolResult[];
}

export interface WorkspaceContext {
  rootPath: string;
  currentPath: string;
  projectName: string;
  loadedInstructions: string[];
  applicableSettings: Record<string, string>;
  availableTools: AgentToolDefinition[];
}

export interface AgentToolDefinition {
  name: string;
  description: string;
  jsonParameterSchema: string;
  resultDescription?: string;
}

export interface ProviderToolCall {
  id: string;
  name: string;
  arguments: Record<string, string>;
}

export interface ProviderToolResult {
  toolCallId: string;
  name: string;
  content: string;
}

export interface ProviderResult {
  assistantMessage: string;
  toolCalls: ProviderToolCall[];
  usageMetadata: Record<string, string>;
  error?: string;
}
