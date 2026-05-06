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
}

interface SubAgentRunsSnapshot {
  runs: SubAgentRunRow[];
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
      actions={<IconButton onClick={() => void load()} title="Refresh subagents" type="button"><RefreshCcw size={15} /></IconButton>}
    >
      <div className="runs-layout">
        <div className="timeline">
          {isLoading && <LoadingState />}
          {error && <ErrorState error={error} />}
          {snapshot && runs.length === 0 && <EmptyState title="No subagent runs" body="Subagent runs appear here after Codex uses the spawn_agent tool." />}
          {runs.map((run) => (
            <button className={`timeline-event ${run.error ? "is-error" : ""}`} key={run.id} onClick={() => setSelectedRunId(run.id)} type="button">
              <span className={`status-square ${run.error ? "error" : ""}`} />
              <time>{formatLocalDateTime(run.startedAt)}</time>
              <strong>{run.status}</strong>
              <em>{run.channel}</em>
              <small>{run.prompt}</small>
            </button>
          ))}
        </div>

        <aside className="panel inspector sticky">
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
              <h3>{selectedRun.prompt}</h3>
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
                <dt>Error</dt>
                <dd>{selectedRun.error ?? "none"}</dd>
                <dt>Result</dt>
                <dd>{selectedRun.finalResponse ?? "none"}</dd>
              </dl>
            </>
          )}
        </aside>
      </div>
    </PageFrame>
  );
}
