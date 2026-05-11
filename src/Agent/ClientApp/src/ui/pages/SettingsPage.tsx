import { type FormEvent, useState } from "react";
import { CalendarDays, Copy, DatabaseZap, FolderInput, Palette, ShieldCheck, ShieldOff, Sparkles, Trash2 } from "lucide-react";
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
  email?: CalendarSettings;
  personalities?: AgentPersonalityProfile[];
};

type CalendarSettings = {
  configured: boolean;
  connected: boolean;
  accountEmail?: string | null;
  updatedAt?: string | null;
};

type AgentPersonalityProfile = {
  id: string;
  name: string;
  description: string;
  personality: string;
  responseStyle: string;
};

export function SettingsPage() {
  const settingsQuery = useGetSettings();
  const snapshot = settingsQuery.data?.data;
  const workspace = (snapshot as (typeof snapshot & SettingsSnapshotWithWorkspace) | undefined)?.workspace;
  const calendar = (snapshot as (typeof snapshot & SettingsSnapshotWithWorkspace) | undefined)?.calendar;
  const email = (snapshot as (typeof snapshot & SettingsSnapshotWithWorkspace) | undefined)?.email;
  const personalities = (snapshot as (typeof snapshot & SettingsSnapshotWithWorkspace) | undefined)?.personalities ?? [];
  const values = snapshot?.values ?? {};
  const appliedLayers = snapshot?.appliedLayers ?? [];
  const activePersonalityId = values["agent.personalityProfile"] ?? personalities[0]?.id ?? "";
  const activePersonality = personalities.find((x) => x.id === activePersonalityId) ?? personalities[0];
  const [isCompacting, setIsCompacting] = useState(false);
  const [compactionResult, setCompactionResult] = useState<string | null>(null);
  const [compactionError, setCompactionError] = useState<string | null>(null);
  const [maintenanceResult, setMaintenanceResult] = useState<string | null>(null);
  const [workspaceError, setWorkspaceError] = useState<string | null>(null);
  const [isUpdatingWorkspace, setIsUpdatingWorkspace] = useState(false);
  const [workspaceRootPath, setWorkspaceRootPath] = useState("");
  const [calendarError, setCalendarError] = useState<string | null>(null);
  const [isUpdatingCalendar, setIsUpdatingCalendar] = useState(false);
  const [emailError, setEmailError] = useState<string | null>(null);
  const [isUpdatingEmail, setIsUpdatingEmail] = useState(false);
  const [personalityError, setPersonalityError] = useState<string | null>(null);
  const [isUpdatingPersonality, setIsUpdatingPersonality] = useState(false);

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

  async function disconnectEmail() {
    setIsUpdatingEmail(true);
    setEmailError(null);

    try {
      const response = await fetch("/api/dashboard/email/disconnect", { method: "POST" });

      if (!response.ok) {
        throw new Error(`Gmail disconnect failed: ${response.status}`);
      }

      await settingsQuery.refetch();
    } catch (error) {
      setEmailError(error instanceof Error ? error.message : String(error));
    } finally {
      setIsUpdatingEmail(false);
    }
  }

  async function updatePersonality(profileId: string) {
    const profile = personalities.find((x) => x.id === profileId);

    if (!profile) {
      return;
    }

    setIsUpdatingPersonality(true);
    setPersonalityError(null);

    try {
      const response = await fetch("/api/dashboard/settings/workspace-values", {
        body: JSON.stringify({
          values: {
            "agent.personalityProfile": profile.id,
            "agent.personality": profile.personality,
            "agent.responseStyle": profile.responseStyle
          }
        }),
        headers: { "Content-Type": "application/json" },
        method: "POST"
      });

      if (!response.ok) {
        const body = await response.text();
        throw new Error(body || `Personality update failed: ${response.status}`);
      }

      await settingsQuery.refetch();
    } catch (error) {
      setPersonalityError(error instanceof Error ? error.message : String(error));
    } finally {
      setIsUpdatingPersonality(false);
    }
  }

  return (
    <PageFrame
      eyebrow="Configuration"
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
      {emailError && <ErrorState error={new Error(emailError)} />}
      {personalityError && <ErrorState error={new Error(personalityError)} />}
      {snapshot && (
        <div className="settings-page-layout">
          <section className="settings-section">
            <header>
              <p className="eyebrow">Assistant</p>
              <h2>Runtime</h2>
            </header>
            <div className="settings-section-grid">
              {workspace && (
                <Panel title="Workspace">
                  <dl className="settings-list settings-list-comfortable">
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
              {activePersonality && (
                <Panel title="Personality">
                  <dl className="settings-list settings-list-comfortable">
                    <div className="settings-row">
                      <dt>Profile</dt>
                      <dd>
                        <select
                          aria-label="Agent personality"
                          disabled={isUpdatingPersonality}
                          onChange={(event) => void updatePersonality(event.target.value)}
                          value={activePersonalityId}
                        >
                          {personalities.map((profile) => (
                            <option key={profile.id} value={profile.id}>{profile.name}</option>
                          ))}
                        </select>
                        <Palette size={14} />
                      </dd>
                    </div>
                    <div className="settings-row settings-row-stack">
                      <dt>Flavor</dt>
                      <dd>
                        <code>{activePersonality.description}</code>
                        <IconButton onClick={() => navigator.clipboard.writeText(activePersonality.personality)} title="Copy personality prompt" type="button">
                          <Copy size={13} />
                        </IconButton>
                      </dd>
                    </div>
                    <div className="settings-row settings-row-stack">
                      <dt>Style</dt>
                      <dd>
                        <code>{values["agent.responseStyle"] ?? activePersonality.responseStyle}</code>
                        <IconButton onClick={() => navigator.clipboard.writeText(values["agent.responseStyle"] ?? activePersonality.responseStyle)} title="Copy response style" type="button">
                          <Copy size={13} />
                        </IconButton>
                      </dd>
                    </div>
                  </dl>
                </Panel>
              )}
            </div>
          </section>

          <section className="settings-section">
            <header>
              <p className="eyebrow">Context</p>
              <h2>Memory and Integrations</h2>
            </header>
            <div className="settings-section-grid">
              <SettingsPanel
                title="Memory"
                rows={[
                  ["Enabled", values["memory.enabled"] ?? "unset"],
                  ["Scout limit", values["memory.scoutLimit"] ?? "unset"],
                  ["Extraction", values["memory.extraction.mode"] ?? "unset"],
                  ["SQLite", snapshot.memoryConnectionString]
                ]}
              />
              {(calendar || email) && (
                <Panel title="Google Integrations">
                  <dl className="settings-list settings-list-comfortable">
                    {calendar && (
                      <IntegrationRow
                        accountTitle="Copy calendar account"
                        connectHref="/api/dashboard/calendar/connect"
                        integration={calendar}
                        isUpdating={isUpdatingCalendar}
                        label="Calendar"
                        onDisconnect={() => void disconnectCalendar()}
                      />
                    )}
                    {email && (
                      <IntegrationRow
                        accountTitle="Copy Gmail account"
                        connectHref="/api/dashboard/email/connect"
                        integration={email}
                        isUpdating={isUpdatingEmail}
                        label="Gmail"
                        onDisconnect={() => void disconnectEmail()}
                      />
                    )}
                  </dl>
                </Panel>
              )}
            </div>
          </section>

          <section className="settings-section">
            <header>
              <p className="eyebrow">Maintenance</p>
              <h2>Operations</h2>
            </header>
            <div className="settings-section-grid">
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
          </section>
        </div>
      )}
    </PageFrame>
  );
}

function IntegrationRow({
  accountTitle,
  connectHref,
  integration,
  isUpdating,
  label,
  onDisconnect
}: {
  accountTitle: string;
  connectHref: string;
  integration: CalendarSettings;
  isUpdating: boolean;
  label: string;
  onDisconnect: () => void;
}) {
  return (
    <>
      <div className="settings-row">
        <dt>{label}</dt>
        <dd>
          <code>{integration.connected ? "connected" : integration.configured ? "not connected" : "not configured"}</code>
          {integration.connected ? (
            <button className="secondary-action" disabled={isUpdating} onClick={onDisconnect} type="button">
              <ShieldOff size={14} />
              Disconnect
            </button>
          ) : !integration.configured ? (
            <button className="secondary-action" disabled type="button">
              <CalendarDays size={14} />
              Connect
            </button>
          ) : (
            <a className="secondary-action" href={connectHref}>
              <CalendarDays size={14} />
              Connect
            </a>
          )}
        </dd>
      </div>
      <div className="settings-row">
        <dt>{label} account</dt>
        <dd>
          <code>{integration.accountEmail ?? "unset"}</code>
          <IconButton onClick={() => navigator.clipboard.writeText(integration.accountEmail ?? "")} title={accountTitle} type="button">
            <Copy size={13} />
          </IconButton>
        </dd>
      </div>
    </>
  );
}

function SettingsPanel({ title, rows }: { title: string; rows: Array<[string, string]> }) {
  return (
    <Panel title={title}>
      <dl className="settings-list settings-list-comfortable">
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
