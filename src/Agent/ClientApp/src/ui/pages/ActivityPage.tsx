import { useEffect, useMemo, useState } from "react";
import { ChevronDown, ChevronRight, RefreshCcw, Search } from "lucide-react";
import { EmptyState, ErrorState, formatLocalDateTime, formatLocalTime, IconButton, LoadingState, PageFrame, StatusChip } from "../components";

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
  steps: TraceStepRow[];
  tools: TraceToolCallRow[];
  memories: TraceMemoryRow[];
  agents: TraceAgentRunRow[];
  rawEvents: RunEventRow[];
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

type TraceToolCallRow = {
  id: string;
  name: string;
  status: string;
  argumentsSummary: string;
  outputSummary: string;
  isError: boolean;
};

type TraceMemoryRow = {
  id: string;
  action: string;
  summary: string;
  reason: string;
};

type TraceAgentRunRow = {
  id: string;
  status: string;
  kind: string;
  taskSummary: string;
};

type RunEventRow = {
  id: string;
  kind: string;
  phase: string;
  createdAt: string;
  summary: string;
  metadata: Record<string, string>;
  isError: boolean;
};

export function ActivityPage() {
  const [snapshot, setSnapshot] = useState<TraceListSnapshot | null>(null);
  const [selectedTurnId, setSelectedTurnId] = useState<string | null>(null);
  const [detail, setDetail] = useState<TraceDetailSnapshot | null>(null);
  const [filter, setFilter] = useState("");
  const [openRaw, setOpenRaw] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);
  const turns = useMemo(() => {
    const values = snapshot?.turns ?? [];

    if (!filter.trim()) {
      return values;
    }

    return values.filter((x) =>
      x.title.includes(filter)
      || x.summary.includes(filter)
      || x.status.includes(filter));
  }, [filter, snapshot?.turns]);
  const selectedTurn = detail?.turn ?? turns.find((x) => x.id === selectedTurnId) ?? turns[0];

  async function load() {
    setIsLoading(true);
    setError(null);

    try {
      const response = await fetch("/api/dashboard/traces?conversationId=main&limit=100");

      if (!response.ok) {
        throw new Error(`Activity request failed: ${response.status}`);
      }

      const data = await response.json() as TraceListSnapshot;
      setSnapshot(data);

      if (!selectedTurnId && data.turns.length > 0) {
        setSelectedTurnId(data.turns[0].id);
      }
    } catch (caught) {
      setError(caught instanceof Error ? caught : new Error(String(caught)));
    } finally {
      setIsLoading(false);
    }
  }

  async function loadDetail(turnId: string) {
    const response = await fetch(`/api/dashboard/traces/${turnId}`);

    if (!response.ok) {
      throw new Error(`Trace detail failed: ${response.status}`);
    }

    setDetail(await response.json() as TraceDetailSnapshot);
  }

  useEffect(() => {
    void load();
  }, []);

  useEffect(() => {
    if (!selectedTurnId) {
      return;
    }

    void loadDetail(selectedTurnId);
  }, [selectedTurnId]);

  return (
    <PageFrame
      eyebrow="Trace history"
      title="Activity"
      actions={<IconButton onClick={() => void load()} title="Refresh activity" type="button"><RefreshCcw size={15} /></IconButton>}
    >
      <div className="activity-toolbar">
        <label className="search-field">
          <Search size={15} />
          <input onChange={(event) => setFilter(event.target.value)} placeholder="Search traces" value={filter} />
        </label>
      </div>
      {isLoading && <LoadingState />}
      {error && <ErrorState error={error} />}
      {!isLoading && !error && (
        <div className="activity-layout">
          <section className="trace-list" aria-label="Prompt traces">
            {turns.length === 0 && <EmptyState title="No activity" body="Prompt traces appear here after a message is processed." />}
            {turns.map((turn) => (
              <button className={`trace-row ${turn.id === selectedTurn?.id ? "is-selected" : ""}`} key={turn.id} onClick={() => setSelectedTurnId(turn.id)} type="button">
                <span className={`status-square ${turn.status === "Failed" ? "error" : turn.status === "Running" ? "active" : ""}`} />
                <div>
                  <strong>{turn.title}</strong>
                  <small>{turn.summary}</small>
                </div>
                <time>{formatLocalDateTime(turn.startedAt)}</time>
                <StatusChip label={turn.status} tone={turn.status === "Failed" ? "red" : turn.status === "Running" ? "green" : "blue"} />
              </button>
            ))}
          </section>

          <aside className="panel inspector sticky">
            {!selectedTurn && <p className="muted">Select a trace to inspect it.</p>}
            {selectedTurn && (
              <>
                <div className="panel-heading">
                  <div>
                    <p className="eyebrow">Selected trace</p>
                    <h2>{selectedTurn.title}</h2>
                  </div>
                  <StatusChip label={selectedTurn.status} tone={selectedTurn.status === "Failed" ? "red" : "blue"} />
                </div>
                <div className="metric-strip">
                  <div><span>Steps</span><strong>{selectedTurn.stepCount}</strong></div>
                  <div><span>Tools</span><strong>{selectedTurn.toolCallCount}</strong></div>
                  <div><span>Memory</span><strong>{selectedTurn.memoryCount}</strong></div>
                </div>
                <div className="trace-step-list">
                  {detail?.steps.map((step) => (
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
                <section className="inspector-card">
                  <h3>Tools, agents, memory</h3>
                  <p>{detail?.tools.length ?? 0} tools, {detail?.agents.length ?? 0} agents, {detail?.memories.length ?? 0} memory entries.</p>
                </section>
                <section className="inspector-card">
                  <button className="disclosure-button" onClick={() => setOpenRaw((x) => !x)} type="button">
                    {openRaw ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
                    <span>Raw events</span>
                  </button>
                  {openRaw && <pre>{JSON.stringify(detail?.rawEvents ?? [], null, 2)}</pre>}
                </section>
              </>
            )}
          </aside>
        </div>
      )}
    </PageFrame>
  );
}
