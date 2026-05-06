import type { ButtonHTMLAttributes, ReactNode } from "react";

export function PageFrame({
  eyebrow,
  title,
  meta,
  actions,
  children
}: {
  eyebrow: string;
  title: string;
  meta?: ReactNode;
  actions?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section className="workspace">
      <TopBar eyebrow={eyebrow} title={title} meta={meta} actions={actions} />
      {children}
    </section>
  );
}

export function TopBar({
  eyebrow,
  title,
  meta,
  actions
}: {
  eyebrow: string;
  title: string;
  meta?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <header className="top-bar">
      <div className="top-title">
        <p className="eyebrow">{eyebrow}</p>
        <h1>{title}</h1>
      </div>
      {meta && <div className="top-meta">{meta}</div>}
      {actions && <div className="top-actions">{actions}</div>}
    </header>
  );
}

export function WorkspaceHeader({
  eyebrow,
  title,
  children
}: {
  eyebrow: string;
  title: string;
  children?: ReactNode;
}) {
  return (
    <header className="workspace-header">
      <div>
        <p className="eyebrow">{eyebrow}</p>
        <h1>{title}</h1>
      </div>
      {children}
    </header>
  );
}

export function Panel({
  title,
  eyebrow,
  actions,
  className = "",
  children
}: {
  title?: string;
  eyebrow?: string;
  actions?: ReactNode;
  className?: string;
  children: ReactNode;
}) {
  return (
    <section className={`panel ${className}`}>
      {(title || eyebrow || actions) && (
        <header className="panel-heading">
          <div>
            {eyebrow && <p className="eyebrow">{eyebrow}</p>}
            {title && <h2>{title}</h2>}
          </div>
          {actions}
        </header>
      )}
      {children}
    </section>
  );
}

export function StatusChip({
  label,
  tone = "neutral"
}: {
  label: string;
  tone?: "neutral" | "blue" | "green" | "violet" | "red" | "amber";
}) {
  return (
    <span className={`status-chip tone-${tone}`}>
      <span aria-hidden="true" />
      {label}
    </span>
  );
}

export function MetricTile({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="metric-tile">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

export function IconButton({
  title,
  children,
  className = "",
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { title: string }) {
  return (
    <button aria-label={title} className={`icon-button ${className}`} title={title} {...props}>
      {children}
    </button>
  );
}

export function LoadingState({ label = "Loading" }: { label?: string }) {
  return (
    <div className="empty-state loading-state">
      <span className="status-square active" />
      {label}
    </div>
  );
}

export function ErrorState({ error }: { error: unknown }) {
  const message = error instanceof Error ? error.message : "Something went wrong.";

  return <div className="callout error">{message}</div>;
}

export function EmptyState({ title, body }: { title: string; body: string }) {
  return (
    <div className="empty-state">
      <h2>{title}</h2>
      <p>{body}</p>
    </div>
  );
}

export function formatLocalTime(value: string) {
  return new Date(value).toLocaleTimeString([], {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit"
  });
}

export function formatLocalDateTime(value: string) {
  return new Date(value).toLocaleString();
}

export function toNumber(value: number | string | null | undefined) {
  if (value === null || value === undefined) {
    return 0;
  }

  return typeof value === "number" ? value : Number(value);
}
