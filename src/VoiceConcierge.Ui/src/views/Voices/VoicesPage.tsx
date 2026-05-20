import { useEffect, useRef, useState } from "react";
import {
  listVoices,
  setActiveVoice,
  updateVoice,
  previewUrl,
  previewWith,
  type Voice,
} from "../../api/voices";

interface EditState {
  description: string;
  sampleText: string;
  isActive: boolean;
}

export function VoicesPage() {
  const [voices, setVoices] = useState<Voice[]>([]);
  const [showDisabled, setShowDisabled] = useState(false);
  const [editing, setEditing] = useState<Record<number, EditState>>({});
  const [previewText, setPreviewText] = useState<Record<number, string>>({});
  const [err, setErr] = useState<string | null>(null);
  const audioRef = useRef<{ audio: HTMLAudioElement; url: string } | null>(null);

  const load = (includeDisabled = showDisabled) =>
    listVoices(includeDisabled).then(setVoices).catch(e => setErr(String(e)));

  useEffect(() => { load(showDisabled); }, [showDisabled]);

  useEffect(() => {
    return () => {
      if (audioRef.current) {
        audioRef.current.audio.pause();
        URL.revokeObjectURL(audioRef.current.url);
        audioRef.current = null;
      }
    };
  }, []);

  const startEdit = (v: Voice) =>
    setEditing(prev => ({ ...prev, [v.id]: { description: v.description, sampleText: "", isActive: v.isActive } }));

  const cancelEdit = (id: number) =>
    setEditing(prev => { const n = { ...prev }; delete n[id]; return n; });

  const saveEdit = async (v: Voice) => {
    const e = editing[v.id];
    if (!e) return;
    try {
      await updateVoice(v.id, {
        description: e.description,
        sampleText: e.sampleText.trim() || undefined,
        isActive: e.isActive,
      });
      cancelEdit(v.id);
      load();
    } catch (ex) { setErr(String(ex)); }
  };

  const playPreview = async (id: number, text: string) => {
    try {
      const resp = await previewWith(id, text);
      if (!resp.ok) { setErr(`Preview failed: ${resp.status}`); return; }
      const blob = await resp.blob();
      const url = URL.createObjectURL(blob);
      const audio = new Audio(url);
      audioRef.current = { audio, url };
      const revoke = () => { URL.revokeObjectURL(url); if (audioRef.current?.url === url) audioRef.current = null; };
      audio.onended = revoke;
      audio.onerror = revoke;
      audio.play();
    } catch (e) { setErr(String(e)); }
  };

  return (
    <section>
      <h2>Voice configuration</h2>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 12 }}>
        <p style={{ color: "#666", margin: 0 }}>
          Choose the concierge voice. Changes apply to new conversations immediately.
        </p>
        <label>
          <input type="checkbox" checked={showDisabled}
            onChange={e => setShowDisabled(e.target.checked)} />
          {" "}Show disabled
        </label>
      </div>
      {err && <p style={{ color: "crimson" }}>{err}</p>}
      <ul style={{ listStyle: "none", padding: 0 }}>
        {voices.map((v) => {
          const ed = editing[v.id];
          return (
            <li key={v.id} style={{
              border: v.active ? "2px solid #1a365d" : "1px solid #eee",
              padding: 12, marginBottom: 8, borderRadius: 6,
              opacity: v.isActive ? 1 : 0.6,
            }}>
              <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
                <strong>{v.name}</strong>
                {v.active && <span style={{ color: "#1a365d", fontSize: 12 }}>● Active</span>}
                {!v.isActive && <span style={{ color: "#999", fontSize: 12 }}>Disabled</span>}
              </div>

              {ed ? (
                <div style={{ display: "grid", gap: 8, marginTop: 8 }}>
                  <label style={{ fontSize: 13 }}>
                    Description
                    <textarea rows={2} style={{ display: "block", width: "100%", marginTop: 2 }}
                      value={ed.description}
                      onChange={e => setEditing(prev => ({ ...prev, [v.id]: { ...ed, description: e.target.value } }))} />
                  </label>
                  <label style={{ fontSize: 13 }}>
                    New sample text <span style={{ color: "#888" }}>(optional — leave blank to keep existing)</span>
                    <input style={{ display: "block", width: "100%", marginTop: 2 }}
                      value={ed.sampleText}
                      placeholder="e.g. Hello, how can I help you today?"
                      onChange={e => setEditing(prev => ({ ...prev, [v.id]: { ...ed, sampleText: e.target.value } }))} />
                  </label>
                  <label style={{ fontSize: 13 }}>
                    <input type="checkbox" checked={ed.isActive}
                      disabled={v.active}
                      onChange={e => setEditing(prev => ({ ...prev, [v.id]: { ...ed, isActive: e.target.checked } }))} />
                    {" "}Enabled{v.active ? " (cannot disable the active voice)" : ""}
                  </label>
                  <div>
                    <button onClick={() => saveEdit(v)}>Save</button>
                    <button onClick={() => cancelEdit(v.id)}>Cancel</button>
                  </div>
                </div>
              ) : (
                <div style={{ color: "#444", marginTop: 4 }}>{v.description}</div>
              )}

              <div style={{ marginTop: 8, display: "flex", gap: 8, alignItems: "center", flexWrap: "wrap" }}>
                {!v.active && v.isActive && (
                  <button onClick={async () => {
                    try { await setActiveVoice(v.id); load(); }
                    catch (e) { setErr(String(e)); }
                  }}>
                    Set active
                  </button>
                )}
                {!ed && <button onClick={() => startEdit(v)}>Edit</button>}
                <audio controls preload="none" src={previewUrl(v.id)} style={{ verticalAlign: "middle" }}>
                  <track kind="captions" />
                </audio>
                <input
                  placeholder="Custom preview text"
                  style={{ fontSize: 12, width: 200 }}
                  value={previewText[v.id] ?? ""}
                  onChange={e => setPreviewText(prev => ({ ...prev, [v.id]: e.target.value }))}
                />
                <button style={{ fontSize: 12 }} onClick={() => {
                  const text = previewText[v.id]?.trim() ?? "";
                  if (text) playPreview(v.id, text);
                }}>
                  Preview
                </button>
              </div>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
