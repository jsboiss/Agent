import { ProviderRequest, ProviderResult } from "./protocol.js";

export async function handleRequest(request: ProviderRequest): Promise<ProviderResult> {
  void request;

  return {
    assistantMessage: "",
    toolCalls: [],
    usageMetadata: {},
    error: "Codex provider is scaffolded but not implemented."
  };
}
