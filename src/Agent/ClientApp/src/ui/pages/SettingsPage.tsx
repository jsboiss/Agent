import { useState } from "react";
import { Copy, DatabaseZap } from "lucide-react";
import { useGetSettings } from "../../api/generated";
import { ErrorState, IconButton, LoadingState, PageFrame, Panel } from "../components";

export function SettingsPage() {
  const settingsQuery = useGetSettings();
  const snapshot = settingsQuery.data?.data;
  const values = snapshot?.values ?? {};
  const appliedLayers = snapshot?.appliedLayers ?? [];
  const [isCompacting, setIsCompacting] = useState(false);
  const [compactionResult, setCompactionResult] = useState<string | null>(null);
  const [compactionError, setCompactionError] = useState<string | null>(null);

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
      {compactionError && <ErrorState error={new Error(compactionError)} />}
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
