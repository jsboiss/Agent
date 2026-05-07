import { useEffect, useState } from "react";
import { Check, Pause, Play, RefreshCcw, Send, Trash2, X } from "lucide-react";
import { EmptyState, ErrorState, formatLocalDateTime, IconButton, LoadingState, PageFrame, Panel, StatusChip } from "../components";

interface TelegramStatus {
  enabled: boolean;
  trustedChatCount: number;
}

interface DraftRow {
  id: string;
  kind: string;
  summary: string;
  payload: string;
  sourceRunId?: string | null;
  conversationId: string;
  channel: string;
  status: string;
  createdAt: string;
  updatedAt: string;
}

interface AutomationRow {
  id: string;
  name: string;
  task: string;
  schedule: string;
  status: string;
  conversationId: string;
  channel: string;
  notificationTarget?: string | null;
  capabilities: string;
  nextRunAt?: string | null;
  lastRunAt?: string | null;
  lastRunId?: string | null;
  lastResult?: string | null;
}

export function OperationsPage() {
  const [telegram, setTelegram] = useState<TelegramStatus | null>(null);
  const [drafts, setDrafts] = useState<DraftRow[]>([]);
  const [automations, setAutomations] = useState<AutomationRow[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);
  const [form, setForm] = useState({
    name: "",
    task: "",
    schedule: "every 1:00:00",
    channel: "local-web",
    capabilities: "ReadOnly,Code"
  });

  async function load() {
    setIsLoading(true);
    setError(null);

    try {
      const [telegramResponse, draftsResponse, automationsResponse] = await Promise.all([
        fetch("/api/dashboard/telegram/status"),
        fetch("/api/dashboard/drafts"),
        fetch("/api/dashboard/automations")
      ]);

      if (!telegramResponse.ok || !draftsResponse.ok || !automationsResponse.ok) {
        throw new Error("Operations request failed.");
      }

      setTelegram(await telegramResponse.json() as TelegramStatus);
      setDrafts(await draftsResponse.json() as DraftRow[]);
      setAutomations(await automationsResponse.json() as AutomationRow[]);
    } catch (caught) {
      setError(caught instanceof Error ? caught : new Error(String(caught)));
    } finally {
      setIsLoading(false);
    }
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

    await load();
  }

  async function createAutomation(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    await post("/api/dashboard/automations", {
      ...form,
      conversationId: "main"
    });
    setForm((current) => ({ ...current, name: "", task: "" }));
  }

  useEffect(() => {
    void load();
  }, []);

  return (
    <PageFrame
      eyebrow="Mobile control"
      title="Operations"
      actions={<IconButton onClick={() => void load()} title="Refresh operations" type="button"><RefreshCcw size={15} /></IconButton>}
    >
      {isLoading && <LoadingState />}
      {error && <ErrorState error={error} />}
      {!isLoading && !error && (
        <div className="settings-grid">
          <Panel title="Telegram">
            <div className="tool-list">
              <StatusChip label={telegram?.enabled ? "enabled" : "disabled"} tone={telegram?.enabled ? "green" : "red"} />
              <span>{telegram?.trustedChatCount ?? 0} trusted chats</span>
            </div>
          </Panel>

          <Panel title="Drafts">
            {drafts.length === 0 && <EmptyState title="No drafts" body="Risky actions staged by agents appear here." />}
            <div className="stack-list">
              {drafts.map((draft) => (
                <article className="compact-row" key={draft.id}>
                  <div>
                    <strong>{draft.summary}</strong>
                    <small>{draft.kind} · {draft.channel} · {formatLocalDateTime(draft.createdAt)}</small>
                  </div>
                  <StatusChip label={draft.status} tone={draft.status === "Pending" ? "blue" : draft.status === "Approved" ? "green" : "red"} />
                  {draft.status === "Pending" && (
                    <div className="row-actions">
                      <IconButton onClick={() => void post(`/api/dashboard/drafts/${draft.id}/approve`)} title="Approve draft" type="button"><Check size={14} /></IconButton>
                      <IconButton onClick={() => void post(`/api/dashboard/drafts/${draft.id}/reject`)} title="Reject draft" type="button"><X size={14} /></IconButton>
                    </div>
                  )}
                </article>
              ))}
            </div>
          </Panel>

          <Panel title="Automations">
            <form className="operation-form" onSubmit={(event) => void createAutomation(event)}>
              <input onChange={(event) => setForm({ ...form, name: event.target.value })} placeholder="Name" required value={form.name} />
              <input onChange={(event) => setForm({ ...form, task: event.target.value })} placeholder="Task" required value={form.task} />
              <input onChange={(event) => setForm({ ...form, schedule: event.target.value })} placeholder="every 1:00:00" required value={form.schedule} />
              <button className="primary-action" type="submit"><Send size={14} />Create</button>
            </form>
            {automations.length === 0 && <EmptyState title="No automations" body="Scheduled sub-agent tasks appear here." />}
            <div className="stack-list">
              {automations.map((automation) => (
                <article className="compact-row" key={automation.id}>
                  <div>
                    <strong>{automation.name}</strong>
                    <small>{automation.schedule} · next {automation.nextRunAt ? formatLocalDateTime(automation.nextRunAt) : "none"}</small>
                  </div>
                  <StatusChip label={automation.status} tone={automation.status === "Enabled" ? "green" : "red"} />
                  <div className="row-actions">
                    <IconButton onClick={() => void post(`/api/dashboard/automations/${automation.id}/toggle`, { enabled: automation.status !== "Enabled" })} title="Toggle automation" type="button">
                      {automation.status === "Enabled" ? <Pause size={14} /> : <Play size={14} />}
                    </IconButton>
                    <IconButton onClick={() => void deleteAutomation(automation.id)} title="Delete automation" type="button"><Trash2 size={14} /></IconButton>
                  </div>
                </article>
              ))}
            </div>
          </Panel>
        </div>
      )}
    </PageFrame>
  );
}
