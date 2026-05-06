import ForceGraph2D, { ForceGraphMethods } from "react-force-graph-2d";
import { RefObject, useEffect, useMemo, useRef, useState } from "react";
import { Crosshair, RefreshCcw, Search } from "lucide-react";
import { MemoryRow, useGetMemories } from "../../api/generated";
import { EmptyState, ErrorState, IconButton, LoadingState, PageFrame, toNumber } from "../components";

type SegmentHubNode = {
  id: string;
  kind: "segment";
  label: string;
  segment: string;
  memoryCount: number;
};

type MemoryNode = {
  id: string;
  kind: "memory";
  label: string;
  memory: MemoryRow;
  segment: string;
};

type GraphNode = SegmentHubNode | MemoryNode;

type GraphLink = {
  source: string;
  target: string;
};

const segmentColors: Record<string, string> = {
  Context: "#64748b",
  Identity: "#f43f5e",
  Knowledge: "#3b82f6",
  Preference: "#14b8a6",
  Procedure: "#22c55e",
  Project: "#f97316",
  Relationship: "#ec4899"
};

const fallbackColors = ["#38bdf8", "#a78bfa", "#f59e0b", "#10b981", "#fb7185", "#60a5fa"];
const graphParams = { lifecycle: "Active" };

export function GraphPage() {
  const graphQuery = useGetMemories(graphParams);
  const graphRef = useRef<ForceGraphMethods | undefined>(undefined);
  const stageRef = useRef<HTMLDivElement | null>(null);
  const stageSize = useElementSize(stageRef);
  const [query, setQuery] = useState("");
  const [segment, setSegment] = useState("All");
  const [tier, setTier] = useState("All");
  const [hoveredNodeId, setHoveredNodeId] = useState<string | null>(null);
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const snapshot = graphQuery.data?.data;
  const memories = snapshot?.memories ?? [];
  const segments = snapshot?.segments ?? ["All"];
  const tiers = snapshot?.tiers ?? ["All"];
  const filteredMemories = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase();

    return memories.filter((memory) => {
      if (segment !== "All" && memory.segment !== segment) {
        return false;
      }

      if (tier !== "All" && memory.tier !== tier) {
        return false;
      }

      if (normalizedQuery) {
        return (
          memory.text.toLowerCase().includes(normalizedQuery) ||
          memory.segment.toLowerCase().includes(normalizedQuery) ||
          memory.tier.toLowerCase().includes(normalizedQuery) ||
          memory.id.toLowerCase().includes(normalizedQuery)
        );
      }

      return true;
    });
  }, [memories, query, segment, tier]);
  const graphData = useMemo(() => buildGraph(filteredMemories), [filteredMemories]);
  const selectedNode = graphData.nodes.find((node) => node.id === selectedNodeId) ?? null;

  useEffect(() => {
    if (selectedNodeId && !graphData.nodes.some((node) => node.id === selectedNodeId)) {
      setSelectedNodeId(null);
    }
  }, [graphData.nodes, selectedNodeId]);

  useEffect(() => {
    if (graphData.nodes.length > 0 && stageSize.width > 0 && stageSize.height > 0) {
      window.setTimeout(fitGraph, 120);
    }
  }, [graphData.nodes.length, stageSize.height, stageSize.width]);

  function fitGraph() {
    graphRef.current?.zoomToFit(450, 48);
  }

  function handleNodeHover(node: GraphNode | null | undefined) {
    setHoveredNodeId(node?.id ?? null);

    if (stageRef.current) {
      stageRef.current.style.cursor = node ? "pointer" : "default";
    }
  }

  return (
    <PageFrame
      eyebrow="Memory topology"
      title="Knowledge Graph"
      actions={
        <>
          <IconButton onClick={() => graphQuery.refetch()} title="Refresh graph" type="button"><RefreshCcw size={15} /></IconButton>
          <IconButton onClick={fitGraph} title="Fit graph" type="button"><Crosshair size={15} /></IconButton>
        </>
      }
    >
      <div className="graph-layout">
        <section className="graph-stage">
          <div className="graph-toolbar">
            <div className="graph-search">
              <Search size={14} />
              <input onChange={(event) => setQuery(event.target.value)} placeholder="Search graph memories" value={query} />
            </div>
            <select onChange={(event) => setSegment(event.target.value)} value={segment}>
              {segments.map((item) => (
                <option key={item}>{item === "All" ? "All segments" : item}</option>
              ))}
            </select>
            <select onChange={(event) => setTier(event.target.value)} value={tier}>
              {tiers.map((item) => (
                <option key={item}>{item === "All" ? "All tiers" : item}</option>
              ))}
            </select>
            <span className="graph-count mono">{filteredMemories.length}/{memories.length}</span>
          </div>

          {graphQuery.isLoading && <LoadingState />}
          {graphQuery.isError && <ErrorState error={graphQuery.error} />}
          {snapshot && memories.length === 0 && <EmptyState title="No graph data" body="No active memories have been captured yet." />}
          {snapshot && memories.length > 0 && filteredMemories.length === 0 && <EmptyState title="No matching memories" body="No active memories match the current graph filters." />}
          {filteredMemories.length > 0 && (
            <>
              <div className="graph-legend">
                {graphData.hubs.slice(0, 6).map((hub) => (
                  <span className="graph-segment-chip" key={hub.id}>
                    <span style={{ background: getNodeColor(hub) }} />
                    {hub.label}
                  </span>
                ))}
              </div>
              <div className="graph-canvas-wrap" ref={stageRef}>
                {stageSize.width > 0 && stageSize.height > 0 && (
                  <ForceGraph2D
                    ref={graphRef}
                    graphData={graphData}
                    width={stageSize.width}
                    height={stageSize.height}
                    backgroundColor="#0c0e12"
                    nodeRelSize={1}
                    linkColor={() => "rgba(138, 145, 158, 0.22)"}
                    linkWidth={(link: any) => getLinkWidth(link)}
                    nodeLabel={(node: any) => getNodeTooltip(node)}
                    onNodeClick={(node: any) => setSelectedNodeId(node.id)}
                    onNodeHover={(node: any) => handleNodeHover(node)}
                    nodeCanvasObject={(node: any, context, globalScale) => drawNode(node, context, globalScale, node.id === selectedNodeId, node.id === hoveredNodeId)}
                    nodePointerAreaPaint={(node: any, color, context) => paintPointerArea(node, color, context)}
                    d3AlphaDecay={0.02}
                    d3VelocityDecay={0.3}
                    cooldownTicks={110}
                    onEngineStop={fitGraph}
                  />
                )}
              </div>
            </>
          )}
        </section>

        <aside className="panel inspector sticky">
          <div className="panel-heading">
            <div>
              <p className="eyebrow">Selected node</p>
              <h2>Inspector</h2>
            </div>
            <Search size={15} />
          </div>
          {!selectedNode && <p className="muted">Select a segment or memory node to inspect it.</p>}
          {selectedNode && <NodeInspector node={selectedNode} />}
        </aside>
      </div>
    </PageFrame>
  );
}

