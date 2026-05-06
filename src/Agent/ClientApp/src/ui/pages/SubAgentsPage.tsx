import { useEffect, useState } from "react";
import { RefreshCcw } from "lucide-react";
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

export function SubAgentsPage() {
  const [snapshot, setSnapshot] = useState<SubAgentRunsSnapshot | null>(null);
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);
  const runs = snapshot?.runs ?? [];
  const selectedRun = runs.find((x) => x.id === selectedRunId) ?? runs[0];

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
    } catch (caught) {
      setError(caught instanceof Error ? caught : new Error(String(caught)));
    } finally {
      setIsLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

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
              <section className="inspector-message">
                <h3>Result</h3>
                <p>{selectedRun.finalResponse ?? "none"}</p>
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
