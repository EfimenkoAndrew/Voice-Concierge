import { useState } from "react";
import { getAdminKey, setAdminKey } from "../store/adminKey";

export function AdminKeyInput() {
  const [adminKey, setAdminKeyState] = useState(getAdminKey());

  const handleKeyChange = (k: string) => {
    setAdminKey(k);
    setAdminKeyState(k);
  };

  return (
    <label
      style={{
        display: "flex",
        flexDirection: "column",
        alignItems: "flex-end",
        gap: 2,
        fontSize: 12,
        color: "#666",
      }}
    >
      Admin key
      <input
        type="password"
        placeholder="paste key here"
        value={adminKey}
        onChange={e => handleKeyChange(e.target.value)}
        style={{ width: 180, fontSize: 12, padding: "3px 6px" }}
      />
      {adminKey
        ? <span style={{ color: "#2d6a4f" }}>● key set</span>
        : <span style={{ color: "#c62828" }}>○ no key</span>}
    </label>
  );
}