function NodeInspector({ node }: { node: GraphNode }) {
  if (node.kind === "segment") {
    return (
      <dl className="metadata-grid">
        <dt>Segment</dt>
        <dd>{node.label}</dd>
        <dt>Memories</dt>
        <dd>{node.memoryCount}</dd>
        <dt>Id</dt>
        <dd>{node.id}</dd>
      </dl>
    );
  }

  const memory = node.memory;

  return (
    <dl className="metadata-grid">
      <dt>Memory</dt>
      <dd>{memory.text}</dd>
      <dt>Segment</dt>
      <dd>{memory.segment}</dd>
      <dt>Tier</dt>
      <dd>{memory.tier}</dd>
      <dt>Lifecycle</dt>
      <dd>{memory.lifecycle}</dd>
      <dt>Scores</dt>
      <dd>{toNumber(memory.importance).toFixed(2)} / {toNumber(memory.confidence).toFixed(2)}</dd>
      <dt>Access</dt>
      <dd>{toNumber(memory.accessCount)}</dd>
      <dt>Created</dt>
      <dd>{new Date(memory.createdAt).toLocaleString()}</dd>
      <dt>Updated</dt>
      <dd>{new Date(memory.updatedAt).toLocaleString()}</dd>
      <dt>Last accessed</dt>
      <dd>{memory.lastAccessedAt ? new Date(memory.lastAccessedAt).toLocaleString() : "never"}</dd>
      <dt>Source</dt>
      <dd>{memory.sourceMessageId ?? "none"}</dd>
    </dl>
  );
}

