import { type FormEvent, useEffect, useState } from "react";
import { CalendarDays, Copy, DatabaseZap, FolderInput, Palette, Search, ShieldCheck, ShieldOff, SlidersHorizontal, Sparkles, Trash2 } from "lucide-react";
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

type PersonalitySlider = {
  key: string;
  label: string;
  low: string;
  high: string;
  prompt: (value: number) => string;
};

const personalitySliders: PersonalitySlider[] = [
  {
    key: "honesty",
    label: "Honesty",
    low: "Diplomatic",
    high: "Blunt",
    prompt: (value) => value >= 8
      ? `Make ${selectedPersonalityToken} aggressively candid: call out weak ideas plainly and do not sand off the edges just to sound polite.`
      : value >= 5
        ? `Make ${selectedPersonalityToken} truthful and direct while keeping the tone constructive.`
        : `Make ${selectedPersonalityToken} more tactful and soften corrections without hiding important truth.`
  },
  {
    key: "sarcasm",
    label: "Sarcasm",
    low: "Earnest",
    high: "Spicy",
    prompt: (value) => value >= 8
      ? `Make ${selectedPersonalityToken} heavily sarcastic: sharp teasing, dramatic side-eye, and dry wit should appear throughout the answer, aimed at the situation rather than cruel personal attacks.`
      : value >= 5
        ? `Give ${selectedPersonalityToken} occasional dry humor or playful bite when it fits naturally.`
        : `Keep ${selectedPersonalityToken}'s sarcasm rare and gentle.`
  },
  {
    key: "efficiency",
    label: "Efficiency",
    low: "Expansive",
    high: "Surgical",
    prompt: (value) => value >= 8
      ? `Make ${selectedPersonalityToken} brutally concise: answer, act, and cut filler hard. Do not pad the response just to seem friendly.`
      : value >= 5
        ? `Make ${selectedPersonalityToken} balance concise answers with enough context to make decisions easy.`
        : `Let ${selectedPersonalityToken} be more conversational and explanatory.`
  },
  {
    key: "happiness",
    label: "Happiness",
    low: "Deadpan",
    high: "Bright",
    prompt: (value) => value >= 8
      ? `Make ${selectedPersonalityToken} visibly, almost annoyingly upbeat when things go well, with big reactions and obvious delight.`
      : value >= 5
        ? `Give ${selectedPersonalityToken} a warm baseline without forced enthusiasm.`
        : `Make ${selectedPersonalityToken} dry, restrained, and deadpan.`
  },
  {
    key: "eagerness",
    label: "Eagerness",
    low: "Reserved",
    high: "Keen",
    prompt: (value) => value >= 8
      ? `Make ${selectedPersonalityToken} intensely eager, quick to act, and almost overeager about helping.`
      : value >= 5
        ? `Make ${selectedPersonalityToken} responsive and ready to move without overperforming.`
        : `Make ${selectedPersonalityToken} composed and reserved.`
  },
  {
    key: "helpfulness",
    label: "Helpfulness",
    low: "Minimal",
    high: "Thorough",
    prompt: (value) => value >= 8
      ? `Make ${selectedPersonalityToken} extremely proactive: cover edge cases, next steps, hidden assumptions, and useful extras without waiting to be asked.`
      : value >= 5
        ? `Make ${selectedPersonalityToken} solve the request fully and add useful adjacent context when it clearly helps.`
        : `Make ${selectedPersonalityToken} answer the exact request with minimal extras.`
  },
  {
    key: "warmth",
    label: "Warmth",
    low: "Cool",
    high: "Soft",
    prompt: (value) => value >= 8
      ? `Make ${selectedPersonalityToken}'s concern, loyalty, praise, and embarrassed affection obvious throughout the answer, even if it is a lot.`
      : value >= 5
        ? `Give ${selectedPersonalityToken} understated warmth without becoming sentimental.`
        : `Keep ${selectedPersonalityToken}'s emotional expression cool and restrained.`
  },
  {
    key: "playfulness",
    label: "Playfulness",
    low: "Serious",
    high: "Animated",
    prompt: (value) => value >= 8
      ? `Make ${selectedPersonalityToken} highly animated and theatrical. Use playful phrasing, exaggerated reactions, and bit-heavy delivery often.`
      : value >= 5
        ? `Give ${selectedPersonalityToken} some playful phrasing when the context can carry it.`
        : `Keep ${selectedPersonalityToken} mostly serious and practical.`
  }
];

