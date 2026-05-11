import { useQueryClient } from "@tanstack/react-query";
import { FormEvent, KeyboardEvent, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { Activity, ChevronDown, ChevronRight, Eye, EyeOff, Gauge, PanelRightClose, PanelRightOpen, SendHorizontal, Shield } from "lucide-react";
import { getGetMainChatQueryKey, getMainChatResponse, sendMainChatMessage, useGetMainChat } from "../../api/generated";
import type { ChatDashboardMessage, TokenUsageBreakdown } from "../../api/generated";
import { EmptyState, ErrorState, formatLocalDateTime, formatLocalTime, IconButton, LoadingState, StatusChip, TopBar } from "../components";

type TraceListSnapshot = {
  conversationId: string;
  turns: TraceTurnRow[];
};

type TraceTurnRow = {
  id: string;
  conversationId: string;
  title: string;
  status: string;
  startedAt: string;
  completedAt?: string | null;
  stepCount: number;
  toolCallCount: number;
  memoryCount: number;
  agentRunCount: number;
  errorCount: number;
  summary: string;
};

type TraceDetailSnapshot = {
  turn: TraceTurnRow;
  provider: string;
  model: string;
  tokens: TokenUsageSummary;
  steps: TraceStepRow[];
  promptSections: TracePromptSection[];
  memories: TraceMemoryRow[];
  tools: TraceToolCallRow[];
  agents: TraceAgentRunRow[];
  rawEvents: RunEventRow[];
  updatedAt: string;
};

type TokenUsageSummary = {
  promptTokens: number;
  completionTokens: number;
  totalTokens: number;
  mainContextTokens: number;
  contextWindowTokens: number;
  remainingContextTokens: number;
  compactionThresholdTokens: number;
  remainingUntilCompactionTokens: number;
  source: string;
};

type TraceStepRow = {
  id: string;
  kind: string;
  phase: string;
  title: string;
  summary: string;
  createdAt: string;
  status: string;
  isError: boolean;
};

type TracePromptSection = {
  id: string;
  title: string;
  redactionState: string;
  summary: string;
  content?: string | null;
};

type TraceMemoryRow = {
  id: string;
  action: string;
  segment: string;
  tier: string;
  redactionState: string;
  summary: string;
  text?: string | null;
  reason: string;
};

type TraceToolCallRow = {
  id: string;
  name: string;
  status: string;
  redactionState: string;
  argumentsSummary: string;
  arguments?: string | null;
  outputSummary: string;
  output?: string | null;
  isError: boolean;
};

type TraceAgentRunRow = {
  id: string;
  status: string;
  kind: string;
  channel: string;
  redactionState: string;
  taskSummary: string;
  task?: string | null;
  finalResponse?: string | null;
  error?: string | null;
  startedAt: string;
  completedAt?: string | null;
};

type RunEventRow = {
  id: string;
  kind: string;
  phase: string;
  conversationId: string;
  createdAt: string;
  summary: string;
  metadata: Record<string, string>;
  isError: boolean;
};

const inspectorTabs = ["Overview", "Timeline", "Context", "Prompts", "Tools", "Agents", "Memory", "Raw"] as const;
type InspectorTab = typeof inspectorTabs[number];

export function ChatPage() {
  const queryClient = useQueryClient();
  const chatQuery = useGetMainChat({
    query: {
      refetchInterval: 2500
    }
  });
  const [prompt, setPrompt] = useState("");
  const [pendingPrompt, setPendingPrompt] = useState<string | null>(null);
  const [queuedPrompts, setQueuedPrompts] = useState<string[]>([]);
  const [streamedText, setStreamedText] = useState("");
  const [isStreaming, setIsStreaming] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [traces, setTraces] = useState<TraceTurnRow[]>([]);
  const [selectedTurnId, setSelectedTurnId] = useState<string | null>(null);
  const [inspectorOpen, setInspectorOpen] = useState(false);
  const transcriptRef = useRef<HTMLDivElement | null>(null);
  const composerRef = useRef<HTMLTextAreaElement | null>(null);
  const previousLastMessageKeyRef = useRef<string | null>(null);
  const snapshot = chatQuery.data?.data;
  const messages = useMemo(() => {
    const loaded = (snapshot?.messages ?? []).filter((x) => !isWorkMessage(x));
    const optimistic = [...loaded];

    if (pendingPrompt && !hasLoadedPrompt(loaded, pendingPrompt)) {
      optimistic.push({
        id: `pending:user:${pendingPrompt}`,
        role: "You",
        content: pendingPrompt,
        htmlContent: pendingPrompt,
        createdAt: new Date().toISOString()
      });
    }

    queuedPrompts.forEach((queuedPrompt, index) => {
      if (!hasLoadedPrompt(loaded, queuedPrompt)) {
        optimistic.push({
          id: `queued:user:${index}:${queuedPrompt}`,
          role: "You",
          content: queuedPrompt,
          htmlContent: queuedPrompt,
          createdAt: new Date().toISOString()
        });
      }
    });

    if (streamedText) {
      optimistic.push({
        id: "pending:assistant",
        role: "Assistant",
        content: streamedText,
        htmlContent: streamedText,
        createdAt: new Date().toISOString()
      });
    }

    return optimistic;
  }, [pendingPrompt, queuedPrompts, snapshot?.messages, streamedText]);
  const tracesById = useMemo(() => new Map(traces.map((x) => [x.id, x])), [traces]);
  const selectedTrace = selectedTurnId ? tracesById.get(selectedTurnId) ?? traces[0] : traces[0];
  const lastMessageKey = getLastMessageKey(messages);

  useEffect(() => {
    void loadTraces();
  }, [snapshot?.conversationId, isStreaming]);

  useEffect(() => {
    if (isStreaming || queuedPrompts.length === 0) {
      return;
    }

    const [nextPrompt, ...remainingPrompts] = queuedPrompts;
    setQueuedPrompts(remainingPrompts);
    void sendPromptValue(nextPrompt);
  }, [isStreaming, queuedPrompts]);

  useLayoutEffect(() => {
    if (messages.length === 0) {
      previousLastMessageKeyRef.current = lastMessageKey;
      return;
    }

    if (previousLastMessageKeyRef.current === lastMessageKey) {
      return;
    }

    previousLastMessageKeyRef.current = lastMessageKey;
    scrollTranscriptToBottom();
  }, [lastMessageKey, messages.length]);

  async function loadTraces() {
    const response = await fetch("/api/dashboard/traces?conversationId=main&limit=80");

    if (!response.ok) {
      return;
    }

    const data = await response.json() as TraceListSnapshot;
    setTraces(data.turns);

    if (!selectedTurnId && data.turns.length > 0) {
      setSelectedTurnId(data.turns[0].id);
    }
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    const value = prompt.trim();

    if (!value) {
      return;
    }

    setPrompt("");

    if (isStreaming) {
      setQueuedPrompts((x) => [...x, value]);
      focusComposer();

      return;
    }

    await sendPromptValue(value);
  }

  async function sendPromptValue(value: string) {
    setPendingPrompt(value);
    setStreamedText("");
    setIsStreaming(true);
    setErrorMessage(null);

    try {
      const response = await fetch("/api/dashboard/chat/main/stream", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ prompt: value })
      });

      if (!response.ok || !response.body) {
        const fallbackResponse = await sendMainChatMessage({ prompt: value });
        setErrorMessage(fallbackResponse.data.errorMessage);
        queryClient.setQueryData<getMainChatResponse>(getGetMainChatQueryKey(), {
          data: fallbackResponse.data.snapshot,
          status: fallbackResponse.status,
          headers: fallbackResponse.headers
        });

        return;
      }

      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let done = false;

      while (!done) {
        const result = await reader.read();
        done = result.done;

        if (result.value) {
          setStreamedText((x) => x + decoder.decode(result.value, { stream: !done }));
        }
      }

      const snapshotResponse = await fetch("/api/dashboard/chat/main");
      const snapshotData = await snapshotResponse.json();
      queryClient.setQueryData<getMainChatResponse>(getGetMainChatQueryKey(), {
        data: snapshotData,
        status: 200,
        headers: snapshotResponse.headers
      });
      await loadTraces();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Message send failed.");
    } finally {
      setPendingPrompt(null);
      setStreamedText("");
      setIsStreaming(false);
      focusComposer();
    }
  }

  function handleKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      event.currentTarget.form?.requestSubmit();
    }
  }

  function inspect(turnId: string) {
    setSelectedTurnId(turnId);
    setInspectorOpen(true);
  }

  function focusComposer() {
    requestAnimationFrame(() => {
      composerRef.current?.focus();
    });
  }

  function scrollTranscriptToBottom() {
    const transcript = transcriptRef.current;

    if (!transcript || messages.length === 0) {
      return;
    }

    const scroll = () => {
      const currentTranscript = transcriptRef.current;

      if (!currentTranscript) {
        return;
      }

      currentTranscript.scrollTo({
        top: currentTranscript.scrollHeight - currentTranscript.clientHeight,
        behavior: "auto"
      });
    };

    scroll();
    requestAnimationFrame(scroll);
    window.setTimeout(scroll, 80);
    window.setTimeout(scroll, 180);
  }

  return (
    <section className={`workspace chat-workspace ${inspectorOpen ? "inspector-visible" : ""}`}>
      <div className="pane pane-chat">
        <TopBar
          eyebrow="Chat"
          title="MainAgent"
          meta={snapshot && (
            <>
              <StatusChip label={snapshot.isRunning || isStreaming ? "processing" : "online"} tone={snapshot.isRunning || isStreaming ? "green" : "blue"} />
              <span>{snapshot.provider}</span>
              <strong>{snapshot.model}</strong>
              <TokenUsageHoverCard tokens={snapshot.tokens} tokenUsage={snapshot.tokenUsage ?? []} />
            </>
          )}
          actions={(
            <IconButton
              aria-expanded={inspectorOpen}
              aria-controls="process-inspector"
              onClick={() => setInspectorOpen((x) => !x)}
              title={inspectorOpen ? "Hide process inspector" : "Show process inspector"}
              type="button"
            >
              {inspectorOpen ? <PanelRightClose size={17} /> : <PanelRightOpen size={17} />}
            </IconButton>
          )}
        />

        <div className="transcript" ref={transcriptRef}>
          {chatQuery.isLoading && <LoadingState />}
          {chatQuery.isError && <ErrorState error={chatQuery.error} />}
          {!chatQuery.isLoading && !chatQuery.isError && messages.length === 0 && (
            <EmptyState title="No messages yet" body="Start the main local-web conversation." />
          )}
          {messages.map((message) => {
            const trace = tracesById.get(message.id);

            return (
              <article className={`message ${getMessageClass(message.role)}`} key={message.id}>
                <header>
                  <span className="avatar-square">{getAvatar(message.role)}</span>
                  <strong>{getRoleLabel(message.role)}</strong>
                  <time>{formatLocalTime(message.createdAt)}</time>
                </header>
                <MessageBody content={message.content} htmlContent={message.htmlContent} />
                {trace && (
                  <button className="activity-pill" onClick={() => inspect(trace.id)} type="button">
                    <Activity size={13} />
                    <span>{trace.summary}</span>
                    <small>{trace.stepCount} steps</small>
                  </button>
                )}
              </article>
            );
          })}
        </div>

        {errorMessage && <div className="callout error">{errorMessage}</div>}

        <form className="composer" onSubmit={submit}>
          <textarea
            ref={composerRef}
            onChange={(event) => setPrompt(event.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Message the agent"
            value={prompt}
          />
          <div className="composer-actions">
            <div className="composer-state" aria-live="polite">
              <span className={`status-square ${isStreaming ? "active" : ""}`} />
              <span>{getComposerState(isStreaming, queuedPrompts.length)}</span>
            </div>
            <IconButton disabled={!prompt.trim()} title={isStreaming ? "Queue message" : "Send message"} type="submit">
              <SendHorizontal size={17} />
            </IconButton>
          </div>
        </form>
      </div>

      {inspectorOpen && (
        <ProcessInspector
          onClose={() => setInspectorOpen(false)}
          selectedTurn={selectedTrace}
          turnId={selectedTrace?.id ?? selectedTurnId}
        />
      )}
    </section>
  );
}

function ProcessInspector({
  onClose,
  selectedTurn,
  turnId
}: {
  onClose: () => void;
  selectedTurn?: TraceTurnRow;
  turnId?: string | null;
}) {
  const [activeTab, setActiveTab] = useState<InspectorTab>("Overview");
  const [detail, setDetail] = useState<TraceDetailSnapshot | null>(null);
  const [exact, setExact] = useState(false);
  const [error, setError] = useState<Error | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  useEffect(() => {
    if (!turnId) {
      setDetail(null);
      return;
    }

    void load();
  }, [turnId, exact]);

  async function load() {
    if (!turnId) {
      return;
    }

    setIsLoading(true);
    setError(null);

    try {
      const response = await fetch(`/api/dashboard/traces/${turnId}?exact=${exact}`);

      if (!response.ok) {
        throw new Error(`Trace request failed: ${response.status}`);
      }

      setDetail(await response.json() as TraceDetailSnapshot);
    } catch (caught) {
      setError(caught instanceof Error ? caught : new Error(String(caught)));
    } finally {
      setIsLoading(false);
    }
  }

  const turn = detail?.turn ?? selectedTurn;

  return (
    <aside className="process-inspector" id="process-inspector" aria-label="Process inspector">
      <header className="inspector-top">
        <div>
          <p className="eyebrow">Process</p>
          <h2>{turn?.title ?? "No turn selected"}</h2>
        </div>
        <div className="row-actions">
          <button className="secondary-action" onClick={() => setExact((x) => !x)} type="button">
            {exact ? <EyeOff size={14} /> : <Eye size={14} />}
            {exact ? "Hide exact" : "Reveal exact"}
          </button>
          <IconButton onClick={onClose} title="Close inspector" type="button">
            <PanelRightClose size={15} />
          </IconButton>
        </div>
      </header>
      {turn && (
        <div className="inspector-summary">
          <StatusChip label={turn.status} tone={turn.status === "Failed" ? "red" : turn.status === "Running" ? "green" : "blue"} />
          <span>{turn.summary}</span>
          <small>{formatLocalDateTime(turn.startedAt)}</small>
        </div>
      )}
      <div className="inspector-tabs" role="tablist" aria-label="Trace detail tabs">
        {inspectorTabs.map((tab) => (
          <button className={activeTab === tab ? "active" : ""} key={tab} onClick={() => setActiveTab(tab)} role="tab" type="button">
            {tab}
          </button>
        ))}
      </div>
      <div className="inspector-body">
        {isLoading && <LoadingState />}
        {error && <ErrorState error={error} />}
        {!isLoading && !error && !detail && <p className="muted">Select a prompt to inspect its process.</p>}
        {detail && <InspectorTabContent detail={detail} exact={exact} tab={activeTab} />}
      </div>
    </aside>
  );
}

function InspectorTabContent({ detail, exact, tab }: { detail: TraceDetailSnapshot; exact: boolean; tab: InspectorTab }) {
  if (tab === "Overview") {
    return (
      <div className="inspector-stack">
        <MetricStrip
          items={[
            ["Provider", detail.provider],
            ["Model", detail.model],
            ["Steps", String(detail.turn.stepCount)],
            ["Tokens", formatTokens(detail.tokens.totalTokens)]
          ]}
        />
        <section className="inspector-card">
          <h3>Result</h3>
          <p>{detail.steps.find((x) => x.isError)?.summary ?? detail.turn.summary}</p>
        </section>
      </div>
    );
  }

  if (tab === "Timeline") {
    return <TimelineRows steps={detail.steps} />;
  }

  if (tab === "Context") {
    return (
      <div className="inspector-stack">
        {detail.promptSections.filter((x) => x.title === "Instruction sources").map((section) => (
          <DisclosureBlock key={section.id} title={section.title} summary={section.summary} content={section.content} />
        ))}
        <section className="inspector-card">
          <h3>Context summary</h3>
          <p>{detail.memories.length} memory events, {detail.tools.length} tools, {detail.agents.length} agent runs.</p>
        </section>
      </div>
    );
  }

  if (tab === "Prompts") {
    return (
      <div className="inspector-stack">
        {detail.promptSections.map((section) => (
          <DisclosureBlock key={section.id} title={section.title} summary={section.summary} content={section.content} redactionState={section.redactionState} />
        ))}
        {!exact && <p className="redaction-note"><Shield size={14} /> Exact prompt text is redacted until revealed.</p>}
      </div>
    );
  }

  if (tab === "Tools") {
    return (
      <div className="inspector-stack">
        {detail.tools.length === 0 && <p className="muted">No tool calls for this turn.</p>}
        {detail.tools.map((tool) => (
          <section className={`inspector-card ${tool.isError ? "is-error" : ""}`} key={tool.id}>
            <h3>{tool.name}</h3>
            <p>{tool.argumentsSummary || "No argument summary."}</p>
            <DisclosureBlock title="Output" summary={tool.outputSummary || tool.status} content={tool.output} redactionState={tool.redactionState} />
          </section>
        ))}
      </div>
    );
  }

  if (tab === "Agents") {
    return (
      <div className="inspector-stack">
        {detail.agents.length === 0 && <p className="muted">No spawned agent runs for this turn.</p>}
        {detail.agents.map((agent) => (
          <section className={`inspector-card ${agent.error ? "is-error" : ""}`} key={agent.id}>
            <h3>{agent.kind} - {agent.status}</h3>
            <p>{agent.taskSummary}</p>
            {agent.finalResponse && <pre>{agent.finalResponse}</pre>}
            {agent.error && <p className="danger">{agent.error}</p>}
          </section>
        ))}
      </div>
    );
  }

  if (tab === "Memory") {
    return (
      <div className="inspector-stack">
        {detail.memories.length === 0 && <p className="muted">No memory activity for this turn.</p>}
        {detail.memories.map((memory) => (
          <section className="inspector-card" key={`${memory.action}:${memory.id}`}>
            <h3>{memory.action}</h3>
            <p>{memory.summary}</p>
            <small>{memory.segment} / {memory.tier} / {memory.reason}</small>
            {memory.text && <pre>{memory.text}</pre>}
          </section>
        ))}
      </div>
    );
  }

  return (
    <div className="inspector-stack">
      {detail.rawEvents.map((event) => (
        <DisclosureBlock
          key={event.id}
          title={`${event.phase} - ${event.kind}`}
          summary={event.summary}
          content={JSON.stringify(event.metadata, null, 2)}
        />
      ))}
    </div>
  );
}

function TimelineRows({ steps }: { steps: TraceStepRow[] }) {
  return (
    <div className="trace-step-list">
      {steps.map((step) => (
        <article className={`trace-step ${step.isError ? "is-error" : ""}`} key={step.id}>
          <span className={`status-square ${step.isError ? "error" : ""}`} />
          <time>{formatLocalTime(step.createdAt)}</time>
          <div>
            <strong>{step.title}</strong>
            <small>{step.phase} / {step.kind}</small>
            <p>{step.summary}</p>
          </div>
        </article>
      ))}
    </div>
  );
}

function DisclosureBlock({
  title,
  summary,
  content,
  redactionState
}: {
  title: string;
  summary: string;
  content?: string | null;
  redactionState?: string;
}) {
  const [open, setOpen] = useState(false);
  const hasContent = Boolean(content);

  return (
    <section className="inspector-card">
      <button className="disclosure-button" disabled={!hasContent} onClick={() => setOpen((x) => !x)} type="button">
        {open ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
        <span>{title}</span>
        {redactionState && <small>{redactionState}</small>}
      </button>
      <p>{summary}</p>
      {open && content && <pre>{content}</pre>}
    </section>
  );
}

function MetricStrip({ items }: { items: Array<[string, string]> }) {
  return (
    <div className="metric-strip">
      {items.map(([label, value]) => (
        <div key={label}>
          <span>{label}</span>
          <strong>{value}</strong>
        </div>
      ))}
    </div>
  );
}

function getMessageClass(role: string) {
  return normalizeRole(role) === "you" ? "from-user" : "from-agent";
}

function getAvatar(role: string) {
  return normalizeRole(role) === "you" ? "Y" : "AI";
}

function getRoleLabel(role: string) {
  return normalizeRole(role) === "assistant" ? "Main agent" : role;
}

function isWorkMessage(message: ChatDashboardMessage) {
  const role = normalizeRole(message.role);

  return role === "tool"
    || role === "sub-agent"
    || role === "subagent"
    || message.content.startsWith("Sub-agent ")
    || message.content.startsWith("Sub-agent run ");
}

function normalizeRole(role: string) {
  return role.trim().toLowerCase().replaceAll("\u2011", "-").replaceAll("\u2013", "-").replaceAll("\u2014", "-");
}

function hasLoadedPrompt(messages: ChatDashboardMessage[], prompt: string) {
  return messages.some((x) => x.role === "You" && x.content.trim() === prompt);
}

function getLastMessageKey(messages: ChatDashboardMessage[]) {
  const message = messages.at(-1);

  return message
    ? `${message.id}:${message.createdAt}`
    : "empty";
}

function getComposerState(isStreaming: boolean, queuedCount: number) {
  if (isStreaming && queuedCount > 0) {
    return `Working - ${queuedCount} queued`;
  }

  if (isStreaming) {
    return "Working - new messages will queue";
  }

  if (queuedCount > 0) {
    return `${queuedCount} queued`;
  }

  return "Ready";
}

function MessageBody({ content, htmlContent }: { content: string; htmlContent: string }) {
  if (htmlContent && htmlContent !== content) {
    return <div className="message-body markdown-body" dangerouslySetInnerHTML={{ __html: htmlContent }} />;
  }

  return <p className="message-body">{content}</p>;
}

function TokenUsageHoverCard({
  tokens,
  tokenUsage
}: {
  tokens: {
    totalTokens: number | string;
    mainContextTokens: number | string;
    contextWindowTokens: number | string;
    remainingUntilCompactionTokens: number | string;
    source: string;
  };
  tokenUsage: TokenUsageBreakdown[];
}) {
  const codexUsage = getProviderUsage(tokenUsage, "Codex");
  const geminiUsage = getProviderUsage(tokenUsage, "Gemini");
  const contextWindowTokens = Number(tokens.contextWindowTokens);
  const contextUsedPercent = contextWindowTokens > 0
    ? Math.min(100, Math.round((Number(tokens.mainContextTokens) / contextWindowTokens) * 100))
    : 0;

  return (
    <div className="usage-hover-card">
      <button className="usage-trigger" type="button" aria-label="Show token usage">
        <Gauge size={14} />
        <span>{formatTokens(tokens.mainContextTokens)}</span>
        <small>{contextUsedPercent}% ctx</small>
      </button>
      <div className="usage-popout" role="tooltip">
        <div className="usage-popout-header">
          <span>Usage</span>
          <strong>{formatTokens(tokens.mainContextTokens)} / {formatTokens(tokens.contextWindowTokens)}</strong>
        </div>
        <div className="usage-meter" aria-hidden="true">
          <span style={{ width: `${contextUsedPercent}%` }} />
        </div>
        <dl className="usage-summary-grid">
          <div>
            <dt>Context</dt>
            <dd>{formatTokens(tokens.mainContextTokens)}</dd>
          </div>
          <div>
            <dt>Until compact</dt>
            <dd>{formatTokens(tokens.remainingUntilCompactionTokens)}</dd>
          </div>
        </dl>
        <div className="usage-provider-list">
          <ProviderUsageRow label="Codex" usage={codexUsage} showRequests={false} />
          <ProviderUsageRow label="Gemini" usage={geminiUsage} showRequests />
        </div>
        <div className="usage-source">{tokens.source}</div>
      </div>
    </div>
  );
}

function ProviderUsageRow({ label, usage, showRequests }: { label: string; usage?: TokenUsageBreakdown; showRequests: boolean }) {
  return (
    <div className="usage-provider-row">
      <span>{label}</span>
      <strong>{formatTokens(usage?.totalTokens ?? 0)} tokens</strong>
      {showRequests && <small>{formatTokens(usage?.requestCount ?? 0)} requests</small>}
      {!showRequests && <small>{usage?.source ?? "estimate"}</small>}
    </div>
  );
}

function getProviderUsage(tokenUsage: TokenUsageBreakdown[] | undefined, provider: string) {
  return tokenUsage?.find((x) => x.provider.toLowerCase() === provider.toLowerCase());
}

function formatTokens(value: number | string) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(Number(value));
}