function buildGraph(memories: MemoryRow[]) {
  const bySegment = new Map<string, MemoryRow[]>();

  memories.forEach((memory) => {
    const key = memory.segment || "Uncategorized";
    const existing = bySegment.get(key);

    if (existing) {
      existing.push(memory);
    } else {
      bySegment.set(key, [memory]);
    }
  });

  const nodes: GraphNode[] = [];
  const links: GraphLink[] = [];
  const hubs: SegmentHubNode[] = [];

  Array.from(bySegment.entries()).sort(([x], [y]) => x.localeCompare(y)).forEach(([name, segmentMemories]) => {
    const hub: SegmentHubNode = {
      id: `segment:${name}`,
      kind: "segment",
      label: name,
      segment: name,
      memoryCount: segmentMemories.length
    };
    hubs.push(hub);
    nodes.push(hub);

    segmentMemories.forEach((memory) => {
      const node: MemoryNode = {
        id: `memory:${memory.id}`,
        kind: "memory",
        label: truncate(memory.text, 54),
        memory,
        segment: name
      };
      nodes.push(node);
      links.push({ source: hub.id, target: node.id });
    });
  });

  return { nodes, links, hubs };
}

function useElementSize(ref: RefObject<HTMLElement | null>) {
  const [size, setSize] = useState({ width: 0, height: 0 });

  useEffect(() => {
    const element = ref.current;

    if (!element) {
      return;
    }

    const observer = new ResizeObserver(([entry]) => {
      setSize({
        width: Math.floor(entry.contentRect.width),
        height: Math.floor(entry.contentRect.height)
      });
    });
    observer.observe(element);

    return () => observer.disconnect();
  }, [ref]);

  return size;
}

function getNodeRadius(node: GraphNode) {
  if (node.kind === "segment") {
    return Math.max(18, Math.min(42, 16 + Math.log2(node.memoryCount + 1) * 7));
  }

  const importance = toNumber(node.memory.importance);
  const confidence = toNumber(node.memory.confidence);
  const accessCount = toNumber(node.memory.accessCount);

  return Math.max(6, Math.min(14, 5 + importance * 6 + confidence * 2 + Math.log2(accessCount + 1)));
}

function getNodeColor(node: GraphNode) {
  const segment = node.segment || "Context";
  const knownColor = segmentColors[segment];

  if (knownColor) {
    return knownColor;
  }

  const index = Math.abs(hashString(segment)) % fallbackColors.length;

  return fallbackColors[index];
}

function getLinkWidth(link: { source?: GraphNode; target?: GraphNode }) {
  const target = link.target;

  if (target?.kind !== "memory") {
    return 1;
  }

  return Math.max(0.75, Math.min(2.2, 0.8 + toNumber(target.memory.confidence)));
}

