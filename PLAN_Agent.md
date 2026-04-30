# C# Agent With Hybrid Memory

## Summary

Build a new C#/.NET Agent, not a Boop upgrade. Keep Boop’s best ideas: dispatcher/executor split, durable memory, memory graph, extraction, decay, consolidation, and chat-app integrations. Use a local Claude Code bridge so the harness can use your Claude Pro/Max Claude Code allocation when available, while keeping C# as the owner of orchestration and memory.

Important billing constraint: the local bridge should be treated as a personal/local runtime. Official docs distinguish Claude Code subscription usage from normal API billing, and the Agent SDK docs describe API-key auth for third-party products.

## Key Architecture

- Use an ASP.NET Core host as the main process.
- Use SQLite as the local memory and event store.
- Use Blazor for the dashboard: chat view, memory table, memory graph, agent timeline, settings, and debug events.
- Use a minimal local Agent SDK bridge process only for Claude Code execution.
  - C# sends structured requests to the bridge.
  - The bridge calls Claude Agent SDK / Claude Code.
  - The bridge returns assistant messages, tool calls, results, usage metadata, and errors.
  - No memory policy, ranking, extraction, or business logic lives in the bridge.
- Do not set `ANTHROPIC_API_KEY` in the Claude Code bridge environment when the goal is subscription-backed usage; Claude Code docs say that API key auth takes precedence and can cause API billing.

## Memory System

- Store memories in SQLite with tiers: `short`, `long`, `permanent`.
- Store segments: `identity`, `preference`, `correction`, `relationship`, `project`, `knowledge`, `context`.
- Store lifecycle: `active`, `archived`, `pruned`.
- Track `importance`, `confidence`, `accessCount`, `createdAt`, `updatedAt`, `lastAccessedAt`, `sourceTurnId`, `supersedes`, and optional embedding vector reference/data.
- Implement hybrid retrieval:
  - rule-based memory relevance classifier,
  - automatic MemoryScout prefetch before each Claude request,
  - metadata-aware reranking,
  - compact upfront memory injection,
  - explicit `search_memory` tool fallback.
- Retrieval ranking should combine semantic similarity, importance, recency, access frequency, permanent-memory boost, and current project/topic hints.
- Keep memory creation conservative by default:
  - explicit “remember this” writes immediately,
  - post-turn extraction proposes durable facts,
  - low-confidence inferred memories are marked accordingly.
- Add consolidation:
  - merge duplicates,
  - supersede stale/conflicting memories,
  - prune low-value context,
  - preserve corrections as high-priority.

## Dashboard And Chat Integrations

- Blazor dashboard should expose:
  - searchable memory table,
  - force-directed memory graph grouped by segment/tier,
  - event stream for recalls, writes, extractions, consolidations, and chat turns,
  - conversation/debug timeline,
  - manual memory edit/archive/delete controls.
- Model memory graph from SQLite records:
  - memory nodes,
  - segment/tier hub nodes,
  - supersession edges,
  - optional source-turn edges.
- Add chat integrations behind adapters:
  - start with a local web chat/debug channel,
  - add Telegram adapter next,
  - add iMessage adapter later with platform-specific delivery isolated from core agent logic.
- Chat adapters should all call the same C# dispatcher pipeline.

## Agent Flow

- Incoming message enters the C# dispatcher.
- Dispatcher saves the message and recent conversation state.
- MemoryScout starts immediately if the prompt is memory-relevant.
- Dispatcher waits briefly for MemoryScout, default 150ms and max 300ms.
- If ready, compact memories are injected into the initial Claude context.
- Claude receives tools for:
  - `search_memory`,
  - `write_memory`,
  - `spawn_agent`,
  - automation/draft tools later.
- Claude Code bridge handles the model loop and tool-call protocol.
- C# executes tools and returns results to the bridge.
- Post-turn extraction runs asynchronously and stores durable memory candidates.
- Dashboard receives live events from the C# host.

## Test Plan

- Test MemoryScout triggers on preferences, prior decisions, projects, and “based on my setup.”
- Test MemoryScout skips generic factual questions and simple one-off tasks.
- Test reranking so semantic relevance dominates, but importance/recency/access improve ordering.
- Test inactive, archived, pruned, and superseded memories are not injected by default.
- Test Claude Code bridge works without `ANTHROPIC_API_KEY` in the bridge environment.
- Test fallback API adapter separately only if added later.
- Test SQLite-backed dashboard endpoints for table, graph, and event views.
- Test Telegram/iMessage adapters do not bypass dispatcher memory flow.

## Assumptions

- C#/.NET owns the harness, memory, tools, dashboard, and integrations.
- A minimal TypeScript or Python bridge is acceptable only because Claude Agent SDK is officially Python/TypeScript.
- Local SQLite is the first storage backend.
- Blazor is the dashboard stack.
- Initial target is a personal/local harness, not a hosted third-party SaaS.
- Relevant docs:
  - [Claude Agent SDK overview](https://code.claude.com/docs/en/agent-sdk/overview)
  - [Using Claude Code with Pro or Max](https://support.claude.com/en/articles/11145838-using-claude-code-with-your-pro-or-max-plan)
  - [Claude Code cost docs](https://code.claude.com/docs/en/costs)
  - [Claude Code settings/auth behavior](https://code.claude.com/docs/en/settings)
