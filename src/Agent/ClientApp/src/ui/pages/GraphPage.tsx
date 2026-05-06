import ForceGraph2D, { ForceGraphMethods } from "react-force-graph-2d";
import { useMemo, useRef, useState } from "react";
import { Crosshair, RefreshCcw, Search } from "lucide-react";
import { MemoryGraphNode, useGetGraph } from "../../api/generated";
import { EmptyState, ErrorState, IconButton, LoadingState, PageFrame, StatusChip, toNumber } from "../components";

const segmentColors: Record<string, string> = {
  Context: "#2e90fa",
  Preference: "#12b76a",
  Identity: "#7a5af8",
  Procedure: "#51df8e",
  Project: "#a6c8ff"
};

const kindColors: Record<string, string> = {
  memory: "#2e90fa",
  segment: "#12b76a",
  tier: "#7a5af8",
  source: "#cabeff"
};

export function GraphPage() {
  const graphQuery = useGetGraph();
  const graphRef = useRef<ForceGraphMethods | undefined>(undefined);
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const snapshot = graphQuery.data?.data;
  const snapshotNodes = snapshot?.nodes ?? [];
  const snapshotEdges = snapshot?.edges ?? [];
  const graphData = useMemo(() => {
    const nodes = snapshotNodes.map((node) => ({ ...node }));
    const links = snapshotEdges.map((edge) => ({
      ...edge,
      source: edge.sourceId,
      target: edge.targetId
    }));

    return { nodes, links };
  }, [snapshotEdges, snapshotNodes]);
  const selectedNode = snapshotNodes.find((node) => node.id === selectedNodeId);

  function fitGraph() {
    graphRef.current?.zoomToFit(450, 36);
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
          {graphQuery.isLoading && <LoadingState />}
          {graphQuery.isError && <ErrorState error={graphQuery.error} />}
          {snapshot && snapshotNodes.length === 0 && <EmptyState title="No graph data" body={snapshot.emptyReason} />}
          {snapshotNodes.length > 0 && (
            <>
              <div className="graph-legend">
                <StatusChip label="memory" tone="blue" />
                <StatusChip label="segment" tone="green" />
                <StatusChip label="tier" tone="violet" />
              </div>
              <ForceGraph2D
                ref={graphRef}
                graphData={graphData}
                backgroundColor="#0c0e12"
                nodeRelSize={1}
                linkColor={() => "rgba(138, 145, 158, 0.32)"}
                linkDirectionalParticles={1}
                linkDirectionalParticleWidth={1.3}
                linkWidth={1}
                nodeLabel={(node: any) => getNodeLabel(node)}
                onNodeClick={(node: any) => setSelectedNodeId(node.id)}
                nodeCanvasObject={(node: any, context, globalScale) => drawNode(node, context, globalScale)}
                d3AlphaDecay={0.022}
                d3VelocityDecay={0.31}
                cooldownTicks={120}
                onEngineStop={fitGraph}
              />
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
          {!selectedNode && <p className="muted">Select a graph node to inspect it.</p>}
          {selectedNode && <NodeInspector node={selectedNode} />}
        </aside>
      </div>
    </PageFrame>
  );
}

function NodeInspector({ node }: { node: MemoryGraphNode }) {
  return (
    <dl className="metadata-grid">
      <dt>Label</dt>
      <dd>{node.label}</dd>
      <dt>Kind</dt>
      <dd>{node.kind}</dd>
      <dt>Id</dt>
      <dd>{node.id}</dd>
      <dt>Segment</dt>
      <dd>{node.segment || "none"}</dd>
      <dt>Tier</dt>
      <dd>{node.tier || "none"}</dd>
      <dt>Lifecycle</dt>
      <dd>{node.lifecycle || "none"}</dd>
      <dt>Text</dt>
      <dd>{node.text || node.label}</dd>
      {Object.entries(node.metadata).map(([key, value]) => (
        <div className="metadata-pair" key={key}>
          <dt>{key}</dt>
          <dd>{value || "none"}</dd>
        </div>
      ))}
    </dl>
  );
}

function getNodeColor(node: MemoryGraphNode) {
  return segmentColors[node.segment] ?? kindColors[node.kind] ?? "#a6c8ff";
}

function getNodeLabel(node: MemoryGraphNode) {
  return `${node.kind}: ${node.label}`;
}

function drawNode(node: MemoryGraphNode & { x: number; y: number }, context: CanvasRenderingContext2D, globalScale: number) {
  const color = getNodeColor(node);
  const size = toNumber(node.size);
  const radius = node.kind === "memory" ? Math.max(6, size) : Math.max(10, size);

  context.fillStyle = color;
  context.globalAlpha = node.kind === "memory" ? 0.86 : 0.95;
  context.fillRect(node.x - radius, node.y - radius, radius * 2, radius * 2);
  context.globalAlpha = 1;
  context.strokeStyle = "rgba(226, 226, 232, 0.72)";
  context.lineWidth = 1 / globalScale;
  context.strokeRect(node.x - radius, node.y - radius, radius * 2, radius * 2);

  if (node.kind !== "memory" || globalScale > 1.25) {
    context.fillStyle = "#e2e2e8";
    context.font = `${Math.max(9, 11 / globalScale)}px Inter, sans-serif`;
    context.textAlign = "center";
    context.textBaseline = "top";
    context.fillText(node.label.slice(0, 34), node.x, node.y + radius + 5);
  }
}
