import { ClaudeRequest, ClaudeResult } from "./protocol.js";

export async function handleRequest(request: ClaudeRequest): Promise<ClaudeResult> {
  void request;

  return {
    assistantMessage: "",
    toolCalls: [],
    usageMetadata: {},
    error: "Claude is scaffolded but not implemented."
  };
}
