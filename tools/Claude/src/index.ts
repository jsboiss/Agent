import { ClaudeTurnRequest, ClaudeTurnResult } from "./protocol.js";

export async function handleTurn(request: ClaudeTurnRequest): Promise<ClaudeTurnResult> {
  void request;

  return {
    assistantMessage: "",
    toolCalls: [],
    usageMetadata: {},
    error: "Claude is scaffolded but not implemented."
  };
}
