import { BridgeTurnRequest, BridgeTurnResult } from "./protocol.js";

export async function handleTurn(request: BridgeTurnRequest): Promise<BridgeTurnResult> {
  void request;

  return {
    assistantMessage: "",
    toolCalls: [],
    usageMetadata: {},
    error: "Claude Code bridge is scaffolded but not implemented."
  };
}
