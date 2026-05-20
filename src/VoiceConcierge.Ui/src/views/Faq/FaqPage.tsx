import { useEffect, useState } from "react";
import {
  listFaqs,
  createFaq,
  updateFaq,
  deleteFaq,
  type Faq,
} from "../../api/faqs";

export function FaqPage() {
  const [items, setItems] = useState<Faq[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [q, setQ] = useState("");
  const [a, setA] = useState("");
  const [editing, setEditing] = useState<string | null>(null);
  const [err, setErr] = useState<string | null>(null);

  const load = async (after?: string) => {
    try {
      const result = await listFaqs(after);
      setItems(prev => after ? [...prev, ...result.items] : result.items);
      setNextCursor(result.nextCursor);
    } catch (e) { setErr(String(e)); }
  };

  useEffect(() => { load(); }, []);

  const submit = async () => {
    try {
      if (!q.trim() || !a.trim()) return;
      if (editing) await updateFaq(editing, { question: q, answer: a });
      else await createFaq({ question: q, answer: a, tags: [] });
      setQ(""); setA(""); setEditing(null); setErr(null); await load();
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e);
      if (msg.includes("409") || msg.includes("Conflict"))
        setErr("A FAQ with this question already exists.");
      else
        setErr(msg);
    }
  };

  return (
    <section>
      <h2>FAQ items ({items.length}{nextCursor ? "+" : ""})</h2>
      {err && <p style={{ color: "crimson" }}>{err}</p>}
      <div style={{ display: "grid", gap: 6, marginBottom: 16 }}>
        <input placeholder="Question" value={q} onChange={(e) => setQ(e.target.value)} />
        <textarea placeholder="Answer" value={a} onChange={(e) => setA(e.target.value)} rows={3} />
        <div>
          <button onClick={submit}>{editing ? "Save changes" : "Add FAQ"}</button>
          {editing && (
            <button onClick={() => { setEditing(null); setQ(""); setA(""); setErr(null); }}>
              Cancel
            </button>
          )}
        </div>
      </div>
      <ul style={{ listStyle: "none", padding: 0 }}>
        {items.map((f) => (
          <li key={f.id} style={{ border: "1px solid #eee", padding: 12, marginBottom: 8, borderRadius: 6 }}>
            <strong>{f.question}</strong>
            <div style={{ color: "#444" }}>{f.answer}</div>
            {f.tags.length > 0 && (
              <div style={{ marginTop: 4 }}>
                {f.tags.map(t => (
                  <span key={t} style={{ marginRight: 4, background: "#f0f0f0", padding: "2px 6px", borderRadius: 10, fontSize: 12 }}>
                    {t}
                  </span>
                ))}
              </div>
            )}
            <div style={{ marginTop: 6 }}>
              <button onClick={() => { setEditing(f.id); setQ(f.question); setA(f.answer); setErr(null); }}>
                Edit
              </button>
              <button onClick={async () => {
                try { await deleteFaq(f.id); load(); }
                catch (e) { setErr(String(e)); }
              }}>
                Delete
              </button>
            </div>
          </li>
        ))}
      </ul>
      {nextCursor && (
        <button onClick={() => load(nextCursor)}>Load more</button>
      )}
    </section>
  );
}
