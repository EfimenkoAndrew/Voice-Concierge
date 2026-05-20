export type Tab = "playground" | "faq" | "queue" | "voices";

const TABS: { id: Tab; label: string }[] = [
  { id: "playground", label: "Playground" },
  { id: "faq", label: "FAQ" },
  { id: "queue", label: "Unanswered" },
  { id: "voices", label: "Voices" },
];

interface Props {
  tab: Tab;
  setTab: (t: Tab) => void;
}

export function Tabs({ tab, setTab }: Props) {
  return (
    <nav style={{ display: "flex", gap: 8, borderBottom: "1px solid #ddd", marginBottom: 16 }}>
      {TABS.map((t) => (
        <button
          key={t.id}
          onClick={() => setTab(t.id)}
          style={{
            padding: "8px 14px",
            border: "none",
            cursor: "pointer",
            background: tab === t.id ? "#1a365d" : "transparent",
            color: tab === t.id ? "#fff" : "#1a365d",
            borderRadius: "6px 6px 0 0",
          }}
        >
          {t.label}
        </button>
      ))}
    </nav>
  );
}
