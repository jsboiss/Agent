import { Copy } from "lucide-react";
import { useGetSettings } from "../../api/generated";
import { ErrorState, IconButton, LoadingState, PageFrame, Panel } from "../components";

export function SettingsPage() {
  const settingsQuery = useGetSettings();
  const snapshot = settingsQuery.data?.data;
  const values = snapshot?.values ?? {};
  const appliedLayers = snapshot?.appliedLayers ?? [];

  return (
    <PageFrame eyebrow="Read-only configuration" title="Settings">
      {settingsQuery.isLoading && <LoadingState />}
      {settingsQuery.isError && <ErrorState error={settingsQuery.error} />}
      {snapshot && (
        <div className="settings-grid">
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
          <SettingsPanel
            title="Compaction"
            rows={[
              ["Threshold", values["compaction.threshold"] ?? "unset"],
              ["Recent entries", values["compaction.recentEntryCount"] ?? "unset"]
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
