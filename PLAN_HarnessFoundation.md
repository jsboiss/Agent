# MainAgent Harness Foundations Plan

## Summary

Implement the next harness layer around MainAgent’s core goal: one durable personal agent conversation that can continue from dashboard, iMessage, Telegram, or local web, with C# owning orchestration, memory, resources, tools, settings, events, and compaction.

Reasoning: Pi’s useful ideas are harness patterns, not ownership. MainAgent should adopt the durable session, event, resource, queue, and tool patterns while keeping provider adapters thin and keeping SQLite/C# as the source of truth.

## Key Changes

### Conversation Continuity

- [x] Add a channel-neutral conversation model with `Conversation`, `ConversationEntry`, and `ConversationKind`.
- [x] `ConversationKind` should include `Main`, `Branch`, and `SubAgent`.
- [x] Store every user, assistant, tool, and system message as a `ConversationEntry`.
- [x] Add `ParentEntryId` so branches and sub-agent conversations can reference the exact point they came from.
- [x] Keep the main conversation continuous by default across dashboard, iMessage, Telegram, and local web.
- [x] Add a conversation resolver used by all chat channels:
  - [x] dashboard messages continue the active main conversation unless a specific conversation is selected.
  - [x] phone/chat-app messages continue the same main conversation by default.
  - [x] branch/sub-agent conversations are created only by explicit tool/action request.

Reasoning: channels are delivery mechanisms, not conversation boundaries. This prevents “dashboard memory” and “phone memory” from diverging and supports the desired always-available main thread.

### Workspace Context And Resource Loading

- [x] Introduce `WorkspaceContext` instead of Pi-style runtime/cwd terminology.
- [x] `WorkspaceContext` should describe the project/folder the agent is operating in:
  - [x] root path
  - [x] current path
  - [x] project name
  - [x] loaded instructions
  - [x] applicable settings
  - [x] available tools
- [x] Add an `IAgentResourceLoader` responsible for building provider context before each request.
- [x] The resource loader should gather:
  - [x] global agent instructions
  - [x] workspace/project instructions such as `AGENTS.md`
  - [x] channel-specific delivery/style instructions
  - [x] provider constraints
  - [x] prompt templates
  - [x] tool catalogue entries
  - [x] compact memory context
  - [x] current conversation summary

Reasoning: provider prompts should be assembled from explicit resources rather than scattered string-building. This keeps future dashboard, iMessage, Telegram, provider, and project behavior consistent.

### Tool Catalogue

- [x] Replace `AvailableTools: IReadOnlyList<string>` in provider requests with real tool definitions.
- [x] Add `AgentToolDefinition` with:
  - [x] `Name`
  - [x] `Description`
  - [x] JSON parameter schema
  - [x] optional result description
- [x] Keep tool execution in C# through `IAgentToolExecutor`.
- [x] Initial catalogue entries should include:
  - [x] `search_memory`
  - [x] `write_memory`
  - [x] `spawn_agent`
- [x] Update provider adapters so they receive schemas and return structured tool calls.

Reasoning: string tool names do not give providers enough information to call tools reliably. A catalogue lets C# remain the authority while providers get clear tool contracts.

### Event Timeline

- [x] Expand `AgentEventKind` into a richer timeline:
  - [x] message received
  - [x] message persisted
  - [x] provider request started
  - [x] provider text delta
  - [x] provider turn completed
  - [x] provider retry started/completed
  - [x] tool call started/output/completed
  - [x] memory scout started/completed
  - [x] memory injected
  - [x] memory extraction started/completed
  - [x] memory consolidation started/completed
  - [x] compaction started/completed
  - [x] provider error
- [x] Keep events linked by `ConversationId`.
- [x] Include `ConversationEntryId` in event data when the event is tied to a specific message or tool result.

Reasoning: the dashboard should explain what the agent did regardless of which channel initiated the work. Fine-grained events also make debugging providers, memory injection, and sub-agents much easier.

### Prompt Queueing