function getNodeTooltip(node: GraphNode) {
  if (node.kind === "segment") {
    return `<div class="graph-tooltip"><strong>${escapeHtml(node.label)}</strong><span>${node.memoryCount} memories</span></div>`;
  }

  return `<div class="graph-tooltip"><strong>${escapeHtml(node.memory.text)}</strong><span>${escapeHtml(node.memory.segment)} / ${escapeHtml(node.memory.tier)}</span><span>Importance ${toNumber(node.memory.importance).toFixed(2)} / Confidence ${toNumber(node.memory.confidence).toFixed(2)} / Access ${toNumber(node.memory.accessCount)}</span></div>`;
}

function drawNode(node: GraphNode & { x: number; y: number }, context: CanvasRenderingContext2D, globalScale: number, isSelected: boolean, isHovered: boolean) {
  const color = getNodeColor(node);
  const radius = getNodeRadius(node);
  const alpha = node.kind === "segment" ? 0.94 : 0.78;

  context.save();
  context.globalAlpha = alpha;
  context.fillStyle = color;
  context.beginPath();
  context.arc(node.x, node.y, radius, 0, Math.PI * 2);
  context.fill();
  context.globalAlpha = 1;

  if (isSelected || isHovered) {
    context.strokeStyle = isSelected ? "#f8fafc" : "rgba(248, 250, 252, 0.76)";
    context.lineWidth = (isSelected ? 3 : 2) / globalScale;
    context.beginPath();
    context.arc(node.x, node.y, radius + 4, 0, Math.PI * 2);
    context.stroke();
  }

  if (node.kind === "segment") {
    context.strokeStyle = "rgba(248, 250, 252, 0.78)";
    context.lineWidth = 1 / globalScale;
    context.beginPath();
    context.arc(node.x, node.y, radius, 0, Math.PI * 2);
    context.stroke();
    drawLabel(context, node.label, node.x, node.y + radius + 7, globalScale, true);
  } else if (isSelected || isHovered || globalScale > 1.55) {
    drawLabel(context, node.label, node.x, node.y + radius + 5, globalScale, false);
  }

  context.restore();
}

function drawLabel(context: CanvasRenderingContext2D, label: string, x: number, y: number, globalScale: number, isStrong: boolean) {
  const fontSize = Math.max(10, (isStrong ? 13 : 11) / globalScale);
  const displayLabel = truncate(label, isStrong ? 24 : 42);
  const paddingX = 5 / globalScale;
  const paddingY = 3 / globalScale;

  context.font = `${isStrong ? "700 " : ""}${fontSize}px Inter, sans-serif`;
  const textWidth = context.measureText(displayLabel).width;
  const width = textWidth + paddingX * 2;
  const height = fontSize + paddingY * 2;

  context.fillStyle = "rgba(12, 14, 18, 0.84)";
  context.strokeStyle = "rgba(138, 145, 158, 0.45)";
  context.lineWidth = 1 / globalScale;
  context.beginPath();
  context.roundRect(x - width / 2, y, width, height, 4 / globalScale);
  context.fill();
  context.stroke();

  context.fillStyle = "#e2e2e8";
  context.textAlign = "center";
  context.textBaseline = "top";
  context.fillText(displayLabel, x, y + paddingY);
}

function paintPointerArea(node: GraphNode & { x: number; y: number }, color: string, context: CanvasRenderingContext2D) {
  const radius = getNodeRadius(node) + 8;

  context.fillStyle = color;
  context.beginPath();
  context.arc(node.x, node.y, radius, 0, Math.PI * 2);
  context.fill();
}

function truncate(value: string, length: number) {
  if (value.length <= length) {
    return value;
  }

  return `${value.slice(0, length - 3)}...`;
}

function escapeHtml(value: string) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll("\"", "&quot;")
    .replaceAll("'", "&#039;");
}

function hashString(value: string) {
  let hash = 0;

  for (let index = 0; index < value.length; index += 1) {
    hash = (hash << 5) - hash + value.charCodeAt(index);
    hash |= 0;
  }

  return hash;
}
