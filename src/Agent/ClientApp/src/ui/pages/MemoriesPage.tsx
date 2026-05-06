import { Fragment, FormEvent, useMemo, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Archive, ChevronDown, ChevronRight, Plus, RotateCcw, Trash2, X } from "lucide-react";
import {
  getGetMemoriesQueryKey,
  MemoryRow,
  useDeleteMemory,
  useGetMemories,
  useUpdateMemoryLifecycle,
  useWriteMemory
} from "../../api/generated";
import { EmptyState, ErrorState, IconButton, LoadingState, MetricTile, PageFrame, StatusChip, toNumber } from "../components";

type SortKey = "updatedAt" | "importance" | "confidence" | "accessCount";

export function MemoriesPage() {
  const queryClient = useQueryClient();
  const [query, setQuery] = useState("");
  const [lifecycle, setLifecycle] = useState("Active");
  const [segment, setSegment] = useState("All");
  const [tier, setTier] = useState("All");
  const [isDrawerOpen, setIsDrawerOpen] = useState(false);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [sortKey, setSortKey] = useState<SortKey>("updatedAt");
  const [rowLimit, setRowLimit] = useState(100);
  const [newText, setNewText] = useState("");
  const [newTier, setNewTier] = useState("Long");
  const [newSegment, setNewSegment] = useState("Context");
  const params = useMemo(() => ({ query, lifecycle, segment, tier }), [lifecycle, query, segment, tier]);
  const memoriesQuery = useGetMemories(params);
  const writeMemory = useWriteMemory({ mutation: { onSuccess: () => invalidateMemories() } });
  const updateLifecycle = useUpdateMemoryLifecycle({ mutation: { onSuccess: () => invalidateMemories() } });
  const deleteMemory = useDeleteMemory({ mutation: { onSuccess: () => invalidateMemories() } });
  const snapshot = memoriesQuery.data?.data;
  const memories = snapshot?.memories ?? [];
  const lifecycles = snapshot?.lifecycles ?? ["All", "Active", "Archived", "Pruned"];
  const segments = snapshot?.segments ?? ["All"];
  const tiers = snapshot?.tiers ?? ["All"];
  const rows = useMemo(() => {
    return [...memories]
      .sort((x, y) => getSortValue(y, sortKey) - getSortValue(x, sortKey))
      .slice(0, rowLimit);
  }, [memories, rowLimit, sortKey]);

  function invalidateMemories() {
    queryClient.invalidateQueries({ queryKey: getGetMemoriesQueryKey(params) });
  }

  async function submit(event: FormEvent) {
    event.preventDefault();

    if (!newText.trim()) {
      return;
    }

    await writeMemory.mutateAsync({
      data: {
        text: newText.trim(),
        tier: newTier,
        segment: newSegment,
        importance: 0.7,
        confidence: 0.9
      }
    });
    setNewText("");
    setIsDrawerOpen(false);
  }

  return (
    <PageFrame
      eyebrow="SQLite memory store"
      title="Memories"
      actions={
        <button className="primary-action" onClick={() => setIsDrawerOpen(true)} type="button">
          <Plus size={15} />
          Write
        </button>
      }
    >
      <div className="metric-grid">
        <MetricTile label="Visible" value={rows.length} />
        <MetricTile label="Total" value={memories.length} />
        <MetricTile label="Duplicates" value={memories.filter((x) => x.hasDuplicateText).length} />
        <MetricTile label="Lifecycle" value={lifecycle} />
      </div>

      <div className="filter-bar">
        <input onChange={(event) => setQuery(event.target.value)} placeholder="Search memories" value={query} />
        <select onChange={(event) => setLifecycle(event.target.value)} value={lifecycle}>
          {lifecycles.map((item) => (
            <option key={item}>{item}</option>
          ))}
        </select>
        <select onChange={(event) => setSegment(event.target.value)} value={segment}>
          {segments.map((item) => (
            <option key={item}>{item}</option>
          ))}
        </select>
        <select onChange={(event) => setTier(event.target.value)} value={tier}>
          {tiers.map((item) => (
            <option key={item}>{item}</option>
          ))}
        </select>
        <select onChange={(event) => setSortKey(event.target.value as SortKey)} value={sortKey}>
          <option value="updatedAt">Updated</option>
          <option value="importance">Importance</option>
          <option value="confidence">Confidence</option>
          <option value="accessCount">Access</option>
        </select>
        <select onChange={(event) => setRowLimit(Number(event.target.value))} value={rowLimit}>
          <option value={50}>50 rows</option>
          <option value={100}>100 rows</option>
          <option value={200}>200 rows</option>
        </select>
      </div>

      {memoriesQuery.isLoading && <LoadingState />}
      {memoriesQuery.isError && <ErrorState error={memoriesQuery.error} />}
      {snapshot && memories.length === 0 && <EmptyState title="No memories" body="No memories match the current filters." />}
      {rows.length > 0 && (
        <div className="data-grid-wrap">
          <table className="data-grid">
            <thead>
              <tr>
                <th></th>
                <th>Lifecycle</th>
                <th>Tier</th>
                <th>Segment</th>
                <th>Memory</th>
                <th>Scores</th>
                <th>Access</th>
                <th>Updated</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((memory) => {
                const isExpanded = expandedId === memory.id;

                return (
                  <Fragment key={memory.id}>
                    <tr className={memory.hasDuplicateText ? "has-duplicate" : ""} key={memory.id}>
                      <td>
                        <IconButton title={isExpanded ? "Collapse row" : "Expand row"} type="button" onClick={() => setExpandedId(isExpanded ? null : memory.id)}>
                          {isExpanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
                        </IconButton>
                      </td>
                      <td><StatusChip label={memory.lifecycle} tone={memory.lifecycle === "Active" ? "green" : "amber"} /></td>
                      <td><span className="chip tone-violet">{memory.tier}</span></td>
                      <td><span className="chip tone-blue">{memory.segment}</span></td>
                      <td>
                        <strong className="wrap-text">{memory.text}</strong>
                        {memory.hasDuplicateText && <small>duplicate text</small>}
                      </td>
                      <td>{toNumber(memory.importance).toFixed(2)} / {toNumber(memory.confidence).toFixed(2)}</td>
                      <td>{toNumber(memory.accessCount)}</td>
                      <td className="mono">{new Date(memory.updatedAt).toLocaleString()}</td>
                      <td>
                        <div className="button-row">
                          <IconButton disabled={memory.lifecycle === "Archived"} onClick={() => updateLifecycle.mutate({ id: memory.id, data: { lifecycle: "Archived" } })} title="Archive" type="button"><Archive size={14} /></IconButton>
                          <IconButton disabled={memory.lifecycle === "Active"} onClick={() => updateLifecycle.mutate({ id: memory.id, data: { lifecycle: "Active" } })} title="Restore" type="button"><RotateCcw size={14} /></IconButton>
                          <IconButton className="danger" onClick={() => deleteMemory.mutate({ id: memory.id })} title="Delete" type="button"><Trash2 size={14} /></IconButton>
                        </div>
                      </td>
                    </tr>
                    {isExpanded && (
                      <tr className="expanded-row" key={`${memory.id}:expanded`}>
                        <td colSpan={9}>
                          <dl className="metadata-grid">
                            <dt>Id</dt>
                            <dd>{memory.id}</dd>
                            <dt>Source</dt>
                            <dd>{memory.sourceMessageId ?? "none"}</dd>
                            <dt>Supersedes</dt>
                            <dd>{memory.supersedes ?? "none"}</dd>
                            <dt>Created</dt>
                            <dd>{new Date(memory.createdAt).toLocaleString()}</dd>
                            <dt>Last accessed</dt>
                            <dd>{memory.lastAccessedAt ? new Date(memory.lastAccessedAt).toLocaleString() : "never"}</dd>
                          </dl>
                        </td>
                      </tr>
                    )}
                  </Fragment>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {isDrawerOpen && (
        <aside className="drawer">
          <form className="drawer-panel" onSubmit={submit}>
            <header className="panel-heading">
              <div>
                <p className="eyebrow">Manual write</p>
                <h2>New Memory</h2>
              </div>
              <IconButton onClick={() => setIsDrawerOpen(false)} title="Close" type="button"><X size={15} /></IconButton>
            </header>
            <textarea onChange={(event) => setNewText(event.target.value)} placeholder="Memory text" value={newText} />
            <select onChange={(event) => setNewTier(event.target.value)} value={newTier}>
              {(tiers.length > 1 ? tiers : ["Short", "Long", "Permanent"]).filter((item) => item !== "All").map((item) => (
                <option key={item}>{item}</option>
              ))}
            </select>
            <select onChange={(event) => setNewSegment(event.target.value)} value={newSegment}>
              {(segments.length > 1 ? segments : ["Context"]).filter((item) => item !== "All").map((item) => (
                <option key={item}>{item}</option>
              ))}
            </select>
            <button className="primary-action" disabled={!newText.trim() || writeMemory.isPending} type="submit">Write memory</button>
          </form>
        </aside>
      )}
    </PageFrame>
  );
}

function getSortValue(memory: MemoryRow, sortKey: SortKey) {
  if (sortKey === "updatedAt") {
    return new Date(memory.updatedAt).getTime();
  }

  return toNumber(memory[sortKey]);
}
