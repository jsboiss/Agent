import { FormEvent, useEffect, useMemo, useState } from "react";
import { Pause, Play, RefreshCcw, Save, Send, Trash2 } from "lucide-react";
import { EmptyState, ErrorState, formatLocalDateTime, IconButton, LoadingState, PageFrame, StatusChip } from "../components";

type AutomationRow = {
  id: string;
  name: string;
  task: string;
  schedule: string;
  status: string;
  mode: string;
  conversationId: string;
  channel: string;
  notificationTarget?: string | null;
  capabilities: string;
  nextRunAt?: string | null;
  lastRunAt?: string | null;
  lastRunId?: string | null;
  lastResult?: string | null;
  workspaceRootPath?: string | null;
  skillIds?: string | null;
  recentRuns: AutomationRunRow[];
};

type AutomationRunRow = {
  id: string;
  status: string;
  trigger: string;
  outputSummary?: string | null;
  error?: string | null;
  startedAt: string;
  completedAt?: string | null;
};

const emptyForm = {
  id: "",
  name: "",
  task: "",
  schedule: "every 1:00:00",
  mode: "Agent",
  channel: "local-web",
  conversationId: "main",
  notificationTarget: "",
  capabilities: "ReadOnly,Code",
  workspaceRootPath: "",
  skillIds: ""
};

