import { useEffect, useState } from "react";
import { RotateCcw, Square, RefreshCcw } from "lucide-react";
import { EmptyState, ErrorState, formatLocalDateTime, IconButton, LoadingState, PageFrame, StatusChip } from "../components";

interface SubAgentRunRow {
  id: string;
  workspaceId: string;
  status: string;
  kind: string;
  channel: string;
  prompt: string;
  codexThreadId?: string | null;
  parentRunId?: string | null;
  parentCodexThreadId?: string | null;
  childConversationId?: string | null;
  startedAt: string;
  completedAt?: string | null;
  finalResponse?: string | null;
  error?: string | null;
  tokens: TokenUsageSummary;
}

interface SubAgentRunsSnapshot {
  runs: SubAgentRunRow[];
  tokens: TokenUsageSummary;
}

interface TokenUsageSummary {
  promptTokens: number;
  completionTokens: number;
  totalTokens: number;
  mainContextTokens: number;
  contextWindowTokens: number;
  remainingContextTokens: number;
  compactionThresholdTokens: number;
  remainingUntilCompactionTokens: number;
  source: string;
}

interface SubAgentRunDetailSnapshot {
  run: SubAgentRunRow;
  childConversationId?: string | null;
  transcriptAvailable: boolean;
  transcriptUnavailableReason?: string | null;
  transcript: SubAgentTranscriptEntry[];
  updatedAt: string;
}

interface SubAgentTranscriptEntry {
  id: string;
  kind: string;
  role: string;
  title: string;
  content: string;
  createdAt: string;
  isError: boolean;
  metadata: Record<string, string>;
}

