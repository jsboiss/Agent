import ForceGraph2D, { ForceGraphMethods } from "react-force-graph-2d";
import { RefCallback, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Crosshair, RefreshCcw, Search } from "lucide-react";
import { MemoryGraphEdge, MemoryGraphNode, useGetGraph } from "../../api/generated";
import { EmptyState, ErrorState, IconButton, LoadingState, PageFrame, toNumber } from "../components";

type GraphNode = MemoryGraphNode;

type GraphLink = {
  id: string;
  source: string;
  target: string;
  kind: string;
  label: string;
};

const kindColors: Record<string, string> = {
  entity: "#f59e0b",
  memory: "#38bdf8",
  scope: "#22c55e",
  segment: "#f43f5e",
  source: "#a78bfa",
  tier: "#14b8a6",
  topic: "#60a5fa"
};

const segmentColors: Record<string, string> = {
  Context: "#64748b",
  Correction: "#ef4444",
  Identity: "#f43f5e",
  Knowledge: "#3b82f6",
  Preference: "#14b8a6",
  Procedure: "#22c55e",
  Project: "#f97316",
  Relationship: "#ec4899"
};

const fallbackColors = ["#38bdf8", "#a78bfa", "#f59e0b", "#10b981", "#fb7185", "#60a5fa"];