export function AutomationsPage() {
  const [automations, setAutomations] = useState<AutomationRow[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [form, setForm] = useState(emptyForm);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);
  const selected = useMemo(() => automations.find((x) => x.id === selectedId) ?? automations[0], [automations, selectedId]);

  async function load() {
    setIsLoading(true);
    setError(null);

    try {
      const response = await fetch("/api/dashboard/automations");

      if (!response.ok) {
        throw new Error(`Automations request failed: ${response.status}`);
      }

      const data = await response.json() as AutomationRow[];
      setAutomations(data);

      if (!selectedId && data.length > 0) {
        setSelectedId(data[0].id);
      }
    } catch (caught) {
      setError(caught instanceof Error ? caught : new Error(String(caught)));
    } finally {
      setIsLoading(false);
    }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const isEdit = Boolean(form.id);
    const response = await fetch(isEdit ? `/api/dashboard/automations/${form.id}` : "/api/dashboard/automations", {
      method: isEdit ? "PUT" : "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(form)
    });

    if (!response.ok) {
      throw new Error(`Automation save failed: ${response.status}`);
    }

    setForm(emptyForm);
    await load();
  }

  async function post(path: string, body?: unknown) {
    const response = await fetch(path, {
      method: "POST",
      headers: body ? { "Content-Type": "application/json" } : undefined,
      body: body ? JSON.stringify(body) : undefined
    });

    if (!response.ok) {
      throw new Error(`Request failed: ${response.status}`);
    }

    await load();
  }

  async function deleteAutomation(id: string) {
    const response = await fetch(`/api/dashboard/automations/${id}`, { method: "DELETE" });

    if (!response.ok) {
      throw new Error(`Delete failed: ${response.status}`);
    }

    if (selectedId === id) {
      setSelectedId(null);
    }

    await load();
  }

  function edit(automation: AutomationRow) {
    setSelectedId(automation.id);
    setForm({
      id: automation.id,
      name: automation.name,
      task: automation.task,
      schedule: automation.schedule,
      mode: automation.mode,
      channel: automation.channel,
      conversationId: automation.conversationId,
      notificationTarget: automation.notificationTarget ?? "",
      capabilities: automation.capabilities,
      workspaceRootPath: automation.workspaceRootPath ?? "",
      skillIds: automation.skillIds ?? ""
    });
  }

  useEffect(() => {
    void load();
  }, []);

  return (
    <PageFrame
      eyebrow="Scheduled work"
      title="Automations"
      actions={<IconButton onClick={() => void load()} title="Refresh automations" type="button"><RefreshCcw size={15} /></IconButton>}
    >
      {isLoading && <LoadingState />}
      {error && <ErrorState error={error} />}
      {!isLoading && !error && (
        <div className="automation-layout">
          <section className="automation-list" aria-label="Automations">
            {automations.length === 0 && <EmptyState title="No automations" body="Create a scheduled workflow to see it here." />}
            {automations.map((automation) => (
              <button className={`automation-row ${automation.id === selected?.id ? "is-selected" : ""}`} key={automation.id} onClick={() => setSelectedId(automation.id)} type="button">
                <div>
                  <strong>{automation.name}</strong>
                  <small>{automation.schedule} / {automation.mode}</small>
                </div>
                <StatusChip label={automation.status} tone={automation.status === "Enabled" ? "green" : "red"} />
                <time>{automation.nextRunAt ? formatLocalDateTime(automation.nextRunAt) : "No next run"}</time>
              </button>
            ))}
          </section>

          <aside className="panel inspector sticky">
            <div className="panel-heading">
              <div>
                <p className="eyebrow">Automation</p>
                <h2>{selected?.name ?? "New automation"}</h2>
              </div>
              {selected && <StatusChip label={selected.status} tone={selected.status === "Enabled" ? "green" : "red"} />}
            </div>
            <form className="automation-form" onSubmit={(event) => void submit(event)}>
              <input onChange={(event) => setForm({ ...form, name: event.target.value })} placeholder="Name" required value={form.name} />
              <textarea onChange={(event) => setForm({ ...form, task: event.target.value })} placeholder="Task" required value={form.task} />
              <div className="form-grid-two">
                <input onChange={(event) => setForm({ ...form, schedule: event.target.value })} placeholder="every 1:00:00" required value={form.schedule} />
                <select onChange={(event) => setForm({ ...form, mode: event.target.value })} value={form.mode}>
                  <option>Agent</option>
                  <option>Deterministic</option>
                </select>
              </div>
              <div className="form-grid-two">
                <input onChange={(event) => setForm({ ...form, capabilities: event.target.value })} placeholder="Capabilities" value={form.capabilities} />
                <input onChange={(event) => setForm({ ...form, notificationTarget: event.target.value })} placeholder="Notification target" value={form.notificationTarget} />
              </div>
              <input onChange={(event) => setForm({ ...form, workspaceRootPath: event.target.value })} placeholder="Workspace root path" value={form.workspaceRootPath} />
              <input onChange={(event) => setForm({ ...form, skillIds: event.target.value })} placeholder="Skill ids" value={form.skillIds} />
              <button className="primary-action" type="submit"><Save size={14} />{form.id ? "Save changes" : "Create automation"}</button>
            </form>
            {selected && (
              <>
                <div className="row-actions">
                  <button className="secondary-action" onClick={() => void post(`/api/dashboard/automations/${selected.id}/run`)} type="button">
                    <Send size={14} />
                    Run now
                  </button>
                  <button className="secondary-action" onClick={() => void post(`/api/dashboard/automations/${selected.id}/toggle`, { enabled: selected.status !== "Enabled" })} type="button">
                    {selected.status === "Enabled" ? <Pause size={14} /> : <Play size={14} />}
                    {selected.status === "Enabled" ? "Pause" : "Resume"}
                  </button>
                  <button className="secondary-action" onClick={() => edit(selected)} type="button">
                    Edit
                  </button>
                  <button className="secondary-action danger" onClick={() => void deleteAutomation(selected.id)} type="button">
                    <Trash2 size={14} />
                    Delete
                  </button>
                </div>
                <section className="inspector-card">
                  <h3>Last result</h3>
                  <p>{selected.lastResult ?? "No result yet."}</p>
                </section>
                <section className="inspector-card">
                  <h3>Recent runs</h3>
                  {selected.recentRuns.length === 0 && <p className="muted">No recorded runs yet.</p>}
                  {selected.recentRuns.map((run) => (
                    <article className={`run-mini-row ${run.error ? "is-error" : ""}`} key={run.id}>
                      <span className={`status-square ${run.status === "Running" ? "active" : run.error ? "error" : ""}`} />
                      <div>
                        <strong>{run.status} / {run.trigger}</strong>
                        <small>{formatLocalDateTime(run.startedAt)}</small>
                        <p>{run.error ?? run.outputSummary ?? "No output recorded."}</p>
                      </div>
                    </article>
                  ))}
                </section>
              </>
            )}
          </aside>
        </div>
      )}
    </PageFrame>
  );
}
