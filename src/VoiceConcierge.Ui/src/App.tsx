import { AdminKeyInput } from "./components/AdminKeyInput";
import { Router } from "./routing/Router";

export function App() {
  return (
    <div style={{ fontFamily: "system-ui, sans-serif", maxWidth: 960, margin: "0 auto", padding: 24 }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
        <div>
          <h1 style={{ marginBottom: 4 }}>The Meridian — Concierge Admin</h1>
          <p style={{ color: "#666", marginTop: 0 }}>
            Manage FAQ content, review unanswered questions, configure the voice.
          </p>
        </div>
        <AdminKeyInput />
      </div>
      <Router />
    </div>
  );
}
