# UI Refactor Plan

## Summary
Rebuild the app UI as a purpose-built Blazor Server “chat cockpit” and remove the existing MudBlazor UI entirely. The new UI will use custom Razor components, app-specific CSS, typed dashboard state/query services, and a small set of vendored JS libraries for the pieces Blazor does not handle well: graph rendering, split panes, and code highlighting.

The first screen will optimize for active use: chat in the center, live run trace/tool activity beside it, memory/context visibility near the prompt, and fast navigation to memory and observability workspaces.

## Key Changes
- [x] Remove the current UI implementation:
  - [x] Delete/replace the existing MudBlazor pages, layout, placeholder graph, ad hoc event/memory tables, and `chat-input.js`.
  - [x] Remove the `MudBlazor` package reference and `AddMudServices`.
  - [x] Replace `MainLayout`, `Home`, `Memories`, `Events`, `Graph`, `Settings`, `app.css`, and shared imports with custom app components/styles.
- [x] Add a new UI shell:
  - [x] Left rail: Chat, Memories, Runs, Graph, Settings.
  - [x] Main cockpit: persistent chat transcript, composer, live run status, current injected memories, active tools, and current provider/model.
  - [x] Right inspector: selected trace event/tool call/memory details with raw metadata.
  - [x] Use native buttons/forms/tables with custom CSS, not a component kit.
- [x] Introduce targeted vendored libraries under `wwwroot/lib`:
  - [x] `cytoscape.js` for memory graph rendering via JS interop.
  - [x] `split.js` for resizable cockpit panes.
  - [x] `highlight.js` for code block highlighting in assistant messages and tool outputs.
  - [x] Use custom chat rendering; do not add a chat UI library.
- [x] Add `Markdig` as a C# package for assistant markdown rendering before display, with HTML sanitization kept minimal and explicit for local-only trusted output.

## Dashboard State And APIs
- [x] Add a dashboard/application layer so Razor components do not query low-level stores directly:
  - [x] `IChatDashboardService`: load main conversation, send prompt, expose active run state.
  - [x] `IMemoryDashboardService`: search, filter, write, archive, restore, delete, and expose review-friendly memory rows.
  - [x] `IRunTimelineService`: list recent events, group events by turn, expose live trace snapshots.
  - [x] `IMemoryGraphService`: build graph nodes/edges from active and archived memories.
- [x] Replace placeholder `/api/dashboard/*` endpoints with typed JSON endpoints:
  - [x] `GET /api/dashboard/chat/main`
  - [x] `POST /api/dashboard/chat/main/messages`
  - [x] `GET /api/dashboard/runs?conversationId=main`
  - [x] `GET /api/dashboard/memories`
  - [x] `POST /api/dashboard/memories`
  - [x] `PATCH /api/dashboard/memories/{id}/lifecycle`
  - [x] `DELETE /api/dashboard/memories/{id}`
  - [x] `GET /api/dashboard/graph`
- [x] Keep Blazor components using injected services for normal UI work; use the JSON graph endpoint for Cytoscape data loading.
- [x] Add a UI state service scoped to the Blazor circuit for selected conversation, selected run event, selected memory, pane sizes, search text, and active filters.

## Main Screens
- [x] Chat cockpit:
  - [x] Transcript persists across page navigation by loading from `IChatDashboardService`.
  - [x] Composer supports Enter to send, Shift+Enter for newline, disabled/loading state, and queued prompt visibility.
  - [x] Live trace updates while a request is running, grouped into phases: message, memory scout, provider call, tool calls, memory extraction.
  - [x] Tool results are inspectable without polluting assistant chat bubbles.
- [x] Memories workspace:
  - [x] Search, lifecycle filter, segment/tier filters, duplicate indicators, source message link, archive/restore/delete actions.
  - [x] Manual write remains available but visually secondary.
  - [x] Show extracted/review metadata where available.
- [x] Runs workspace:
  - [x] Chronological timeline of turns and events.
  - [x] Filter by provider/tool/memory/error.
  - [x] Event detail inspector with raw key/value metadata.
- [x] Graph workspace:
  - [x] Cytoscape graph with nodes for memories, segments, tiers, source messages, and supersession/related edges where data exists.
  - [x] Empty graph state must explain missing backend graph data without leaving placeholder UI.
- [x] Settings workspace:
  - [x] Read-only current provider/model/memory settings first.
  - [x] Future editable settings can be added later; do not imply controls are editable until wired.

## Test Plan
- [x] Build and static checks:
  - [x] `dotnet build Agent.slnx`
  - [x] `git ls-files --eol | Select-String "w/mixed|i/mixed"`
  - [x] `git diff --check`
- [ ] UI behavior scenarios:
  - [ ] Send a prompt with Enter; verify no newline is inserted.
  - [ ] Use Shift+Enter; verify newline remains in composer.
  - [ ] Send a prompt that triggers memory tools; verify live trace updates before final assistant response.
  - [ ] Navigate Chat -> Memories -> Chat; verify transcript, trace, and selected conversation remain.
  - [ ] Archive, restore, and delete memory; verify list updates and actions visibly change state.
  - [ ] Open Graph with no graph data; verify a real empty state, not placeholder/debug text.
  - [ ] Open Graph with memory data; verify Cytoscape renders nonblank nodes/edges.
- [ ] Regression scenarios:
  - [ ] Provider returns tool call but no final assistant text; chat shows friendly fallback, not raw GUID/tool output.
  - [ ] Provider error appears in run inspector and does not break the chat page.
  - [ ] Existing memory SQLite data remains readable.

## Assumptions
- The UI refactor may add dashboard services/endpoints, not just Razor markup.
- MudBlazor should be fully removed from the app.
- JS dependencies should be vendored under `wwwroot/lib`, pinned, and loaded locally without adding an npm build pipeline.
- The initial graph should visualize available memory metadata only; deeper relationship extraction can come later.
- Conversation persistence remains whatever the current backend provides unless a separate persistence phase is requested.