- [x] Add queued message semantics:
  - [x] `Prompt`: normal message when idle.
  - [x] `Steer`: user correction/instruction while an agent run is active.
  - [x] `FollowUp`: message to process after the current run completes.
- [x] Add a per-conversation queue.
- [x] When a channel receives a message while the conversation is active, classify it as steer or follow-up.
- [x] Start with conservative rules:
  - [x] explicit correction language such as “actually”, “stop”, “instead”, or “use this” becomes `Steer`.
  - [x] additive language such as “also”, “after that”, or “when done” becomes `FollowUp`.
  - [x] otherwise default to `FollowUp` while busy and `Prompt` while idle.

Reasoning: phone messages often arrive while the agent is already working. Separating steering from follow-up avoids losing corrections or accidentally starting parallel work in the same conversation.

### Settings Overrides

- [x] Add layered settings resolution:
  1. [x] app defaults
  2. [x] global user settings
  3. [x] workspace settings
  4. [x] conversation settings
  5. [x] channel settings
  6. [x] per-message overrides
- [x] Settings should cover provider selection, model options, queue behavior, compaction thresholds, memory behavior, and channel delivery preferences.
- [x] Channel settings must not create separate conversation identity by default.

Reasoning: settings need to vary by project, channel, and conversation without hardcoding provider choice inside `AgentMessageProcessor`.

### Compaction And Sub-Agent Threads

- [x] Store the exact conversation log separately from prompt context.
- [x] Add rolling summaries for long-running conversations.
- [x] Keep durable memory extraction separate from summaries.
- [x] For the main conversation:
  - [x] never delete exact entries as part of compaction.
  - [x] use summaries to keep provider prompts bounded.
  - [x] continue extracting durable memory into SQLite.
- [x] For sub-agents:
  - [x] create a child conversation.
  - [x] pass a bounded context package from the main conversation.
  - [x] write the sub-agent result and summary back into the main conversation.
  - [x] avoid copying all sub-agent intermediate entries into the main thread.

Reasoning: the main chat is intended to be continuous over a long period, so prompt context must compact without losing source history. Sub-agents should reduce main-thread noise while still contributing useful results and memories.

## Implementation Order

1. [x] Add conversation persistence abstractions and records.
2. [x] Route `MessageRequest` through conversation resolution instead of treating `ConversationId` as a raw caller-owned value.
3. [x] Add resource loading and `WorkspaceContext`.
4. [x] Replace string tool availability with `AgentToolDefinition`.
5. [x] Expand event kinds and emit the new lifecycle events from `AgentMessageProcessor`.
6. [x] Add queue semantics for busy conversations.
7. [x] Add settings resolution layers.
8. [x] Add conversation summary/compaction interfaces.
9. [x] Add sub-agent child conversation creation and result reporting.

Reasoning: conversation identity must come first because resources, events, queues, settings, and compaction all attach to conversations.

## Test Plan

- Dashboard message starts or continues the main conversation.
- iMessage/Telegram/local web message continues the same main conversation by default.
- A branch or sub-agent creates a child conversation linked to the parent entry.
- Resource loader includes global, workspace, channel, memory, tool, and summary context.
- Provider requests include structured tool definitions instead of string names.
- Tool calls execute through C# and emit start/output/completed events.
- Busy conversation messages classify as steer or follow-up.
- Settings resolve in the documented override order.
- Main conversation compaction creates or updates a summary without deleting exact entries.
- Sub-agent conversations write final summaries/results back to the main conversation.
- Existing scaffolded Ollama provider flow still works after adapting to the new request shape.

## Assumptions And Defaults

- The main conversation is the default target for every channel unless the user explicitly selects another conversation.
- C# and SQLite remain the source of truth for conversation history, memory, settings, tools, and events.
- Provider adapters remain thin and must not own durable memory, channel routing, or conversation identity.
- `cwd` will be documented and exposed as `WorkspaceContext` because that term better matches MainAgent’s product model.
- Initial steer/follow-up classification can be rule-based; an LLM classifier can be added later if needed.
- Compaction summarizes prompt context only; it does not remove exact stored conversation entries.
