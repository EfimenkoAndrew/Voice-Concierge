import { useState } from "react";
import { FaqPage } from "../views/Faq/FaqPage";
import { QueuePage } from "../views/Queue/QueuePage";
import { VoicesPage } from "../views/Voices/VoicesPage";
import { PlaygroundPage } from "../views/Playground/PlaygroundPage";
import { Tabs, type Tab } from "./Tabs";

export function Router() {
  const [tab, setTab] = useState<Tab>("playground");

  return (
    <>
      <Tabs tab={tab} setTab={setTab} />
      {tab === "faq" && <FaqPage />}
      {tab === "queue" && <QueuePage />}
      {tab === "voices" && <VoicesPage />}
      {tab === "playground" && <PlaygroundPage />}
    </>
  );
}