export function GraphPage() {
  const graphQuery = useGetGraph();
  const graphRef = useRef<ForceGraphMethods | undefined>(undefined);
  const [stageRef, stageElement, stageSize] = useElementSize<HTMLDivElement>();
  const [query, setQuery] = useState("");
  const [segment, setSegment] = useState("All");
  const [tier, setTier] = useState("All");
  const [hoveredNodeId, setHoveredNodeId] = useState<string | null>(null);
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const snapshot = graphQuery.data?.data;
  const graphData = useMemo(() => filterGraph(snapshot?.nodes ?? [], snapshot?.edges ?? [], query, segment, tier), [query, segment, snapshot?.edges, snapshot?.nodes, tier]);
  const memoryCount = (snapshot?.nodes ?? []).filter((node) => node.kind === "memory").length;
  const filteredMemoryCount = graphData.nodes.filter((node) => node.kind === "memory").length;
  const segments = useMemo(() => ["All", ...uniqueSorted((snapshot?.nodes ?? []).filter((node) => node.kind === "segment").map((node) => node.label))], [snapshot?.nodes]);
  const tiers = useMemo(() => ["All", ...uniqueSorted((snapshot?.nodes ?? []).filter((node) => node.kind === "tier").map((node) => node.tier || node.label))], [snapshot?.nodes]);
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

  useEffect(() => {
    const graph = graphRef.current as ForceGraphMethods & {
      d3Force?: (forceName: string) => any;
      d3ReheatSimulation?: () => void;
    } | undefined;

    if (!graph) {
      return;
    }

    graph.d3Force?.("charge")?.strength(-320);
    graph.d3Force?.("link")?.distance((link: ForceGraphLink) => getLinkDistance(link));
    graph.d3Force?.("center")?.strength?.(0.02);
    graph.d3ReheatSimulation?.();
  }, [graphData.links.length, graphData.nodes.length]);

  function fitGraph() {
    graphRef.current?.zoomToFit(450, 48);
  }

  function handleNodeHover(node: GraphNode | null | undefined) {
    setHoveredNodeId(node?.id ?? null);

    if (stageElement) {
      stageElement.style.cursor = node ? "pointer" : "default";
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
            <span className="graph-count mono">{filteredMemoryCount}/{memoryCount}</span>
          </div>

          {graphQuery.isLoading && <LoadingState />}
          {graphQuery.isError && <ErrorState error={graphQuery.error} />}
          {snapshot && memoryCount === 0 && <EmptyState title="No graph data" body={snapshot.emptyReason || "No memories have been captured yet."} />}
          {snapshot && memoryCount > 0 && filteredMemoryCount === 0 && <EmptyState title="No matching memories" body="No memories match the current graph filters." />}
          {filteredMemoryCount > 0 && (
            <>
              <div className="graph-legend">
                {graphData.hubs.slice(0, 8).map((hub) => (
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
                    linkColor={(link: any) => getLinkColor(link)}
                    linkWidth={(link: any) => getLinkWidth(link)}
                    nodeLabel={(node: any) => getNodeTooltip(node)}
                    onNodeClick={(node: any) => setSelectedNodeId(node.id)}
                    onNodeHover={(node: any) => handleNodeHover(node)}
                    nodeCanvasObject={(node: any, context, globalScale) => drawNode(node, context, globalScale, node.id === selectedNodeId, node.id === hoveredNodeId)}
                    nodePointerAreaPaint={(node: any, color, context) => paintPointerArea(node, color, context)}
                    d3AlphaDecay={0.02}
                    d3VelocityDecay={0.42}
                    cooldownTicks={180}
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
          {!selectedNode && <p className="muted">Select a segment, tier, topic, entity, scope, or memory node to inspect it.</p>}
          {selectedNode && <NodeInspector node={selectedNode} />}
        </aside>
      </div>
    </PageFrame>
  );
}

function NodeInspector({ node }: { node: GraphNode }) {
  return (
    <dl className="metadata-grid">
      <dt>Label</dt>
      <dd>{node.label}</dd>
      <dt>Kind</dt>
      <dd>{node.kind}</dd>
      <dt>Segment</dt>
      <dd>{node.segment || "none"}</dd>
      <dt>Tier</dt>
      <dd>{node.tier || "none"}</dd>
      <dt>Lifecycle</dt>
      <dd>{node.lifecycle || "none"}</dd>
      <dt>Count</dt>
      <dd>{toNumber(node.count)}</dd>
      <dt>Importance</dt>
      <dd>{toNumber(node.importance).toFixed(2)}</dd>
      <dt>Text</dt>
      <dd>{node.text || "none"}</dd>
      {Object.entries(node.metadata).map(([key, value]) => (
        <div className="metadata-pair" key={key}>
          <dt>{key}</dt>
          <dd>{value || "none"}</dd>
        </div>
      ))}
    </dl>
  );
}

function filterGraph(nodes: MemoryGraphNode[], edges: MemoryGraphEdge[], query: string, segment: string, tier: string) {
  const normalizedQuery = query.trim().toLowerCase();
  const nodesById = new Map(nodes.map((node) => [node.id, node]));
  const includedIds = new Set<string>();

  nodes.filter((node) => node.kind === "memory" && matchesMemory(node, normalizedQuery, segment, tier)).forEach((node) => {
    includedIds.add(node.id);
  });

  let changed = true;
  while (changed) {
    changed = false;
    edges.forEach((edge) => {
      if (includedIds.has(edge.sourceId) && !includedIds.has(edge.targetId) && nodesById.has(edge.targetId)) {
        includedIds.add(edge.targetId);
        changed = true;
      }

      if (includedIds.has(edge.targetId) && !includedIds.has(edge.sourceId) && nodesById.has(edge.sourceId)) {
        includedIds.add(edge.sourceId);
        changed = true;
      }
    });
  }

  const filteredNodes = nodes.filter((node) => includedIds.has(node.id));
  const filteredLinks = edges
    .filter((edge) => includedIds.has(edge.sourceId) && includedIds.has(edge.targetId))
    .map((edge) => ({
      id: edge.id,
      source: edge.sourceId,
      target: edge.targetId,
      kind: edge.kind,
      label: edge.label
    }));
  const hubs = filteredNodes
    .filter((node) => node.kind !== "memory")
    .sort((x, y) => getKindRank(x.kind) - getKindRank(y.kind) || x.label.localeCompare(y.label));

  return { nodes: filteredNodes, links: filteredLinks, hubs };
}

function matchesMemory(node: MemoryGraphNode, normalizedQuery: string, segment: string, tier: string) {
  if (segment !== "All" && node.segment !== segment) {
    return false;
  }

  if (tier !== "All" && node.tier !== tier) {
    return false;
  }

  if (!normalizedQuery) {
    return true;
  }

  return [
    node.id,
    node.label,
    node.text,
    node.segment,
    node.tier,
    node.lifecycle,
    ...Object.values(node.metadata)
  ].some((value) => value.toLowerCase().includes(normalizedQuery));
}

function useElementSize<T extends HTMLElement>() {
  const [element, setElement] = useState<T | null>(null);
  const [size, setSize] = useState({ width: 0, height: 0 });
  const ref: RefCallback<T> = useCallback((node) => {
    setElement(node);
  }, []);

  useEffect(() => {
    if (!element) {
      setSize({ width: 0, height: 0 });

      return;
    }

    function updateSize() {
      const rect = element!.getBoundingClientRect();

      setSize({
        width: Math.floor(rect.width),
        height: Math.floor(rect.height)
      });
    }

    updateSize();
    const animationFrameId = window.requestAnimationFrame(updateSize);
    const observer = new ResizeObserver(([entry]) => {
      setSize({
        width: Math.floor(entry.contentRect.width),
        height: Math.floor(entry.contentRect.height)
      });
    });
    observer.observe(element);

    return () => {
      window.cancelAnimationFrame(animationFrameId);
      observer.disconnect();
    };
  }, [element]);

  return [ref, element, size] as const;
}

function getNodeRadius(node: GraphNode) {
  if (node.kind === "memory") {
    const importance = toNumber(node.importance);
    const confidence = toNumber(node.metadata.confidence ?? 0);
    const accessCount = toNumber(node.count);

    return Math.max(6, Math.min(14, 5 + importance * 6 + confidence * 2 + Math.log2(accessCount + 1)));
  }

  const count = toNumber(node.count);
  const size = toNumber(node.size);

  return Math.max(9, Math.min(42, size + Math.log2(count + 1) * 4));
}

function getNodeColor(node: GraphNode) {
  const knownSegmentColor = node.segment ? segmentColors[node.segment] : undefined;

  if (knownSegmentColor) {
    return knownSegmentColor;
  }

  const knownKindColor = kindColors[node.kind];

  if (knownKindColor) {
    return knownKindColor;
  }

  const index = Math.abs(hashString(node.kind || node.label)) % fallbackColors.length;

  return fallbackColors[index];
}

function getLinkColor(link: GraphLink) {
  if (link.kind === "supersedes") {
    return "rgba(248, 113, 113, 0.62)";
  }

  if (link.kind === "topic" || link.kind === "entity") {
    return "rgba(96, 165, 250, 0.32)";
  }

  return "rgba(138, 145, 158, 0.24)";
}

function getLinkWidth(link: { kind?: string; target?: GraphNode }) {
  if (link.kind === "supersedes") {
    return 2.2;
  }

  const target = link.target;

  if (target?.kind !== "memory") {
    return 1.1;
  }

  return Math.max(0.75, Math.min(2.2, 0.8 + toNumber(target.metadata.confidence ?? 0)));
}

type ForceGraphLink = {
  kind?: string;
  source?: string | GraphNode;
  target?: string | GraphNode;
};

function getLinkDistance(link: ForceGraphLink) {
  const source = typeof link.source === "object" ? link.source : undefined;
  const target = typeof link.target === "object" ? link.target : undefined;

  if (source?.kind === "memory" && target?.kind === "memory") {
    return 190;
  }

  if (source?.kind === "memory" || target?.kind === "memory") {
    return 145;
  }

  return 165;
}

function getNodeTooltip(node: GraphNode) {
  if (node.kind === "memory") {
    return `<div class="graph-tooltip"><strong>${escapeHtml(node.text)}</strong><span>${escapeHtml(node.segment)} / ${escapeHtml(node.tier)}</span><span>Importance ${toNumber(node.importance).toFixed(2)} / Confidence ${toNumber(node.metadata.confidence ?? 0).toFixed(2)} / Access ${toNumber(node.count)}</span></div>`;
  }

  return `<div class="graph-tooltip"><strong>${escapeHtml(node.label)}</strong><span>${escapeHtml(node.kind)}${node.count ? ` / ${toNumber(node.count)} linked` : ""}</span></div>`;
}

function drawNode(node: GraphNode & { x: number; y: number }, context: CanvasRenderingContext2D, globalScale: number, isSelected: boolean, isHovered: boolean) {
  const color = getNodeColor(node);
  const radius = getNodeRadius(node);
  const alpha = node.kind === "memory" ? 0.78 : 0.94;

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

  if (node.kind !== "memory") {
    context.strokeStyle = "rgba(248, 250, 252, 0.78)";
    context.lineWidth = 1 / globalScale;
    context.beginPath();
    context.arc(node.x, node.y, radius, 0, Math.PI * 2);
    context.stroke();
    drawLabel(context, node.label, node.x, node.y + radius + 7, globalScale, true);
  } else if (isSelected || isHovered || globalScale > 2.8) {
    drawLabel(context, node.label, node.x, node.y + radius + 5, globalScale, false);
  }

  context.restore();
}

function drawLabel(context: CanvasRenderingContext2D, label: string, x: number, y: number, globalScale: number, isStrong: boolean) {
  const screenFontSize = isStrong ? 10 : 8;
  const fontSize = screenFontSize / globalScale;
  const displayLabel = truncate(label, isStrong ? 18 : 28);
  const paddingX = 4 / globalScale;
  const paddingY = 2 / globalScale;

  context.font = `${isStrong ? "700 " : ""}${fontSize}px Inter, sans-serif`;
  const textWidth = context.measureText(displayLabel).width;
  const width = textWidth + paddingX * 2;
  const height = fontSize + paddingY * 2;

  context.fillStyle = "rgba(12, 14, 18, 0.84)";
  context.strokeStyle = "rgba(138, 145, 158, 0.45)";
  context.lineWidth = 1 / globalScale;
  context.beginPath();
  context.roundRect(x - width / 2, y, width, height, 3 / globalScale);
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

function getKindRank(kind: string) {
  const index = ["segment", "tier", "scope", "topic", "entity"].indexOf(kind);

  return index === -1 ? 99 : index;
}

function uniqueSorted(values: string[]) {
  return Array.from(new Set(values.filter(Boolean))).sort((x, y) => x.localeCompare(y));
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
