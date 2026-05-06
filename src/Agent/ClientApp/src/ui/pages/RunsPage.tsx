import { useMemo, useState } from "react";
import { ChevronDown, ChevronRight, RefreshCcw } from "lucide-react";
import { useGetRuns } from "../../api/generated";
import { EmptyState, ErrorState, formatLocalDateTime, formatLocalTime, IconButton, LoadingState, PageFrame, StatusChip } from "../components";

export function RunsPage() {
  const [conversationId, setConversationId] = useState("main");
  const [filter, setFilter] = useState("All");
  const [selectedEventId, setSelectedEventId] = useState<string | null>(null);
  const [collapsedTurns, setCollapsedTurns] = useState<Set<string>>(new Set());
  const runsQuery = useGetRuns({ conversationId, filter });
  const snapshot = runsQuery.data?.data;
  const events = snapshot?.events ?? [];
  const turns = snapshot?.turns ?? [];
  const selectedEvent = useMemo(
    () => events.find((event) => event.id === selectedEventId) ?? events[0],
    [events, selectedEventId]
  );

  function toggleTurn(title: string) {
    setCollapsedTurns((current) => {
      const next = new Set(current);

      if (!next.delete(title)) {
        next.add(title);
      }

      return next;
    });
  }

  return (
    <PageFrame
      eyebrow="Turn timeline"
      title="Runs"
      actions={<IconButton onClick={() => runsQuery.refetch()} title="Refresh runs" type="button"><RefreshCcw size={15} /></IconButton>}
    >
      <div className="filter-bar">
        <input onChange={(event) => setConversationId(event.target.value)} placeholder="Conversation id" value={conversationId} />
        <select onChange={(event) => setFilter(event.target.value)} value={filter}>
          <option>All</option>
          <option>Provider</option>
          <option>Tool</option>
          <option>Memory</option>
          <option>Error</option>
        </select>
      </div>

      <div className="runs-layout">
        <div className="timeline">
          {runsQuery.isLoading && <LoadingState />}
          {runsQuery.isError && <ErrorState error={runsQuery.error} />}
          {snapshot && events.length === 0 && <EmptyState title="No run events" body="Events appear here after a message is processed." />}
          {turns.map((turn) => {
            const collapsed = collapsedTurns.has(turn.title);

            return (
              <section className="turn-group" key={`${turn.title}:${turn.startedAt}`}>
                <button className="turn-header" onClick={() => toggleTurn(turn.title)} type="button">
                  {collapsed ? <ChevronRight size={14} /> : <ChevronDown size={14} />}
                  <strong>{turn.title}</strong>
                  <span>{formatLocalDateTime(turn.startedAt)}</span>
                  <small>{turn.events.length} events</small>
                </button>
                {!collapsed && turn.events.map((event) => (
                  <button className={`timeline-event ${event.isError ? "is-error" : ""}`} key={event.id} onClick={() => setSelectedEventId(event.id)} type="button">
                    <span className={`status-square ${event.isError ? "error" : ""}`} />
                    <time>{formatLocalTime(event.createdAt)}</time>
                    <strong>{event.phase}</strong>
                    <em>{event.kind}</em>
                    <small>{event.summary}</small>
                  </button>
                ))}
              </section>
            );
          })}
        </div>

        <aside className="panel inspector sticky">
          <div className="panel-heading">
            <div>
              <p className="eyebrow">Event detail</p>
              <h2>Inspector</h2>
            </div>
            {selectedEvent && <StatusChip label={selectedEvent.isError ? "error" : selectedEvent.phase} tone={selectedEvent.isError ? "red" : "blue"} />}
          </div>
          {!selectedEvent && <p className="muted">Select an event to inspect its raw metadata.</p>}
          {selectedEvent && (
            <>
              <h3>{selectedEvent.kind}</h3>
              <p>{selectedEvent.summary}</p>
              <dl className="metadata-grid">
                <dt>Id</dt>
                <dd>{selectedEvent.id}</dd>
                <dt>Conversation</dt>
                <dd>{selectedEvent.conversationId}</dd>
                <dt>Phase</dt>
                <dd>{selectedEvent.phase}</dd>
                {Object.entries(selectedEvent.metadata).map(([key, value]) => (
                  <div className="metadata-pair" key={key}>
                    <dt>{key}</dt>
                    <dd>{value}</dd>
                  </div>
                ))}
              </dl>
            </>
          )}
        </aside>
      </div>
    </PageFrame>
  );
}
