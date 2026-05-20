import { useEffect, useState } from "react";
import {
  listUnanswered,
  convertUnanswered,
  dismissUnanswered,
  type Unanswered,
} from "../../api/unanswered";

type StatusFilter = "Open" | "Converted" | "Dismissed" | "All";

const STATUS_COLORS: Record<string, string> = {
  Open: "#888",
  Converted: "#2d6a4f",
  Dismissed: "#c62828",
};

export function QueuePage() {
  const [items, setItems] = useState<Unanswered[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [status, setStatus] = useState<StatusFilter>("Open");
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [err, setErr] = useState<string | null>(null);

  const load = async (s: StatusFilter, after?: string) => {
    try {
      const result = await listUnanswered(s, after);
      setItems(prev => after ? [...prev, ...result.items] : result.items);
      setNextCursor(result.nextCursor);
    } catch (e) { setErr(String(e)); }
  };

  useEffect(() => { load(status); }, [status]);

  return (
    <section>
      <h2>Unanswered questions ({items.length}{nextCursor ? "+" : ""})</h2>
      <div style={{ display: "flex", gap: 16, alignItems: "center", marginBottom: 12 }}>
        <p style={{ color: "#666", margin: 0 }}>Most-asked first. Convert to a FAQ or dismiss noise.</p>
        <select value={status} onChange={e => setStatus(e.target.value as StatusFilter)}>
          <option>Open</option>
          <option>Converted</option>
          <option>Dismissed</option>
          <option>All</option>
        </select>
      </div>
      {err && <p style={{ color: "crimson" }}>{err}</p>}
      <ul style={{ listStyle: "none", padding: 0 }}>
        {items.map((u) => (
          <li key={u.id} style={{ border: "1px solid #eee", padding: 12, marginBottom: 8, borderRadius: 6 }}>
            <div style={{ display: "flex", gap: 8, alignItems: "center", flexWrap: "wrap" }}>
              <strong>{u.question}</strong>
              <span style={{ color: "#888" }}>· asked {u.frequency}×</span>
              <span style={{
                fontSize: 11, padding: "2px 7px", borderRadius: 8,
                background: STATUS_COLORS[u.status] ?? "#888", color: "#fff",
              }}>
                {u.status}
              </span>
            </div>
            {u.status === "Open" && (
              <div style={{ display: "grid", gap: 6, marginTop: 6 }}>
                <textarea
                  placeholder="Provide an answer to convert into a FAQ"
                  rows={2}
                  value={answers[u.id] ?? ""}
                  onChange={(e) => setAnswers({ ...answers, [u.id]: e.target.value })}
                />
                <div>
                  <button onClick={async () => {
                    const ans = answers[u.id]?.trim();
                    if (!ans) return;
                    try {
                      await convertUnanswered(u.id, { answer: ans, tags: [] });
                      await load(status);
                    } catch (e) { setErr(String(e)); }
                  }}>
                    Convert to FAQ
                  </button>
                  <button onClick={async () => {
                    try { await dismissUnanswered(u.id); await load(status); }
                    catch (e) { setErr(String(e)); }
                  }}>
                    Dismiss
                  </button>
                </div>
              </div>
            )}
          </li>
        ))}
      </ul>
      {nextCursor && (
        <button onClick={() => load(status, nextCursor)}>Load more</button>
      )}
    </section>
  );
}
