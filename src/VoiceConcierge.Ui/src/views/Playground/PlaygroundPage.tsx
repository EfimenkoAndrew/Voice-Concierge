import { useState } from "react";
import { LiveKitRoom, RoomAudioRenderer, useConnectionState } from "@livekit/components-react";
import { ConnectionState } from "livekit-client";
import { getLiveKitConfig, getLiveKitToken } from "../../api/livekit";

const DEFAULT_ROOM = "concierge";

function StatusBadge() {
  const state = useConnectionState();
  const color =
    state === ConnectionState.Connected ? "#1a7d3c"
    : state === ConnectionState.Connecting || state === ConnectionState.Reconnecting ? "#b8860b"
    : "#888";
  return <span style={{ color, fontWeight: 600 }}>● {state}</span>;
}

export function PlaygroundPage() {
  const [conn, setConn] = useState<{ token: string; url: string } | null>(null);
  const [err, setErr] = useState<string | null>(null);

  const start = async () => {
    try {
      setErr(null);
      let room = DEFAULT_ROOM;
      try { room = (await getLiveKitConfig()).room || room; }
      catch { void 0; }
      setConn(await getLiveKitToken(room, `admin-${Date.now()}`));
    } catch (e) { setErr(String(e)); }
  };

  return (
    <section>
      <h2>
        Playground{" "}
        <span style={{ background: "#b8860b", color: "#fff", padding: "2px 8px", borderRadius: 4, fontSize: 12 }}>
          TEST MODE
        </span>
      </h2>
      <p style={{ color: "#666" }}>
        Talk to the concierge using the current voice and FAQ configuration.
      </p>
      {err && <p style={{ color: "crimson" }}>{err}</p>}

      {!conn ? (
        <button onClick={start}>Start conversation</button>
      ) : (
        <LiveKitRoom
          token={conn.token}
          serverUrl={conn.url}
          connect
          audio
          video={false}
          onDisconnected={() => setConn(null)}
          style={{ border: "1px solid #ddd", borderRadius: 6, padding: 12 }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
            <StatusBadge />
            <button onClick={() => setConn(null)}>End conversation</button>
          </div>
          <RoomAudioRenderer />
        </LiveKitRoom>
      )}
    </section>
  );
}
