import { ProviderRequest, ProviderResult } from "./protocol.js";
import { Buffer } from "node:buffer";
import process from "node:process";

export async function handleRequest(request: ProviderRequest): Promise<ProviderResult> {
  return {
    assistantMessage: `Claude Code provider received: ${request.userMessage}`,
    toolCalls: [],
    usageMetadata: {
      conversationId: request.conversationId,
      injectedMemoryCount: request.injectedMemoryIds.length.toString(),
      availableToolCount: request.availableTools.length.toString()
    }
  };
}

async function readStdin(): Promise<string> {
  const chunks: Buffer[] = [];

  for await (const chunk of process.stdin) {
    chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
  }

  return Buffer.concat(chunks).toString("utf8");
}

async function main(): Promise<void> {
  const input = await readStdin();
  const request = JSON.parse(input) as ProviderRequest;
  const result = await handleRequest(request);

  process.stdout.write(JSON.stringify(result));
}

main().catch((error: unknown) => {
  const result: ProviderResult = {
    assistantMessage: "",
    toolCalls: [],
    usageMetadata: {},
    error: error instanceof Error ? error.message : String(error)
  };

  process.stdout.write(JSON.stringify(result));
  process.exitCode = 1;
});