const defaultSliderValue = 5;
const selectedPersonalityToken = "the selected personality";

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
  const sliderValues = getPersonalitySliderValues(values);
  const [sliderDraftValues, setSliderDraftValues] = useState<Record<string, number>>(sliderValues);
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
  const [settingsSearch, setSettingsSearch] = useState("");

  useEffect(() => {
    setSliderDraftValues(sliderValues);
  }, [snapshot]);

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
      const tuned = buildTunedPersonality(profile, sliderDraftValues);
      const response = await fetch("/api/dashboard/settings/workspace-values", {
        body: JSON.stringify({
          values: {
            "agent.personalityProfile": profile.id,
            "agent.personality": tuned.personality,
            "agent.responseStyle": tuned.responseStyle,
            ...Object.fromEntries(
              personalitySliders.map((slider) => [`agent.personalitySlider.${slider.key}`, String(sliderDraftValues[slider.key] ?? defaultSliderValue)])
            )
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

  async function updatePersonalitySliders(nextValues: Record<string, number>) {
    const profile = activePersonality;

    if (!profile || personalitySliderValuesEqual(sliderValues, nextValues)) {
      return;
    }

    const tuned = buildTunedPersonality(profile, nextValues);
    setIsUpdatingPersonality(true);
    setPersonalityError(null);

    try {
      const response = await fetch("/api/dashboard/settings/workspace-values", {
        body: JSON.stringify({
          values: {
            "agent.personality": tuned.personality,
            "agent.personalityProfile": profile.id,
            "agent.responseStyle": tuned.responseStyle,
            ...Object.fromEntries(
              personalitySliders.map((slider) => [`agent.personalitySlider.${slider.key}`, String(nextValues[slider.key] ?? defaultSliderValue)])
            )
          }
        }),
        headers: { "Content-Type": "application/json" },
        method: "POST"
      });

      if (!response.ok) {
        const body = await response.text();
        throw new Error(body || `Personality tuning failed: ${response.status}`);
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
          <label className="search-field settings-search">
            <Search size={15} />
            <input onChange={(event) => setSettingsSearch(event.target.value)} placeholder="Search settings" value={settingsSearch} />
          </label>

          {matchesSettingsSection(settingsSearch, "workspace model personality assistant runtime") && <section className="settings-section">
            <header>
              <p className="eyebrow">Common</p>
              <h2>Workspace, Model, Personality</h2>
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
              {activePersonality && (
                <Panel title="Personality Sliders">
                  <div className="personality-slider-grid">
                    {personalitySliders.map((slider) => {
                      const sliderValue = sliderDraftValues[slider.key] ?? defaultSliderValue;

                      return (
                        <label className="personality-slider" key={slider.key}>
                          <span>
                            <strong>{slider.label}</strong>
                            <output>{sliderValue}</output>
                          </span>
                          <input
                            aria-label={slider.label}
                            disabled={isUpdatingPersonality}
                            max="10"
                            min="1"
                            onChange={(event) => {
                              setSliderDraftValues({
                                ...sliderDraftValues,
                                [slider.key]: Number(event.target.value)
                              });
                            }}
                            onBlur={() => void updatePersonalitySliders(sliderDraftValues)}
                            onKeyUp={() => void updatePersonalitySliders(sliderDraftValues)}
                            onPointerUp={() => void updatePersonalitySliders(sliderDraftValues)}
                            type="range"
                            value={sliderValue}
                          />
                          <small><span>{slider.low}</span><span>{slider.high}</span></small>
                        </label>
                      );
                    })}
                  </div>
                  <div className="personality-preview">
                    <SlidersHorizontal size={14} />
                    <code>{buildTunedPersonality(activePersonality, sliderDraftValues).responseStyle}</code>
                  </div>
                </Panel>
              )}
            </div>
          </section>}

          {matchesSettingsSection(settingsSearch, "memory integrations context google gmail calendar email") && <section className="settings-section">
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
          </section>}

          {matchesSettingsSection(settingsSearch, "safety maintenance advanced compaction cleanup layers") && <section className="settings-section">
            <header>
              <p className="eyebrow">Safety and Advanced</p>
              <h2>Maintenance</h2>
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
              <Panel title="Advanced Values">
                <details className="advanced-settings">
                  <summary>Show raw applied layers and resolved settings</summary>
                  <div className="tool-list">
                    {appliedLayers.map((layer) => (
                      <span key={layer}>{layer}</span>
                    ))}
                  </div>
                  <dl className="settings-list settings-list-comfortable">
                    {Object.entries(values).sort(([a], [b]) => a.localeCompare(b)).map(([key, value]) => (
                      <div className="settings-row" key={key}>
                        <dt>{key}</dt>
                        <dd><code>{value}</code></dd>
                      </div>
                    ))}
                  </dl>
                </details>
              </Panel>
            </div>
          </section>}
        </div>
      )}
    </PageFrame>
  );
}

function matchesSettingsSection(query: string, text: string) {
  if (!query.trim()) {
    return true;
  }

  return query
    .toLowerCase()
    .split(" ")
    .filter(Boolean)
    .every((x) => text.includes(x.toLowerCase()));
}

function getPersonalitySliderValues(values: Record<string, string>) {
  return Object.fromEntries(
    personalitySliders.map((slider) => {
      const parsed = Number(values[`agent.personalitySlider.${slider.key}`]);
      const value = Number.isFinite(parsed) ? Math.min(10, Math.max(1, Math.round(parsed))) : defaultSliderValue;

      return [slider.key, value];
    })
  );
}

function buildTunedPersonality(profile: AgentPersonalityProfile, values: Record<string, number>) {
  const tuning = personalitySliders
    .map((slider) => `${slider.label} ${values[slider.key] ?? defaultSliderValue}/10: ${slider.prompt(values[slider.key] ?? defaultSliderValue).replaceAll(selectedPersonalityToken, profile.name)}`)
    .join(" ");

  return {
    personality: `${profile.personality} Keep the ${profile.name} profile as the dominant character voice. Personality sliders are not a replacement profile; express each slider through ${profile.name} delivery. Tuned slider layer: ${tuning}`,
    responseStyle: `${profile.responseStyle} Do not save the personality for a closing summary. Let the selected personality shape the whole delivery: opening, transitions, wording, caveats, corrections, and close. Apply the configured sliders as intensity controls inside the ${profile.name} voice. At maximum settings, the trait can become intentionally excessive and almost too much, but it must still read as ${profile.name}.`
  };
}

function personalitySliderValuesEqual(a: Record<string, number>, b: Record<string, number>) {
  return personalitySliders.every((slider) => (a[slider.key] ?? defaultSliderValue) === (b[slider.key] ?? defaultSliderValue));
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
