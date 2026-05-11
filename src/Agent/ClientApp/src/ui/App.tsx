import { useEffect, useMemo, useState } from "react";
import { Activity, Bot, BrainCircuit, CalendarClock, LogOut, MessageSquareText, Network, Settings } from "lucide-react";
import { ActivityPage } from "./pages/ActivityPage";
import { AutomationsPage } from "./pages/AutomationsPage";
import { ChatPage } from "./pages/ChatPage";
import { GraphPage } from "./pages/GraphPage";
import { MemoriesPage } from "./pages/MemoriesPage";
import { RunsPage } from "./pages/RunsPage";
import { SettingsPage } from "./pages/SettingsPage";
import { SubAgentsPage } from "./pages/SubAgentsPage";
import { OperationsPage } from "./pages/OperationsPage";

const navItems = [
  { path: "/", label: "Chat", icon: MessageSquareText },
  { path: "/activity", label: "Activity", icon: Activity },
  { path: "/memories", label: "Memory", icon: BrainCircuit },
  { path: "/automations", label: "Automations", icon: CalendarClock },
  { path: "/graph", label: "Graph", icon: Network },
  { path: "/subagents", label: "Agents", icon: Bot },
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
      case "/activity":
      case "/runs":
      case "/events":
        return <ActivityPage />;
      case "/memories":
        return <MemoriesPage />;
      case "/automations":
      case "/operations":
        return <AutomationsPage />;
      case "/subagents":
        return <SubAgentsPage />;
      case "/graph":
        return <GraphPage />;
      case "/settings":
        return <SettingsPage />;
      case "/legacy-runs":
        return <RunsPage />;
      case "/legacy-operations":
        return <OperationsPage />;
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
            <button className={`rail-link ${path === item.path || (item.path === "/activity" && (path === "/runs" || path === "/events")) || (item.path === "/automations" && path === "/operations") ? "active" : ""}`} key={item.path} onClick={() => navigate(item.path)} title={item.label}>
              <item.icon aria-hidden="true" size={17} strokeWidth={1.8} />
              <span>{item.label}</span>
            </button>
          ))}
        </nav>
        <form action="/logout" className="rail-footer" method="post">
          <button className="rail-link" title="Sign out" type="submit">
            <LogOut aria-hidden="true" size={17} strokeWidth={1.8} />
            <span>Logout</span>
          </button>
        </form>
      </aside>
      <main className="app-main">{page}</main>
    </div>
  );
}
