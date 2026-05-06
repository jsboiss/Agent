import { useEffect, useMemo, useState } from "react";
import { Bot, BrainCircuit, GitBranch, MessageSquareText, Network, Settings, SlidersHorizontal } from "lucide-react";
import { ChatPage } from "./pages/ChatPage";
import { GraphPage } from "./pages/GraphPage";
import { MemoriesPage } from "./pages/MemoriesPage";
import { RunsPage } from "./pages/RunsPage";
import { SettingsPage } from "./pages/SettingsPage";
import { SubAgentsPage } from "./pages/SubAgentsPage";
import { OperationsPage } from "./pages/OperationsPage";

const navItems = [
  { path: "/", label: "Chat", icon: MessageSquareText },
  { path: "/memories", label: "Memories", icon: BrainCircuit },
  { path: "/runs", label: "Runs", icon: GitBranch },
  { path: "/subagents", label: "Subagents", icon: Bot },
  { path: "/operations", label: "Operations", icon: SlidersHorizontal },
  { path: "/graph", label: "Graph", icon: Network },
  { path: "/settings", label: "Settings", icon: Settings }
];

export function App() {
  const [path, setPath] = useState(window.location.pathname);

  useEffect(() => {
    const onPopState = () => setPath(window.location.pathname);
    window.addEventListener("popstate", onPopState);

    return () => window.removeEventListener("popstate", onPopState);
  }, []);

  const page = useMemo(() => {
    switch (path) {
      case "/memories":
        return <MemoriesPage />;
      case "/runs":
      case "/events":
        return <RunsPage />;
      case "/subagents":
        return <SubAgentsPage />;
      case "/operations":
        return <OperationsPage />;
      case "/graph":
        return <GraphPage />;
      case "/settings":
        return <SettingsPage />;
      default:
        return <ChatPage />;
    }
  }, [path]);

  function navigate(nextPath: string) {
    window.history.pushState(null, "", nextPath);
    setPath(nextPath);
  }

  return (
    <div className="app-shell">
      <aside className="left-rail" aria-label="Main navigation">
        <button className="brand-mark" onClick={() => navigate("/")} title="Agent">
          A
        </button>
        <nav className="rail-nav">
          {navItems.map((item) => (
            <button className={`rail-link ${path === item.path || (item.path === "/runs" && path === "/events") ? "active" : ""}`} key={item.path} onClick={() => navigate(item.path)} title={item.label}>
              <item.icon aria-hidden="true" size={17} strokeWidth={1.8} />
              <span>{item.label}</span>
            </button>
          ))}
        </nav>
      </aside>
      <main className="app-main">{page}</main>
    </div>
  );
}
