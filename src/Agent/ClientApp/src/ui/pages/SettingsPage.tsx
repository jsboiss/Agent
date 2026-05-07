import { type FormEvent, useState } from "react";
import { CalendarDays, Copy, DatabaseZap, FolderInput, ShieldCheck, ShieldOff, Sparkles, Trash2 } from "lucide-react";
import { useGetSettings } from "../../api/generated";
import { ErrorState, IconButton, LoadingState, PageFrame, Panel } from "../components";

type WorkspaceSettings = {
  id: string;
  name: string;
  rootPath: string;
  remoteExecutionAllowed: boolean;
};

type SettingsSnapshotWithWorkspace = {
  workspace?: WorkspaceSettings;
  calendar?: CalendarSettings;
};

type CalendarSettings = {
  configured: boolean;
  connected: boolean;
  accountEmail?: string | null;
  updatedAt?: string | null;
};

export function SettingsPage() {
  const settingsQuery = useGetSettings();
  const snapshot = settingsQuery.data?.data;
  const workspace = (snapshot as (typeof snapshot & SettingsSnapshotWithWorkspace) | undefined)?.workspace;
  const calendar = (snapshot as (typeof snapshot & SettingsSnapshotWithWorkspace) | undefined)?.calendar;
  const values = snapshot?.values ?? {};
  const appliedLayers = snapshot?.appliedLayers ?? [];
  const [isCompacting, setIsCompacting] = useState(false);
  const [compactionResult, setCompactionResult] = useState<string | null>(null);
  const [compactionError, setCompactionError] = useState<string | null>(null);
  const [maintenanceResult, setMaintenanceResult] = useState<string | null>(null);
  const [workspaceError, setWorkspaceError] = useState<string | null>(null);
  const [isUpdatingWorkspace, setIsUpdatingWorkspace] = useState(false);
  const [workspaceRootPath, setWorkspaceRootPath] = useState("");
  const [calendarError, setCalendarError] = useState<string | null>(null);
  const [isUpdatingCalendar, setIsUpdatingCalendar] = useState(false);

  async function compactMain() {
    setIsCompacting(true);
    setCompactionResult(null);
    setCompactionError(null);

    try {
      const response = await fetch("/api/dashboard/compaction/main", { method: "POST" });

      if (!response.ok) {
        throw new Error(`Compaction failed: ${response.status}`);
      }

      const contentType = response.headers.get("content-type") ?? "";

      if (!contentType.includes("application/json")) {
        const body = await response.text();
        const title = /<title>(?<value>.*?)<\/title>/is.exec(body)?.groups?.value;
        throw new Error(title ? `Compaction returned HTML: ${title}` : "Compaction endpoint returned HTML instead of JSON.");
      }

      const result = await response.json() as {
        exactEntryCount: number;
        newlyCompactedEntryCount: number;
        memoryExtractionEntryCount: number;
        proposedMemoryCount: number;
        writtenMemoryCount: number;
        skippedMemoryCount: number;
        throughEntryId?: string | null;
      };
      setCompactionResult(
        `Compacted ${result.newlyCompactedEntryCount} new entries and checked ${result.memoryExtractionEntryCount} entries for memories: ${result.writtenMemoryCount} written, ${result.skippedMemoryCount} skipped, ${result.proposedMemoryCount} proposed.`
      );
    } catch (error) {
      setCompactionError(error instanceof Error ? error.message : String(error));
    } finally {
      setIsCompacting(false);
    }
  }

  async function runMemoryMaintenance(path: string) {
    setMaintenanceResult(null);

    const response = await fetch(path, { method: "POST" });

    if (!response.ok) {
      throw new Error(`Memory maintenance failed: ${response.status}`);
    }

    const result = await response.json() as {
      scanned: number;
      archived: number;
      pruned: number;
      merged: number;
      superseded: number;
      summary: string;
    };
    setMaintenanceResult(`${result.summary} Scanned ${result.scanned}; archived ${result.archived}; pruned ${result.pruned}; merged ${result.merged}; superseded ${result.superseded}.`);
  }

  async function updateWorkspacePermissions(remoteExecutionAllowed: boolean) {
    setIsUpdatingWorkspace(true);
    setWorkspaceError(null);

    try {
      const response = await fetch("/api/dashboard/settings/workspace-permissions", {
        body: JSON.stringify({ remoteExecutionAllowed }),
        headers: { "Content-Type": "application/json" },
        method: "POST"
      });

      if (!response.ok) {
        throw new Error(`Workspace permission update failed: ${response.status}`);
      }

      await settingsQuery.refetch();
    } catch (error) {
      setWorkspaceError(error instanceof Error ? error.message : String(error));
    } finally {
      setIsUpdatingWorkspace(false);
    }
  }

  async function updateWorkspaceRootPath(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const rootPath = workspaceRootPath.trim();

    if (!rootPath) {
      return;
    }

    setIsUpdatingWorkspace(true);
    setWorkspaceError(null);

    try {
      const response = await fetch("/api/dashboard/settings/workspace-root", {
        body: JSON.stringify({ rootPath }),
        headers: { "Content-Type": "application/json" },
        method: "POST"
      });

      if (!response.ok) {
        const body = await response.text();
        throw new Error(body || `Workspace root update failed: ${response.status}`);
      }

      setWorkspaceRootPath("");
      await settingsQuery.refetch();
    } catch (error) {
      setWorkspaceError(error instanceof Error ? error.message : String(error));
    } finally {
      setIsUpdatingWorkspace(false);
    }
  }

  async function disconnectCalendar() {
    setIsUpdatingCalendar(true);
    setCalendarError(null);

    try {
      const response = await fetch("/api/dashboard/calendar/disconnect", { method: "POST" });

      if (!response.ok) {
        throw new Error(`Calendar disconnect failed: ${response.status}`);
      }

      await settingsQuery.refetch();
    } catch (error) {
      setCalendarError(error instanceof Error ? error.message : String(error));
    } finally {
      setIsUpdatingCalendar(false);
    }
  }

  return (
    <PageFrame
      eyebrow="Read-only configuration"
      title="Settings"
      actions={
        <button className="primary-action" disabled={isCompacting} onClick={() => void compactMain()} type="button">
          <DatabaseZap size={15} />
          {isCompacting ? "Compacting" : "Compact now"}
        </button>
      }
    >
      {settingsQuery.isLoading && <LoadingState />}
      {settingsQuery.isError && <ErrorState error={settingsQuery.error} />}
      {compactionResult && <p className="muted">{compactionResult}</p>}
      {maintenanceResult && <p className="muted">{maintenanceResult}</p>}
      {compactionError && <ErrorState error={new Error(compactionError)} />}
      {workspaceError && <ErrorState error={new Error(workspaceError)} />}
      {calendarError && <ErrorState error={new Error(calendarError)} />}
      {snapshot && (
        <div className="settings-grid">
          {workspace && (
            <Panel title="Workspace">
              <dl className="settings-list">
                <div className="settings-row">
                  <dt>Project</dt>
                  <dd>
                    <code>{workspace.name}</code>
                    <IconButton onClick={() => navigator.clipboard.writeText(workspace.rootPath)} title="Copy workspace path" type="button">
                      <Copy size={13} />
                    </IconButton>
                  </dd>
                </div>
                <div className="settings-row">
                  <dt>Remote execution</dt>
                  <dd>
                    <code>{workspace.remoteExecutionAllowed ? "enabled" : "disabled"}</code>
                    <button
                      className="secondary-action"
                      disabled={isUpdatingWorkspace}
                      onClick={() => void updateWorkspacePermissions(!workspace.remoteExecutionAllowed)}
                      type="button"
                    >
                      {workspace.remoteExecutionAllowed ? <ShieldOff size={14} /> : <ShieldCheck size={14} />}
                      {workspace.remoteExecutionAllowed ? "Disable" : "Enable"}
                    </button>
                  </dd>
                </div>
              </dl>
              <form className="workspace-path-form" onSubmit={(event) => void updateWorkspaceRootPath(event)}>
                <input
                  aria-label="Workspace root path"
                  onChange={(event) => setWorkspaceRootPath(event.target.value)}
                  placeholder={workspace.rootPath}
                  value={workspaceRootPath}
                />
                <button className="secondary-action" disabled={isUpdatingWorkspace || !workspaceRootPath.trim()} type="submit">
                  <FolderInput size={14} />
                  Change
                </button>
              </form>
            </Panel>
          )}
          <SettingsPanel
            title="Provider"
            rows={[
              ["Provider", values.provider ?? "unset"],
              ["Model", values.model ?? "unset"],
              ["Queue behavior", values["queue.behavior"] ?? "unset"]
            ]}
          />
          <SettingsPanel
            title="Memory"
            rows={[
              ["Enabled", values["memory.enabled"] ?? "unset"],
              ["Scout limit", values["memory.scoutLimit"] ?? "unset"],
              ["Extraction", values["memory.extraction.mode"] ?? "unset"],
              ["SQLite", snapshot.memoryConnectionString]
            ]}
          />
          {calendar && (
            <Panel title="Google Calendar">
              <dl className="settings-list">
                <div className="settings-row">
                  <dt>Status</dt>
                  <dd>
                    <code>{calendar.connected ? "connected" : calendar.configured ? "not connected" : "not configured"}</code>
                    {calendar.connected ? (
                      <button className="secondary-action" disabled={isUpdatingCalendar} onClick={() => void disconnectCalendar()} type="button">
                        <ShieldOff size={14} />
                        Disconnect
                      </button>
                    ) : !calendar.configured ? (
                      <button className="secondary-action" disabled type="button">
                        <CalendarDays size={14} />
                        Connect
                      </button>
                    ) : (
                      <a className="secondary-action" href="/api/dashboard/calendar/connect">
                        <CalendarDays size={14} />
                        Connect
                      </a>
                    )}
                  </dd>
                </div>
                <div className="settings-row">
                  <dt>Account</dt>
                  <dd>
                    <code>{calendar.accountEmail ?? "unset"}</code>
                    <IconButton onClick={() => navigator.clipboard.writeText(calendar.accountEmail ?? "")} title="Copy calendar account" type="button">
                      <Copy size={13} />
                    </IconButton>
                  </dd>
                </div>
              </dl>
            </Panel>
          )}
          <Panel title="Memory Maintenance">
            <div className="row-actions">
              <button className="secondary-action" onClick={() => void runMemoryMaintenance("/api/dashboard/memory/cleanup")} type="button">
                <Trash2 size={14} />
                Cleanup
              </button>
              <button className="secondary-action" onClick={() => void runMemoryMaintenance("/api/dashboard/memory/consolidate")} type="button">
                <Sparkles size={14} />
                Consolidate
              </button>
            </div>
          </Panel>
          <SettingsPanel
            title="Compaction"
            rows={[
              ["Threshold", values["compaction.threshold"] ?? "unset"],
              ["Recent entries", values["compaction.recentEntryCount"] ?? "unset"],
              ["Memory extraction", values["memory.compactionExtraction.enabled"] ?? "unset"],
              ["Extraction provider", values["memory.compactionExtraction.provider"] ?? "unset"],
              ["Extraction mode", values["memory.compactionExtraction.mode"] ?? "unset"],
              ["Extraction max entries", values["memory.compactionExtraction.maxEntries"] ?? "unset"]
            ]}
          />
          <Panel title="Applied Layers">
            <div className="tool-list">
              {appliedLayers.map((layer) => (
                <span key={layer}>{layer}</span>
              ))}
            </div>
          </Panel>
        </div>
      )}
    </PageFrame>
  );
}

function SettingsPanel({ title, rows }: { title: string; rows: Array<[string, string]> }) {
  return (
    <Panel title={title}>
      <dl className="settings-list">
        {rows.map(([label, value]) => (
          <div className="settings-row" key={label}>
            <dt>{label}</dt>
            <dd>
              <code>{value}</code>
              <IconButton onClick={() => navigator.clipboard.writeText(value)} title={`Copy ${label}`} type="button">
                <Copy size={13} />
              </IconButton>
            </dd>
          </div>
        ))}
      </dl>
    </Panel>
  );
}