export function SubAgentsPage() {
  const [snapshot, setSnapshot] = useState<SubAgentRunsSnapshot | null>(null);
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null);
  const [detail, setDetail] = useState<SubAgentRunDetailSnapshot | null>(null);
  const [detailError, setDetailError] = useState<Error | null>(null);
  const [isStreaming, setIsStreaming] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);
  const runs = snapshot?.runs ?? [];
  const selectedRun = detail?.run ?? runs.find((x) => x.id === selectedRunId) ?? runs[0];

  async function load() {
    setIsLoading(true);
    setError(null);

    try {
      const response = await fetch("/api/dashboard/subagents");

      if (!response.ok) {
        throw new Error(`Request failed: ${response.status}`);
      }

      const data = await response.json() as SubAgentRunsSnapshot;
      setSnapshot(data);

      if (!selectedRunId && data.runs.length > 0) {
        setSelectedRunId(data.runs[0].id);
      }
    } catch (caught) {
      setError(caught instanceof Error ? caught : new Error(String(caught)));
    } finally {
      setIsLoading(false);
    }
  }

  async function runAction(path: string) {
    const response = await fetch(path, { method: "POST" });

    if (!response.ok) {
      throw new Error(`Run action failed: ${response.status}`);
    }

    await load();
  }

  async function loadDetail(runId: string) {
    try {
      const response = await fetch(`/api/dashboard/subagents/${runId}`);

      if (!response.ok) {
        throw new Error(`Detail request failed: ${response.status}`);
      }

      const data = await response.json() as SubAgentRunDetailSnapshot;
      setDetail(data);
      setDetailError(null);
    } catch (caught) {
      setDetailError(caught instanceof Error ? caught : new Error(String(caught)));
    }
  }

  useEffect(() => {
    void load();
  }, []);

  useEffect(() => {
    if (!selectedRunId) {
      setDetail(null);
      setDetailError(null);
      setIsStreaming(false);
      return;
    }

    setDetail(null);
    setDetailError(null);
    setIsStreaming(true);

    const source = new EventSource(`/api/dashboard/subagents/${selectedRunId}/stream`);

    function handleMessage(event: MessageEvent<string>) {
      const data = JSON.parse(event.data) as SubAgentRunDetailSnapshot;
      setDetail(data);
      setDetailError(null);

      if (isTerminal(data.run.status)) {
        setIsStreaming(false);
        source.close();
      }
    }

    source.addEventListener("snapshot", handleMessage as EventListener);
    source.addEventListener("done", handleMessage as EventListener);
    source.onerror = () => {
      setIsStreaming(false);
      source.close();
      void loadDetail(selectedRunId);
    };

    return () => {
      source.close();
      setIsStreaming(false);
    };
  }, [selectedRunId]);

  return (
    <PageFrame
      eyebrow="Background workers"
      title="Subagents"
      actions={(
        <>
          {snapshot && <StatusChip label={`${formatTokens(snapshot.tokens.totalTokens)} tokens`} tone="blue" />}
          <IconButton onClick={() => void load()} title="Refresh subagents" type="button"><RefreshCcw size={15} /></IconButton>
        </>
      )}
    >
      <div className="runs-layout">
        <div className="subagent-list">
          {isLoading && <LoadingState />}
          {error && <ErrorState error={error} />}
          {snapshot && runs.length === 0 && <EmptyState title="No subagent runs" body="Subagent runs appear here after Codex uses the spawn_agent tool." />}
          {runs.length > 0 && (
            <div className="subagent-list-header">
              <span>Status</span>
              <span>Started</span>
              <span>Channel</span>
              <span>Task</span>
              <span>Tokens</span>
            </div>
          )}
          {runs.map((run) => (
            <button
              className={`subagent-row ${run.id === selectedRun?.id ? "is-selected" : ""} ${run.error ? "is-error" : ""}`}
              key={run.id}
              onClick={() => setSelectedRunId(run.id)}
              type="button"
            >
              <span><span className={`status-square ${run.error ? "error" : run.status === "Running" ? "active" : ""}`} />{run.status}</span>
              <time>{formatLocalDateTime(run.startedAt)}</time>
              <em>{run.channel}</em>
              <strong title={run.prompt}>{run.prompt}</strong>
              <small>{formatTokens(run.tokens.totalTokens)}</small>
            </button>
          ))}
        </div>

        <aside className="panel inspector subagent-inspector sticky">
          <div className="panel-heading">
            <div>
              <p className="eyebrow">Run detail</p>
              <h2>Inspector</h2>
            </div>
            {selectedRun && <StatusChip label={selectedRun.status} tone={selectedRun.error ? "red" : selectedRun.status === "Completed" ? "green" : "blue"} />}
          </div>
          {!selectedRun && <p className="muted">Select a subagent run to inspect it.</p>}
          {selectedRun && (
            <>
              <div className="row-actions">
                {(selectedRun.status === "Created" || selectedRun.status === "Running") && (
                  <button className="secondary-action" onClick={() => void runAction(`/api/dashboard/runs/${selectedRun.id}/cancel`)} type="button">
                    <Square size={14} />
                    Cancel
                  </button>
                )}
                <button className="secondary-action" onClick={() => void runAction(`/api/dashboard/runs/${selectedRun.id}/retry`)} type="button">
                  <RotateCcw size={14} />
                  Retry
                </button>
              </div>
              <section className="inspector-message">
                <h3>Task</h3>
                <p>{selectedRun.prompt}</p>
              </section>
              <dl className="metadata-grid">
                <dt>Run</dt>
                <dd>{selectedRun.id}</dd>
                <dt>Workspace</dt>
                <dd>{selectedRun.workspaceId}</dd>
                <dt>Codex thread</dt>
                <dd>{selectedRun.codexThreadId ?? "none"}</dd>
                <dt>Started</dt>
                <dd>{formatLocalDateTime(selectedRun.startedAt)}</dd>
                <dt>Completed</dt>
                <dd>{selectedRun.completedAt ? formatLocalDateTime(selectedRun.completedAt) : "running"}</dd>
                <dt>Tokens</dt>
                <dd>{formatTokens(selectedRun.tokens.totalTokens)} total, {formatTokens(selectedRun.tokens.remainingUntilCompactionTokens)} until compaction</dd>
                <dt>Token source</dt>
                <dd>{selectedRun.tokens.source}</dd>
                <dt>Error</dt>
                <dd>{selectedRun.error ?? "none"}</dd>
              </dl>
              <section className="subagent-transcript">
                <div className="transcript-heading">
                  <h3>Live transcript</h3>
                  <span className={`status-square ${isStreaming ? "active" : ""}`} />
                </div>
                {detailError && <ErrorState error={detailError} />}
                {!detail && !detailError && <LoadingState />}
                {detail?.transcriptUnavailableReason && <p className="muted">{detail.transcriptUnavailableReason}</p>}
                {detail && detail.transcript.length === 0 && <p className="muted">No visible transcript entries are available yet.</p>}
                {detail?.transcript.map((entry) => (
                  <article className={`transcript-entry ${entry.isError ? "is-error" : ""}`} key={entry.id}>
                    <header>
                      <strong>{entry.title}</strong>
                      <span>{entry.role}</span>
                      <time>{formatLocalDateTime(entry.createdAt)}</time>
                    </header>
                    <p>{entry.content || entry.kind}</p>
                  </article>
                ))}
              </section>
            </>
          )}
        </aside>
      </div>
    </PageFrame>
  );
}

function formatTokens(value: number) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(value);
}

function isTerminal(status: string) {
  return status === "Completed" || status === "Failed" || status === "Cancelled";
}
